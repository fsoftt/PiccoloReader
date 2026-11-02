using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

public sealed class PianoKeyEventArgs(int note, int velocity) : EventArgs
{
    public int Note { get; } = note;

    public int Velocity { get; } = velocity;
}

/// <summary>
/// Playable keyboard drawn with Skia. It redraws only when something changes (a key goes down or
/// up, the range, size, labels or theme change), with paints, shaders and the layout cached. The
/// touch handler maps each finger to a key (chords, glissando) and only raises events: the owner
/// forwards them to the audio engine, which just enqueues commands.
/// </summary>
public class PianoKeyboardView : SKCanvasView
{
    private const float GapDp = 1f;
    private const float CornerDp = 4f;
    private const float PressOffsetDp = 1f;
    private const float LabelSizeDp = 12f;
    private const float MinLabelKeyWidthDp = 16f;

    private readonly Dictionary<long, int> _pointers = new();
    private readonly int[] _pressCounts = new int[PianoKeyboardLayout.HighestNote + 1];
    private readonly string[] _fullLabels = new string[PianoKeyboardLayout.HighestNote + 1];
    private readonly string[] _shortLabels = new string[PianoKeyboardLayout.HighestNote + 1];
    private readonly SKPoint[] _bottomRadii = new SKPoint[4];
    private readonly SKRoundRect _roundRect = new();
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _dimPaint = new();
    private readonly SKPaint _textPaint = new() { IsAntialias = true };
    private readonly SKFont _labelFont = new() { Embolden = false };
    private readonly SKFont _labelBoldFont = new() { Embolden = true };
    private readonly SKFont _statusFont = new() { Embolden = true };

    private PianoKeyboardLayout? _layout;
    private SKShader? _whiteShader;
    private SKShader? _blackShader;
    private int _shaderHeight = -1;
    private bool _shaderDark;
    private bool _dark;
    private bool _subscribed;
    private int _firstWhiteKey = 60;
    private int _whiteKeyCount = 10;
    private PianoLabels _labels = PianoLabels.COnly;
    private NoteNaming _naming = NoteNaming.Letter;
    private int _lowNote = PianoKeyboardLayout.LowestNote;
    private int _highNote = PianoKeyboardLayout.HighestNote;
    private bool _isLoading;
    private string _statusText = string.Empty;

    public PianoKeyboardView()
    {
        EnableTouchEvents = true;
        _dimPaint.Color = new SKColor(255, 255, 255, 102);
        _bottomRadii[0] = _bottomRadii[1] = SKPoint.Empty;
        BuildLabels();
        Touch += OnTouch;
        PaintSurface += OnPaintSurface;
        Loaded += (_, _) => SubscribeTheme();
        Unloaded += (_, _) => UnsubscribeTheme();
    }

    /// <summary>A key went down under a finger (also when a glissando slides onto it).</summary>
    public event EventHandler<PianoKeyEventArgs>? KeyPressed;

    /// <summary>The last finger left a key.</summary>
    public event EventHandler<PianoKeyEventArgs>? KeyReleased;

    public int FirstWhiteKey
    {
        get => _firstWhiteKey;
        set => SetRange(value, _whiteKeyCount);
    }

    public int WhiteKeyCount
    {
        get => _whiteKeyCount;
        set => SetRange(_firstWhiteKey, value);
    }

    public PianoLabels Labels
    {
        get => _labels;
        set => Set(ref _labels, value);
    }

    /// <summary>Resolved naming (letters or solfege) used on the key labels.</summary>
    public NoteNaming Naming
    {
        get => _naming;
        set
        {
            if (_naming == value)
            {
                return;
            }

            _naming = value;
            BuildLabels();
            InvalidateSurface();
        }
    }

    /// <summary>The sounding range of the instrument; keys outside it are drawn muted.</summary>
    public void SetInstrumentRange(int lowNote, int highNote)
    {
        if (_lowNote == lowNote && _highNote == highNote)
        {
            return;
        }

        _lowNote = lowNote;
        _highNote = highNote;
        InvalidateSurface();
    }

