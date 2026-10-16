namespace PiccoloReader.Core.Tests.Services.Tuner;

/// <summary>Minimal reader for 16-bit PCM mono WAV fixtures.</summary>
internal static class WavReader
{
    public static (float[] Samples, int SampleRate) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var rate = BitConverter.ToInt32(bytes, 24);
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "data")
            {
                var count = size / 2;
                var samples = new float[count];
                for (var i = 0; i < count; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2) / 32768f;
                }

                return (samples, rate);
            }

            pos += 8 + size;
        }

        throw new InvalidDataException("No data chunk in " + path);
    }

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
