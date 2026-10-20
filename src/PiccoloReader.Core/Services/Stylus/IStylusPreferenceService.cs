namespace PiccoloReader.Core.Services.Stylus;

public interface IStylusPreferenceService
{
    /// <summary>
    /// When on, a stylus draws on the page without entering annotation mode. On by default.
    /// The value is cached in memory so touch handlers can read it without I/O.
    /// </summary>
    bool DrawsDirectly { get; set; }
}
