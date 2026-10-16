namespace PiccoloReader.Views;

/// <summary>
/// Cents scale (-50..+50) with a needle. Green within +-5 cents, yellow within
/// +-15, red beyond. Draws only when invalidated (on a reading change).
/// </summary>
public sealed class TunerNeedleDrawable : IDrawable
{
    public const float MaxCents = 50f;
    public const float InTuneCents = 5f;
    public const float CloseCents = 15f;

    private static readonly Color Green = Color.FromArgb("#2E9E4F");
    private static readonly Color Yellow = Color.FromArgb("#E0A800");
    private static readonly Color Red = Color.FromArgb("#D93636");

    public bool HasPitch { get; set; }

    public float Cents { get; set; }

    public Color TickColor { get; set; } = Colors.Gray;

    public static Color ColorFor(float cents)
    {
        var abs = MathF.Abs(cents);
        return abs <= InTuneCents ? Green : abs <= CloseCents ? Yellow : Red;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        const float margin = 16f;
        var left = dirtyRect.Left + margin;
        var width = dirtyRect.Width - (2 * margin);
        var baseline = dirtyRect.Bottom - 12f;
        var tickTop = dirtyRect.Top + 30f;

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = TickColor;
        canvas.StrokeSize = 1.5f;
        canvas.DrawLine(left, baseline, left + width, baseline);

        for (var c = -50; c <= 50; c += 10)
        {
            var x = left + ((c + MaxCents) / (2 * MaxCents) * width);
            var major = c == 0;
            canvas.StrokeSize = major ? 3f : 1.5f;
            canvas.DrawLine(x, major ? tickTop - 10f : tickTop + 8f, x, baseline);
        }

        if (!HasPitch)
        {
            return;
        }

        var clamped = Math.Clamp(Cents, -MaxCents, MaxCents);
        var needleX = left + ((clamped + MaxCents) / (2 * MaxCents) * width);
        var color = ColorFor(Cents);
        canvas.StrokeColor = color;
        canvas.StrokeSize = 5f;
        canvas.DrawLine(needleX, dirtyRect.Top + 6f, needleX, baseline);
        canvas.FillColor = color;
        canvas.FillCircle(needleX, dirtyRect.Top + 6f, 7f);
    }
}
