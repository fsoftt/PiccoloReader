using Plugin.AdMob;
using Plugin.AdMob.Services;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiDonateAdService : IDonateAdService
{
    private readonly IRewardedAdService _rewardedAdService;

    public MauiDonateAdService(IRewardedAdService rewardedAdService)
    {
        _rewardedAdService = rewardedAdService;
    }

    public Task<bool> ShowRewardedAdAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var ad = _rewardedAdService.CreateAd();

        void Unsubscribe()
        {
            ad.OnAdLoaded -= OnLoaded;
            ad.OnAdFailedToLoad -= OnFailedToLoad;
        }

        void OnLoaded(object? sender, EventArgs e)
        {
            Unsubscribe();
            ad.Show();
            tcs.TrySetResult(true);
        }

        void OnFailedToLoad(object? sender, IAdError e)
        {
            Unsubscribe();
            tcs.TrySetResult(false);
        }

        ad.OnAdLoaded += OnLoaded;
        ad.OnAdFailedToLoad += OnFailedToLoad;
        ad.Load();

        return tcs.Task;
    }
}
