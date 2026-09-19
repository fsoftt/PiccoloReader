using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeDonateAdService : IDonateAdService
{
    public bool ResultToReturn { get; set; } = true;

    public int ShowRewardedAdCallCount { get; private set; }

    public Task<bool> ShowRewardedAdAsync()
    {
        ShowRewardedAdCallCount++;
        return Task.FromResult(ResultToReturn);
    }
}
