using PiccoloReader.Core.Resources.Strings;


namespace PiccoloReader.Views;

// Stage B of the concert mode tutorial: coach marks for the tap zones that
// switch to the next / previous score. Shown once, the first time the viewer
// opens a sheet that has a neighbour in its list.
public partial class SheetViewerPage
{
    private bool _coachPageVisible;
    private bool _concertTutorialRunning;

    private bool ConcertTutorialDue() =>
        _coachPageVisible
        && !IsEditing
        && !_viewModel.IsContinuousEditing
        && _viewModel.PageCount > 0
        && _concertTutorial.ShouldShowViewer(_coachPageVisible, _viewModel.HasConcertNeighbour);

    private async Task TryShowConcertTutorialAsync()
    {
        if (_concertTutorialRunning || !ConcertTutorialDue())
        {
            return;
        }

        _concertTutorialRunning = true;
        try
        {
            // Let the first page render and the layout settle.
            await Task.Delay(900);
            if (!ConcertTutorialDue())
            {
                return;
            }

            var result = await CoachOverlay.ShowAsync(BuildConcertTutorialSteps());
            if (result is CoachMarkResult.Finished or CoachMarkResult.Skipped)
            {
                _concertTutorial.MarkViewerSeen();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Concert viewer tutorial failed: {ex.Message}");
        }
        finally
        {
            _concertTutorialRunning = false;
        }
    }

    private IReadOnlyList<CoachMarkStep> BuildConcertTutorialSteps() => new[]
    {
        new CoachMarkStep
        {
            Text = AppStrings.CoachViewerNext,
            Target = () => TapZoneBounds(next: true),
            Anchor = TapHintAnchor,
            Gesture = CoachGesture.Tap,
            CornerRadius = 12,
            Padding = 0
        },
        new CoachMarkStep
        {
            Text = AppStrings.CoachViewerPrevious,
            Target = () => TapZoneBounds(next: false),
            Anchor = TapHintAnchor,
            Gesture = CoachGesture.Tap,
            CornerRadius = 12,
            Padding = 0
        },
        new CoachMarkStep
        {
            // No cutout: a plain note, centred.
            Text = AppStrings.CoachViewerSettings
        }
    };

    // The 30% side zones of the page area (top / bottom strips in vertical reading).
    private Rect? TapZoneBounds(bool next)
    {
        if (ViewBoundsOfViewerArea() is not { } area)
        {
            return null;
        }

        if (_viewModel.IsVerticalPaged && !_viewModel.IsContinuousReading)
        {
            var height = area.Height * SideTapZoneFraction;
            return new Rect(area.X, next ? area.Bottom - height : area.Y, area.Width, height);
        }

        var width = area.Width * SideTapZoneFraction;
        return new Rect(next ? area.Right - width : area.X, area.Y, width, area.Height);
    }

    private Rect? ViewBoundsOfViewerArea() => PiccoloReader.Services.ViewBounds.Of(ViewerArea, CoachOverlay);

    // The hand and the callout go to the middle of the (tall) zone, not around all of it.
    private static Rect TapHintAnchor(Rect zone) =>
        new(zone.X + zone.Width / 2 - 32, zone.Y + zone.Height / 2 - 32, 64, 64);
}