    /// <summary>Keys are dimmed and ignore touches; <see cref="StatusText"/> is drawn over them.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            if (value)
            {
                ReleaseAll();
            }

            InvalidateSurface();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>Lifts every finger's key (screen hidden, app in the background, range changed).</summary>
    public void ReleaseAll()
    {
        if (_pointers.Count == 0)
        {
            return;
        }

        var held = new List<int>();
        for (var note = 0; note < _pressCounts.Length; note++)
        {
            if (_pressCounts[note] > 0)
            {
                held.Add(note);
                _pressCounts[note] = 0;
            }
        }

        _pointers.Clear();
        foreach (var note in held)
        {
            KeyReleased?.Invoke(this, new PianoKeyEventArgs(note, 0));
        }

        InvalidateSurface();
    }

    private void SetRange(int first, int count)
    {
        var clampedCount = PianoKeyboardLayout.ClampWhiteKeyCount(count);
        var clampedFirst = PianoKeyboardLayout.ClampFirstWhiteKey(first, clampedCount);
        if (clampedFirst == _firstWhiteKey && clampedCount == _whiteKeyCount)
        {
            return;
        }

        // Fingers stay on screen positions, not notes: lift them rather than leave a note stuck.
        ReleaseAll();
        _firstWhiteKey = clampedFirst;
        _whiteKeyCount = clampedCount;
        InvalidateSurface();
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        InvalidateSurface();
    }

    private void SubscribeTheme()
    {
        if (_subscribed || Application.Current is null)
        {
            return;
        }

        _subscribed = true;
        Application.Current.RequestedThemeChanged += OnThemeChanged;
        OnThemeChanged(null, null!);
    }

    private void UnsubscribeTheme()
    {
        if (!_subscribed || Application.Current is null)
        {
            return;
        }

        _subscribed = false;
        Application.Current.RequestedThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        _dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        InvalidateSurface();
    }

    private void BuildLabels()
    {
        for (var note = PianoKeyboardLayout.LowestNote; note <= PianoKeyboardLayout.HighestNote; note++)
        {
            var name = NoteNames.Get(note % 12, _naming);
            _shortLabels[note] = name;
            _fullLabels[note] = name + ((note / 12) - 1);
        }
    }

