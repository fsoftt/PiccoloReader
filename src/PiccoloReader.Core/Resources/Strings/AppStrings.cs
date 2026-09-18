using System.Globalization;
using System.Resources;

namespace PiccoloReader.Core.Resources.Strings;

public static class AppStrings
{
    private static readonly ResourceManager ResourceManager =
        new("PiccoloReader.Core.Resources.Strings.AppStrings", typeof(AppStrings).Assembly);

    public static string Library => Get(nameof(Library));
    public static string Settings => Get(nameof(Settings));
    public static string MadeWithLoveBy => Get(nameof(MadeWithLoveBy));
    public static string Folders => Get(nameof(Folders));
    public static string Sheets => Get(nameof(Sheets));
    public static string SearchFoldersPlaceholder => Get(nameof(SearchFoldersPlaceholder));
    public static string SearchSheetsPlaceholder => Get(nameof(SearchSheetsPlaceholder));
    public static string SelectPdfsPickerTitle => Get(nameof(SelectPdfsPickerTitle));
    public static string CreateFolderTitle => Get(nameof(CreateFolderTitle));
    public static string FolderNamePrompt => Get(nameof(FolderNamePrompt));
    public static string SortFoldersByTitle => Get(nameof(SortFoldersByTitle));
    public static string SortSheetsByTitle => Get(nameof(SortSheetsByTitle));
    public static string SortByTitle => Get(nameof(SortByTitle));
    public static string Cancel => Get(nameof(Cancel));
    public static string SortNameAscending => Get(nameof(SortNameAscending));
    public static string SortNameDescending => Get(nameof(SortNameDescending));
    public static string SortDateAddedAscending => Get(nameof(SortDateAddedAscending));
    public static string SortDateAddedDescending => Get(nameof(SortDateAddedDescending));
    public static string DeleteSheetsOption => Get(nameof(DeleteSheetsOption));
    public static string KeepSheetsOption => Get(nameof(KeepSheetsOption));
    public static string Move => Get(nameof(Move));
    public static string Delete => Get(nameof(Delete));
    public static string DeleteSheetTitle => Get(nameof(DeleteSheetTitle));
    public static string DeleteSheetMessageFormat => Get(nameof(DeleteSheetMessageFormat));
    public static string RootOption => Get(nameof(RootOption));
    public static string MoveToTitle => Get(nameof(MoveToTitle));
    public static string NoFoldersToMoveMessage => Get(nameof(NoFoldersToMoveMessage));
    public static string OK => Get(nameof(OK));
    public static string Folder => Get(nameof(Folder));
    public static string GoToPageTitle => Get(nameof(GoToPageTitle));
    public static string GoToPageMessageFormat => Get(nameof(GoToPageMessageFormat));
    public static string BookmarksTitle => Get(nameof(BookmarksTitle));
    public static string AddBookmarkOption => Get(nameof(AddBookmarkOption));
    public static string BookmarkPageLabelFormat => Get(nameof(BookmarkPageLabelFormat));
    public static string BookmarkNamedPageLabelFormat => Get(nameof(BookmarkNamedPageLabelFormat));
    public static string PageNumberLabel => Get(nameof(PageNumberLabel));
    public static string NameOptionalLabel => Get(nameof(NameOptionalLabel));
    public static string NamePlaceholderExample => Get(nameof(NamePlaceholderExample));
    public static string Add => Get(nameof(Add));
    public static string MusicIcons => Get(nameof(MusicIcons));
    public static string Pencil => Get(nameof(Pencil));
    public static string Color => Get(nameof(Color));
    public static string Width => Get(nameof(Width));
    public static string Eraser => Get(nameof(Eraser));
    public static string Size => Get(nameof(Size));
    public static string PageIndicatorFormat => Get(nameof(PageIndicatorFormat));
    public static string CategoryDynamics => Get(nameof(CategoryDynamics));
    public static string CategoryArticulations => Get(nameof(CategoryArticulations));
    public static string CategoryFermataBreath => Get(nameof(CategoryFermataBreath));
    public static string CategoryHairpins => Get(nameof(CategoryHairpins));
    public static string IconStaccato => Get(nameof(IconStaccato));
    public static string IconAccent => Get(nameof(IconAccent));
    public static string IconTenuto => Get(nameof(IconTenuto));
    public static string IconMarcato => Get(nameof(IconMarcato));
    public static string IconFermata => Get(nameof(IconFermata));
    public static string IconBreathMark => Get(nameof(IconBreathMark));
    public static string IconCrescendo => Get(nameof(IconCrescendo));
    public static string IconDecrescendo => Get(nameof(IconDecrescendo));
    public static string LanguageSectionHeader => Get(nameof(LanguageSectionHeader));
    public static string RestartRequiredTitle => Get(nameof(RestartRequiredTitle));
    public static string RestartRequiredMessage => Get(nameof(RestartRequiredMessage));

    private static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
