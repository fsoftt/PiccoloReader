namespace PiccoloReader.Core.Services.Piano;

/// <summary>A key rectangle in view coordinates. Black keys are drawn over the white ones.</summary>
public readonly record struct PianoKeyRect(int Note, float Left, float Top, float Right, float Bottom, bool IsBlack);

/// <summary>
/// Geometry of a keyboard window over the 88 keys (A0 to C8): key rectangles, hit testing and
/// the clamps for the visible range. Pure maths, shared by the tab and the viewer strip.
/// </summary>
public sealed class PianoKeyboardLayout
{
    public const int LowestNote = 21;
    public const int HighestNote = 108;
    public const int MinWhiteKeys = 7;
    public const int MaxWhiteKeys = 36;

    public const float BlackHeightRatio = 0.62f;
    public const float BlackWidthRatio = 0.58f;

    public const int MinVelocity = 70;
    public const int MaxVelocity = 120;

    private static readonly int[] WhiteNotes = BuildWhiteNotes();

    private readonly PianoKeyRect[] _whites;
    private readonly PianoKeyRect[] _blacks;
    private readonly float _whiteWidth;

    public PianoKeyboardLayout(int firstWhiteKey, int whiteKeyCount, float width, float height)
    {
        WhiteKeyCount = ClampWhiteKeyCount(whiteKeyCount);
        FirstWhiteKey = ClampFirstWhiteKey(firstWhiteKey, WhiteKeyCount);
        Width = width;
        Height = height;
        _whiteWidth = width / WhiteKeyCount;

        var firstIndex = WhiteIndexOf(FirstWhiteKey);
        var blackWidth = _whiteWidth * BlackWidthRatio;
        var blackHeight = height * BlackHeightRatio;
        var blacks = new List<PianoKeyRect>();
        _whites = new PianoKeyRect[WhiteKeyCount];
        for (var i = 0; i < WhiteKeyCount; i++)
        {
            var note = WhiteNotes[firstIndex + i];
            _whites[i] = new PianoKeyRect(note, i * _whiteWidth, 0f, (i + 1) * _whiteWidth, height, false);
            if (i + 1 < WhiteKeyCount && WhiteNotes[firstIndex + i + 1] - note == 2)
            {
                var center = (i + 1) * _whiteWidth;
                blacks.Add(new PianoKeyRect(note + 1, center - (blackWidth / 2f), 0f, center + (blackWidth / 2f), blackHeight, true));
            }
        }

        _blacks = blacks.ToArray();
    }

    public int FirstWhiteKey { get; }

    public int WhiteKeyCount { get; }

    public float Width { get; }

    public float Height { get; }

    public IReadOnlyList<PianoKeyRect> WhiteKeys => _whites;

    public IReadOnlyList<PianoKeyRect> BlackKeys => _blacks;

    /// <summary>Highest white key shown.</summary>
    public int LastWhiteKey => _whites[^1].Note;

    /// <summary>Number of white keys on the full keyboard (52).</summary>
    public static int WhiteKeyTotal => WhiteNotes.Length;

    /// <summary>
    /// Finds the key under a point (black keys first). The velocity grows from
    /// <see cref="MinVelocity"/> at the key's root to <see cref="MaxVelocity"/> at its front edge.
    /// </summary>
    public bool TryHitTest(float x, float y, out int note, out int velocity)
    {
        note = 0;
        velocity = 0;
        if (x < 0f || y < 0f || x >= Width || y >= Height)
        {
            return false;
        }

        foreach (var black in _blacks)
        {
            if (x >= black.Left && x < black.Right && y < black.Bottom)
            {
                note = black.Note;
                velocity = VelocityAt(y, black.Bottom);
                return true;
            }
        }

        var index = Math.Min((int)(x / _whiteWidth), WhiteKeyCount - 1);
        note = _whites[index].Note;
        velocity = VelocityAt(y, Height);
        return true;
    }

    public static bool IsBlack(int note) => (((note % 12) + 12) % 12) is 1 or 3 or 6 or 8 or 10;

    public static int VelocityAt(float y, float keyHeight)
    {
        var fraction = keyHeight <= 0f ? 0f : Math.Clamp(y / keyHeight, 0f, 1f);
        return MinVelocity + (int)MathF.Round((MaxVelocity - MinVelocity) * fraction);
    }

    public static int ClampWhiteKeyCount(int count) => Math.Clamp(count, MinWhiteKeys, MaxWhiteKeys);

    /// <summary>Snaps to a white key and keeps <paramref name="whiteKeyCount"/> keys inside A0 to C8.</summary>
    public static int ClampFirstWhiteKey(int note, int whiteKeyCount)
    {
        var count = ClampWhiteKeyCount(whiteKeyCount);
        var index = WhiteIndexOf(note);
        return WhiteNotes[Math.Clamp(index, 0, WhiteNotes.Length - count)];
    }

    /// <summary>Moves the window by whole octaves (7 white keys), clamped to the keyboard.</summary>
    public static int ShiftOctaves(int firstWhiteKey, int whiteKeyCount, int octaves)
    {
        var count = ClampWhiteKeyCount(whiteKeyCount);
        var index = WhiteIndexOf(ClampFirstWhiteKey(firstWhiteKey, count)) + (7 * octaves);
        return WhiteNotes[Math.Clamp(index, 0, WhiteNotes.Length - count)];
    }

    /// <summary>Position of a note on the 52 white keys; a black key maps to the white one below it.</summary>
    public static int WhiteIndexOf(int note)
    {
        var clamped = Math.Clamp(note, LowestNote, HighestNote);
        var index = Array.BinarySearch(WhiteNotes, clamped);
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>
    /// Mini-map: the window's left edge (in mini-map pixels) to the first white key it stands for.
    /// Each of the 52 white keys takes an equal slice of <paramref name="mapWidth"/>.
    /// </summary>
    public static int FirstWhiteKeyFromMiniMap(float windowLeft, float mapWidth, int whiteKeyCount)
    {
        var cell = mapWidth / WhiteNotes.Length;
        var index = cell <= 0f ? 0 : (int)MathF.Round(windowLeft / cell);
        var count = ClampWhiteKeyCount(whiteKeyCount);
        return WhiteNotes[Math.Clamp(index, 0, WhiteNotes.Length - count)];
    }

    /// <summary>Left edge and width of the visible window on the mini-map.</summary>
    public static (float Left, float Width) MiniMapWindow(int firstWhiteKey, int whiteKeyCount, float mapWidth)
    {
        var cell = mapWidth / WhiteNotes.Length;
        return (WhiteIndexOf(firstWhiteKey) * cell, ClampWhiteKeyCount(whiteKeyCount) * cell);
    }

    /// <summary>Horizontal position of a key's left edge on the mini-map (a black key sits on the line between two whites).</summary>
    public static float MiniMapX(int note, float mapWidth)
    {
        var cell = mapWidth / WhiteNotes.Length;
        var clamped = Math.Clamp(note, LowestNote, HighestNote);
        var index = WhiteIndexOf(clamped);
        return IsBlack(clamped) ? (index + 1) * cell : index * cell;
    }

    private static int[] BuildWhiteNotes()
    {
        var notes = new List<int>();
        for (var note = LowestNote; note <= HighestNote; note++)
        {
            if (!IsBlack(note))
            {
                notes.Add(note);
            }
        }

        return notes.ToArray();
    }
}
