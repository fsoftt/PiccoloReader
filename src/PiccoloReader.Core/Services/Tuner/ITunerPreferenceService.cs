namespace PiccoloReader.Core.Services.Tuner;

public interface ITunerPreferenceService
{
    TunerSettings Load();

    void Save(TunerSettings settings);
}
