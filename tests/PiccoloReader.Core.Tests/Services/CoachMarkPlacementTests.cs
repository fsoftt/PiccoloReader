using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Core.Tests.Services;

public class CoachMarkPlacementTests
{
    private static readonly CoachInsets None = new(0, 0, 0, 0);

    [Fact]
    public void TargetNearTop_BubbleGoesBelow_CentredOnTarget()
    {
        var p = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(180, 100, 40, 48), 240, 100, None);

        Assert.True(p.BelowTarget);
        Assert.Equal(200 - 120, p.X);
        Assert.Equal(148 + 12, p.Y);
    }

    [Fact]
    public void TargetNearBottom_BubbleGoesAbove()
    {
        var p = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(180, 700, 40, 48), 240, 100, None);

        Assert.False(p.BelowTarget);
        Assert.Equal(700 - 12 - 100, p.Y);
    }

    [Fact]
    public void BubbleIsKeptInsideHorizontally()
    {
        var left = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(0, 100, 20, 20), 240, 100, None);
        var right = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(380, 100, 20, 20), 240, 100, None);

        Assert.Equal(16, left.X);
        Assert.Equal(400 - 16 - 240, right.X);
    }

    [Fact]
    public void InsetsAreRespected()
    {
        var insets = new CoachInsets(10, 50, 10, 40);
        var top = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(100, 0, 40, 40), 240, 100, insets);
        var bottom = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(100, 760, 40, 40), 240, 100, insets);
        var side = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(0, 300, 10, 10), 240, 100, insets);

        Assert.True(top.Y >= 50 + 16);
        Assert.True(bottom.Y + 100 <= 800 - 40 - 16);
        Assert.Equal(10 + 16, side.X);
    }

    [Fact]
    public void LittleRoomEitherSide_PicksTheRoomierOne()
    {
        // Tall target in the middle of a short screen: more room above than below.
        var p = CoachMarkPlacement.PlaceBubble(400, 400, new CoachRect(100, 150, 200, 200), 300, 120, None);

        Assert.False(p.BelowTarget);
        Assert.InRange(p.Y, 16, 400 - 16 - 120);
    }

    [Fact]
    public void NoTarget_CentresOnScreen()
    {
        var p = CoachMarkPlacement.PlaceBubble(400, 800, null, 240, 100, None);

        Assert.Equal(80, p.X);
        Assert.Equal(350, p.Y);
    }

    [Fact]
    public void BubbleWiderThanScreen_DoesNotThrow()
    {
        var p = CoachMarkPlacement.PlaceBubble(200, 800, new CoachRect(50, 50, 20, 20), 400, 100, None);

        Assert.Equal(16, p.X);
    }
}
