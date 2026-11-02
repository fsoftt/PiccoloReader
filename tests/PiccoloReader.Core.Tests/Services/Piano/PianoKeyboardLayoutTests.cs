using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Core.Tests.Services.Piano;

public class PianoKeyboardLayoutTests
{
    private const int C4 = 60;

    [Fact]
    public void Constructor_TenWhiteKeysFromC4_EndsAtE5WithSevenBlackKeys()
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        Assert.Equal(10, sut.WhiteKeys.Count);
        Assert.Equal(76, sut.LastWhiteKey);
        Assert.Equal([61, 63, 66, 68, 70, 73, 75], sut.BlackKeys.Select(k => k.Note));
        Assert.All(sut.WhiteKeys, k => Assert.Equal(100f, k.Right - k.Left, 3));
    }

    [Fact]
    public void BlackKeys_AreNarrowerAndShorterThanWhiteKeys()
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        var black = sut.BlackKeys[0];
        Assert.Equal(58f, black.Right - black.Left, 3);
        Assert.Equal(186f, black.Bottom, 3);
        Assert.Equal(100f, (black.Left + black.Right) / 2f, 3);
    }

    [Fact]
    public void HitTest_BlackKeyOverlappingWhite_BlackWins()
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        Assert.True(sut.TryHitTest(100f, 50f, out var note, out _));
        Assert.Equal(61, note);
    }

    [Fact]
    public void HitTest_BelowBlackKey_HitsWhiteKey()
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        Assert.True(sut.TryHitTest(100f, 250f, out var note, out _));
        Assert.Equal(62, note);
    }

    [Theory]
    [InlineData(0f, 60)]
    [InlineData(99.9f, 60)]
    [InlineData(100f, 62)]
    [InlineData(999.9f, 76)]
    public void HitTest_WhiteKeyEdges_PicksExpectedKey(float x, int expected)
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        Assert.True(sut.TryHitTest(x, 290f, out var note, out _));
        Assert.Equal(expected, note);
    }

    [Theory]
    [InlineData(-1f, 10f)]
    [InlineData(1000f, 10f)]
    [InlineData(500f, -1f)]
    [InlineData(500f, 300f)]
    public void HitTest_OutsideTheView_ReturnsFalse(float x, float y)
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        Assert.False(sut.TryHitTest(x, y, out _, out _));
    }

    [Fact]
    public void HitTest_VelocityGrowsTowardTheFrontEdge()
    {
        var sut = new PianoKeyboardLayout(C4, 10, 1000f, 300f);

        sut.TryHitTest(50f, 1f, out _, out var root);
        sut.TryHitTest(50f, 299f, out _, out var tip);

        Assert.InRange(root, PianoKeyboardLayout.MinVelocity, PianoKeyboardLayout.MinVelocity + 2);
        Assert.InRange(tip, PianoKeyboardLayout.MaxVelocity - 2, PianoKeyboardLayout.MaxVelocity);
    }

    [Fact]
    public void ClampWhiteKeyCount_StaysBetweenSevenAndThirtySix()
    {
        Assert.Equal(7, PianoKeyboardLayout.ClampWhiteKeyCount(0));
        Assert.Equal(14, PianoKeyboardLayout.ClampWhiteKeyCount(14));
        Assert.Equal(36, PianoKeyboardLayout.ClampWhiteKeyCount(99));
    }

    [Fact]
    public void ClampFirstWhiteKey_KeepsTheWindowInsideTheKeyboard()
    {
        Assert.Equal(21, PianoKeyboardLayout.ClampFirstWhiteKey(0, 10));
        Assert.Equal(60, PianoKeyboardLayout.ClampFirstWhiteKey(61, 10));

        var last = PianoKeyboardLayout.ClampFirstWhiteKey(200, 10);
        var layout = new PianoKeyboardLayout(last, 10, 100f, 10f);
        Assert.Equal(108, layout.LastWhiteKey);
    }

    [Fact]
    public void ShiftOctaves_MovesBySevenWhiteKeysAndClamps()
    {
        Assert.Equal(72, PianoKeyboardLayout.ShiftOctaves(C4, 10, 1));
        Assert.Equal(48, PianoKeyboardLayout.ShiftOctaves(C4, 10, -1));
        Assert.Equal(21, PianoKeyboardLayout.ShiftOctaves(21, 10, -1));
        Assert.Equal(PianoKeyboardLayout.ClampFirstWhiteKey(108, 10), PianoKeyboardLayout.ShiftOctaves(96, 10, 1));
    }

    [Fact]
    public void FullKeyboard_HasFiftyTwoWhiteKeys()
    {
        var sut = new PianoKeyboardLayout(21, 36, 1000f, 100f);

        Assert.Equal(52, PianoKeyboardLayout.WhiteKeyTotal);
        Assert.Equal(21, sut.WhiteKeys[0].Note);
    }

    [Fact]
    public void MiniMap_WindowAndDragMapBackToTheSameFirstKey()
    {
        var (left, width) = PianoKeyboardLayout.MiniMapWindow(C4, 10, 520f);

        Assert.Equal(100f, width, 3);
        Assert.Equal(C4, PianoKeyboardLayout.FirstWhiteKeyFromMiniMap(left, 520f, 10));
        Assert.Equal(21, PianoKeyboardLayout.FirstWhiteKeyFromMiniMap(-50f, 520f, 10));
    }
}
