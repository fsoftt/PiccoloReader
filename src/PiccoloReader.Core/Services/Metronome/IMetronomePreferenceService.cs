namespace PiccoloReader.Core.Services.Metronome;

public interface IMetronomePreferenceService
{
    MetronomeSettings Load();

    void Save(MetronomeSettings settings);
}
