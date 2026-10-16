namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Streaming decimate-by-2 with a symmetric half-band FIR (23 taps, Hamming).
/// Only the center tap and the odd offsets are non-zero, so each output costs
/// 7 multiplies. Blocks are filtered from a linear scratch buffer (no index
/// masking in the inner loop, which matters on a JIT without loop optimisations).
/// Allocation-free after construction.
/// </summary>
public sealed class HalfBandDecimator
{
    private const int Radius = 11;
    private const int Pairs = (Radius + 1) / 2;
    private const int Tail = 2 * Radius;
    private const int Block = 512;

    private readonly float[] _work = new float[Tail + Block];
    private readonly float[] _taps = new float[Pairs];
    private readonly float _center;
    private int _skip;

    public HalfBandDecimator()
    {
        var sum = 0.5;
        var raw = new double[Pairs];
        for (var k = 0; k < Pairs; k++)
        {
            var n = 2 * k + 1;
            var sinc = Math.Sin(Math.PI * n / 2) / (Math.PI * n);
            var window = 0.54 + 0.46 * Math.Cos(Math.PI * n / (Radius + 1));
            raw[k] = sinc * window;
            sum += 2 * raw[k];
        }

        // Unity gain at DC.
        _center = (float)(0.5 / sum);
        for (var k = 0; k < Pairs; k++)
        {
            _taps[k] = (float)(raw[k] / sum);
        }
    }

    /// <summary>Upper bound of outputs produced for <paramref name="inputLength"/> inputs.</summary>
    public static int MaxOutputLength(int inputLength) => (inputLength + 1) / 2 + 1;

    public void Reset()
    {
        Array.Clear(_work);
        _skip = 0;
    }

    /// <summary>
    /// Stands in for <paramref name="inputLength"/> silent input samples: writes
    /// the matching number of zero outputs without filtering anything.
    /// </summary>
    public int ProcessSilence(int inputLength, Span<float> output)
    {
        var count = 0;
        for (var j = _skip; j < inputLength; j += 2)
        {
            output[count++] = 0f;
        }

        _skip = (_skip - inputLength) & 1;
        Array.Clear(_work, 0, Tail);
        return count;
    }

    /// <summary>Decimates the block; <paramref name="output"/> must hold <see cref="MaxOutputLength"/> samples. Returns the number written.</summary>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var written = 0;
        while (!input.IsEmpty)
        {
            var take = Math.Min(input.Length, Block);
            written += ProcessBlock(input[..take], output[written..]);
            input = input[take..];
        }

        return written;
    }

    private int ProcessBlock(ReadOnlySpan<float> input, Span<float> output)
    {
        var work = _work;
        var n = input.Length;
        input.CopyTo(work.AsSpan(Tail));

        var c = _center;
        var t0 = _taps[0];
        var t1 = _taps[1];
        var t2 = _taps[2];
        var t3 = _taps[3];
        var t4 = _taps[4];
        var t5 = _taps[5];
        var written = 0;
        var j = _skip;
        for (; j < n; j += 2)
        {
            // Latest sample of the 23-sample window is work[Tail + j]; its middle is Radius earlier.
            var m = j + Radius;
            output[written++] = (c * work[m])
                + (t0 * (work[m - 1] + work[m + 1]))
                + (t1 * (work[m - 3] + work[m + 3]))
                + (t2 * (work[m - 5] + work[m + 5]))
                + (t3 * (work[m - 7] + work[m + 7]))
                + (t4 * (work[m - 9] + work[m + 9]))
                + (t5 * (work[m - 11] + work[m + 11]));
        }

        _skip = j - n;
        Array.Copy(work, n, work, 0, Tail);
        return written;
    }
}
