using LedMenu.Core.Calibration;

namespace LedMenu.Core.Tests;

public class BlackoutStateTests
{
    [Fact]
    public void Starts_off_so_the_app_always_starts_live() => Assert.False(new BlackoutState().IsActive);

    [Fact]
    public void Cannot_be_switched_on_while_output_is_stopped()
    {
        var b = new BlackoutState();
        Assert.False(b.CanToggle(outputRunning: false));
        Assert.False(b.Toggle(outputRunning: false));
        Assert.False(b.IsActive);
    }

    [Fact]
    public void Toggles_on_and_off_while_running()
    {
        var b = new BlackoutState();
        Assert.True(b.Toggle(true));
        Assert.True(b.IsActive);
        Assert.True(b.Toggle(true));
        Assert.False(b.IsActive);
    }

    [Fact]
    public void While_on_it_can_always_be_switched_off_even_if_output_just_went_away()
    {
        var b = new BlackoutState();
        b.Toggle(true);
        Assert.True(b.CanToggle(outputRunning: false));
    }

    [Fact]
    public void Stopping_output_clears_it_so_the_next_start_is_never_black()
    {
        var b = new BlackoutState();
        b.Toggle(true);
        Assert.True(b.OutputStopped());
        Assert.False(b.IsActive);
        Assert.False(b.OutputStopped());
    }

    [Fact]
    public void Blackout_replaces_the_frame_with_pure_black_and_off_passes_it_through_untouched()
    {
        var frame = new PixelBuffer(4, 4);
        var b = new BlackoutState();
        Assert.Same(frame, b.Apply(frame));
        b.Toggle(true);
        Assert.Null(b.Apply(frame));            // null = pure black on the output window
        Assert.Null(b.Apply<PixelBuffer>(null));
        b.Toggle(true);
        Assert.Same(frame, b.Apply(frame));     // the underlying picture was never touched
    }
}
