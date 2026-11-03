using PiccoloReader.Core.Services.Piano;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PiccoloReader.Views;

/// <summary>
/// Thin overview of the 88 keys with a window for the visible range. Drag the window or tap to
/// move it; pressed keys and the instrument range show here too. Redraws only on state change.
/// </summary>
public class PianoMiniMapView : SKCanvasView
{
    private const float BlackHeightRatio = 0.6f;
    private const float BlackWidthRatio = 0.6f;
    private const float BracketHeightDp = 4f;

    private readonly bool[] _pressed = new bool[PianoKeyboardLayout.HighestNote + 1];
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    private int _firstWhiteKey = 60;
    private int _whiteKeyCount = 10;
    private int _lowNote = PianoKeyboardLayout.LowestNote;
    private int _highNote = PianoKeyboardLayout.HighestNote;
    private bool _dark;
    private bool _subscribed;
    private bool _dragging;
    private float _grabOffset;

    public PianoMiniMapView()
    {
        EnableTouchEvents = true;
        Touch += OnTouch;
        PaintSurface += OnPaintSurface;
        Loaded += (_, _) => SubscribeTheme();
        Unloaded += (_, _) => UnsubscribeTheme();
    }

    /// <summary>The user moved the window; the value is the new first white key.</summary>
    public event EventHandler<int>? FirstWhiteKeyRequested;

    public void SetWindow(int firstWhiteKey, int whiteKeyCount)
    {
        if (_firstWhiteKey == firstWhiteKey && _whiteKeyCount == whiteKeyCount)
        {
            return;
        }

        _firstWhiteKey = firstWhiteKey;
        _whiteKeyCount = whiteKeyCount;
        InvalidateSurface();
    }

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

    public void SetPressed(int note, bool pressed)
    {
        if (note < 0 || note >= _pressed.Length || _pressed[note] == pressed)
        {
            return;
        }

        _pressed[note] = pressed;
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
        _dark = Application.Current.RequestedTheme == AppTheme.Dark;
        InvalidateSurface();
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
        _dark = e.RequestedTheme == AppTheme.Dark;
        InvalidateSurface();
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
        var mapWidth = (float)CanvasSize.Width;
        if (mapWidth <= 0f)
        {
            return;
        }

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                {
                    _dragging = true;
                    var (left, width) = PianoKeyboardLayout.MiniMapWindow(_firstWhiteKey, _whiteKeyCount, mapWidth);

                    // Grabbing the window keeps its offset under the finger; a tap elsewhere centres it there.
                    _grabOffset = e.Location.X >= left && e.Location.X <= left + width ? e.Location.X - left : width / 2f;
                    Request(e.Location.X, mapWidth);
                    break;
                }

            case SKTouchAction.Moved when _dragging:
                Request(e.Location.X, mapWidth);
                break;
            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                _dragging = false;
                break;
        }
    }

    private void Request(float x, float mapWidth)
    {
        var first = PianoKeyboardLayout.FirstWhiteKeyFromMiniMap(x - _grabOffset, mapWidth, _whiteKeyCount);
        if (first != _firstWhiteKey)
        {
            FirstWhiteKeyRequested?.Invoke(this, first);
        }
    }

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
        var primary = _dark ? new SKColor(0x8B, 0x6C, 0xFF) : new SKColor(0x5B, 0x3F, 0xD9);
        var whiteKey = _dark ? new SKColor(0xB8, 0xB8, 0xC6) : new SKColor(0xFF, 0xFF, 0xFF);
        var blackKey = _dark ? new SKColor(0x2A, 0x2A, 0x38) : new SKColor(0x30, 0x30, 0x3C);
        var outline = _dark ? new SKColor(0x2E, 0x2E, 0x3E) : new SKColor(0xE3, 0xE3, 0xEC);
        var bracketHeight = BracketHeightDp * density;
        var keysBottom = height - bracketHeight - (density * 2f);
        var cell = (float)width / PianoKeyboardLayout.WhiteKeyTotal;
        var gap = Math.Max(1f, density * 0.5f);

        for (var note = PianoKeyboardLayout.LowestNote; note <= PianoKeyboardLayout.HighestNote; note++)
        {
            if (PianoKeyboardLayout.IsBlack(note))
            {
                continue;
            }

            var left = PianoKeyboardLayout.MiniMapX(note, width);
            _fill.Color = _pressed[note] ? primary : whiteKey;
            canvas.DrawRect(left + (gap / 2f), 0f, cell - gap, keysBottom, _fill);
        }

        for (var note = PianoKeyboardLayout.LowestNote; note <= PianoKeyboardLayout.HighestNote; note++)
        {
            if (!PianoKeyboardLayout.IsBlack(note))
            {
                continue;
            }

            var center = PianoKeyboardLayout.MiniMapX(note, width);
            var blackWidth = cell * BlackWidthRatio;
            _fill.Color = _pressed[note] ? primary : blackKey;
            canvas.DrawRect(center - (blackWidth / 2f), 0f, blackWidth, keysBottom * BlackHeightRatio, _fill);
        }

        // Frame around the whole strip, then the visible window on top.
        _stroke.StrokeWidth = density;
        _stroke.Color = outline;
        canvas.DrawRect(density / 2f, density / 2f, width - density, keysBottom - density, _stroke);

        var (windowLeft, windowWidth) = PianoKeyboardLayout.MiniMapWindow(_firstWhiteKey, _whiteKeyCount, width);
        _fill.Color = primary.WithAlpha(56);
        canvas.DrawRect(windowLeft, 0f, windowWidth, keysBottom, _fill);
        _stroke.StrokeWidth = 2f * density;
        _stroke.Color = primary;
        canvas.DrawRect(windowLeft + density, density, windowWidth - (2f * density), keysBottom - (2f * density), _stroke);

        // Bracket under the strip: where the instrument's real range lies.
        var from = PianoKeyboardLayout.MiniMapX(_lowNote, width);
        var to = PianoKeyboardLayout.MiniMapX(_highNote, width) + cell;
        _stroke.StrokeWidth = 1.5f * density;
        _stroke.Color = primary.WithAlpha(190);
        var y = height - (density * 1.5f);
        canvas.DrawLine(from, y, to, y, _stroke);
        canvas.DrawLine(from, y - bracketHeight, from, y, _stroke);
        canvas.DrawLine(to, y - bracketHeight, to, y, _stroke);
    }
}
