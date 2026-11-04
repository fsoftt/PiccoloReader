namespace PiccoloReader.Core.Services.Stylus;

public interface IStylusPreferenceService
{
    /// <summary>
    /// When on, a stylus draws on the page without entering annotation mode. On by default.
    /// The value is cached in memory so touch handlers can read it without I/O.
    /// </summary>
    bool DrawsDirectly { get; set; }

    /// <summary>
    /// When on, a slur or hairpin drawn with the pencil or stylus is saved as a clean figure
    /// instead of a stroke. On by default. Cached like DrawsDirectly.
    /// </summary>
    bool RecognizesShapes { get; set; }
}
