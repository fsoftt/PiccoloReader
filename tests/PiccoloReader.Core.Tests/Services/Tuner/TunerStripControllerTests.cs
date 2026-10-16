using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class TunerStripControllerTests
{
    private sealed class FakeStripView : ITunerStripView
    {
        public List<(bool Visible, int Offset, TunerStripBucket Bucket)> Calls { get; } = [];

        public void Apply(bool visible, int offsetPx, TunerStripBucket bucket) => Calls.Add((visible, offsetPx, bucket));
    }

    private sealed class FakeStripPreferences : ITunerStripPreferenceService
    {
        public bool IsEnabled { get; set; }
    }

    private readonly FakeTunerInput _input = new();
    private readonly FakeTunerPreferenceService _tunerPreferences = new();
    private readonly FakeStripPreferences _stripPreferences = new() { IsEnabled = true };
    private readonly FakeMicrophonePermissionService _permission = new();
    private readonly FakeStripView _view = new();
    private readonly List<Action> _posted = [];
    private readonly TunerStripController _controller;

    public TunerStripControllerTests()
    {
        _controller = new TunerStripController(_input, _tunerPreferences, _stripPreferences, _permission, _posted.Add);
        _controller.Bind(_view);
        _controller.SetTravel(100f);
    }

    private static float HzAtCents(float cents, float a4 = 440f) => a4 * MathF.Pow(2f, cents / 1200f);

    private static TunerReading Pitch(float cents, float a4 = 440f) => new(true, HzAtCents(cents, a4), 0f);

    private void Flush()
    {
        var pending = _posted.ToArray();
        _posted.Clear();
        foreach (var action in pending)
        {
            action();
        }
    }

    private async Task StartAsync()
    {
        await _controller.AttachAsync();
        _view.Calls.Clear();
    }

    [Fact]
    public async Task Reading_in_tune_centers_marker_green()
    {
        await StartAsync();
        _input.Raise(Pitch(0f));
        Flush();

        Assert.Equal((true, 50, TunerStripBucket.InTune), _view.Calls.Single());
    }

    [Theory]
    [InlineData(-30f, 20)]
    [InlineData(30f, 80)]
    [InlineData(-10f, 40)]
    public async Task Offset_follows_cents_across_track(float cents, int expectedPx)
    {
        await StartAsync();
        _input.Raise(Pitch(cents));
        Flush();

        Assert.Equal(expectedPx, _view.Calls.Single().Offset);
    }

    [Fact]
    public async Task Cents_are_relative_to_the_nearest_note_not_to_a4()
    {
        await StartAsync();
        // 20 cents sharp of A#4, a semitone above A4.
        _input.Raise(Pitch(120f));
        Flush();

        Assert.Equal(70, _view.Calls.Single().Offset);
    }

    [Fact]
    public async Task Cents_follow_the_configured_a4()
    {
        _tunerPreferences.Stored = new TunerSettings { A4Hz = 442 };
        await StartAsync();
        _input.Raise(new TunerReading(true, 442f, 0f));
        Flush();

        Assert.Equal((true, 50, TunerStripBucket.InTune), _view.Calls.Single());
    }

    [Theory]
    [InlineData(0f, TunerStripBucket.InTune)]
    [InlineData(4.5f, TunerStripBucket.InTune)]
    [InlineData(-4.5f, TunerStripBucket.InTune)]
    [InlineData(5.5f, TunerStripBucket.Close)]
    [InlineData(-5.5f, TunerStripBucket.Close)]
    [InlineData(14.5f, TunerStripBucket.Close)]
    [InlineData(15.5f, TunerStripBucket.Far)]
    [InlineData(-15.5f, TunerStripBucket.Far)]
    [InlineData(45f, TunerStripBucket.Far)]
    public async Task Color_bucket_follows_deviation(float cents, TunerStripBucket expected)
    {
        await StartAsync();
        _input.Raise(Pitch(cents));
        Flush();

        Assert.Equal(expected, _view.Calls.Single().Bucket);
    }

    [Fact]
    public async Task Offset_stays_inside_the_track_at_both_extremes()
    {
        await StartAsync();
        _input.Raise(Pitch(49.9f));
        Flush();
        _input.Raise(Pitch(-49.9f));
        Flush();

        Assert.All(_view.Calls, call => Assert.InRange(call.Offset, 0, 100));
    }

    [Fact]
    public async Task Silence_hides_the_strip()
    {
        await StartAsync();
        _input.Raise(Pitch(10f));
        Flush();
        _input.Raise(new TunerReading(false, 0f, 0f));
        Flush();

        Assert.False(_view.Calls[^1].Visible);
    }

    [Fact]
    public async Task Movement_under_one_pixel_posts_nothing()
    {
        await StartAsync();
        _input.Raise(Pitch(10f));
        Flush();
        _input.Raise(Pitch(10.2f));
        _input.Raise(Pitch(9.8f));

        Assert.Empty(_posted);
        Assert.Single(_view.Calls);
    }

    [Fact]
    public async Task Color_change_posts_even_without_a_pixel_change()
    {
        _controller.SetTravel(10f);
        await StartAsync();
        _input.Raise(Pitch(4.6f));
        Flush();
        _input.Raise(Pitch(5.4f));
        Flush();

        Assert.Equal(TunerStripBucket.InTune, _view.Calls[0].Bucket);
        Assert.Equal(TunerStripBucket.Close, _view.Calls[1].Bucket);
    }

    [Fact]
    public async Task Only_one_post_is_pending_and_it_applies_the_latest_reading()
    {
        await StartAsync();
        _input.Raise(Pitch(-20f));
        _input.Raise(Pitch(0f));
        _input.Raise(Pitch(20f));

        Assert.Single(_posted);
        Flush();
        Assert.Equal(70, _view.Calls.Single().Offset);
    }

    [Fact]
    public async Task Reading_in_flight_after_detach_is_dropped()
    {
        await StartAsync();
        _input.Raise(Pitch(10f));
        _controller.Detach();
        _view.Calls.Clear();
        Flush();

        Assert.Empty(_view.Calls);
    }

    [Fact]
    public async Task Attach_starts_the_input_when_enabled_and_granted()
    {
        await _controller.AttachAsync();

        Assert.True(_controller.IsOn);
        Assert.Equal(1, _input.StartCount);
        Assert.Equal(1, _input.SubscriberCount);
    }

    [Fact]
    public async Task Attach_does_nothing_when_the_preference_is_off()
    {
        _stripPreferences.IsEnabled = false;
        await _controller.AttachAsync();

        Assert.False(_controller.IsOn);
        Assert.Equal(0, _input.StartCount);
    }

    [Fact]
    public async Task Attach_never_prompts_without_permission()
    {
        _permission.Granted = false;
        await _controller.AttachAsync();

        Assert.False(_controller.IsOn);
        Assert.Equal(0, _permission.EnsureCount);
        Assert.Equal(0, _input.StartCount);
        Assert.True(_stripPreferences.IsEnabled);
    }

    [Fact]
    public async Task Detach_stops_and_unsubscribes()
    {
        await _controller.AttachAsync();
        _controller.Detach();

        Assert.False(_controller.IsOn);
        Assert.Equal(1, _input.StopCount);
        Assert.Equal(0, _input.SubscriberCount);
    }

    [Fact]
    public void Detach_without_attach_does_not_stop_the_shared_input()
    {
        _controller.Detach();

        Assert.Equal(0, _input.StopCount);
    }

    [Fact]
    public async Task Resume_after_stop_restarts()
    {
        await _controller.AttachAsync();
        _controller.Detach();
        await _controller.AttachAsync();

        Assert.True(_controller.IsOn);
        Assert.Equal(2, _input.StartCount);
        Assert.Equal(1, _input.SubscriberCount);
    }

    [Fact]
    public async Task Failed_start_turns_the_strip_off_but_keeps_the_preference()
    {
        _input.StartResult = TunerStartResult.Failed;
        await _controller.AttachAsync();

        Assert.False(_controller.IsOn);
        Assert.Equal(0, _input.SubscriberCount);
        Assert.True(_stripPreferences.IsEnabled);
    }

    [Fact]
    public async Task Toggle_on_with_permission_saves_the_preference_and_starts()
    {
        _stripPreferences.IsEnabled = false;

        var on = await _controller.ToggleAsync();

        Assert.True(on);
        Assert.True(_stripPreferences.IsEnabled);
        Assert.Equal(1, _input.StartCount);
    }

    [Fact]
    public async Task Toggle_on_prompts_and_a_denial_keeps_it_off()
    {
        _stripPreferences.IsEnabled = false;
        _permission.Granted = false;

        var on = await _controller.ToggleAsync();

        Assert.False(on);
        Assert.Equal(1, _permission.EnsureCount);
        Assert.False(_stripPreferences.IsEnabled);
        Assert.Equal(0, _input.StartCount);
    }

    [Fact]
    public async Task Toggle_on_prompts_when_the_preference_is_on_but_permission_is_missing()
    {
        _permission.Granted = false;
        _permission.GrantOnPrompt = true;
        await _controller.AttachAsync();

        var on = await _controller.ToggleAsync();

        Assert.True(on);
        Assert.Equal(1, _permission.EnsureCount);
        Assert.Equal(1, _input.StartCount);
    }

    [Fact]
    public async Task Toggle_off_stops_and_clears_the_preference()
    {
        await _controller.AttachAsync();

        var on = await _controller.ToggleAsync();

        Assert.False(on);
        Assert.False(_stripPreferences.IsEnabled);
        Assert.Equal(1, _input.StopCount);
    }
}
