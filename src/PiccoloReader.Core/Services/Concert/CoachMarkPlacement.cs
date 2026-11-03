namespace PiccoloReader.Core.Services.Concert;

public readonly record struct CoachRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public readonly record struct CoachInsets(double Left, double Top, double Right, double Bottom);

/// <param name="X">Left edge of the bubble.</param>
/// <param name="Y">Top edge of the bubble.</param>
/// <param name="BelowTarget">The bubble sits under the target (false: above it or no target).</param>
public readonly record struct CoachBubblePlacement(double X, double Y, bool BelowTarget);

/// <summary>Position math of a coach mark callout bubble (pure, no UI types).</summary>
public static class CoachMarkPlacement
{
    /// <summary>
    /// Places a bubble next to <paramref name="target"/> (below it when it fits, else above,
    /// else on the side with more room), centred on it horizontally and always kept inside
    /// the screen minus <paramref name="insets"/> and <paramref name="margin"/>.
    /// Without a target the bubble is centred on screen.
    /// </summary>
    public static CoachBubblePlacement PlaceBubble(
        double screenWidth,
        double screenHeight,
        CoachRect? target,
        double bubbleWidth,
        double bubbleHeight,
        CoachInsets insets,
        double margin = 16,
        double gap = 12)
    {
        var minX = insets.Left + margin;
        var maxX = Math.Max(minX, screenWidth - insets.Right - margin - bubbleWidth);
        var minY = insets.Top + margin;
        var maxY = Math.Max(minY, screenHeight - insets.Bottom - margin - bubbleHeight);

        if (target is not { } t)
        {
            return new CoachBubblePlacement(
                Math.Clamp((screenWidth - bubbleWidth) / 2, minX, maxX),
                Math.Clamp((screenHeight - bubbleHeight) / 2, minY, maxY),
                false);
        }

        var x = Math.Clamp(t.X + t.Width / 2 - bubbleWidth / 2, minX, maxX);
        var spaceBelow = screenHeight - insets.Bottom - margin - (t.Bottom + gap);
        var spaceAbove = t.Y - gap - (insets.Top + margin);

        var below = spaceBelow >= bubbleHeight || (spaceAbove < bubbleHeight && spaceBelow >= spaceAbove);
        var y = below ? t.Bottom + gap : t.Y - gap - bubbleHeight;
        return new CoachBubblePlacement(x, Math.Clamp(y, minY, maxY), below);
    }
}
