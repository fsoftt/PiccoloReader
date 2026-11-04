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

    [Fact]
    public void GestureReserve_PushesTheBubbleBelowTheHand()
    {
        var p = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(180, 100, 40, 48), 240, 100, None, gestureReserve: 70);

        Assert.True(p.BelowTarget);
        Assert.Equal(148 + 12 + 70, p.Y);
    }

    [Fact]
    public void GestureReserve_MovesTheBubbleAboveWhenBelowNoLongerFits()
    {
        // 800 - 16 - (700 + 12) = 72 below: fits 60 without a reserve, not with one.
        var without = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(180, 660, 40, 40), 240, 60, None);
        var with = CoachMarkPlacement.PlaceBubble(400, 800, new CoachRect(180, 660, 40, 40), 240, 60, None, gestureReserve: 70);

        Assert.True(without.BelowTarget);
        Assert.False(with.BelowTarget);
        Assert.Equal(660 - 12 - 60, with.Y);
    }

    [Fact]
    public void Beside_LeftZone_PlacesBubbleInTheRightArea_VerticallyCentred()
    {
        // Left tap zone: 30% of a 1000-wide, 2000-tall screen.
        var p = CoachMarkPlacement.PlaceBubbleBeside(
            1000, 2000, new CoachRect(0, 100, 300, 1800), 360, width => 120, None);

        Assert.NotNull(p);
        Assert.Equal(312, p!.Value.X);
        Assert.Equal(360, p.Value.Width);
        Assert.Equal((2000 - 120) / 2.0, p.Value.Y);
        Assert.False(p.Value.BelowTarget);
    }

    [Fact]
    public void Beside_RightZone_PlacesBubbleInTheLeftArea_WidthLimitedToIt()
    {
        // Free area on the left: 1000 - 300 - 12 - 16 = 672 wide; bubble asked for 800.
        var p = CoachMarkPlacement.PlaceBubbleBeside(
            1000, 2000, new CoachRect(700, 100, 300, 1800), 800, width => width < 700 ? 150 : 100, None);

        Assert.NotNull(p);
        Assert.Equal(672, p!.Value.Width);
        Assert.Equal(700 - 12 - 672, p.Value.X);
        Assert.Equal((2000 - 150) / 2.0, p.Value.Y);
    }

    [Fact]
    public void Beside_ReturnsNull_WhenTheBubbleFitsAboveOrBelow()
    {
        var p = CoachMarkPlacement.PlaceBubbleBeside(
            1000, 2000, new CoachRect(0, 100, 300, 300), 360, width => 120, None);

        Assert.Null(p);
    }

    [Fact]
    public void Beside_ReturnsNull_WhenTheFreeAreaIsTooNarrow()
    {
        var p = CoachMarkPlacement.PlaceBubbleBeside(
            400, 2000, new CoachRect(0, 100, 300, 1800), 360, width => 120, None);

        Assert.Null(p);
    }
}
