namespace PiccoloReader.Core.Services;

public record MusicIcon(string Key, string DisplayName, int Codepoint, double AspectRatio);

public record MusicIconCategory(string Name, IReadOnlyList<MusicIcon> Icons);

public static class MusicIconCatalog
{
    public static IReadOnlyList<MusicIconCategory> Categories { get; } = new List<MusicIconCategory>
    {
        new("Dynamics", new List<MusicIcon>
        {
            new("dynamicPP", "pp", 0xE52B, 1.95),
            new("dynamicPiano", "p", 0xE520, 1.09),
            new("dynamicForte", "f", 0xE522, 0.85),
            new("dynamicFF", "ff", 0xE52F, 1.25),
            new("dynamicFortePiano", "fp", 0xE534, 1.28),
        }),
        new("Articulations", new List<MusicIcon>
        {
            new("articStaccatoAbove", "Staccato", 0xE4A2, 1.00),
            new("articAccentAbove", "Accent", 0xE4A0, 1.39),
            new("articTenutoAbove", "Tenuto", 0xE4A4, 7.06),
            new("articMarcatoAbove", "Marcato", 0xE4AC, 0.93),
        }),
        new("Fermata & Breath", new List<MusicIcon>
        {
            new("fermataAbove", "Fermata", 0xE4C0, 1.81),
            new("breathMarkComma", "Breath mark", 0xE4CE, 0.61),
        }),
        new("Hairpins", new List<MusicIcon>
        {
            new("dynamicCrescendoHairpin", "Crescendo", 0xE53E, 2.78),
            new("dynamicDiminuendoHairpin", "Decrescendo", 0xE53F, 2.78),
        }),
    };

    public static MusicIcon? FindByKey(string key) =>
        Categories.SelectMany(c => c.Icons).FirstOrDefault(i => i.Key == key);
}
