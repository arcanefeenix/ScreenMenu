using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedMenu.App.Controls;
using LedMenu.App.Output;
using LedMenu.Core.Calibration;
using LedMenu.Core.Layout;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Core.Screens;
using LedMenu.Core.Video;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.Tests;

public class FrameSurfaceTests
{
    private static PixelBuffer Pattern(int w, int h, int seed)
    {
        var f = new PixelBuffer(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                f.Data[i] = (byte)(x * 5 + seed);
                f.Data[i + 1] = (byte)(y * 7 + seed * 3);
                f.Data[i + 2] = (byte)(x ^ y ^ seed);
            }
        return f;
    }

    private static byte[] Render(FrameSurface s, int w, int h)
    {
        s.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        s.Arrange(new Rect(s.DesiredSize));
        s.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(s);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return px;
    }

    private static int Differences(byte[] shown, PixelBuffer expected)
    {
        var n = 0;
        for (var i = 0; i < expected.Data.Length; i += 4)
            if (shown[i] != expected.Data[i] || shown[i + 1] != expected.Data[i + 1] || shown[i + 2] != expected.Data[i + 2]) n++;
        return n;
    }

    [Fact]
    public void A_whole_picture_is_shown_pixel_for_pixel() => UiPump.Run(_ =>
    {
        var s = new FrameSurface();
        var f = Pattern(120, 80, 1);
        s.Show(f, 96, 96);
        Assert.Equal((120, 80), s.PictureSize);
        Assert.Equal(0, Differences(Render(s, 120, 80), f));
    });

    [Fact]
    public void Updating_a_rectangle_refreshes_exactly_that_rectangle_and_nothing_else() => UiPump.Run(_ =>
    {
        var s = new FrameSurface();
        var f = Pattern(120, 80, 1);
        s.Show(f, 96, 96);

        var other = Pattern(120, 80, 9);
        // change two separate rectangles in the picture, but tell the surface about only the first
        void Paint(PixelRegion r) { for (var y = r.Y; y < r.Y + r.Height; y++) for (var x = r.X; x < r.X + r.Width; x++) Array.Copy(other.Data, (y * 120 + x) * 4, f.Data, (y * 120 + x) * 4, 4); }
        var told = new PixelRegion(10, 10, 30, 20);
        var untold = new PixelRegion(70, 40, 30, 20);
        Paint(told); Paint(untold);
        s.Update(f, told);

        var shown = Render(s, 120, 80);
        var firstWrong = 0; var secondStillOld = 0; var secondCount = 0;
        for (var y = 0; y < 80; y++)
            for (var x = 0; x < 120; x++)
            {
                var i = (y * 120 + x) * 4;
                var matchesNew = shown[i] == f.Data[i] && shown[i + 1] == f.Data[i + 1] && shown[i + 2] == f.Data[i + 2];
                if (x >= told.X && x < told.X + told.Width && y >= told.Y && y < told.Y + told.Height && !matchesNew) firstWrong++;
                if (x >= untold.X && x < untold.X + untold.Width && y >= untold.Y && y < untold.Y + untold.Height)
                {
                    secondCount++;
                    if (!matchesNew) secondStillOld++;      // still the previous picture's pixel
                }
            }
        Assert.Equal(0, firstWrong);                                      // the rectangle that was updated shows the new pixels
        Assert.True(secondStillOld > secondCount / 2, $"only {secondStillOld} of {secondCount} pixels in the untold rectangle are still the old ones, so more than the told rectangle was refreshed");
        // and the proof of "only that rectangle": telling the surface about the second one now brings the whole picture up to date
        s.Update(f, untold);
        Assert.Equal(0, Differences(Render(s, 120, 80), f));
    });

    [Fact]
    public void An_update_for_a_picture_of_a_different_size_shows_the_whole_new_picture_instead_of_tearing() => UiPump.Run(_ =>
    {
        var s = new FrameSurface();
        s.Show(Pattern(120, 80, 1), 96, 96);
        var bigger = Pattern(140, 90, 4);
        s.Update(bigger, new PixelRegion(0, 0, 10, 10));
        Assert.Equal((140, 90), s.PictureSize);
        Assert.Equal(0, Differences(Render(s, 140, 90), bigger));
    });

