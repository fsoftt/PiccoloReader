using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class TutorialViewModel : ObservableObject
{
    private readonly ITutorialPreferenceService _preferenceService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLastSlide))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowSkip))]
    private int _currentIndex;

    /// <summary>Hidden in landscape so the slide text gets the available height.</summary>
    [ObservableProperty]
    private bool _showSlideIcon = true;

    public TutorialViewModel(ITutorialPreferenceService preferenceService)
    {
        _preferenceService = preferenceService;
        Slides = new[]
        {
            new TutorialSlide("\uE030", AppStrings.TutorialWelcomeTitle, AppStrings.TutorialWelcomeText),
            new TutorialSlide("\uE2C7", AppStrings.TutorialFoldersTitle, AppStrings.TutorialFoldersText),
            new TutorialSlide("\uEA19", AppStrings.TutorialReadingTitle, AppStrings.TutorialReadingText),
            new TutorialSlide("\uE3C9", AppStrings.TutorialAnnotationsTitle, AppStrings.TutorialAnnotationsText),
            new TutorialSlide("\uE866", AppStrings.TutorialBookmarksTitle, AppStrings.TutorialBookmarksText),
            new TutorialSlide("\uE8B8", AppStrings.TutorialSettingsTitle, AppStrings.TutorialSettingsText),
        };
    }

    public IReadOnlyList<TutorialSlide> Slides { get; }

    public bool IsLastSlide => CurrentIndex >= Slides.Count - 1;

    public bool ShowSkip => !IsLastSlide;

    public string PrimaryButtonText => IsLastSlide ? AppStrings.TutorialGetStarted : AppStrings.TutorialNext;

    /// <summary>Raised once the tutorial is closed (skipped or finished).</summary>
    public event EventHandler? Finished;

    public bool IsTutorialCompleted => _preferenceService.GetTutorialCompleted();

    [RelayCommand]
    private void Next()
    {
        if (IsLastSlide)
        {
            Finish();
            return;
        }

        CurrentIndex++;
    }

    [RelayCommand]
    private void Skip() => Finish();

    private void Finish()
    {
        _preferenceService.SetTutorialCompleted(true);
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
