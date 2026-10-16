namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Streaming decimate-by-2 with a symmetric half-band FIR (23 taps, Hamming).
/// Only the center tap and the odd offsets are non-zero, so each output costs
/// 7 multiplies. Allocation-free after construction.
/// </summary>
public sealed class HalfBandDecimator
{
    private const int Radius = 11;
    private const int HistorySize = 32;
    private const int HistoryMask = HistorySize - 1;
    private const int Pairs = (Radius + 1) / 2;

    private readonly float[] _history = new float[HistorySize];
    private readonly float[] _taps = new float[Pairs];
    private readonly float _center;
    private int _pos;
    private bool _emit;

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
        Array.Clear(_history);
        _pos = 0;
        _emit = false;
    }

    /// <summary>Decimates the block; <paramref name="output"/> must hold <see cref="MaxOutputLength"/> samples. Returns the number written.</summary>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        var written = 0;
        var history = _history;
        var taps = _taps;
        var pos = _pos;
        var emit = _emit;

        for (var i = 0; i < input.Length; i++)
        {
            history[pos] = input[i];
            pos = (pos + 1) & HistoryMask;
            emit = !emit;
            if (!emit)
            {
                continue;
            }

            var middle = pos - 1 - Radius;
            var acc = _center * history[middle & HistoryMask];
            for (var k = 0; k < taps.Length; k++)
            {
                var offset = 2 * k + 1;
                acc += taps[k] * (history[(middle - offset) & HistoryMask] + history[(middle + offset) & HistoryMask]);
            }

            output[written++] = acc;
        }

        _pos = pos;
        _emit = emit;
        return written;
    }
}
