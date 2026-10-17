using Android.Content;
using Color = Android.Graphics.Color;
using Android.Views;
using View = Android.Views.View;
using Android.Widget;
using Microsoft.Maui.Handlers;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Views;

namespace PiccoloReader.Platforms.Android;

public class TunerStripHandler : ViewHandler<TunerStripView, TunerStripNativeView>
{
    public static readonly IPropertyMapper<TunerStripView, TunerStripHandler> Mapper =
        new PropertyMapper<TunerStripView, TunerStripHandler>(ViewHandler.ViewMapper);

    public TunerStripHandler() : base(Mapper)
    {
    }

    protected override TunerStripNativeView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(TunerStripNativeView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.HalfTrackChanged = VirtualView.HalfTrackChanged;
        VirtualView.VerticalChanged = platformView.SetVertical;
        platformView.SetVertical(VirtualView.IsVertical);
        VirtualView.Surface = platformView;
    }

    protected override void DisconnectHandler(TunerStripNativeView platformView)
    {
        platformView.HalfTrackChanged = null;
        VirtualView.VerticalChanged = null;
        VirtualView.Surface = null;
        base.DisconnectHandler(platformView);
    }
}

/// <summary>
/// A faint track with a fixed center tick (only while the strip is on; transparent
/// when off) and a colored fill that grows from the center toward the deviation.
/// The fill is a 1 px wide child scaled horizontally from its left edge (negative
/// scale mirrors it to the left), so per reading only ScaleX, TranslationX,
/// background color and visibility change: no layout pass, no invalidation of
/// anything else. Invisible (not Gone) keeps the layout stable. In vertical mode
/// (strip on the left or right edge) the same applies on the Y axis, with sharp
/// growing upward and flat downward.
/// </summary>
public class TunerStripNativeView : FrameLayout, ITunerStripView
{
    // Total width of the centered "in tune" segment.
    private const float InTuneSegmentDp = 32f;
    private const float TickWidthDp = 2f;

    private static readonly Color TrackColor = Color.Argb(0x40, 0x80, 0x80, 0x80);
    private static readonly Color TickColor = Color.Argb(0xA0, 0x80, 0x80, 0x80);
    private static readonly Color InTuneColor = Color.Argb(0xFF, 0x4C, 0xAF, 0x50);
    private static readonly Color CloseColor = Color.Argb(0xFF, 0xFF, 0xC1, 0x07);
    private static readonly Color FarColor = Color.Argb(0xFF, 0xF4, 0x43, 0x36);

    private readonly View _fill;
    private readonly View _tick;
    private readonly float _inTuneSegmentPx;
    private readonly int _tickPx;
    private float _centerPx;
    private bool _vertical;
    private bool _lastVisible;
    private int _lastFillPx;
    private TunerStripBucket? _bucket;

    public TunerStripNativeView(Context? context) : base(context!)
    {
        var density = context!.Resources!.DisplayMetrics!.Density;
        _inTuneSegmentPx = InTuneSegmentDp * density;
        _tickPx = (int)Math.Round(TickWidthDp * density);
        _fill = new View(context) { Visibility = ViewStates.Invisible, PivotX = 0f };
        AddView(_fill, new LayoutParams(1, ViewGroup.LayoutParams.MatchParent, GravityFlags.Start));
        _tick = new View(context) { Visibility = ViewStates.Invisible };
        _tick.SetBackgroundColor(TickColor);
        AddView(_tick, new LayoutParams(_tickPx, ViewGroup.LayoutParams.MatchParent, GravityFlags.CenterHorizontal));
    }

    /// <summary>Switches between a horizontal (top/bottom edge) and vertical (left/right edge) strip.</summary>
    public void SetVertical(bool vertical)
    {
        if (_vertical == vertical)
        {
            return;
        }

        _vertical = vertical;
        _fill.ScaleX = 1f;
        _fill.ScaleY = 1f;
        _fill.TranslationX = 0f;
        _fill.TranslationY = 0f;
        _fill.PivotX = vertical ? _fill.PivotX : 0f;
        _fill.PivotY = vertical ? 0f : _fill.PivotY;
        _fill.LayoutParameters = vertical
            ? new LayoutParams(ViewGroup.LayoutParams.MatchParent, 1, GravityFlags.Top)
            : new LayoutParams(1, ViewGroup.LayoutParams.MatchParent, GravityFlags.Start);
        _tick.LayoutParameters = vertical
            ? new LayoutParams(ViewGroup.LayoutParams.MatchParent, _tickPx, GravityFlags.CenterVertical)
            : new LayoutParams(_tickPx, ViewGroup.LayoutParams.MatchParent, GravityFlags.CenterHorizontal);
        UpdateCenter();
        if (_lastVisible)
        {
            Apply(true, _lastFillPx, _bucket ?? TunerStripBucket.Far);
        }
    }

    internal Action<float>? HalfTrackChanged { get; set; }

    public void SetActive(bool active)
    {
        SetBackgroundColor(active ? TrackColor : Color.Transparent);
        _tick.Visibility = active ? ViewStates.Visible : ViewStates.Invisible;
        if (!active)
        {
            _fill.Visibility = ViewStates.Invisible;
        }
    }

    public void Apply(bool visible, int fillPx, TunerStripBucket bucket)
    {
        _lastVisible = visible;
        _lastFillPx = fillPx;
        if (!visible)
        {
            if (_fill.Visibility != ViewStates.Invisible)
            {
                _fill.Visibility = ViewStates.Invisible;
            }

            return;
        }

        if (_bucket != bucket)
        {
            _bucket = bucket;
            _fill.SetBackgroundColor(bucket switch
            {
                TunerStripBucket.InTune => InTuneColor,
                TunerStripBucket.Close => CloseColor,
                _ => FarColor
            });
        }

        if (_vertical)
        {
            // Screen Y grows downward, so a positive (sharp) fill is a negative scale.
            if (fillPx == 0)
            {
                _fill.TranslationY = _centerPx - _inTuneSegmentPx / 2f;
                _fill.ScaleY = _inTuneSegmentPx;
            }
            else
            {
                _fill.TranslationY = _centerPx;
                _fill.ScaleY = -fillPx;
            }
        }
        else if (fillPx == 0)
        {
            _fill.TranslationX = _centerPx - _inTuneSegmentPx / 2f;
            _fill.ScaleX = _inTuneSegmentPx;
        }
        else
        {
            _fill.TranslationX = _centerPx;
            _fill.ScaleX = fillPx;
        }

        if (_fill.Visibility != ViewStates.Visible)
        {
            _fill.Visibility = ViewStates.Visible;
        }
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        UpdateCenter();
    }

    // The marker travels along the long axis: half the width, or half the height when vertical.
    private void UpdateCenter()
    {
        var length = _vertical ? Height : Width;
        if (length <= 0)
        {
            return;
        }

        _centerPx = length / 2f;
        HalfTrackChanged?.Invoke(_centerPx);
    }
}
