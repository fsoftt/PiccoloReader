using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

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
        if (ToolSheet.IsVisible || _viewModel.ActiveTool is AnnotationTool.Pencil or AnnotationTool.Highlighter or AnnotationTool.Eraser || PageContainer.Width <= 0 || PageContainer.Height <= 0)
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
        var edit = AppStrings.EditText;
        var options = figure.IsText ? new[] { edit, copy } : new[] { copy };
        var choice = await DisplayActionSheetAsync(null, AppStrings.Cancel, null, options);
        if (choice == copy)
        {
            await _viewModel.CopyAnnotationAsync(figure);
            UpdateSelectionOverlay();
        }
        else if (choice == edit)
        {
            await EditTextAsync(figure);
        }
    }

    private bool _textPromptOpen;

    // Asks for text and adds it with its top-left at the tapped page point.
    private async Task AddTextAtAsync(double pageX, double pageY)
    {
        if (_textPromptOpen)
        {
            return;
        }

        var text = await PromptTextAsync(string.Empty);
        if (text is null)
        {
            return;
        }

        var aspect = _viewModel.CurrentPageAspectRatio;
        var (width, height) = TextFigure.Size(AnnotationPainter.MeasureTextAspect(text), TextFigure.DefaultHeight, aspect);
        var (x, y) = TextFigure.Place(pageX, pageY, width, height, _viewModel.CurrentPageCrop);
        await _viewModel.AddTextAsync(text, _viewModel.PencilColorHex, x, y, width, height);
        UpdateSelectionOverlay();
    }

    // The new text box keeps the height and takes the new text's width.
    private async Task EditTextAsync(Annotation annotation)
    {
        var text = await PromptTextAsync(annotation.Text ?? string.Empty);
        if (text is null || text == annotation.Text)
        {
            return;
        }

        var aspect = _viewModel.CurrentPageAspectRatio;
        var (width, _) = TextFigure.Size(AnnotationPainter.MeasureTextAspect(text), annotation.Height, aspect);
        var crop = _viewModel.CurrentPageCrop;
        var x = Math.Clamp(annotation.X, crop.Left, Math.Max(crop.Left, crop.Right - width));
        await _viewModel.EditTextAsync(annotation, text, x, width);
        UpdateSelectionOverlay();
    }

    // The normalized text typed by the user, or null when cancelled or empty
    // (an empty text keeps what was there).
    private async Task<string?> PromptTextAsync(string initial)
    {
        _textPromptOpen = true;
        try
        {
            var input = await DisplayPromptAsync(
                AppStrings.AddTextTitle,
                AppStrings.TextTool,
                accept: AppStrings.OK,
                cancel: AppStrings.Cancel,
                maxLength: TextFigure.MaxLength,
                initialValue: initial);
#if ANDROID
            // The keyboard leaves the window insets stale, see OnPageIndicatorTapped.
            RequestAndroidWindowInsetsRefresh();
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), RequestAndroidWindowInsetsRefresh);
#endif
            return TextFigure.Normalize(input);
        }
        finally
        {
            _textPromptOpen = false;
        }
    }
}
