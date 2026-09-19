using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Core.Services;

public record MusicIcon(string Key, string DisplayName, int Codepoint, double AspectRatio, double VisualScale = 1.0);

public record MusicIconCategory(string Name, IReadOnlyList<MusicIcon> Icons);

public static class MusicIconCatalog
{
    public static IReadOnlyList<MusicIconCategory> Categories { get; } = new List<MusicIconCategory>
    {
        // pp/p/f/ff/fp are standard musical dynamics notation - identical
        // in every language, not translated (see AppStrings.cs for every
        // other name in this catalog, which are genuine words).
        new(AppStrings.CategoryDynamics, new List<MusicIcon>
        {
            new("dynamicPP", "pp", 0xE52B, 1.95),
            new("dynamicPiano", "p", 0xE520, 1.09),
            new("dynamicForte", "f", 0xE522, 0.85),
            new("dynamicFF", "ff", 0xE52F, 1.25),
            new("dynamicFortePiano", "fp", 0xE534, 1.28),
        }),
        new(AppStrings.CategoryArticulations, new List<MusicIcon>
        {
            // Staccato's glyph is a small dot with tight ink bounds, so fitting
            // it to the same box as every other icon (DrawGlyphFitted scales
            // each glyph's own ink to nearly fill its target rect) blows it up
            // far larger than the dot is meant to read - VisualScale reins
            // that back in without affecting how any other icon renders.
            new("articStaccatoAbove", AppStrings.IconStaccato, 0xE4A2, 1.00, 0.4),
            new("articAccentAbove", AppStrings.IconAccent, 0xE4A0, 1.39),
            new("articTenutoAbove", AppStrings.IconTenuto, 0xE4A4, 7.06),
            new("articMarcatoAbove", AppStrings.IconMarcato, 0xE4AC, 0.93),
        }),
        new(AppStrings.CategoryFermataBreath, new List<MusicIcon>
        {
            new("fermataAbove", AppStrings.IconFermata, 0xE4C0, 1.81),
            new("breathMarkComma", AppStrings.IconBreathMark, 0xE4CE, 0.61),
        }),
        new(AppStrings.CategoryHairpins, new List<MusicIcon>
        {
            new("dynamicCrescendoHairpin", AppStrings.IconCrescendo, 0xE53E, 2.78),
            new("dynamicDiminuendoHairpin", AppStrings.IconDecrescendo, 0xE53F, 2.78),
        }),
    };

    public static MusicIcon? FindByKey(string key) =>
        Categories.SelectMany(c => c.Icons).FirstOrDefault(i => i.Key == key);
}
