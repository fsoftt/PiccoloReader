using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests;

public class TutorialViewModelTests
{
    private static (TutorialViewModel Vm, FakeTutorialPreferenceService Prefs) Create()
    {
        var prefs = new FakeTutorialPreferenceService();
        return (new TutorialViewModel(prefs), prefs);
    }

    [Fact]
    public void Slides_HaveContent()
    {
        var (vm, _) = Create();

        Assert.Equal(6, vm.Slides.Count);
        Assert.All(vm.Slides, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Glyph));
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }

    [Fact]
    public void Next_AdvancesUntilLastSlide()
    {
        var (vm, prefs) = Create();

        vm.NextCommand.Execute(null);

        Assert.Equal(1, vm.CurrentIndex);
        Assert.False(vm.IsLastSlide);
        Assert.True(vm.ShowSkip);
        Assert.False(prefs.Completed);
    }

    [Fact]
    public void Next_OnLastSlide_FinishesAndMarksCompleted()
    {
        var (vm, prefs) = Create();
        var finished = 0;
        vm.Finished += (_, _) => finished++;

        for (var i = 0; i < vm.Slides.Count - 1; i++)
        {
            vm.NextCommand.Execute(null);
        }

        Assert.True(vm.IsLastSlide);
        Assert.False(vm.ShowSkip);
        Assert.Equal(0, finished);

        vm.NextCommand.Execute(null);

        Assert.Equal(1, finished);
        Assert.True(prefs.Completed);
        Assert.Equal(vm.Slides.Count - 1, vm.CurrentIndex);
    }

    [Fact]
    public void Skip_MarksCompletedAndRaisesFinished()
    {
        var (vm, prefs) = Create();
        var finished = 0;
        vm.Finished += (_, _) => finished++;

        vm.SkipCommand.Execute(null);

        Assert.Equal(1, finished);
        Assert.True(prefs.Completed);
    }

    [Fact]
    public void PrimaryButtonText_ChangesOnLastSlide()
    {
        var (vm, _) = Create();
        var first = vm.PrimaryButtonText;
        vm.CurrentIndex = vm.Slides.Count - 1;

        Assert.NotEqual(first, vm.PrimaryButtonText);
    }
}
