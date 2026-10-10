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
    public static string FolderNameEmptyMessage => Get(nameof(FolderNameEmptyMessage));
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
    public static string ReadingModeHorizontalMessage => Get(nameof(ReadingModeHorizontalMessage));
    public static string ReadingModeVerticalPagedMessage => Get(nameof(ReadingModeVerticalPagedMessage));
    public static string ReadingModeVerticalContinuousMessage => Get(nameof(ReadingModeVerticalContinuousMessage));
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
    public static string CategoryOrnaments => Get(nameof(CategoryOrnaments));
    public static string CategoryBowingPlucking => Get(nameof(CategoryBowingPlucking));
    public static string CategoryPedalMarks => Get(nameof(CategoryPedalMarks));
    public static string CategoryRepeatsNavigation => Get(nameof(CategoryRepeatsNavigation));
    public static string CategoryMoreDynamics => Get(nameof(CategoryMoreDynamics));
    public static string CategoryAccidentals => Get(nameof(CategoryAccidentals));
    public static string CategoryTremoloGlissando => Get(nameof(CategoryTremoloGlissando));
    public static string IconTrill => Get(nameof(IconTrill));
    public static string IconMordent => Get(nameof(IconMordent));
    public static string IconMordentWithLine => Get(nameof(IconMordentWithLine));
    public static string IconTurn => Get(nameof(IconTurn));
    public static string IconTurnInverted => Get(nameof(IconTurnInverted));
    public static string IconGraceNote => Get(nameof(IconGraceNote));
    public static string IconUpBow => Get(nameof(IconUpBow));
    public static string IconDownBow => Get(nameof(IconDownBow));
    public static string IconSnapPizzicato => Get(nameof(IconSnapPizzicato));
    public static string IconHarmonic => Get(nameof(IconHarmonic));
    public static string IconPedalDown => Get(nameof(IconPedalDown));
    public static string IconPedalUp => Get(nameof(IconPedalUp));
    public static string IconHalfPedal => Get(nameof(IconHalfPedal));
    public static string IconSegno => Get(nameof(IconSegno));
    public static string IconCoda => Get(nameof(IconCoda));
    public static string IconRepeatStart => Get(nameof(IconRepeatStart));
    public static string IconRepeatEnd => Get(nameof(IconRepeatEnd));
    public static string IconMezzoPiano => Get(nameof(IconMezzoPiano));
    public static string IconMezzoForte => Get(nameof(IconMezzoForte));
    public static string IconPPP => Get(nameof(IconPPP));
    public static string IconFFF => Get(nameof(IconFFF));
    public static string IconSforzando => Get(nameof(IconSforzando));
    public static string IconRinforzando => Get(nameof(IconRinforzando));
    public static string IconSharp => Get(nameof(IconSharp));
    public static string IconFlat => Get(nameof(IconFlat));
    public static string IconNatural => Get(nameof(IconNatural));
    public static string IconDoubleSharp => Get(nameof(IconDoubleSharp));
    public static string IconDoubleFlat => Get(nameof(IconDoubleFlat));
    public static string IconTremolo1 => Get(nameof(IconTremolo1));
    public static string IconTremolo2 => Get(nameof(IconTremolo2));
    public static string IconTremolo3 => Get(nameof(IconTremolo3));
    public static string IconGlissandoUp => Get(nameof(IconGlissandoUp));
    public static string IconGlissandoDown => Get(nameof(IconGlissandoDown));
    public static string LanguageSectionHeader => Get(nameof(LanguageSectionHeader));
    public static string RestartRequiredTitle => Get(nameof(RestartRequiredTitle));
    public static string RestartRequiredMessage => Get(nameof(RestartRequiredMessage));
    public static string DonateMenuItem => Get(nameof(DonateMenuItem));
    public static string DonatePageTitle => Get(nameof(DonatePageTitle));
    public static string DonateExplanation => Get(nameof(DonateExplanation));
    public static string SeeAdButton => Get(nameof(SeeAdButton));
    public static string SupportWithAdsOption => Get(nameof(SupportWithAdsOption));
    public static string DonateSupportWithAdsExplanation => Get(nameof(DonateSupportWithAdsExplanation));
    public static string LegalSectionHeader => Get(nameof(LegalSectionHeader));
    public static string TutorialSkip => Get(nameof(TutorialSkip));
    public static string TutorialNext => Get(nameof(TutorialNext));
    public static string TutorialGetStarted => Get(nameof(TutorialGetStarted));
    public static string ShowTutorial => Get(nameof(ShowTutorial));
    public static string TutorialWelcomeTitle => Get(nameof(TutorialWelcomeTitle));
    public static string TutorialWelcomeText => Get(nameof(TutorialWelcomeText));
    public static string TutorialFoldersTitle => Get(nameof(TutorialFoldersTitle));
    public static string TutorialFoldersText => Get(nameof(TutorialFoldersText));
    public static string TutorialReadingTitle => Get(nameof(TutorialReadingTitle));
    public static string TutorialReadingText => Get(nameof(TutorialReadingText));
    public static string TutorialAnnotationsTitle => Get(nameof(TutorialAnnotationsTitle));
    public static string TutorialAnnotationsText => Get(nameof(TutorialAnnotationsText));
    public static string TutorialBookmarksTitle => Get(nameof(TutorialBookmarksTitle));
    public static string TutorialBookmarksText => Get(nameof(TutorialBookmarksText));
    public static string TutorialSettingsTitle => Get(nameof(TutorialSettingsTitle));
    public static string TutorialSettingsText => Get(nameof(TutorialSettingsText));
    public static string PrivacyPolicyLabel => Get(nameof(PrivacyPolicyLabel));
    public static string PrivacyPolicyDescription => Get(nameof(PrivacyPolicyDescription));
    public static string TermsOfServiceLabel => Get(nameof(TermsOfServiceLabel));
    public static string TermsOfServiceDescription => Get(nameof(TermsOfServiceDescription));
    public static string OpenPrivacyPolicy => Get(nameof(OpenPrivacyPolicy));

    public static string ImportPdf => Get(nameof(ImportPdf));
    public static string NewFolder => Get(nameof(NewFolder));
    public static string MoreOptions => Get(nameof(MoreOptions));
    public static string CropPage => Get(nameof(CropPage));
    public static string CropApply => Get(nameof(CropApply));
    public static string CropReset => Get(nameof(CropReset));
    public static string CropHint => Get(nameof(CropHint));
    public static string SortLabel => Get(nameof(SortLabel));
    public static string BackLabel => Get(nameof(BackLabel));
    public static string LibrarySummaryFormat => Get(nameof(LibrarySummaryFormat));
    public static string SheetCountFormat => Get(nameof(SheetCountFormat));
    public static string SheetCountSingularFormat => Get(nameof(SheetCountSingularFormat));
    public static string FolderCountFormat => Get(nameof(FolderCountFormat));
    public static string FolderCountSingularFormat => Get(nameof(FolderCountSingularFormat));
    public static string PagesFormat => Get(nameof(PagesFormat));
    public static string PagesSingularFormat => Get(nameof(PagesSingularFormat));

    public static string SheetCount(int count) =>
        string.Format(count == 1 ? SheetCountSingularFormat : SheetCountFormat, count);
    public static string FolderCount(int count) =>
        string.Format(count == 1 ? FolderCountSingularFormat : FolderCountFormat, count);
    public static string Pages(int count) =>
        string.Format(count == 1 ? PagesSingularFormat : PagesFormat, count);
    public static string LibrarySummary(int sheets, int folders) =>
        string.Format(LibrarySummaryFormat, SheetCount(sheets), FolderCount(folders));
    public static string EmptyLibraryTitle => Get(nameof(EmptyLibraryTitle));
    public static string EmptyLibraryMessage => Get(nameof(EmptyLibraryMessage));
    public static string NoResultsTitle => Get(nameof(NoResultsTitle));
    public static string NoResultsMessage => Get(nameof(NoResultsMessage));
    public static string EmptyFolderTitle => Get(nameof(EmptyFolderTitle));
    public static string EmptyFolderMessage => Get(nameof(EmptyFolderMessage));
    public static string ReadingModeNameHorizontal => Get(nameof(ReadingModeNameHorizontal));
    public static string ReadingModeNameVerticalPaged => Get(nameof(ReadingModeNameVerticalPaged));
    public static string ReadingModeNameVerticalContinuous => Get(nameof(ReadingModeNameVerticalContinuous));
    public static string UndoAction => Get(nameof(UndoAction));
    public static string RedoAction => Get(nameof(RedoAction));
    public static string ToolSettingsAction => Get(nameof(ToolSettingsAction));
    public static string StopToolAction => Get(nameof(StopToolAction));
    public static string ReadingModeLabel => Get(nameof(ReadingModeLabel));
    public static string AnnotationToolsLabel => Get(nameof(AnnotationToolsLabel));

    public static string EditorPageFormat => Get(nameof(EditorPageFormat));
    public static string EditorDone => Get(nameof(EditorDone));
    public static string Symbols => Get(nameof(Symbols));
    public static string EditorDoneLabel => Get(nameof(EditorDoneLabel));

    public static string SettingsGeneralSection => Get(nameof(SettingsGeneralSection));
    public static string SettingsHelpSection => Get(nameof(SettingsHelpSection));
    public static string SettingsStorageSection => Get(nameof(SettingsStorageSection));
    public static string StorageLocationLabel => Get(nameof(StorageLocationLabel));
    public static string StorageLocationValue => Get(nameof(StorageLocationValue));
    public static string StorageLastSyncFormat => Get(nameof(StorageLastSyncFormat));
    public static string StorageNeverSynced => Get(nameof(StorageNeverSynced));
    public static string StoragePendingFormat => Get(nameof(StoragePendingFormat));
    public static string RecoverLibraryLabel => Get(nameof(RecoverLibraryLabel));
    public static string RecoverLibraryDescription => Get(nameof(RecoverLibraryDescription));
    public static string RecoverLibraryButton => Get(nameof(RecoverLibraryButton));
    public static string RecoverLibraryIntroTitle => Get(nameof(RecoverLibraryIntroTitle));
    public static string RecoverLibraryIntroMessage => Get(nameof(RecoverLibraryIntroMessage));
    public static string RecoverChooseFolder => Get(nameof(RecoverChooseFolder));
    public static string RecoverNothingFound => Get(nameof(RecoverNothingFound));
    public static string RecoverResultFormat => Get(nameof(RecoverResultFormat));
    public static string AnnotationCountFormat => Get(nameof(AnnotationCountFormat));
    public static string AnnotationCountSingularFormat => Get(nameof(AnnotationCountSingularFormat));
    public static string BookmarkCountFormat => Get(nameof(BookmarkCountFormat));
    public static string BookmarkCountSingularFormat => Get(nameof(BookmarkCountSingularFormat));
    public static string CropCountFormat => Get(nameof(CropCountFormat));
    public static string CropCountSingularFormat => Get(nameof(CropCountSingularFormat));
    public static string ListAndFormat => Get(nameof(ListAndFormat));

    public static string AnnotationCount(int count) =>
        string.Format(count == 1 ? AnnotationCountSingularFormat : AnnotationCountFormat, count);

    public static string BookmarkCount(int count) =>
        string.Format(count == 1 ? BookmarkCountSingularFormat : BookmarkCountFormat, count);

    public static string CropCount(int count) =>
        string.Format(count == 1 ? CropCountSingularFormat : CropCountFormat, count);

    // "Restored 2 sheets, 1 folder, 2 annotations and 1 bookmark." Sheets are
    // always listed; the other kinds only when something was restored.
    public static string RecoverResult(int sheets, int folders, int annotations, int bookmarks, int crops)
    {
        var parts = new List<string> { SheetCount(sheets) };
        if (folders > 0)
        {
            parts.Add(FolderCount(folders));
        }

        if (annotations > 0)
        {
            parts.Add(AnnotationCount(annotations));
        }

        if (bookmarks > 0)
        {
            parts.Add(BookmarkCount(bookmarks));
        }

        if (crops > 0)
        {
            parts.Add(CropCount(crops));
        }

        var list = parts.Count == 1
            ? parts[0]
            : string.Format(ListAndFormat, string.Join(", ", parts.Take(parts.Count - 1)), parts[^1]);
        return string.Format(RecoverResultFormat, list);
    }
    public static string RecoverMissingFormat => Get(nameof(RecoverMissingFormat));
    public static string RecoverFailedMessage => Get(nameof(RecoverFailedMessage));
    public static string StorageSyncProgressFormat => Get(nameof(StorageSyncProgressFormat));
    public static string StorageSyncDone => Get(nameof(StorageSyncDone));
    public static string StorageSyncPending => Get(nameof(StorageSyncPending));
    public static string CategoryNotes => Get(nameof(CategoryNotes));
    public static string CategoryRests => Get(nameof(CategoryRests));
    public static string IconNoteWhole => Get(nameof(IconNoteWhole));
    public static string IconNoteHalfUp => Get(nameof(IconNoteHalfUp));
    public static string IconNoteHalfDown => Get(nameof(IconNoteHalfDown));
    public static string IconNoteQuarterUp => Get(nameof(IconNoteQuarterUp));
    public static string IconNoteQuarterDown => Get(nameof(IconNoteQuarterDown));
    public static string IconNote8thUp => Get(nameof(IconNote8thUp));
    public static string IconNote8thDown => Get(nameof(IconNote8thDown));
    public static string IconNote16thUp => Get(nameof(IconNote16thUp));
    public static string IconNote16thDown => Get(nameof(IconNote16thDown));
    public static string IconNote32ndUp => Get(nameof(IconNote32ndUp));
    public static string IconNote32ndDown => Get(nameof(IconNote32ndDown));
    public static string IconAugmentationDot => Get(nameof(IconAugmentationDot));
    public static string IconAcciaccaturaDown => Get(nameof(IconAcciaccaturaDown));
    public static string IconAppoggiaturaUp => Get(nameof(IconAppoggiaturaUp));
    public static string IconAppoggiaturaDown => Get(nameof(IconAppoggiaturaDown));
    public static string IconRestWhole => Get(nameof(IconRestWhole));
    public static string IconRestHalf => Get(nameof(IconRestHalf));
    public static string IconRestQuarter => Get(nameof(IconRestQuarter));
    public static string IconRest8th => Get(nameof(IconRest8th));
    public static string IconRest16th => Get(nameof(IconRest16th));
    public static string IconRest32nd => Get(nameof(IconRest32nd));
    public static string AppVersionFormat => Get(nameof(AppVersionFormat));

    private static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
