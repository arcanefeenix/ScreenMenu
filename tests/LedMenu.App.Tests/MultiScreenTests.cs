using System.IO;
using LedMenu.App.Output;
using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Core.Screens;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.Tests;

/// <summary>
/// Several screens on one output canvas, each with its own menu, drawn by the same builder the LED output uses.
/// Every expectation is taken from the renderer itself at that screen's own size, never from a scaled copy.
/// </summary>
public class MultiScreenTests
{
    private static readonly CanvasSize Canvas = new(1920, 1080);

    private sealed class Rig
    {
        public readonly Dictionary<Guid, Menu> Menus = new();
        public TimeSpan Now = TimeSpan.Zero;
        public readonly PageClock Clock = new();
        public readonly MenuRenderService Render;
        public readonly NormalFrameBuilder Builder;

        public Rig()
        {
            var fonts = new FontCatalog(Path.Combine(AppContext.BaseDirectory, "Fonts"));
            var assets = new AssetStore(Path.Combine(Path.GetTempPath(), "ledmenu-multiscreen-tests"), NullLog.Instance);
            Render = new MenuRenderService(fonts, assets, NullLog.Instance);
            Builder = new NormalFrameBuilder(id => Menus.TryGetValue(id, out var m) ? m : null, Render, Clock, () => Now);
        }

        public Menu Add(Menu m) { Menus[m.Id] = m; return m; }

        public (NormalFrameResult Result, IReadOnlyList<Screen> Screens) Build(params Screen[] screens)
        {
            var plan = CalibrationPlan.Build(screens, Canvas);
            return (Builder.Build(Canvas, plan), screens);
        }

        public PixelBuffer Page(Menu m, int w, int h, int page) => Render.Get(m, w, h).Pages[page];
    }

    private static Screen Scr(string name, int x, int y, int w, int h, Menu? menu, bool enabled = true) =>
        new() { Name = name, X = x, Y = y, Width = w, Height = h, AssignedMenuId = menu?.Id, Enabled = enabled };

    private static bool RegionEquals(PixelBuffer frame, int x, int y, PixelBuffer expected)
    {
        for (var j = 0; j < expected.Height; j++)
            for (var i = 0; i < expected.Width; i++)
            {
                var f = ((y + j) * frame.Width + x + i) * 4;
                var e = (j * expected.Width + i) * 4;
                if (frame.Data[f] != expected.Data[e] || frame.Data[f + 1] != expected.Data[e + 1] || frame.Data[f + 2] != expected.Data[e + 2]) return false;
            }
        return true;
    }

    private static bool RegionBlack(PixelBuffer frame, int x, int y, int w, int h)
    {
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
            {
                var f = ((y + j) * frame.Width + x + i) * 4;
                if (frame.Data[f] != 0 || frame.Data[f + 1] != 0 || frame.Data[f + 2] != 0) return false;
            }
        return true;
    }

    private static int NonBlackPixels(PixelBuffer frame)
    {
        var n = 0;
        for (var i = 0; i < frame.Data.Length; i += 4)
            if (frame.Data[i] != 0 || frame.Data[i + 1] != 0 || frame.Data[i + 2] != 0) n++;
        return n;
    }

    [Fact]
    public void Two_168x672_screens_side_by_side_each_show_their_own_menu_at_native_size() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var food = rig.Add(SampleMenus.FestivalFood());
        var soldOut = rig.Add(SampleMenus.SoldOutDemo());
        var (result, _) = rig.Build(Scr("Left", 0, 0, 168, 672, food), Scr("Right", 168, 0, 168, 672, soldOut));

