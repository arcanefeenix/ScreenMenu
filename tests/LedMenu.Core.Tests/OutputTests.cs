using LedMenu.Core.Display;

namespace LedMenu.Core.Tests;

public class OutputStartPolicyTests
{
    private static DisplayInfo D(string path, long handle = 1, bool primary = false, int x = 0, int w = 1920, int h = 1080) =>
        new(path, "dev", "Monitor", x, 0, w, h, 96, 96, primary, handle);

    private static MatchResult Exact(DisplayInfo d) => new(MatchKind.Exact, d);

    [Fact]
    public void Not_configured_refuses()
    {
        var r = OutputStartPolicy.Evaluate(new(MatchKind.NotConfigured, null), new[] { D("a") }, 0);
        Assert.False(r.CanStart);
        Assert.Null(r.Display);
        Assert.Contains("selected", r.Refusal);
    }

    [Fact]
    public void Missing_display_refuses_and_names_no_substitute()
    {
        var r = OutputStartPolicy.Evaluate(new(MatchKind.NotFound, null), new[] { D("a", primary: true) }, 0);
        Assert.False(r.CanStart);
        Assert.Null(r.Display);
        Assert.Contains("No other display was used", r.Refusal);
    }

    [Fact]
    public void Unconfirmed_fallback_match_refuses()
    {
        var d = D("new");
        var r = OutputStartPolicy.Evaluate(new(MatchKind.Fallback, d), new[] { D("a"), d }, 0);
        Assert.False(r.CanStart);
        Assert.Contains("confirmed", r.Refusal);
    }

    [Fact]
    public void Exact_second_display_starts_without_warnings()
    {
        var op = D("op", handle: 1, primary: true);
        var led = D("led", handle: 2, x: 1920);
        var r = OutputStartPolicy.Evaluate(Exact(led), new[] { op, led }, operatorWindowMonitor: 1);
        Assert.True(r.CanStart);
        Assert.Same(led, r.Display);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Operator_window_on_output_display_warns_but_may_start()
    {
        var led = D("led", handle: 2);
        var other = D("other", handle: 1);
        var r = OutputStartPolicy.Evaluate(Exact(led), new[] { other, led }, operatorWindowMonitor: 2);
        Assert.True(r.CanStart);
        Assert.Single(r.Warnings);
        Assert.Contains("operator window", r.Warnings[0]);
    }

    [Fact]
    public void Only_display_warns()
    {
        var only = D("only", primary: true);
        var r = OutputStartPolicy.Evaluate(Exact(only), new[] { only }, 0);
        Assert.True(r.CanStart);
        Assert.Contains("only display", r.Warnings.Single());
    }

    [Fact]
    public void Primary_display_alone_is_not_rewarned_at_every_start()
    {
        var op = D("op", handle: 1);
        var primaryLed = D("led", handle: 2, primary: true);
        var r = OutputStartPolicy.Evaluate(Exact(primaryLed), new[] { op, primaryLed }, operatorWindowMonitor: 1);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Zero_size_display_refuses()
    {
        var r = OutputStartPolicy.Evaluate(Exact(D("z", w: 0, h: 0)), new[] { D("z", w: 0, h: 0) }, 0);
        Assert.False(r.CanStart);
    }
}

public class OutputGeometryTests
{
    private static readonly PixelRect Bounds = new(-3840, 0, 3840, 2160);

    [Fact]
    public void Matching_values_agree_including_negative_origin()
    {
        var r = OutputGeometry.Evaluate(Bounds, Bounds, 3840, 2160, 144, 144, 144, 144);
        Assert.True(r.Agrees);
        Assert.Empty(r.Problems);
    }

    [Fact]
    public void Smaller_client_area_is_reported()
    {
        var r = OutputGeometry.Evaluate(Bounds, Bounds, 3838, 2159, 144, 144, 144, 144);
        Assert.False(r.Agrees);
        Assert.Contains(r.Problems, p => p.Contains("Client area"));
    }

    [Fact]
    public void Offset_window_is_reported()
    {
        var r = OutputGeometry.Evaluate(Bounds, new PixelRect(-3839, 0, 3840, 2160), 3840, 2160, 144, 144, 144, 144);
        Assert.False(r.Agrees);
        Assert.Contains(r.Problems, p => p.Contains("Window rectangle"));
    }

    [Fact]
    public void Dpi_disagreement_between_wpf_and_monitor_is_reported()
    {
        // e.g. the window kept the operator monitor's DPI after being moved to a monitor at 100%
        var r = OutputGeometry.Evaluate(Bounds, Bounds, 3840, 2160, 144, 144, 96, 96);
        Assert.False(r.Agrees);
        Assert.Contains(r.Problems, p => p.Contains("DPI"));
    }

    [Theory]
    [InlineData(1920, 96, 1920.0)]
    [InlineData(1920, 120, 1536.0)]
    [InlineData(3840, 144, 2560.0)]
    [InlineData(1176, 96, 1176.0)]
    public void Pixels_to_dips(int px, double dpi, double expected) =>
        Assert.Equal(expected, OutputGeometry.ToDips(px, dpi), 6);

    [Theory]
    [InlineData(1536.0, 120, 1920)]
    [InlineData(2560.0, 144, 3840)]
    [InlineData(336.0, 96, 336)]
    public void Dips_to_pixels(double dips, double dpi, int expected) =>
        Assert.Equal(expected, OutputGeometry.ToPixels(dips, dpi));

    [Theory]
    [InlineData(1920, 96)]
    [InlineData(1920, 120)]
    [InlineData(1920, 144)]
    [InlineData(3840, 192)]
    [InlineData(1176, 120)]
    [InlineData(672, 144)]
    public void Round_trip_is_exact_for_common_dpis(int px, int dpi) =>
        Assert.Equal(px, OutputGeometry.ToPixels(OutputGeometry.ToDips(px, dpi), dpi));
}
