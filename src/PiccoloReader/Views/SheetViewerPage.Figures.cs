using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Views;

// Figure actions that are not part of the basic select / move / resize flow:
// the long-press menu (copy) on a figure.
public partial class SheetViewerPage
{
    // The figure under a container-normalized point, or null. Strokes have no
    // box (X/Y/Width/Height stay 0) so they are never found here. Annotations
    // outside the crop are not on screen, so not hit.
    private Annotation? FindFigureAt(double normalizedX, double normalizedY)
    {
        var pageRect = EditorPageRect();
        if (pageRect.Width <= 0 || pageRect.Height <= 0)
        {
            return null;
        }

        var pageX = (normalizedX * PageContainer.Width - pageRect.Left) / pageRect.Width;
        var pageY = (normalizedY * PageContainer.Height - pageRect.Top) / pageRect.Height;
        if (!_viewModel.CurrentPageCrop.Contains(pageX, pageY))
        {
            return null;
        }

        return _viewModel.CurrentPageAnnotations.FirstOrDefault(a =>
            !a.IsStroke &&
            pageX >= a.X && pageX <= a.X + a.Width &&
            pageY >= a.Y && pageY <= a.Y + a.Height);
    }

    // Long-press on the page (Android touch listener). Returns true when it
    // landed on a figure, so the listener drops the rest of the gesture.
    private bool HandleContainerLongPress(double normalizedX, double normalizedY)
    {
        if (ToolSheet.IsVisible || _viewModel.IsDrawingToolActive || PageContainer.Width <= 0 || PageContainer.Height <= 0)
        {
            return false;
        }

        var hit = FindFigureAt(normalizedX, normalizedY);
        if (hit is null)
        {
            return false;
        }

        _ = ShowFigureMenuAsync(hit);
        return true;
    }

    private async Task ShowFigureMenuAsync(Annotation? figure)
    {
        if (figure is null || figure.IsStroke)
        {
            return;
        }

        // Shows which figure the menu is about.
        if (!ReferenceEquals(_viewModel.SelectedAnnotation, figure))
        {
            _viewModel.SelectedAnnotation = figure;
            UpdateSelectionOverlay();
        }

        var copy = AppStrings.CopyFigure;
        var choice = await DisplayActionSheetAsync(null, AppStrings.Cancel, null, copy);
        if (choice == copy)
        {
            await _viewModel.CopyAnnotationAsync(figure);
            UpdateSelectionOverlay();
        }
    }
}
