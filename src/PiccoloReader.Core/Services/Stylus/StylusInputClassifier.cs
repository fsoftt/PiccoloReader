namespace PiccoloReader.Core.Services.Stylus;

/// <summary>Kind of tool that produced a pointer (platform-neutral mirror of Android's tool types).</summary>
public enum PointerToolKind
{
    Unknown,
    Finger,
    Stylus,
    Eraser,
    Mouse
}

/// <summary>What a pointer should do on the page.</summary>
public enum StylusRole
{
    /// <summary>Not a stylus action: normal touch handling (pan, pinch, tap).</summary>
    Finger,
    Draw,
    Erase
}

public static class StylusInputClassifier
{
    /// <summary>
    /// The eraser end of a pen, or a stylus with its side button held, erases;
    /// a plain stylus draws; anything else is handled as a finger.
    /// </summary>
    public static StylusRole Classify(PointerToolKind tool, bool primaryButton, bool secondaryButton) => tool switch
    {
        PointerToolKind.Eraser => StylusRole.Erase,
        PointerToolKind.Stylus => primaryButton || secondaryButton ? StylusRole.Erase : StylusRole.Draw,
        _ => StylusRole.Finger
    };
}
