using LedMenu.Core.Display;

namespace LedMenu.Core.Tests;

public class DisplayMatcherTests
{
    private static DisplayInfo D(string path, string name = "Monitor", int x = 0, int y = 0, int w = 1920, int h = 1080,
        int dpi = 96, bool primary = false, long handle = 1, string dev = @"\\.\DISPLAY1") =>
        new(path, dev, name, x, y, w, h, dpi, dpi, primary, handle);

    [Fact]
    public void No_identity_is_NotConfigured()
    {
        var r = DisplayMatcher.Match(null, new[] { D("a") });
        Assert.Equal(MatchKind.NotConfigured, r.Kind);
        Assert.Null(r.Display);
        Assert.False(r.IsAvailable);
    }

    [Fact]
    public void Matches_by_device_path()
    {
        var wanted = D("path-B", "VP2", x: 1920, handle: 2);
        var id = DisplayIdentity.From(wanted);
        var r = DisplayMatcher.Match(id, new[] { D("path-A", handle: 1, primary: true), wanted });
        Assert.Equal(MatchKind.Exact, r.Kind);
        Assert.Same(wanted, r.Display);
    }

    [Fact]
    public void Match_survives_windows_renumbering_and_moved_position()
    {
        var before = D("path-B", "VP2", x: 1920, dev: @"\\.\DISPLAY2");
        var id = DisplayIdentity.From(before);
        // Windows now calls it DISPLAY1, it sits to the left, and resolution changed.
        var after = D("path-B", "VP2", x: -1176, w: 1176, h: 672, dev: @"\\.\DISPLAY1");
        var r = DisplayMatcher.Match(id, new[] { D("path-A", primary: true, dev: @"\\.\DISPLAY2"), after });
        Assert.Equal(MatchKind.Exact, r.Kind);
        Assert.Same(after, r.Display);
    }

    [Fact]
    public void Path_comparison_ignores_case()
    {
        var id = DisplayIdentity.From(D(@"\\?\DISPLAY#ABC#1"));
        var r = DisplayMatcher.Match(id, new[] { D(@"\\?\display#abc#1") });
        Assert.Equal(MatchKind.Exact, r.Kind);
    }

    [Fact]
    public void Missing_display_is_NotFound_and_never_replaced_by_another()
    {
        var id = DisplayIdentity.From(D("path-VP2", "VP2", x: 1920));
        var r = DisplayMatcher.Match(id, new[] { D("path-A", "Dell", primary: true) });
        Assert.Equal(MatchKind.NotFound, r.Kind);
        Assert.Null(r.Display);
    }

    [Fact]
    public void Changed_path_with_single_identical_lookalike_is_Fallback()
    {
        var id = DisplayIdentity.From(D("old-path", "VP2", x: 1920));
        var r = DisplayMatcher.Match(id, new[] { D("a", "Dell", primary: true), D("new-path", "VP2", x: 1920) });
        Assert.Equal(MatchKind.Fallback, r.Kind);
        Assert.Equal("new-path", r.Display!.DevicePath);
        Assert.True(r.IsAvailable);
    }

    [Fact]
    public void Two_identical_lookalikes_are_ambiguous_so_NotFound()
    {
        var id = DisplayIdentity.From(D("old-path", "VP2", x: 1920));
        var r = DisplayMatcher.Match(id, new[] { D("n1", "VP2", x: 1920), D("n2", "VP2", x: 1920) });
        Assert.Equal(MatchKind.NotFound, r.Kind);
    }

    [Fact]
    public void Lookalike_with_different_resolution_is_not_accepted()
    {
        var id = DisplayIdentity.From(D("old-path", "VP2", x: 1920));
        var r = DisplayMatcher.Match(id, new[] { D("new-path", "VP2", x: 1920, w: 1280, h: 720) });
        Assert.Equal(MatchKind.NotFound, r.Kind);
    }

