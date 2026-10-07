using LedMenu.Core.Logging;

namespace LedMenu.Core.Tests;

public class ErrorDialogThrottleTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0);

    [Fact]
    public void The_first_error_may_show_a_dialog() => Assert.True(new ErrorDialogThrottle().TryShow("boom", T0));

    [Fact]
    public void While_a_dialog_is_open_no_other_opens_even_for_a_different_message()
    {
        var t = new ErrorDialogThrottle();
        Assert.True(t.TryShow("a", T0));
        Assert.False(t.TryShow("a", T0.AddSeconds(1)));
        Assert.False(t.TryShow("b", T0.AddSeconds(1)));
    }

    [Fact]
    public void A_timer_failing_every_quarter_second_opens_one_dialog_not_hundreds()
    {
        var t = new ErrorDialogThrottle();
        var shown = 0;
        for (var i = 0; i < 400; i++)       // 100 seconds of 250 ms ticks
        {
            if (t.TryShow("same error", T0.AddMilliseconds(i * 250))) { shown++; t.Dismissed(T0.AddMilliseconds(i * 250 + 1)); }
        }
        Assert.Equal(1, shown);
    }

    [Fact]
    public void The_same_error_may_show_again_after_the_quiet_period()
    {
        var t = new ErrorDialogThrottle(TimeSpan.FromMinutes(2));
        Assert.True(t.TryShow("x", T0));
        t.Dismissed(T0.AddSeconds(5));
        Assert.False(t.TryShow("x", T0.AddMinutes(1)));
        Assert.True(t.TryShow("x", T0.AddMinutes(3)));
    }

    [Fact]
    public void A_different_error_after_dismissal_waits_out_the_quiet_period_too()
    {
        var t = new ErrorDialogThrottle(TimeSpan.FromMinutes(2));
        t.TryShow("x", T0);
        t.Dismissed(T0.AddSeconds(5));
        Assert.True(t.TryShow("brand new", T0.AddSeconds(10)));
    }
}
