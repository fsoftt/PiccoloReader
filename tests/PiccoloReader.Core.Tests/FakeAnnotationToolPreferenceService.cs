using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeAnnotationToolPreferenceService : IAnnotationToolPreferenceService
{
    public AnnotationToolSettings Stored { get; set; } = new();

    public int SaveCount { get; private set; }

    public AnnotationToolSettings Load() => Stored;

    public void Save(AnnotationToolSettings settings)
    {
        Stored = settings;
        SaveCount++;
    }
}