    [Fact]
    public void An_update_before_anything_was_shown_shows_the_whole_picture() => UiPump.Run(_ =>
    {
        var s = new FrameSurface();
        var f = Pattern(64, 48, 2);
        s.Update(f, new PixelRegion(5, 5, 5, 5));
        Assert.Equal(0, Differences(Render(s, 64, 48), f));
    });

    [Fact]
    public void A_region_that_hangs_off_the_picture_or_is_empty_is_clamped_and_harmless() => UiPump.Run(_ =>
    {
        var s = new FrameSurface();
        var f = Pattern(64, 48, 2);
        s.Show(f, 96, 96);
        s.Update(f, new PixelRegion(50, 40, 100, 100));
        s.Update(f, new PixelRegion(-20, -20, 30, 30));
        s.Update(f, new PixelRegion(500, 500, 10, 10));
        s.Update(f, new PixelRegion(5, 5, 0, 0));
        Assert.Equal(0, Differences(Render(s, 64, 48), f));
    });

    [Fact]
    public void Clearing_goes_back_to_pure_black_nothing_drawn() => UiPump.Run(_ =>
    {
        var s = new FrameSurface();
        s.Show(Pattern(64, 48, 2), 96, 96);
        s.Show(null, 96, 96);
        Assert.Equal((0, 0), s.PictureSize);
    });

    [Fact]
    public void The_scaled_preview_redraws_when_the_pixels_changed_in_place_and_the_revision_moved() => UiPump.Run(_ =>
    {
        var f = new PixelBuffer(20, 20);
        for (var i = 0; i < f.Data.Length; i += 4) { f.Data[i + 2] = 255; }                    // red
        var c = new ScaledFrameControl { Frame = f, Region = new PixelRegion(0, 0, 20, 20), Scale = 1 };
        Render(c, 20, 20);

        for (var i = 0; i < f.Data.Length; i += 4) { f.Data[i + 2] = 0; f.Data[i + 1] = 255; }  // the SAME buffer, now green
        c.Revision++;
        var after = Render(c, 20, 20);
        Assert.Equal(255, after[1]);
        Assert.Equal(0, after[2]);
    });

    private static byte[] Render(ScaledFrameControl c, int w, int h)
    {
        c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        c.Arrange(new Rect(c.DesiredSize));
        c.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(c);
        var pw = (int)Math.Round(c.DesiredSize.Width * dpi.DpiScaleX);
        var ph = (int)Math.Round(c.DesiredSize.Height * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(c);
        var px = new byte[pw * ph * 4];
        rtb.CopyPixels(px, pw * 4, 0);
        return px;
    }
}

/// <summary>Video screens in the output: one player per screen, pictures into the shared picture, menus beside them at the same time.</summary>
public class VideoHostAndOutputTests : IDisposable
{
    private static readonly string Media = Path.Combine(AppContext.BaseDirectory, "media");
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ledmenu-host-" + Guid.NewGuid().ToString("N"));

    public VideoHostAndOutputTests() => Directory.CreateDirectory(_work);
    public void Dispose() { try { Directory.Delete(_work, true); } catch { } }

    private string? Resolve(string name)
    {
        var a = Path.Combine(Media, name);
        if (File.Exists(a)) return a;
        var b = Path.Combine(_work, name);
        return File.Exists(b) ? b : null;
    }

    private static Screen VideoScreen(string name, int x, int y, int w, int h, params string[] clips) => new()
    {
        Name = name, X = x, Y = y, Width = w, Height = h, ContentKind = ScreenContentKind.Video,
        Playlist = new VideoPlaylist { Loop = true, Items = clips.Select(c => new VideoItem { FileName = c, DisplayName = c, Width = 168, Height = 672 }).ToList() },
    };

    private static Screen MenuScreen(string name, int x, int y, int w, int h, Menu menu) =>
        new() { Name = name, X = x, Y = y, Width = w, Height = h, AssignedMenuId = menu.Id };

    private static string Colour(PixelBuffer f, int x, int y)
    {
        var i = (y * f.Width + x) * 4;
        int b = f.Data[i], g = f.Data[i + 1], r = f.Data[i + 2];
        if (r < 24 && g < 24 && b < 24) return "black";
        if (r > 225 && g < 30 && b < 30) return "red";
        if (g > 200 && r < 40 && b < 40) return "green";
        if (b > 225 && r < 30 && g < 30) return "blue";
        return $"other({r},{g},{b})";
    }

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

