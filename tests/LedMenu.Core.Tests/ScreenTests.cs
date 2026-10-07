using LedMenu.Core.Display;
using LedMenu.Core.Screens;

namespace LedMenu.Core.Tests;

public class ScreenValidatorTests
{
    private static readonly CanvasSize Canvas1080 = new(1920, 1080);

    private static Screen S(string name, int x, int y, int w, int h, bool enabled = true) =>
        new() { Name = name, X = x, Y = y, Width = w, Height = h, Enabled = enabled };

    private static IReadOnlyList<ScreenIssue> V(CanvasSize? canvas, params Screen[] screens) =>
        ScreenValidator.Validate(screens, canvas);

    [Fact]
    public void Example_layout_from_the_spec_is_clean()
    {
        var issues = V(Canvas1080, S("Food Menu", 0, 0, 336, 672), S("Drink Menu", 336, 0, 336, 672));
        Assert.Empty(issues);   // touching edges are not an overlap
    }

    [Fact]
    public void Nothing_assumes_the_screen_is_336_by_672()
    {
        Assert.Empty(V(Canvas1080, S("Landscape", 100, 50, 1176, 336), S("Tiny", 0, 500, 1, 1), S("Big", 1300, 0, 620, 1080)));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(100, -1)]
    public void Non_positive_size_is_an_error(int w, int h)
    {
        var s = S("A", 0, 0, w, h);
        var i = Assert.Single(V(Canvas1080, s), x => x.Code == IssueCode.NonPositiveSize);
        Assert.Equal(IssueSeverity.Error, i.Severity);
        Assert.Equal(s.Id, i.ScreenId);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-100, -100)]
    public void Negative_position_is_an_error(int x, int y) =>
        Assert.Contains(V(Canvas1080, S("A", x, y, 100, 100)),
            i => i.Code == IssueCode.NegativePosition && i.Severity == IssueSeverity.Error);

    [Fact]
    public void Screen_exactly_filling_the_canvas_is_valid() =>
        Assert.Empty(V(Canvas1080, S("Full", 0, 0, 1920, 1080)));

    [Theory]
    [InlineData(1, 0, 1920, 1080)]     // one pixel past the right edge
    [InlineData(0, 1, 1920, 1080)]     // one pixel past the bottom edge
    [InlineData(1700, 0, 336, 672)]    // partially outside right
    [InlineData(0, 800, 336, 672)]     // partially outside bottom
    [InlineData(5000, 5000, 10, 10)]   // entirely outside
    public void Screen_outside_the_canvas_is_an_error_and_is_not_clipped(int x, int y, int w, int h)
    {
        var s = S("A", x, y, w, h);
        var issues = V(Canvas1080, s);
        Assert.Contains(issues, i => i.Code == IssueCode.OutsideCanvas && i.Severity == IssueSeverity.Error);
        // validation never edits the screen
        Assert.Equal((x, y, w, h), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void Canvas_shrinking_flags_an_existing_screen_without_changing_it()
    {
        var s = S("Wall", 0, 0, 1176, 672);
        Assert.Empty(V(new CanvasSize(1920, 1080), s));
        var issues = V(new CanvasSize(1024, 768), s);
        Assert.Contains(issues, i => i.Code == IssueCode.OutsideCanvas && i.Severity == IssueSeverity.Error);
        Assert.Equal(1176, s.Width);
    }

    [Fact]
    public void Unknown_canvas_skips_bounds_checks_only()
    {
        Assert.Empty(V(null, S("A", 99999, 99999, 336, 672)));
        Assert.Contains(V(null, S("A", 0, 0, 0, 5)), i => i.Code == IssueCode.NonPositiveSize);
    }

    [Fact]
    public void Disabled_screen_outside_canvas_is_only_a_warning()
    {
        var i = Assert.Single(V(Canvas1080, S("Off", 3000, 0, 336, 672, enabled: false)));
        Assert.Equal(IssueSeverity.Warning, i.Severity);
    }

    [Fact]
    public void Overlap_of_enabled_screens_warns_on_both_but_is_allowed()
    {
        var a = S("A", 0, 0, 400, 400);
        var b = S("B", 300, 100, 400, 400);
        var issues = V(Canvas1080, a, b);
        var overlaps = issues.Where(i => i.Code == IssueCode.Overlap).ToList();
        Assert.Equal(2, overlaps.Count);
        Assert.All(overlaps, o => Assert.Equal(IssueSeverity.Warning, o.Severity));
        Assert.Contains(overlaps, o => o.ScreenId == a.Id && o.OtherScreenId == b.Id && o.Message.Contains("100×300"));
        Assert.Contains(overlaps, o => o.ScreenId == b.Id && o.OtherScreenId == a.Id);
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Disabled_screen_does_not_cause_overlap_warnings()
    {
        Assert.Empty(V(Canvas1080, S("A", 0, 0, 400, 400), S("B", 100, 100, 400, 400, enabled: false)));
    }

    [Fact]
    public void Identical_screens_overlap() =>
        Assert.Equal(2, V(Canvas1080, S("A", 10, 10, 50, 50), S("B", 10, 10, 50, 50)).Count(i => i.Code == IssueCode.Overlap));

    [Fact]
    public void Edge_touching_is_not_overlap()
    {
        Assert.False(ScreenValidator.Overlaps(S("A", 0, 0, 100, 100), S("B", 100, 0, 100, 100)));
        Assert.False(ScreenValidator.Overlaps(S("A", 0, 0, 100, 100), S("B", 0, 100, 100, 100)));
        Assert.True(ScreenValidator.Overlaps(S("A", 0, 0, 100, 100), S("B", 99, 99, 100, 100)));
    }

    [Fact]
    public void Huge_values_do_not_overflow()
    {
        var issues = V(Canvas1080, S("A", int.MaxValue, 0, int.MaxValue, 10), S("B", 0, 0, 10, 10));
        Assert.Contains(issues, i => i.Code == IssueCode.OutsideCanvas);
    }

    [Fact]
    public void Empty_and_duplicate_names_warn()
    {
        var issues = V(Canvas1080, S("", 0, 0, 10, 10), S("Food", 100, 0, 10, 10), S("food ", 200, 0, 10, 10));
        Assert.Contains(issues, i => i.Code == IssueCode.EmptyName);
        Assert.Equal(2, issues.Count(i => i.Code == IssueCode.DuplicateName));
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Renderable_excludes_disabled_and_invalid_but_keeps_overlapping()
    {
        var ok = S("Ok", 0, 0, 336, 672);
        var overlapping = S("Overlapping", 300, 0, 336, 672);
        var disabled = S("Disabled", 700, 0, 100, 100, enabled: false);
        var outside = S("Outside", 1900, 0, 336, 672);
        var bad = S("Bad", 0, 0, 0, 0);
        var r = ScreenValidator.RenderableScreens(new[] { ok, overlapping, disabled, outside, bad }, Canvas1080);
        Assert.Equal(new[] { ok.Id, overlapping.Id }, r.Select(s => s.Id));
    }
}

public class ScreenLayoutTests
{
    [Fact]
    public void Geometry_problems_do_not_make_the_file_invalid()
    {
        var layout = new ScreenLayout();
        layout.Screens.Add(new Screen { Name = "Off canvas", X = -50, Y = 99999, Width = 0, Height = -4 });
        Assert.Null(ScreenLayout.ValidateFile(layout));
    }

    [Fact]
    public void Structural_problems_are_rejected()
    {
        Assert.NotNull(ScreenLayout.ValidateFile(null));
        Assert.NotNull(ScreenLayout.ValidateFile(new ScreenLayout { SchemaVersion = 0 }));
        Assert.NotNull(ScreenLayout.ValidateFile(new ScreenLayout { SchemaVersion = 99 }));
        Assert.NotNull(ScreenLayout.ValidateFile(new ScreenLayout { Screens = null! }));
        var dup = new ScreenLayout();
        var id = Guid.NewGuid();
        dup.Screens.Add(new Screen { Id = id });
        dup.Screens.Add(new Screen { Id = id });
        Assert.NotNull(ScreenLayout.ValidateFile(dup));
    }

    [Fact]
    public void Empty_layout_is_valid() => Assert.Null(ScreenLayout.ValidateFile(new ScreenLayout()));
}

public class OutputCanvasResolverTests
{
    private static DisplayInfo D(int w, int h) => new("p", "d", "Mon", 0, 0, w, h, 96, 96, false, 1);

    [Fact]
    public void Connected_output_gives_live_size()
    {
        var info = OutputCanvasResolver.Resolve(new(MatchKind.Exact, D(1920, 1080)), null);
        Assert.Equal(CanvasSource.Live, info.Source);
        Assert.Equal(new CanvasSize(1920, 1080), info.Size);
    }

    [Fact]
    public void Missing_output_uses_last_known_size()
    {
        var saved = new DisplayIdentity { FriendlyName = "VP2", Width = 1920, Height = 1080 };
        var info = OutputCanvasResolver.Resolve(new(MatchKind.NotFound, null), saved);
        Assert.Equal(CanvasSource.LastKnown, info.Source);
        Assert.Equal(new CanvasSize(1920, 1080), info.Size);
    }

    [Fact]
    public void Unconfirmed_fallback_does_not_count_as_live()
    {
        var saved = new DisplayIdentity { FriendlyName = "VP2", Width = 1920, Height = 1080 };
        var info = OutputCanvasResolver.Resolve(new(MatchKind.Fallback, D(1280, 720)), saved);
        Assert.Equal(CanvasSource.LastKnown, info.Source);
        Assert.Equal(new CanvasSize(1920, 1080), info.Size);
    }

    [Fact]
    public void Nothing_selected_is_unknown()
    {
        var info = OutputCanvasResolver.Resolve(new(MatchKind.NotConfigured, null), null);
        Assert.Equal(CanvasSource.Unknown, info.Source);
        Assert.Null(info.Size);
    }
}

public class ScreenPlacementTests
{
    private static Screen S(int x, int y, int w, int h, bool enabled = true) =>
        new() { X = x, Y = y, Width = w, Height = h, Enabled = enabled };

    [Fact]
    public void First_screen_goes_to_top_left() =>
        Assert.Equal((0, 0), ScreenPlacement.NextFreePosition(Array.Empty<Screen>(), new CanvasSize(1920, 1080), 336, 672));

    [Fact]
    public void Second_screen_goes_to_the_right_of_the_first() =>
        Assert.Equal((336, 0), ScreenPlacement.NextFreePosition(new[] { S(0, 0, 336, 672) }, new CanvasSize(1920, 1080), 336, 672));

    [Fact]
    public void Skips_to_the_next_row_when_the_row_is_full()
    {
        var existing = new[] { S(0, 0, 960, 500), S(960, 0, 960, 500) };
        Assert.Equal((0, 500), ScreenPlacement.NextFreePosition(existing, new CanvasSize(1920, 1080), 336, 400));
    }

    [Fact]
    public void Disabled_screens_do_not_block_placement() =>
        Assert.Equal((0, 0), ScreenPlacement.NextFreePosition(new[] { S(0, 0, 336, 672, enabled: false) }, new CanvasSize(1920, 1080), 336, 672));

    [Fact]
    public void Falls_back_to_origin_when_nothing_fits() =>
        Assert.Equal((0, 0), ScreenPlacement.NextFreePosition(new[] { S(0, 0, 1920, 1080) }, new CanvasSize(1920, 1080), 336, 672));

    [Fact]
    public void Unknown_canvas_still_places_beside_existing() =>
        Assert.Equal((336, 0), ScreenPlacement.NextFreePosition(new[] { S(0, 0, 336, 672) }, null, 336, 672));
}

public class SnapHelperTests
{
    [Fact] public void Snaps_within_threshold() => Assert.Equal(336, SnapHelper.Snap(339, new[] { 0, 336, 1920 }, 5));
    [Fact] public void Leaves_value_outside_threshold() => Assert.Equal(345, SnapHelper.Snap(345, new[] { 0, 336 }, 5));
    [Fact] public void Picks_the_nearest_target() => Assert.Equal(102, SnapHelper.Snap(103, new[] { 100, 102, 106 }, 5));

    [Fact]
    public void Moving_span_snaps_by_its_end_edge()
    {
        // a 336-wide screen dragged so its right edge is 3px short of the 1920 canvas edge
        Assert.Equal(1584, SnapHelper.SnapSpanStart(1581, 336, new[] { 0, 1920 }, 5));
    }

    [Fact]
    public void Moving_span_snaps_by_its_start_edge() =>
        Assert.Equal(336, SnapHelper.SnapSpanStart(333, 336, new[] { 0, 336 }, 5));

    [Fact]
    public void Moving_span_far_from_targets_is_unchanged() =>
        Assert.Equal(700, SnapHelper.SnapSpanStart(700, 336, new[] { 0, 1920 }, 5));
}