    // Touch -----------------------------------------------------------------

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
        if (_isLoading || _layout is null)
        {
            return;
        }

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                Press(e.Id, e.Location);
                break;
            case SKTouchAction.Moved when _pointers.ContainsKey(e.Id):
                MoveTo(e.Id, e.Location);
                break;
            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                Lift(e.Id);
                break;
        }
    }

    private void Press(long id, SKPoint location)
    {
        Lift(id);
        _pointers[id] = -1;
        MoveTo(id, location);
    }

    private void MoveTo(long id, SKPoint location)
    {
        var velocity = PianoKeyboardLayout.MinVelocity;
        var note = -1;
        var hit = _layout is not null && _layout.TryHitTest(location.X, location.Y, out note, out velocity);
        var target = hit ? note : -1;
        if (_pointers.TryGetValue(id, out var current) && current == target)
        {
            return;
        }

        if (current >= 0)
        {
            ReleaseKey(current);
        }

        _pointers[id] = target;
        if (hit)
        {
            _pressCounts[target]++;
            if (_pressCounts[target] == 1)
            {
                KeyPressed?.Invoke(this, new PianoKeyEventArgs(target, velocity));
            }
        }

        InvalidateSurface();
    }

    private void Lift(long id)
    {
        if (!_pointers.Remove(id, out var note))
        {
            return;
        }

        if (note >= 0)
        {
            ReleaseKey(note);
        }

        InvalidateSurface();
    }

    private void ReleaseKey(int note)
    {
        _pressCounts[note]--;
        if (_pressCounts[note] == 0)
        {
            KeyReleased?.Invoke(this, new PianoKeyEventArgs(note, 0));
        }
    }

    // Drawing ---------------------------------------------------------------

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var width = e.Info.Width;
        var height = e.Info.Height;
        if (width <= 0 || height <= 0 || Width <= 0)
        {
            return;
        }

        var density = (float)(width / Width);
        if (_layout is null || _layout.Width != width || _layout.Height != height
            || _layout.FirstWhiteKey != _firstWhiteKey || _layout.WhiteKeyCount != _whiteKeyCount)
        {
            _layout = new PianoKeyboardLayout(_firstWhiteKey, _whiteKeyCount, width, height);
        }

        EnsureShaders(height);
        var palette = Palette.For(_dark);
        var gap = GapDp * density;
        var radius = CornerDp * density;
        var press = PressOffsetDp * density;
        _bottomRadii[2] = _bottomRadii[3] = new SKPoint(radius, radius);

        if (_isLoading)
        {
            canvas.SaveLayer(_dimPaint);
        }

        foreach (var key in _layout.WhiteKeys)
        {
            DrawWhiteKey(canvas, key, palette, gap, press, density);
        }

        foreach (var key in _layout.BlackKeys)
        {
            DrawBlackKey(canvas, key, palette, gap, press, density);
        }

        if (_isLoading)
        {
            canvas.Restore();
            DrawStatus(canvas, width, height, density, palette);
        }
    }

    private void DrawWhiteKey(SKCanvas canvas, PianoKeyRect key, Palette palette, float gap, float press, float density)
    {
        var pressed = _pressCounts[key.Note] > 0;
        var rect = new SKRect(key.Left + (gap / 2f), key.Top, key.Right - (gap / 2f), key.Bottom);
        if (pressed)
        {
            rect.Offset(0f, press);
        }

        _roundRect.SetRectRadii(rect, _bottomRadii);
        _fill.Shader = pressed ? null : _whiteShader;
        _fill.Color = pressed ? palette.WhitePressed : SKColors.White;
        canvas.DrawRoundRect(_roundRect, _fill);
        _fill.Shader = null;

        if (!_isLoading && !IsInRange(key.Note))
        {
            _fill.Color = palette.WhiteMute;
            canvas.DrawRoundRect(_roundRect, _fill);
        }

        DrawLabel(canvas, key, rect, palette, density);
    }

    private void DrawBlackKey(SKCanvas canvas, PianoKeyRect key, Palette palette, float gap, float press, float density)
    {
        var pressed = _pressCounts[key.Note] > 0;
        var rect = new SKRect(key.Left, key.Top, key.Right, key.Bottom);
        if (pressed)
        {
            rect.Offset(0f, press);
        }

        var radius = CornerDp * density * 0.75f;
        _bottomRadii[2] = _bottomRadii[3] = new SKPoint(radius, radius);
        _roundRect.SetRectRadii(rect, _bottomRadii);
        _fill.Shader = pressed ? null : _blackShader;
        _fill.Color = pressed ? palette.BlackPressed : SKColors.Black;
        canvas.DrawRoundRect(_roundRect, _fill);
        _fill.Shader = null;

        if (!pressed)
        {
            // Thin top highlight, removed while the key is down.
            _fill.Color = palette.BlackHighlight;
            var inset = density * 2.5f;
            canvas.DrawRect(rect.Left + inset, rect.Top + (density * 1.5f), rect.Width - (2f * inset), density * 1.5f, _fill);
        }

        if (!_isLoading && !IsInRange(key.Note))
        {
            _fill.Color = palette.BlackMute;
            canvas.DrawRoundRect(_roundRect, _fill);
        }
    }

    private void DrawLabel(SKCanvas canvas, PianoKeyRect key, SKRect rect, Palette palette, float density)
    {
        if (_labels == PianoLabels.Off)
        {
            return;
        }

        var keyWidth = rect.Width;
        if (keyWidth < MinLabelKeyWidthDp * density)
        {
            return;
        }

        var isC = key.Note % 12 == 0;
        if (_labels == PianoLabels.COnly && !isC)
        {
            return;
        }

        var font = isC ? _labelBoldFont : _labelFont;
        font.Size = MathF.Min(LabelSizeDp * density, keyWidth * 0.38f);
        _textPaint.Color = IsInRange(key.Note) ? palette.Label : palette.LabelMuted;
        var text = isC ? _fullLabels[key.Note] : _shortLabels[key.Note];
        canvas.DrawText(text, rect.MidX, rect.Bottom - (8f * density), SKTextAlign.Center, font, _textPaint);
    }

    private void DrawStatus(SKCanvas canvas, int width, int height, float density, Palette palette)
    {
        if (string.IsNullOrEmpty(_statusText))
        {
            return;
        }

        _statusFont.Size = 15f * density;
        _textPaint.Color = palette.Label;
        canvas.DrawText(_statusText, width / 2f, (height / 2f) + (5f * density), SKTextAlign.Center, _statusFont, _textPaint);
    }

    private bool IsInRange(int note) => note >= _lowNote && note <= _highNote;

    // The gradients span the whole key height, so one shader per size and theme serves every key.
    private void EnsureShaders(int height)
    {
        if (_whiteShader is not null && _shaderHeight == height && _shaderDark == _dark)
        {
            return;
        }

        _whiteShader?.Dispose();
        _blackShader?.Dispose();
        var palette = Palette.For(_dark);
        _whiteShader = SKShader.CreateLinearGradient(
            new SKPoint(0f, 0f), new SKPoint(0f, height), [palette.WhiteTop, palette.WhiteBottom], SKShaderTileMode.Clamp);
        var blackHeight = height * PianoKeyboardLayout.BlackHeightRatio;
        _blackShader = SKShader.CreateLinearGradient(
            new SKPoint(0f, 0f), new SKPoint(0f, blackHeight), [palette.BlackTop, palette.BlackBottom], SKShaderTileMode.Clamp);
        _shaderHeight = height;
        _shaderDark = _dark;
    }

    // Visual spec colors: Primary light #5B3FD9 / dark #8B6CFF, off-white keys in the dark theme.
    private readonly record struct Palette(
        SKColor WhiteTop,
        SKColor WhiteBottom,
        SKColor WhitePressed,
        SKColor WhiteMute,
        SKColor BlackTop,
        SKColor BlackBottom,
        SKColor BlackPressed,
        SKColor BlackMute,
        SKColor BlackHighlight,
        SKColor Label,
        SKColor LabelMuted)
    {
        private static readonly Palette Light = new(
            WhiteTop: new SKColor(0xFF, 0xFF, 0xFF),
            WhiteBottom: new SKColor(0xEC, 0xEC, 0xF3),
            WhitePressed: new SKColor(0xB4, 0xA6, 0xF0),
            WhiteMute: new SKColor(0xF6, 0xF6, 0xFA, 150),
            BlackTop: new SKColor(0x30, 0x30, 0x3C),
            BlackBottom: new SKColor(0x10, 0x10, 0x18),
            BlackPressed: new SKColor(0x5B, 0x3F, 0xD9),
            BlackMute: new SKColor(0xF6, 0xF6, 0xFA, 130),
            BlackHighlight: new SKColor(0xFF, 0xFF, 0xFF, 70),
            Label: new SKColor(0x5C, 0x5C, 0x70),
            LabelMuted: new SKColor(0x5C, 0x5C, 0x70, 90));

        private static readonly Palette Dark = new(
            WhiteTop: new SKColor(0xE6, 0xE6, 0xEE),
            WhiteBottom: new SKColor(0xCB, 0xCB, 0xD8),
            WhitePressed: new SKColor(0x8B, 0x7C, 0xD8),
            WhiteMute: new SKColor(0x0E, 0x0E, 0x16, 140),
            BlackTop: new SKColor(0x3C, 0x3C, 0x4C),
            BlackBottom: new SKColor(0x1A, 0x1A, 0x26),
            BlackPressed: new SKColor(0x8B, 0x6C, 0xFF),
            BlackMute: new SKColor(0x0E, 0x0E, 0x16, 130),
            BlackHighlight: new SKColor(0xFF, 0xFF, 0xFF, 50),
            Label: new SKColor(0x4A, 0x4A, 0x5C),
            LabelMuted: new SKColor(0x4A, 0x4A, 0x5C, 90));

        public static Palette For(bool dark) => dark ? Dark : Light;
    }
}