    private VideoScreenHost Host(UiPump ui) => new(ui.Dispatcher, Resolve, NullLog.Instance);

    private static CalibrationPlan Plan(params Screen[] screens) => CalibrationPlan.Build(screens, new CanvasSize(1920, 1080));

    [Fact]
    public void Only_video_screens_get_a_player_each_sized_to_its_screen() => UiPump.Run(ui =>
    {
        using var host = Host(ui);
        var menu = SampleMenus.FestivalFood();
        var screens = new[] { MenuScreen("Menu", 0, 0, 168, 672, menu), VideoScreen("A", 200, 0, 168, 672, "solid-red.mp4"), VideoScreen("B", 400, 0, 336, 672, "solid-green.mp4") };
        host.Sync(screens, Plan(screens).Drawn);

        Assert.Equal(2, host.PlayerCount);
        Assert.Null(host.PictureFor(screens[0].Id));
        Assert.Equal((168, 672), (host.PictureFor(screens[1].Id)!.Width, host.PictureFor(screens[1].Id)!.Height));
        Assert.Equal((336, 672), (host.PictureFor(screens[2].Id)!.Width, host.PictureFor(screens[2].Id)!.Height));
    });

    [Fact]
    public void Videos_play_only_while_output_runs_and_the_screens_go_black_when_it_stops() => UiPump.Run(ui =>
    {
        using var host = Host(ui);
        var v = VideoScreen("A", 0, 0, 168, 672, "solid-red.mp4");
        host.Sync(new[] { v }, Plan(v).Drawn);
        ui.For(500);
        Assert.Equal(VideoPlayerState.Idle, host.StateOf(v.Id));                // output not running: nothing decodes

        host.SetRunning(true);
        Assert.True(ui.Until(() => Colour(host.PictureFor(v.Id)!, 84, 336) == "red"));
        host.SetRunning(false);
        Assert.Equal(VideoPlayerState.Idle, host.StateOf(v.Id));
        Assert.Equal("black", Colour(host.PictureFor(v.Id)!, 84, 336));
    });

    [Fact]
    public void Syncing_again_keeps_the_players_and_passes_playlist_edits_on_without_a_restart() => UiPump.Run(ui =>
    {
        using var host = Host(ui);
        var v = VideoScreen("A", 0, 0, 168, 672, "solid-red.mp4", "solid-green.mp4");
        var plan = Plan(v);
        host.Sync(new[] { v }, plan.Drawn);
        host.SetRunning(true);
        var picture = host.PictureFor(v.Id);
        Assert.True(ui.Until(() => Colour(host.PictureFor(v.Id)!, 84, 336) == "red"));

        v.Playlist.Items[0].Enabled = false;                                    // the operator switches red off
        host.Sync(new[] { v }, plan.Drawn);
        Assert.Same(picture, host.PictureFor(v.Id));                            // same player, same buffer
        Assert.True(ui.Until(() => Colour(host.PictureFor(v.Id)!, 84, 336) == "green"));
    });

    [Fact]
    public void Resizing_a_video_screen_rebuilds_its_player_at_the_new_size() => UiPump.Run(ui =>
    {
        using var host = Host(ui);
        var v = VideoScreen("A", 0, 0, 168, 672, "solid-red.mp4");
        host.Sync(new[] { v }, Plan(v).Drawn);
        var old = host.PictureFor(v.Id)!;
        v.Width = 336;
        host.Sync(new[] { v }, Plan(v).Drawn);
        var now = host.PictureFor(v.Id)!;
        Assert.NotSame(old, now);
        Assert.Equal((336, 672), (now.Width, now.Height));
        Assert.Equal(1, host.PlayerCount);
    });

    [Fact]
    public void A_screen_that_stops_being_video_or_is_no_longer_drawn_loses_its_player() => UiPump.Run(ui =>
    {
        using var host = Host(ui);
        var a = VideoScreen("A", 0, 0, 168, 672, "solid-red.mp4");
        var b = VideoScreen("B", 200, 0, 168, 672, "solid-green.mp4");
        host.Sync(new[] { a, b }, Plan(a, b).Drawn);
        Assert.Equal(2, host.PlayerCount);

        a.ContentKind = ScreenContentKind.Menu;                                 // A becomes a menu screen again
        b.Enabled = false;                                                      // B is switched off
        host.Sync(new[] { a, b }, Plan(a, b).Drawn);
        Assert.Equal(0, host.PlayerCount);
        Assert.Null(host.PictureFor(a.Id));
        Assert.Null(host.PictureFor(b.Id));
    });

