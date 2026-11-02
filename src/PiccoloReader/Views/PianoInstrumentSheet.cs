using CommunityToolkit.Maui.Views;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Views;

/// <summary>
/// Bottom-sheet list of the instruments, grouped by family. Tapping one reports it and closes
/// the sheet; the current one has a check mark.
/// </summary>
public class PianoInstrumentSheet : Popup
{
    private const string CheckGlyph = "";

    public PianoInstrumentSheet(PianoInstrument current, double maxHeight, Action<PianoInstrument> onSelected)
    {
        AutomationId = "PianoInstrumentSheet";
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(0);
        Padding = new Thickness(0);
        this.SetAppThemeColor(BackgroundColorProperty, ThemeColor("SurfaceLight"), ThemeColor("SurfaceRaisedDark"));

        var list = new VerticalStackLayout { Padding = new Thickness(0, 0, 0, 16) };
        list.Add(new Label
        {
            Text = AppStrings.PianoChooseInstrument,
            Style = (Style)Application.Current!.Resources["RowTitle"],
            FontSize = 18,
            Margin = new Thickness(20, 20, 20, 4)
        });

        PianoFamily? family = null;
        foreach (var instrument in PianoInstruments.All)
        {
            if (family != instrument.Family)
            {
                family = instrument.Family;
                var header = new Label
                {
                    Text = PianoInstruments.FamilyName(instrument.Family),
                    Style = (Style)Application.Current.Resources["SectionLabel"],
                    Margin = new Thickness(20, 14, 20, 2)
                };
                list.Add(header);
            }

            list.Add(CreateRow(instrument, instrument == current, onSelected));
        }

        Content = new ScrollView { MaximumHeightRequest = maxHeight, Content = list };
    }

    private View CreateRow(PianoInstrument instrument, bool isCurrent, Action<PianoInstrument> onSelected)
    {
        var row = new Grid
        {
            AutomationId = "PianoInstrument_" + instrument.Id,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            MinimumHeightRequest = 48,
            Padding = new Thickness(20, 0),
            BackgroundColor = Colors.Transparent
        };
        SemanticProperties.SetDescription(row, instrument.Name);

        var name = new Label
        {
            Text = instrument.Name,
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center,
            FontFamily = isCurrent ? "OpenSansSemibold" : "OpenSansRegular"
        };
        name.SetAppThemeColor(Label.TextColorProperty, ThemeColor("TextPrimaryLight"), ThemeColor("TextPrimaryDark"));
        row.Add(name, 0, 0);

        if (isCurrent)
        {
            var check = new Image
            {
                VerticalOptions = LayoutOptions.Center,
                Source = new FontImageSource
                {
                    Glyph = CheckGlyph,
                    FontFamily = "MaterialOutlined",
                    Size = 22,
                    Color = ThemeColor(Application.Current?.RequestedTheme == AppTheme.Dark ? "PrimaryDark" : "PrimaryLight")
                }
            };
            row.Add(check, 1, 0);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            onSelected(instrument);
            try
            {
                await CloseAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Closing the instrument sheet failed: {ex.Message}");
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private static Color ThemeColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Gray;
}
