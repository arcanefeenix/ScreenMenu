using LedMenu.Core.Display;

namespace LedMenu.Core.Tests;

public class OutputDisplayWatchTests
{
    private static DisplayInfo D(string path = "p1", int x = -3840, int y = 0, int w = 3840, int h = 2160) =>
        new(path, "DISPLAY2", "LED", x, y, w, h, 144, 144, false, 1);

    private static MatchResult Exact(DisplayInfo d) => new(MatchKind.Exact, d);

    [Fact]
    public void Unchanged_display_means_nothing_to_do() =>
        Assert.Equal(DisplayChangeAction.None, OutputDisplayWatch.WhileRunning(Exact(D()), D()));

    [Fact]
    public void The_display_vanishing_stops_output()
    {
        Assert.Equal(DisplayChangeAction.Stop, OutputDisplayWatch.WhileRunning(new(MatchKind.NotFound, null), D()));
        Assert.Equal(DisplayChangeAction.Stop, OutputDisplayWatch.WhileRunning(new(MatchKind.NotConfigured, null), D()));
    }

    [Fact]
    public void A_lookalike_display_is_never_good_enough_to_keep_output_running()
    {
        var lookalike = D(path: "different-path");
        Assert.Equal(DisplayChangeAction.Stop, OutputDisplayWatch.WhileRunning(new(MatchKind.Fallback, lookalike), D()));
    }

    [Theory]
    [InlineData(0, 0, 3840, 2160)]       // moved
    [InlineData(-3840, 0, 1920, 1080)]   // resolution changed
    [InlineData(-3840, 100, 3840, 2160)] // arrangement changed
    public void The_same_display_changing_geometry_is_followed_in_place(int x, int y, int w, int h) =>
        Assert.Equal(DisplayChangeAction.Reposition, OutputDisplayWatch.WhileRunning(Exact(D(x: x, y: y, w: w, h: h)), D()));

    [Fact]
    public void A_dpi_only_change_needs_no_move() =>
        Assert.Equal(DisplayChangeAction.None, OutputDisplayWatch.WhileRunning(Exact(D() with { DpiX = 96, DpiY = 96 }), D()));

    private static OutputStartDecision CanStart(params string[] warnings) => new(true, D(), null, warnings);
    private static OutputStartDecision CannotStart => new(false, null, "not connected", Array.Empty<string>());

    [Fact]
    public void Output_lost_with_its_display_comes_back_by_itself_when_the_same_display_returns() =>
        Assert.True(OutputDisplayWatch.ShouldRestore(stoppedByDisplayLoss: true, CanStart()));

    [Fact]
    public void Output_the_operator_stopped_is_never_restarted_by_itself() =>
        Assert.False(OutputDisplayWatch.ShouldRestore(stoppedByDisplayLoss: false, CanStart()));

    [Fact]
    public void While_the_display_is_still_missing_nothing_restarts() =>
        Assert.False(OutputDisplayWatch.ShouldRestore(true, CannotStart));

    [Fact]
    public void A_restart_that_would_need_a_confirmation_prompt_is_left_to_the_operator() =>
        Assert.False(OutputDisplayWatch.ShouldRestore(true, CanStart("The operator window is on the LED display.")));
}
