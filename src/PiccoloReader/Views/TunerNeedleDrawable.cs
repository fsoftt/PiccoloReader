namespace PiccoloReader.Views;

/// <summary>
/// Cents scale (-50..+50) with a needle. Green within +-5 cents, yellow within
/// +-15, red beyond. Draws only when invalidated; <see cref="SetReading"/> says
/// whether a new reading moved the needle enough to need a redraw. Draw
/// allocates nothing (static colors, no paths).
/// </summary>
public sealed class TunerNeedleDrawable : IDrawable
{
    public const float MaxCents = 50f;
    public const float InTuneCents = 5f;
    public const float CloseCents = 15f;

    private const float Margin = 16f;

    private static readonly Color Green = Color.FromArgb("#2E9E4F");
    private static readonly Color Yellow = Color.FromArgb("#E0A800");
    private static readonly Color Red = Color.FromArgb("#D93636");

    private float _drawnX = float.NaN;
    private int _drawnBand = -1;
    private bool _drawnHasPitch;

    public bool HasPitch { get; private set; }

    public float Cents { get; private set; }

    public Color TickColor { get; set; } = Colors.Gray;

    public static Color ColorFor(float cents)
    {
        var abs = MathF.Abs(cents);
        return abs <= InTuneCents ? Green : abs <= CloseCents ? Yellow : Red;
    }

    private static int BandFor(float cents)
    {
        var abs = MathF.Abs(cents);
        return abs <= InTuneCents ? 0 : abs <= CloseCents ? 1 : 2;
    }

    private static float NeedleX(float cents, float width)
    {
        var clamped = Math.Clamp(cents, -MaxCents, MaxCents);
        return Margin + ((clamped + MaxCents) / (2 * MaxCents) * (width - (2 * Margin)));
    }

    /// <summary>
    /// Stores the reading and returns true when the view must be invalidated:
    /// pitch appeared or vanished, the color band changed, or the needle moved
    /// at least one physical pixel since the last drawn position.
    /// </summary>
    public bool SetReading(bool hasPitch, float cents, float width, float density)
    {
        HasPitch = hasPitch;
        Cents = cents;

        if (!hasPitch)
        {
            return _drawnHasPitch || float.IsNaN(_drawnX);
        }

        if (!_drawnHasPitch || width <= 0f || BandFor(cents) != _drawnBand)
        {
            return true;
        }

        return MathF.Abs(NeedleX(cents, width) - _drawnX) * density >= 1f;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var left = dirtyRect.Left + Margin;
        var width = dirtyRect.Width - (2 * Margin);
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

        _drawnHasPitch = HasPitch;
        _drawnX = float.IsNaN(_drawnX) ? 0f : _drawnX;
        if (!HasPitch)
        {
            return;
        }

        var needleX = NeedleX(Cents, dirtyRect.Width);
        _drawnX = needleX;
        _drawnBand = BandFor(Cents);
        var color = ColorFor(Cents);
        canvas.StrokeColor = color;
        canvas.StrokeSize = 5f;
        canvas.DrawLine(needleX, dirtyRect.Top + 6f, needleX, baseline);
        canvas.FillColor = color;
        canvas.FillCircle(needleX, dirtyRect.Top + 6f, 7f);
    }
}
