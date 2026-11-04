using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Services;

/// <summary>Where a view sits on screen, relative to another view (device-independent units).</summary>
public static class ViewBounds
{
    /// <summary>
    /// Bounds of <paramref name="target"/> in the coordinate space of <paramref name="reference"/>,
    /// or null while it is not laid out or not attached. Measured from the native views, so it is
    /// right inside scrolling lists and under transforms.
    /// </summary>
    public static Rect? Of(VisualElement target, VisualElement reference)
    {
#if ANDROID
        if (target.Handler?.PlatformView is Android.Views.View targetView
            && reference.Handler?.PlatformView is Android.Views.View referenceView
            && targetView.IsAttachedToWindow
            && targetView.Width > 0
            && targetView.Height > 0)
        {
            var a = new int[2];
            var b = new int[2];
            targetView.GetLocationOnScreen(a);
            referenceView.GetLocationOnScreen(b);
            var density = targetView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            return new Rect((a[0] - b[0]) / density, (a[1] - b[1]) / density, targetView.Width / density, targetView.Height / density);
        }

        return null;
#else
        if (target.Width <= 0 || target.Height <= 0)
        {
            return null;
        }

        double x = 0, y = 0;
        Element? current = target;
        while (current is VisualElement element && !ReferenceEquals(element, reference))
        {
            x += element.X;
            y += element.Y;
            current = element.Parent;
        }

        return ReferenceEquals(current, reference) ? new Rect(x, y, target.Width, target.Height) : null;
#endif
    }

    /// <summary>
    /// How far system bars and display cutouts overlap the edges of <paramref name="reference"/>
    /// (so callouts stay clear of them).
    /// </summary>
    public static CoachInsets SystemInsets(VisualElement reference)
    {
#if ANDROID
        try
        {
            if (reference.Handler?.PlatformView is Android.Views.View view && view.RootView is { } root)
            {
                var windowInsets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(view);
                var insets = windowInsets?.GetInsets(
                    AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars() | AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout());
                if (insets is not null)
                {
                    var v = new int[2];
                    var r = new int[2];
                    view.GetLocationOnScreen(v);
                    root.GetLocationOnScreen(r);
                    var left = v[0] - r[0];
                    var top = v[1] - r[1];
                    var right = root.Width - (left + view.Width);
                    var bottom = root.Height - (top + view.Height);
                    var density = view.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                    return new CoachInsets(
                        Math.Max(0, insets.Left - left) / density,
                        Math.Max(0, insets.Top - top) / density,
                        Math.Max(0, insets.Right - right) / density,
                        Math.Max(0, insets.Bottom - bottom) / density);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading system insets failed: {ex.Message}");
        }
#endif
        return new CoachInsets(0, 0, 0, 0);
    }
}
