using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class DonateViewModelTests
{
    [Fact]
    public async Task SeeAdCommand_Success_CallsShowRewardedAdAndResetsLoading()
    {
        var adService = new FakeDonateAdService { ResultToReturn = true };
        var sut = new DonateViewModel(adService);

        await sut.SeeAdCommand.ExecuteAsync(null);

        Assert.Equal(1, adService.ShowRewardedAdCallCount);
        Assert.False(sut.IsAdLoading);
    }

    [Fact]
    public async Task SeeAdCommand_Failure_StillResetsLoading()
    {
        var adService = new FakeDonateAdService { ResultToReturn = false };
        var sut = new DonateViewModel(adService);

        await sut.SeeAdCommand.ExecuteAsync(null);

        Assert.Equal(1, adService.ShowRewardedAdCallCount);
        Assert.False(sut.IsAdLoading);
    }

    [Fact]
    public void IsAdLoading_InitiallyFalse()
    {
        var sut = new DonateViewModel(new FakeDonateAdService());

        Assert.False(sut.IsAdLoading);
    }
}
