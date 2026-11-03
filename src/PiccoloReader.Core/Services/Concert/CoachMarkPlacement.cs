namespace PiccoloReader.Core.Services.Concert;

public readonly record struct CoachRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

public readonly record struct CoachInsets(double Left, double Top, double Right, double Bottom);

/// <param name="X">Left edge of the bubble.</param>
/// <param name="Y">Top edge of the bubble.</param>
/// <param name="BelowTarget">The bubble sits under the target (false: above it, beside it or no target).</param>
/// <param name="Width">Width to give the bubble; 0 keeps the width it was asked with.</param>
public readonly record struct CoachBubblePlacement(double X, double Y, bool BelowTarget, double Width = 0);

/// <summary>Position math of a coach mark callout bubble (pure, no UI types).</summary>
public static class CoachMarkPlacement
{
    /// <summary>
    /// Places a bubble next to <paramref name="target"/> (below it when it fits, else above,
    /// else on the side with more room), centred on it horizontally and always kept inside
    /// the screen minus <paramref name="insets"/> and <paramref name="margin"/>.
    /// Without a target the bubble is centred on screen. <paramref name="gestureReserve"/> keeps that
    /// much extra room under the target free for a gesture hint (the hand) when the bubble goes below.
    /// </summary>
    public static CoachBubblePlacement PlaceBubble(
        double screenWidth,
        double screenHeight,
        CoachRect? target,
        double bubbleWidth,
        double bubbleHeight,
        CoachInsets insets,
        double margin = 16,
        double gap = 12,
        double gestureReserve = 0)
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
        var spaceBelow = screenHeight - insets.Bottom - margin - (t.Bottom + gap + gestureReserve);
        var spaceAbove = t.Y - gap - (insets.Top + margin);

        var below = spaceBelow >= bubbleHeight || (spaceAbove < bubbleHeight && spaceBelow >= spaceAbove);
        var y = below ? t.Bottom + gap + gestureReserve : t.Y - gap - bubbleHeight;
        return new CoachBubblePlacement(x, Math.Clamp(y, minY, maxY), below, bubbleWidth);
    }

    /// <summary>
    /// For a target so tall that the bubble fits neither above nor below it (a tap zone spanning
    /// the screen): places the bubble in the free area beside it, on the roomier side, vertically
    /// centred, with its width limited to that area. Null when the bubble fits above or below
    /// (use <see cref="PlaceBubble"/>) or neither side has at least <paramref name="minWidth"/>.
    /// </summary>
    /// <param name="measureHeight">Bubble height for a given width (text wraps more when narrower).</param>
    public static CoachBubblePlacement? PlaceBubbleBeside(
        double screenWidth,
        double screenHeight,
        CoachRect target,
        double bubbleWidth,
        Func<double, double> measureHeight,
        CoachInsets insets,
        double margin = 16,
        double gap = 12,
        double minWidth = 160)
    {
        var minY = insets.Top + margin;
        var availableHeight = screenHeight - insets.Bottom - margin - minY;
        var fullHeight = measureHeight(bubbleWidth);
        var spaceBelow = screenHeight - insets.Bottom - margin - (target.Bottom + gap);
        var spaceAbove = target.Y - gap - minY;
        if (spaceBelow >= fullHeight || spaceAbove >= fullHeight)
        {
            return null;
        }

        var leftSpace = target.X - gap - (insets.Left + margin);
        var rightSpace = screenWidth - insets.Right - margin - (target.Right + gap);
        var onRight = rightSpace >= leftSpace;
        var space = onRight ? rightSpace : leftSpace;
        if (space < minWidth)
        {
            return null;
        }

        var width = Math.Min(bubbleWidth, space);
        var height = Math.Min(measureHeight(width), Math.Max(0, availableHeight));
        var x = onRight ? target.Right + gap : target.X - gap - width;
        var y = Math.Clamp((screenHeight - height) / 2, minY, Math.Max(minY, screenHeight - insets.Bottom - margin - height));
        return new CoachBubblePlacement(x, y, false, width);
    }
}
