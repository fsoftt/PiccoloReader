using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Core.Tests;

public class StylusTapDetectorTests
{
    [Theory]
    [InlineData(0, 50, 8, true)]
    [InlineData(7.9, 299, 8, true)]
    [InlineData(8, 50, 8, false)]
    [InlineData(20, 50, 8, false)]
    [InlineData(2, 300, 8, false)]
    [InlineData(2, 1000, 8, false)]
    public void IsTap_UsesSlopAndDurationThresholds(double travel, long duration, double slop, bool expected)
    {
        Assert.Equal(expected, StylusTapDetector.IsTap(travel, duration, slop));
    }
}
