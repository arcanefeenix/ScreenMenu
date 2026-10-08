using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;
using LedMenu.Rendering;

namespace LedMenu.App.Tests;

/// <summary>Runs a test body on an STA thread and lets it keep the WPF dispatcher turning while it waits for the real decoder.</summary>
internal sealed class UiPump
{
    public Dispatcher Dispatcher { get; } = Dispatcher.CurrentDispatcher;

    public void For(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Once(); Thread.Sleep(1); }
    }

    public bool Until(Func<bool> condition, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs) { Once(); Thread.Sleep(1); }
        return condition();
    }

    private void Once()
    {
        var frame = new DispatcherFrame();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public static void Run(Action<UiPump> body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { body(new UiPump()); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

/// <summary>The real Windows decoder driven by the real player, on small solid-colour clips so the test can tell exactly which video is on screen.</summary>
public class VideoScreenPlayerTests : IDisposable
{
    private static readonly string Media = Path.Combine(AppContext.BaseDirectory, "media");
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ledmenu-player-" + Guid.NewGuid().ToString("N"));

    public VideoScreenPlayerTests() => Directory.CreateDirectory(_work);
    public void Dispose() { try { Directory.Delete(_work, true); } catch { } }

    private string? Resolve(string name)
    {
        var inMedia = Path.Combine(Media, name);
        if (File.Exists(inMedia)) return inMedia;
        var inWork = Path.Combine(_work, name);
        return File.Exists(inWork) ? inWork : null;
    }

    private static VideoItem Clip(string file) => new() { FileName = file, DisplayName = file, Width = 168, Height = 672, Enabled = true };

    private static VideoPlaylist List(bool loop, params string[] files) =>
        new() { Loop = loop, Items = files.Select(Clip).ToList() };

    private VideoScreenPlayer Player(UiPump ui, int w = 168, int h = 672) => new(ui.Dispatcher, w, h, Resolve);

    /// <summary>What colour a picture is (looked at in the middle).</summary>
    private static string Colour(PixelBuffer f, int x = -1, int y = -1)
    {
        x = x < 0 ? f.Width / 2 : x; y = y < 0 ? f.Height / 2 : y;
        var i = (y * f.Width + x) * 4;
        int b = f.Data[i], g = f.Data[i + 1], r = f.Data[i + 2];
        if (r < 24 && g < 24 && b < 24) return "black";
        if (r > 225 && g < 30 && b < 30) return "red";
        if (g > 200 && r < 40 && b < 40) return "green";
        if (b > 225 && r < 30 && g < 30) return "blue";
        return $"other({r},{g},{b})";
    }

    private string WriteJunk(string name)
    {
        var path = Path.Combine(_work, name);
        var bytes = new byte[4000];
        new Random(2).NextBytes(bytes);
        bytes[3] = 0x20; bytes[4] = 0x66; bytes[5] = 0x74; bytes[6] = 0x79; bytes[7] = 0x70;     // looks like an MP4, is not one
        File.WriteAllBytes(path, bytes);
        return name;
    }

    [Fact]
    public void A_clip_the_screens_size_appears_at_that_size_with_full_range_colour() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(false, "solid-red.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 3), "no pictures arrived");

        Assert.Equal(VideoPlayerState.Playing, p.State);
        Assert.Equal((168, 672), (p.Frame.Width, p.Frame.Height));
        var c = Colour(p.Frame);
        Assert.Equal("red", c);                                   // pure red, not the dull (233,16,15) Windows hands over
        Assert.Equal("red", Colour(p.Frame, 0, 0));
        Assert.Equal("red", Colour(p.Frame, 167, 671));           // every corner: the picture fills the screen exactly
    });

    [Fact]
    public void Videos_play_one_after_another_in_order_and_a_playlist_that_does_not_loop_ends_black() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        var seen = new List<string>();
        p.FrameUpdated += () => { var c = Colour(p.Frame); if (seen.Count == 0 || seen[^1] != c) seen.Add(c); };
        p.SetPlaylist(List(false, "solid-red.mp4", "solid-green.mp4", "solid-blue.mp4"));
        p.Play();

        Assert.True(ui.Until(() => p.State == VideoPlayerState.Finished, 12000), $"never finished; seen: {string.Join(",", seen)}");
        var real = seen.Where(c => c != "black").ToList();
        Assert.Equal(new[] { "red", "green", "blue" }, real);       // in order, each once
        Assert.Equal("black", Colour(p.Frame));
        Assert.Equal("black", Colour(p.Frame, 3, 3));
    });

    [Fact]
    public void A_single_video_loops_without_a_visible_gap() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        var times = new List<long>();
        var clock = Stopwatch.StartNew();
        var blackDuringLoop = 0;
        var shownAny = false;
        p.FrameUpdated += () =>
        {
            times.Add(clock.ElapsedMilliseconds);
            var black = Colour(p.Frame) == "black";
            if (!black) shownAny = true;
            else if (shownAny) blackDuringLoop++;               // black before the first picture is just start-up; black after it would be a gap
        };
        p.SetPlaylist(List(true, "solid-red.mp4"));        // a 1-second clip
        p.Play();

        ui.For(4500);                                        // long enough for three or four loops
        Assert.Equal(VideoPlayerState.Playing, p.State);
        Assert.True(p.ItemChanges >= 3, $"only {p.ItemChanges} starts");
        var maxGap = times.Skip(1).Zip(times.Skip(2), (a, b) => b - a).Max();      // ignoring the start-up interval
        Assert.True(maxGap < 450, $"longest pause between pictures was {maxGap} ms");
        Assert.Equal(0, blackDuringLoop);                    // the screen never went black between loops
    });

    [Fact]
    public void A_video_that_cannot_be_played_is_skipped_with_a_message_and_the_next_one_plays() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        var junk = WriteJunk("junk.mp4");
        p.SetPlaylist(List(true, junk, "solid-green.mp4"));
        p.Play();

        Assert.True(ui.Until(() => p.FramesGrabbed > 2 && Colour(p.Frame) == "green", 10000), "green never appeared");
        Assert.Contains("skipped", p.Problem);
        Assert.Contains("junk.mp4", p.Problem);
        ui.For(2500);                                         // across later loops the bad file is not tried again
        Assert.Equal(VideoPlayerState.Playing, p.State);
    });

    [Fact]
    public void A_video_whose_file_has_gone_missing_is_skipped_too() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(true, "deleted-since.mp4", "solid-blue.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 2 && Colour(p.Frame) == "blue", 10000));
        Assert.Contains("missing", p.Problem);
    });

    [Fact]
    public void When_nothing_can_play_the_screen_is_black_the_player_says_why_and_nothing_throws() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(true, WriteJunk("a.mp4"), "gone.mp4", WriteJunk("b.mp4")));
        p.Play();
        Assert.True(ui.Until(() => p.State == VideoPlayerState.NothingPlayable, 15000), $"state {p.State}");
        Assert.Equal("black", Colour(p.Frame));
        Assert.False(string.IsNullOrWhiteSpace(p.Problem));
    });

    [Fact]
    public void An_empty_playlist_is_black_and_explains_itself() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(new VideoPlaylist());
        p.Play();
        Assert.Equal(VideoPlayerState.NothingPlayable, p.State);
        Assert.Contains("no videos", p.Problem);
        Assert.Equal("black", Colour(p.Frame));
    });

    [Fact]
    public void Next_and_Previous_change_the_video_on_demand() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(true, "solid-red.mp4", "solid-green.mp4", "solid-blue.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 2 && Colour(p.Frame) == "red"));

        p.Next();
        Assert.True(ui.Until(() => Colour(p.Frame) == "green"), "Next did not reach green");
        p.Next();
        Assert.True(ui.Until(() => Colour(p.Frame) == "blue"), "Next did not reach blue");
        p.Previous();
        Assert.True(ui.Until(() => Colour(p.Frame) == "green"), "Previous did not go back to green");
    });

    [Fact]
    public void Pause_holds_the_picture_and_Play_carries_on() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(true, "solid-red.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 3));

        p.Pause();
        Assert.Equal(VideoPlayerState.Paused, p.State);
        var held = p.FramesGrabbed;
        ui.For(600);
        Assert.Equal(held, p.FramesGrabbed);                  // no work while paused
        Assert.Equal("red", Colour(p.Frame));                 // and the picture stays up

        p.Play();
        Assert.Equal(VideoPlayerState.Playing, p.State);
        Assert.True(ui.Until(() => p.FramesGrabbed > held + 3));
    });

    [Fact]
    public void Switching_off_the_video_that_is_playing_moves_on_without_stopping_the_show() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        var pl = List(true, "solid-red.mp4", "solid-green.mp4");
        p.SetPlaylist(pl);
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 2 && Colour(p.Frame) == "red"));

        pl.Items[0].Enabled = false;                           // the operator switches red off
        p.SetPlaylist(pl);
        Assert.True(ui.Until(() => Colour(p.Frame) == "green"));
        ui.For(2500);                                          // red never comes back while it is off
        Assert.NotEqual("red", Colour(p.Frame));
    });

    [Fact]
    public void Editing_a_playlist_leaves_the_playing_video_alone() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        var pl = List(true, "solid-red.mp4", "solid-green.mp4");
        p.SetPlaylist(pl);
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 2));
        var changes = p.ItemChanges;
        pl.Items.Add(Clip("solid-blue.mp4"));
        p.SetPlaylist(pl);
        Assert.Equal(changes, p.ItemChanges);                  // no restart
    });

    [Fact]
    public void A_picture_of_another_shape_is_letterboxed_by_Fit_and_cropped_by_Fill() => UiPump.Run(ui =>
    {
        // a 320x180 red picture on a 168x672 screen
        using var fit = Player(ui);
        var pf = List(true, "solid-red-320x180.mp4"); pf.Fit = VideoFit.Fit;
        fit.SetPlaylist(pf); fit.Play();
        Assert.True(ui.Until(() => fit.FramesGrabbed > 3));
        Assert.Equal("red", Colour(fit.Frame));                // the middle is picture
        Assert.Equal("black", Colour(fit.Frame, 84, 10));      // bars above and below
        Assert.Equal("black", Colour(fit.Frame, 84, 660));
        Assert.Equal("red", Colour(fit.Frame, 0, 336));        // and the picture spans the full width

        using var fill = Player(ui);
        var pl = List(true, "solid-red-320x180.mp4"); pl.Fit = VideoFit.Fill;
        fill.SetPlaylist(pl); fill.Play();
        Assert.True(ui.Until(() => fill.FramesGrabbed > 3));
        foreach (var (x, y) in new[] { (0, 0), (167, 0), (0, 671), (167, 671), (84, 10), (84, 660) })
            Assert.Equal("red", Colour(fill.Frame, x, y));     // covers everything, nothing black
    });

    [Fact]
    public void The_picture_never_leaves_its_own_screen_rectangle() => UiPump.Run(ui =>
    {
        // a wide clip filled into a small screen is scaled up far past the screen edges; the buffer is only the screen, so nothing can spill
        using var p = Player(ui, 40, 40);
        var pl = List(true, "solid-red-320x180.mp4"); pl.Fit = VideoFit.Fill;
        p.SetPlaylist(pl); p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 3));
        Assert.Equal((40, 40), (p.Frame.Width, p.Frame.Height));
        Assert.Equal(40 * 40 * 4, p.Frame.Data.Length);
        Assert.Equal("red", Colour(p.Frame, 39, 39));
    });

    [Fact]
    public void A_clip_with_a_sound_track_plays_silently_and_looks_the_same() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(true, "tiny-168x672-audio.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 5), "no pictures arrived");
        Assert.Equal(VideoPlayerState.Playing, p.State);
        Assert.Equal((168, 672), (p.Frame.Width, p.Frame.Height));
        Assert.Null(p.Problem);
    });

    [Fact]
    public void Stop_blanks_the_screen_and_a_stopped_player_does_nothing() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(true, "solid-red.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 3));
        p.Stop();
        Assert.Equal(VideoPlayerState.Idle, p.State);
        Assert.Equal("black", Colour(p.Frame));
        var n = p.FramesGrabbed;
        ui.For(500);
        Assert.Equal(n, p.FramesGrabbed);
        p.Next(); p.Previous(); p.Pause();                      // harmless when stopped
        Assert.Equal(VideoPlayerState.Idle, p.State);
    });

    [Fact]
    public void Disposing_while_playing_is_clean_and_later_calls_do_nothing() => UiPump.Run(ui =>
    {
        var p = Player(ui);
        p.SetPlaylist(List(true, "solid-red.mp4", "solid-green.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.FramesGrabbed > 2));
        p.Dispose();
        p.Dispose();
        p.Play(); p.Next();
        ui.For(300);
    });

    [Fact]
    public void Playing_again_after_a_failure_gives_every_video_another_chance() => UiPump.Run(ui =>
    {
        using var p = Player(ui);
        p.SetPlaylist(List(false, "not-there-yet.mp4"));
        p.Play();
        Assert.True(ui.Until(() => p.State == VideoPlayerState.NothingPlayable, 8000));

        File.Copy(Path.Combine(Media, "solid-green.mp4"), Path.Combine(_work, "not-there-yet.mp4"));     // the file turns up
        p.Play();
        Assert.True(ui.Until(() => p.State == VideoPlayerState.Playing && Colour(p.Frame) == "green", 8000));
    });
}