    [Fact]
    public void One_screens_bad_video_is_reported_by_name_and_the_other_screen_plays_on() => UiPump.Run(ui =>
    {
        using var host = Host(ui);
        var bad = VideoScreen("Broken", 0, 0, 168, 672, "does-not-exist.mp4");
        var good = VideoScreen("Fine", 200, 0, 168, 672, "solid-blue.mp4");
        var screens = new[] { bad, good };
        host.Sync(screens, Plan(screens).Drawn);
        var told = 0;
        host.ProblemsChanged += () => told++;
        host.SetRunning(true);

        Assert.True(ui.Until(() => Colour(host.PictureFor(good.Id)!, 84, 336) == "blue"));
        Assert.True(ui.Until(() => host.Problems.Count > 0));
        Assert.Single(host.Problems);
        Assert.Contains("Broken", host.Problems[0]);
        Assert.DoesNotContain("Fine", host.Problems[0]);
        Assert.True(told >= 1);
        Assert.Equal(VideoPlayerState.NothingPlayable, host.StateOf(bad.Id));
        Assert.Equal(VideoPlayerState.Playing, host.StateOf(good.Id));
    });

    [Fact]
    public void A_menu_screen_and_a_video_screen_run_at_the_same_time_each_correct_and_independent() => UiPump.Run(ui =>
    {
        // the intended first use: Screen 1 = 168x672 menu, Screen 2 = 168x672 video, side by side on one canvas
        var canvas = new CanvasSize(1920, 1080);
        var menu = SampleMenus.DenseMenu();                                     // several pages at 168 wide, so the menu rotates too
        var screens = new[] { MenuScreen("Menu", 0, 0, 168, 672, menu), VideoScreen("Video", 168, 0, 168, 672, "solid-red.mp4", "solid-green.mp4") };
        var plan = CalibrationPlan.Build(screens, canvas);

        var fonts = new FontCatalog(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        var render = new MenuRenderService(fonts, new AssetStore(_work), NullLog.Instance);
        var now = TimeSpan.Zero;
        var builder = new NormalFrameBuilder(id => id == menu.Id ? menu : null, render, new PageClock(), () => now);
        using var host = Host(ui);

        // output starts: menu drawn, video players start
        var result = builder.Build(canvas, plan);
        host.Sync(screens, plan.Drawn);
        host.SetRunning(true);
        var picture = VideoCompositor.Compose(result.Frame, canvas, plan.Drawn, host.PictureFor)!;
        var videoScreen = plan.Drawn.Single(s => s.IsVideo);
        host.PictureUpdated += id => { if (id == videoScreen.Id) VideoCompositor.Blit(picture, videoScreen, host.PictureFor(id)!, plan.Drawn); };

        var menuPage0 = render.Get(menu, 168, 672).Pages[0];
        Assert.True(ui.Until(() => Colour(picture, 168 + 84, 336) == "red"), "the video never appeared next to the menu");
        Assert.True(RegionEquals(picture, 0, 0, menuPage0), "the menu changed when the video started");
        Assert.Equal("black", Colour(picture, 400, 300));                       // the rest of the canvas is still black

        // the video moves on by itself; the menu page does not, and nothing outside the video screen is touched
        Assert.True(ui.Until(() => Colour(picture, 168 + 84, 336) == "green", 6000), "the video did not move on to its second clip");
        Assert.True(RegionEquals(picture, 0, 0, menuPage0), "the video's progress disturbed the menu");

        // the menu rotates on its own clock (a rebuild, as the app does every few seconds); the video is unaffected by it
        now = TimeSpan.FromSeconds(menu.Theme.PageSeconds + 1);
        var second = builder.Build(canvas, plan);
        Assert.True(second.Shown[plan.Drawn[0].Id].Page > 0, "the menu did not rotate");
        var recomposed = VideoCompositor.Compose(second.Frame, canvas, plan.Drawn, host.PictureFor)!;
        Assert.True(RegionEquals(recomposed, 0, 0, render.Get(menu, 168, 672).Pages[second.Shown[plan.Drawn[0].Id].Page]), "the rotated menu page is wrong");
        Assert.NotEqual("black", Colour(recomposed, 168 + 84, 336));            // and the video is still in place after the menu rebuild
    });
}
