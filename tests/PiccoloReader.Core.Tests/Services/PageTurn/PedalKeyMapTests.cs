using PiccoloReader.Core.Services.PageTurn;

namespace PiccoloReader.Core.Tests.Services.PageTurn;

public class PedalKeyMapTests
{
    [Theory]
    [InlineData(PageTurnKey.PageDown)]
    [InlineData(PageTurnKey.DpadRight)]
    [InlineData(PageTurnKey.DpadDown)]
    [InlineData(PageTurnKey.Space)]
    [InlineData(PageTurnKey.Enter)]
    [InlineData(PageTurnKey.NumpadEnter)]
    [InlineData(PageTurnKey.MediaNext)]
    public void Next_keys(PageTurnKey key) =>
        Assert.Equal(PageTurnIntent.Next, PedalKeyMap.Resolve(key, false));

    [Theory]
    [InlineData(PageTurnKey.PageUp)]
    [InlineData(PageTurnKey.DpadLeft)]
    [InlineData(PageTurnKey.DpadUp)]
    [InlineData(PageTurnKey.MediaPrevious)]
    public void Previous_keys(PageTurnKey key) =>
        Assert.Equal(PageTurnIntent.Previous, PedalKeyMap.Resolve(key, false));

    [Fact]
    public void Shift_space_is_previous() =>
        Assert.Equal(PageTurnIntent.Previous, PedalKeyMap.Resolve(PageTurnKey.Space, true));

    [Fact]
    public void Other_key_is_none() =>
        Assert.Equal(PageTurnIntent.None, PedalKeyMap.Resolve(PageTurnKey.Other, false));

    [Fact]
    public void Input_is_not_handled_without_listener()
    {
        var input = new PageTurnInput();
        Assert.False(input.Handle(PageTurnKey.PageDown, false));
    }

    [Fact]
    public void Input_raises_intent_for_mapped_key_only()
    {
        var input = new PageTurnInput();
        var seen = new List<PageTurnIntent>();
        input.Requested += seen.Add;

        Assert.True(input.Handle(PageTurnKey.PageUp, false));
        Assert.False(input.Handle(PageTurnKey.Other, false));
        Assert.Equal(new[] { PageTurnIntent.Previous }, seen);
    }

    [Fact]
    public void Unsubscribed_input_passes_keys_through()
    {
        var input = new PageTurnInput();
        void Handler(PageTurnIntent _) { }
        input.Requested += Handler;
        input.Requested -= Handler;
        Assert.False(input.HasListener);
        Assert.False(input.Handle(PageTurnKey.Space, false));
    }
}
