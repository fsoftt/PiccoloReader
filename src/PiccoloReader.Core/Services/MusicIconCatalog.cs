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
        new(AppStrings.CategoryOrnaments, new List<MusicIcon>
        {
            new("ornamentTrill", AppStrings.IconTrill, 0xE566, 1.30),
            new("ornamentShortTrill", AppStrings.IconMordent, 0xE56C, 2.96),
            new("ornamentMordent", AppStrings.IconMordentWithLine, 0xE56D, 1.86),
            new("ornamentTurn", AppStrings.IconTurn, 0xE567, 2.11),
            new("ornamentTurnInverted", AppStrings.IconTurnInverted, 0xE568, 2.11),
            new("graceNoteAcciaccaturaStemUp", AppStrings.IconGraceNote, 0xE560, 0.59),
        }),
        new(AppStrings.CategoryBowingPlucking, new List<MusicIcon>
        {
            new("stringsUpBow", AppStrings.IconUpBow, 0xE612, 0.50),
            new("stringsDownBow", AppStrings.IconDownBow, 0xE610, 0.98),
            new("pluckedSnapPizzicatoAbove", AppStrings.IconSnapPizzicato, 0xE631, 0.67),
            new("stringsHarmonic", AppStrings.IconHarmonic, 0xE614, 1.00),
        }),
        new(AppStrings.CategoryPedalMarks, new List<MusicIcon>
        {
            new("keyboardPedalPed", AppStrings.IconPedalDown, 0xE650, 1.81),
            new("keyboardPedalUp", AppStrings.IconPedalUp, 0xE655, 1.00),
            new("keyboardPedalHalf", AppStrings.IconHalfPedal, 0xE656, 1.41),
        }),
        new(AppStrings.CategoryRepeatsNavigation, new List<MusicIcon>
        {
            new("segno", AppStrings.IconSegno, 0xE047, 0.69),
            new("coda", AppStrings.IconCoda, 0xE048, 0.91),
            new("repeatLeft", AppStrings.IconRepeatStart, 0xE040, 0.37),
            new("repeatRight", AppStrings.IconRepeatEnd, 0xE041, 0.37),
        }),
        // mp/mf were assumed unavailable as single glyphs when the original
        // Dynamics category was built (SMuFL documents them as two glyphs
        // composed side by side) - direct inspection of this project's
        // Bravura.otf via fonttools shows real, non-degenerate precomposed
        // outlines at both codepoints, so they're included here instead.
        new(AppStrings.CategoryMoreDynamics, new List<MusicIcon>
        {
            new("dynamicMP", AppStrings.IconMezzoPiano, 0xE52C, 2.03),
            new("dynamicMF", AppStrings.IconMezzoForte, 0xE52D, 1.41),
            new("dynamicPPP", AppStrings.IconPPP, 0xE52A, 2.80),
            new("dynamicFFF", AppStrings.IconFFF, 0xE530, 1.65),
            new("dynamicSforzando", AppStrings.IconSforzando, 0xE524, 0.81),
            new("dynamicRinforzando", AppStrings.IconRinforzando, 0xE523, 1.08),
        }),
        new(AppStrings.CategoryAccidentals, new List<MusicIcon>
        {
            new("accidentalSharp", AppStrings.IconSharp, 0xE262, 0.36),
            new("accidentalFlat", AppStrings.IconFlat, 0xE260, 0.37),
            new("accidentalNatural", AppStrings.IconNatural, 0xE261, 0.25),
            new("accidentalDoubleSharp", AppStrings.IconDoubleSharp, 0xE263, 0.98),
            new("accidentalDoubleFlat", AppStrings.IconDoubleFlat, 0xE264, 0.67),
        }),
        new(AppStrings.CategoryTremoloGlissando, new List<MusicIcon>
        {
            new("tremolo1", AppStrings.IconTremolo1, 0xE220, 1.60),
            new("tremolo2", AppStrings.IconTremolo2, 0xE221, 0.80),
            new("tremolo3", AppStrings.IconTremolo3, 0xE222, 0.54),
            new("glissandoUp", AppStrings.IconGlissandoUp, 0xE585, 0.96),
            new("glissandoDown", AppStrings.IconGlissandoDown, 0xE586, 0.96),
        }),
    };

    public static MusicIcon? FindByKey(string key) =>
        Categories.SelectMany(c => c.Icons).FirstOrDefault(i => i.Key == key);
}