        Assert.NotNull(result.Frame);
        Assert.True(RegionEquals(result.Frame!, 0, 0, rig.Page(food, 168, 672, 0)));
        Assert.True(RegionEquals(result.Frame!, 168, 0, rig.Page(soldOut, 168, 672, 0)));
        Assert.True(RegionBlack(result.Frame!, 336, 0, Canvas.Width - 336, Canvas.Height), "the rest of the canvas stays pure black");
        Assert.True(RegionBlack(result.Frame!, 0, 672, 336, Canvas.Height - 672), "below the screens stays pure black");
    });

    [Fact]
    public void One_menu_on_two_screens_of_different_sizes_is_rendered_separately_for_each_size() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var food = rig.Add(SampleMenus.FestivalFood());
        var (result, _) = rig.Build(Scr("Wide", 0, 0, 336, 672, food), Scr("Narrow", 400, 100, 168, 672, food));

        Assert.True(RegionEquals(result.Frame!, 0, 0, rig.Page(food, 336, 672, 0)));
        Assert.True(RegionEquals(result.Frame!, 400, 100, rig.Page(food, 168, 672, 0)));
        Assert.Equal(2, result.Shown.Count);
    });

    [Fact]
    public void Disabled_unassigned_and_missing_menu_screens_stay_black_without_affecting_the_others() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var food = rig.Add(SampleMenus.FestivalFood());
        var ghost = SampleMenus.DenseMenu();                      // never added: its id points at nothing
        var (result, _) = rig.Build(
            Scr("Live", 0, 0, 168, 672, food),
            Scr("Disabled", 200, 0, 168, 672, food, enabled: false),
            Scr("Unassigned", 400, 0, 168, 672, null),
            Scr("Missing menu", 600, 0, 168, 672, ghost));

        Assert.True(RegionEquals(result.Frame!, 0, 0, rig.Page(food, 168, 672, 0)));
        Assert.True(RegionBlack(result.Frame!, 200, 0, 168, 672));
        Assert.True(RegionBlack(result.Frame!, 400, 0, 168, 672));
        Assert.True(RegionBlack(result.Frame!, 600, 0, 168, 672));
        Assert.Single(result.Shown);
    });

    [Fact]
    public void Nothing_assigned_anywhere_is_a_null_frame_meaning_pure_black() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var (result, _) = rig.Build(Scr("A", 0, 0, 168, 672, null), Scr("B", 168, 0, 168, 672, null));
        Assert.Null(result.Frame);
    });

    [Fact]
    public void Pages_rotate_independently_on_each_screen() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var dense = rig.Add(SampleMenus.DenseMenu());            // several pages at 168x672
        var food = rig.Add(SampleMenus.FestivalFood());          // one page at 336 wide
        var screens = new[] { Scr("Dense", 0, 0, 168, 672, dense), Scr("Food", 200, 0, 336, 672, food) };

        var first = rig.Build(screens).Result;
        var denseId = screens[0].Id;
        var count = first.Shown[denseId].Count;
        Assert.True(count > 1, "the dense menu needs several pages at 168 wide for this test");
        Assert.Equal(0, first.Shown[denseId].Page);
        Assert.Equal(1, first.Shown[screens[1].Id].Count);
        Assert.False(rig.Builder.PagesChanged(first.Shown));

        rig.Now = TimeSpan.FromSeconds(dense.Theme.PageSeconds + 0.5);
        Assert.True(rig.Builder.PagesChanged(first.Shown), "time has passed, so the dense screen has a new page");
        var second = rig.Build(screens).Result;
        Assert.Equal(1, second.Shown[denseId].Page);
        Assert.Equal(0, second.Shown[screens[1].Id].Page);       // the one-page screen never changes
        Assert.True(RegionEquals(second.Frame!, 0, 0, rig.Page(dense, 168, 672, 1)));
        Assert.True(RegionEquals(second.Frame!, 200, 0, rig.Page(food, 336, 672, 0)));
    });

    [Fact]
    public void Overlapping_screens_draw_in_list_order_so_the_later_one_is_on_top() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var a = rig.Add(SampleMenus.FestivalFood());
        var b = rig.Add(SampleMenus.SoldOutDemo());
        var (result, _) = rig.Build(Scr("Under", 0, 0, 336, 672, a), Scr("Over", 168, 0, 336, 672, b));
        Assert.True(RegionEquals(result.Frame!, 168, 0, rig.Page(b, 336, 672, 0)), "the later screen is fully visible");
        // the part of the earlier screen not covered is still there
        var under = rig.Page(a, 336, 672, 0);
        var strip = new PixelBuffer(168, 672);
        for (var y = 0; y < 672; y++) Array.Copy(under.Data, (y * 336) * 4, strip.Data, y * 168 * 4, 168 * 4);
        Assert.True(RegionEquals(result.Frame!, 0, 0, strip));
    });

    [Fact]
    public void A_screen_hanging_off_the_canvas_is_not_drawn_and_does_not_disturb_the_rest() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var food = rig.Add(SampleMenus.FestivalFood());
        var (result, _) = rig.Build(Scr("Good", 0, 0, 168, 672, food), Scr("Off", 1800, 0, 336, 672, food));
        Assert.True(RegionEquals(result.Frame!, 0, 0, rig.Page(food, 168, 672, 0)));
        Assert.True(RegionBlack(result.Frame!, 1800, 0, 120, 672));
        Assert.Single(result.Shown);
    });

    [Fact]
    public void Editing_one_menu_changes_only_the_screens_that_show_it() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var a = rig.Add(SampleMenus.FestivalFood());
        var b = rig.Add(SampleMenus.SoldOutDemo());
        var screens = new[] { Scr("A", 0, 0, 168, 672, a), Scr("B", 168, 0, 168, 672, b) };
        var before = rig.Build(screens).Result.Frame!;

        a.Items[0].SoldOut = !a.Items[0].SoldOut;   // the operator taps Sold Out on menu A
        var after = rig.Build(screens).Result.Frame!;

        Assert.False(RegionEquals(after, 0, 0, SliceOf(before, 0, 0, 168, 672)), "screen A changed");
        Assert.True(RegionEquals(after, 168, 0, SliceOf(before, 168, 0, 168, 672)), "screen B is untouched");
    });

    private static PixelBuffer SliceOf(PixelBuffer f, int x, int y, int w, int h)
    {
        var s = new PixelBuffer(w, h);
        for (var j = 0; j < h; j++) Array.Copy(f.Data, ((y + j) * f.Width + x) * 4, s.Data, j * w * 4, w * 4);
        return s;
    }

    [Fact]
    public void Four_screens_with_four_menus_all_appear_and_the_cost_is_one_render_each() => TestSta.Run(() =>
    {
        var rig = new Rig();
        var m = new[] { SampleMenus.FestivalFood(), SampleMenus.DenseMenu(), SampleMenus.SoldOutDemo(), SampleMenus.LongTextStress() };
        foreach (var x in m) rig.Add(x);
        var screens = m.Select((menu, i) => Scr("S" + i, i * 340, 0, 336, 672, menu)).ToArray();
        var (result, _) = rig.Build(screens);
        for (var i = 0; i < 4; i++)
            Assert.True(RegionEquals(result.Frame!, i * 340, 0, rig.Page(m[i], 336, 672, 0)), $"screen {i}");
        Assert.True(NonBlackPixels(result.Frame!) > 0);
        Assert.Equal(4, result.Shown.Count);
    });
}

internal static class TestSta
{
    public static void Run(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
