namespace PiccoloReader.Core.Services.Concert;

public interface IConcertModePreferenceService
{
    /// <summary>
    /// When on, "next" on the last page opens the next score of the list. On by default.
    /// </summary>
    bool Enabled { get; set; }
}
