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
        platformView.TravelChanged = VirtualView.TravelChanged;
        VirtualView.Surface = platformView;
    }

    protected override void DisconnectHandler(TunerStripNativeView platformView)
    {
        platformView.TravelChanged = null;
        VirtualView.Surface = null;
        base.DisconnectHandler(platformView);
    }
}

/// <summary>
/// A faint track with a small marker child. Updates only change the marker's
/// TranslationX and background color and the strip's visibility: no layout pass,
/// no invalidation of anything else. Invisible (not Gone) keeps the layout stable.
/// </summary>
public class TunerStripNativeView : FrameLayout, ITunerStripView
{
    private const float MarkerWidthDp = 12f;

    private static readonly Color TrackColor = Color.Argb(0x40, 0x80, 0x80, 0x80);
    private static readonly Color InTuneColor = Color.Argb(0xFF, 0x4C, 0xAF, 0x50);
    private static readonly Color CloseColor = Color.Argb(0xFF, 0xFF, 0xC1, 0x07);
    private static readonly Color FarColor = Color.Argb(0xFF, 0xF4, 0x43, 0x36);

    private readonly View _marker;
    private readonly int _markerWidthPx;
    private TunerStripBucket? _bucket;

    public TunerStripNativeView(Context? context) : base(context!)
    {
        _markerWidthPx = (int)Math.Round(MarkerWidthDp * context!.Resources!.DisplayMetrics!.Density);
        SetBackgroundColor(TrackColor);
        _marker = new View(context);
        AddView(_marker, new LayoutParams(_markerWidthPx, ViewGroup.LayoutParams.MatchParent, GravityFlags.Start));
        Visibility = ViewStates.Invisible;
    }

    internal Action<float>? TravelChanged { get; set; }

    public void Apply(bool visible, int offsetPx, TunerStripBucket bucket)
    {
        if (!visible)
        {
            if (Visibility != ViewStates.Invisible)
            {
                Visibility = ViewStates.Invisible;
            }

            return;
        }

        if (_bucket != bucket)
        {
            _bucket = bucket;
            _marker.SetBackgroundColor(bucket switch
            {
                TunerStripBucket.InTune => InTuneColor,
                TunerStripBucket.Close => CloseColor,
                _ => FarColor
            });
        }

        _marker.TranslationX = offsetPx;
        if (Visibility != ViewStates.Visible)
        {
            Visibility = ViewStates.Visible;
        }
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        TravelChanged?.Invoke(Math.Max(0, w - _markerWidthPx));
    }
}
