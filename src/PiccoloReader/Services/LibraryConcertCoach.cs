using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Views;

namespace PiccoloReader.Services;

/// <summary>
/// Stage A of the concert mode tutorial, shared by the library and folder pages: points at the
/// first row drag handle, the sheet list and the sort button. Shows once (see ConcertTutorialService).
/// </summary>
public sealed class LibraryConcertCoach
{
    private readonly ConcertTutorialService _tutorial;
    private readonly CoachMarkOverlay _overlay;
    private readonly ItemsView _list;
    private readonly VisualElement _sortButton;
    private readonly Func<object?> _firstItem;
    private readonly Func<bool> _handlesVisible;
    private readonly Func<bool> _pageVisible;
    private readonly List<WeakReference<VisualElement>> _handles = new();
    private bool _running;

    public LibraryConcertCoach(
        ConcertTutorialService tutorial,
        CoachMarkOverlay overlay,
        ItemsView list,
        VisualElement sortButton,
        Func<object?> firstItem,
        Func<bool> handlesVisible,
        Func<bool> pageVisible)
    {
        _tutorial = tutorial;
        _overlay = overlay;
        _list = list;
        _sortButton = sortButton;
        _firstItem = firstItem;
        _handlesVisible = handlesVisible;
        _pageVisible = pageVisible;
    }

    public bool IsShowing => _overlay.IsShowing;

    /// <summary>Remembers a realized row handle (called from the template HandlerChanged).</summary>
    public void TrackHandle(VisualElement handle)
    {
        if (handle.Handler is null)
        {
            return;
        }

        _handles.RemoveAll(reference => !reference.TryGetTarget(out var existing) || ReferenceEquals(existing, handle));
        _handles.Add(new WeakReference<VisualElement>(handle));
    }

    public void Skip() => _overlay.Skip();

    public void Cancel() => _overlay.Cancel();

    /// <summary>Shows the guide when it is due. Never throws; returns quickly when not due.</summary>
    public async Task TryShowAsync()
    {
        if (_running || !_tutorial.ShouldShowLibrary(_pageVisible(), _handlesVisible()))
        {
            return;
        }

        _running = true;
        try
        {
            // Let the list finish its first layout and the handles be realized.
            await Task.Delay(700);
            if (!_tutorial.ShouldShowLibrary(_pageVisible(), _handlesVisible()))
            {
                return;
            }

            if (!IsFirstHandleOnScreen())
            {
                // Folders can push the first row below the fold.
                _list.ScrollTo(0, position: ScrollToPosition.MakeVisible, animate: false);
                await Task.Delay(400);
                if (!_pageVisible() || !IsFirstHandleOnScreen())
                {
                    return;
                }
            }

            var result = await _overlay.ShowAsync(BuildSteps());
            if (result is CoachMarkResult.Finished or CoachMarkResult.Skipped)
            {
                _tutorial.MarkLibrarySeen();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Concert library tutorial failed: {ex.Message}");
        }
        finally
        {
            _running = false;
        }
    }

    private IReadOnlyList<CoachMarkStep> BuildSteps() => new[]
    {
        new CoachMarkStep
        {
            Text = AppStrings.CoachLibraryHandle,
            Target = () => FirstHandle() is { } handle ? ViewBounds.Of(handle, _overlay) : null,
            Gesture = CoachGesture.LongPressDrag,
            ShowDragIcon = true,
            CornerRadius = 16,
            Padding = 4,
            RequireTarget = true
        },
        new CoachMarkStep
        {
            Text = AppStrings.CoachLibraryList,
            Target = ListRowsBounds,
            CornerRadius = 16,
            Padding = 0,
            RequireTarget = true
        },
        new CoachMarkStep
        {
            Text = AppStrings.CoachLibrarySort,
            Target = () => ViewBounds.Of(_sortButton, _overlay),
            CornerRadius = 16,
            Padding = 4,
            RequireTarget = true
        }
    };

    private bool IsFirstHandleOnScreen()
    {
        var handle = FirstHandle();
        var bounds = handle is null ? null : ViewBounds.Of(handle, _overlay);
        var listBounds = ViewBounds.Of(_list, _overlay);
        return bounds is { } b && listBounds is { } l
            && b.Top >= l.Top - 2 && b.Bottom <= l.Bottom + 2;
    }

    // The realized handle that belongs to the first sheet of the list.
    private VisualElement? FirstHandle()
    {
        var first = _firstItem();
        if (first is null)
        {
            return null;
        }

        foreach (var reference in _handles)
        {
            if (reference.TryGetTarget(out var handle)
                && handle.Handler is not null
                && handle.IsVisible
                && ReferenceEquals(handle.BindingContext, first))
            {
                return handle;
            }
        }

        return null;
    }

    // The first rows of the list: from the first row down, at most three rows tall.
    private Rect? ListRowsBounds()
    {
        var handle = FirstHandle();
        var row = handle?.Parent?.Parent as VisualElement;
        var rowBounds = row is null ? null : ViewBounds.Of(row, _overlay);
        var listBounds = ViewBounds.Of(_list, _overlay);
        if (rowBounds is not { } r || listBounds is not { } l)
        {
            return null;
        }

        var bottom = Math.Min(r.Top + r.Height * 3, l.Bottom);
        return new Rect(l.Left + 8, r.Top, l.Width - 16, Math.Max(0, bottom - r.Top));
    }
}