    [Fact]
    public void Two_identical_models_on_different_ports_are_told_apart_by_path()
    {
        var left = D("port-1", "Same Model", x: 0, handle: 1);
        var right = D("port-2", "Same Model", x: 1920, handle: 2);
        var r = DisplayMatcher.Match(DisplayIdentity.From(right), new[] { left, right });
        Assert.Equal(MatchKind.Exact, r.Kind);
        Assert.Equal("port-2", r.Display!.DevicePath);
    }

    [Fact]
    public void Blank_device_path_never_matches_by_path()
    {
        var id = new DisplayIdentity { DevicePath = "", FriendlyName = "X", Width = 10, Height = 10 };
        var r = DisplayMatcher.Match(id, new[] { D("", "Other") });
        Assert.Equal(MatchKind.NotFound, r.Kind);
    }

    [Fact]
    public void Order_is_left_to_right_then_top_to_bottom()
    {
        var a = D("a", x: 1920, y: 0);
        var b = D("b", x: 0, y: 0);
        var c = D("c", x: 0, y: 1080);
        var ordered = DisplayMatcher.Order(new[] { a, c, b });
        Assert.Equal(new[] { "b", "c", "a" }, ordered.Select(d => d.DevicePath));
    }

    [Theory]
    [InlineData(96, 100)]
    [InlineData(120, 125)]
    [InlineData(144, 150)]
    [InlineData(192, 200)]
    public void Scale_percent_follows_dpi(int dpi, int expected) =>
        Assert.Equal(expected, D("a", dpi: dpi).ScalePercent);

    [Fact]
    public void Different_monitors_can_report_different_scaling()
    {
        var a = D("a", dpi: 144);
        var b = D("b", dpi: 96);
        Assert.NotEqual(a.ScalePercent, b.ScalePercent);
    }

    [Fact]
    public void Identity_detects_changed_geometry()
    {
        var d = D("a", "VP2", x: 0);
        var id = DisplayIdentity.From(d);
        Assert.False(id.DiffersFrom(d));
        Assert.True(id.DiffersFrom(d with { X = 100 }));
        Assert.True(id.DiffersFrom(d with { Width = 800 }));
    }
}

public class SelectionPolicyTests
{
    private static DisplayInfo D(string path, bool primary = false, long handle = 1) =>
        new(path, @"\\.\DISPLAY1", "M", 0, 0, 1920, 1080, 96, 96, primary, handle);

    [Fact]
    public void Secondary_display_with_operator_elsewhere_has_no_warnings()
    {
        var op = D("op", primary: true, handle: 1);
        var led = D("led", handle: 2);
        Assert.Empty(SelectionPolicy.CheckLedOutput(led, new[] { op, led }, op, operatorWindowMonitor: 1));
    }

    [Fact]
    public void Only_display_warns()
    {
        var only = D("only", primary: true);
        Assert.Contains(SelectionPolicy.CheckLedOutput(only, new[] { only }, null, 0), w => w.Contains("only display"));
    }

    [Fact]
    public void Operator_selection_warns()
    {
        var op = D("op", handle: 1);
        var other = D("other", handle: 2);
        Assert.Contains(SelectionPolicy.CheckLedOutput(op, new[] { op, other }, op, 0), w => w.Contains("Operator display"));
    }

    [Fact]
    public void Display_hosting_operator_window_warns()
    {
        var a = D("a", handle: 5);
        var b = D("b", handle: 6);
        Assert.Contains(SelectionPolicy.CheckLedOutput(a, new[] { a, b }, null, operatorWindowMonitor: 5),
            w => w.Contains("operator window"));
    }

    [Fact]
    public void Primary_display_warns()
    {
        var a = D("a", primary: true, handle: 1);
        var b = D("b", handle: 2);
        Assert.Contains(SelectionPolicy.CheckLedOutput(a, new[] { a, b }, null, 0), w => w.Contains("primary"));
    }

    [Fact]
    public void Operator_equal_to_led_output_warns()
    {
        var led = D("led");
        Assert.NotEmpty(SelectionPolicy.CheckOperator(led, led));
        Assert.Empty(SelectionPolicy.CheckOperator(D("op", handle: 9), led));
        Assert.Empty(SelectionPolicy.CheckOperator(led, null));
    }
}
