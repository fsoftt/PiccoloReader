using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

// UI side of sharing a sheet: shows a blocking "Preparing PDF" page while the
// file is built off the UI thread, then opens the system share sheet.
public class SheetShareFlow
{
    private readonly SheetShareService _service;
    private bool _busy;

    public SheetShareFlow(SheetShareService service) => _service = service;

    public bool IsSupported => _service.IsSupported;

    public async Task ShareAsync(Page page, Sheet sheet, (double Width, double Height) legacyReferenceArea)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        var busyPage = new ContentPage
        {
            BackgroundColor = Colors.Black.WithAlpha(0.6f),
            Content = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Spacing = 16,
                Children =
                {
                    new ActivityIndicator { IsRunning = true, Color = Colors.White },
                    new Label { Text = AppStrings.PreparingPdf, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        };
        NavigationPage.SetHasNavigationBar(busyPage, false);

        string? path = null;
        try
        {
            await page.Navigation.PushModalAsync(busyPage, false);
            path = await _service.PrepareAsync(sheet, legacyReferenceArea);
        }
        catch (Exception)
        {
            await CloseAsync(page, busyPage);
            _busy = false;
            await page.DisplayAlertAsync(AppStrings.Share, AppStrings.SharePdfFailed, AppStrings.OK);
            return;
        }

        await CloseAsync(page, busyPage);
        _busy = false;

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = sheet.Title,
                File = new ShareFile(path, "application/pdf")
            });
        }
        catch (Exception)
        {
            await page.DisplayAlertAsync(AppStrings.Share, AppStrings.SharePdfFailed, AppStrings.OK);
        }
    }

    private static async Task CloseAsync(Page page, Page busyPage)
    {
        if (page.Navigation.ModalStack.Contains(busyPage))
        {
            await page.Navigation.PopModalAsync(false);
        }
    }
}
