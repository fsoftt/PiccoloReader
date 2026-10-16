using PiccoloReader.Core.Services.PageTurn;

namespace PiccoloReader.Core.Tests.Services.PageTurn;

public class PedalKeyTrackerTests
{
    private static readonly long A = PedalKeyTracker.IdOf(3, 93);
    private static readonly long B = PedalKeyTracker.IdOf(3, 92);

    [Fact]
    public void Normal_press_turns_once_and_swallows_its_up()
    {
        var t = new PedalKeyTracker();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 0));
        t.Record(A, handled: true);
        Assert.Equal(PedalKeyDecision.Swallow, t.OnUp(A));
        // Up consumed the entry: a second Up would be an orphan.
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnUp(A));
    }

    [Fact]
    public void Held_press_swallows_repeats_and_up()
    {
        var t = new PedalKeyTracker();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 0));
        t.Record(A, true);
        Assert.Equal(PedalKeyDecision.Swallow, t.OnDown(A, 1));
        Assert.Equal(PedalKeyDecision.Swallow, t.OnDown(A, 2));
        Assert.Equal(PedalKeyDecision.Swallow, t.OnUp(A));
    }

    [Fact]
    public void Orphan_up_is_a_press_to_try()
    {
        var t = new PedalKeyTracker();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnUp(A));
    }

    [Fact]
    public void Held_press_with_eaten_down_turns_on_first_repeat_only()
    {
        var t = new PedalKeyTracker();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 1));
        t.Record(A, true);
        Assert.Equal(PedalKeyDecision.Swallow, t.OnDown(A, 2));
        Assert.Equal(PedalKeyDecision.Swallow, t.OnUp(A));
    }

    [Fact]
    public void Passed_through_down_does_not_turn_on_its_up()
    {
        var t = new PedalKeyTracker();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 0));
        t.Record(A, handled: false);
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnDown(A, 1));
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnUp(A));
    }

    [Fact]
    public void Two_overlapping_keys_are_independent()
    {
        var t = new PedalKeyTracker();
        t.OnDown(A, 0); t.Record(A, true);
        t.OnDown(B, 0); t.Record(B, false);
        Assert.Equal(PedalKeyDecision.Swallow, t.OnUp(A));
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnUp(B));
    }

    [Fact]
    public void Same_key_on_different_devices_is_tracked_separately()
    {
        var t = new PedalKeyTracker();
        var other = PedalKeyTracker.IdOf(4, 93);
        t.OnDown(A, 0); t.Record(A, true);
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnUp(other));
    }

    [Fact]
    public void Fresh_press_clears_stale_state_from_a_lost_up()
    {
        var t = new PedalKeyTracker();
        t.OnDown(A, 0); t.Record(A, true); // up lost
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 0));
        t.Record(A, true);
        Assert.Equal(PedalKeyDecision.Swallow, t.OnUp(A));
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var t = new PedalKeyTracker();
        t.OnDown(A, 0); t.Record(A, true);
        t.Clear();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnUp(A));
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 3));
    }

    [Fact]
    public void Full_tracker_drops_the_oldest_entry()
    {
        var t = new PedalKeyTracker();
        for (var i = 0; i < 9; i++)
        {
            var id = PedalKeyTracker.IdOf(1, 100 + i);
            t.OnDown(id, 0);
            t.Record(id, true);
        }

        Assert.Equal(PedalKeyDecision.TryTurn, t.OnUp(PedalKeyTracker.IdOf(1, 100)));
        Assert.Equal(PedalKeyDecision.Swallow, t.OnUp(PedalKeyTracker.IdOf(1, 108)));
    }

    [Fact]
    public void Down_passed_through_by_a_guard_does_not_turn_on_its_up_after_the_guard_clears()
    {
        // Enter in a text field: the Down passes through, the field's action
        // closes the overlay, then the Up arrives with every guard now open.
        var t = new PedalKeyTracker();
        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 0));
        t.Record(A, handled: false);
        // (guards change here)
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnUp(A));
    }

    [Fact]
    public void Down_before_the_viewer_subscribed_does_not_turn_on_its_up_after_it_did()
    {
        // Enter on the Library opens a sheet; the viewer subscribes before
        // the Up arrives.
        var input = new PageTurnInput();
        var t = new PedalKeyTracker();

        Assert.Equal(PedalKeyDecision.TryTurn, t.OnDown(A, 0));
        t.Record(A, input.Handle(PageTurnKey.Enter, false)); // no listener: false

        input.Requested += _ => true; // viewer appears

        Assert.Equal(PedalKeyDecision.PassThrough, t.OnUp(A));
    }

    [Fact]
    public void Repeats_of_a_passed_through_down_pass_through_too()
    {
        var t = new PedalKeyTracker();
        t.OnDown(A, 0);
        t.Record(A, false);
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnDown(A, 1));
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnDown(A, 2));
        Assert.Equal(PedalKeyDecision.PassThrough, t.OnUp(A));
    }
}
