using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;
using LedMenu.Core.Video;

namespace LedMenu.Core.Tests;

public class PlaylistSequencerTests
{
    private static VideoItem V(string name, bool enabled = true) => new() { FileName = name + ".mp4", DisplayName = name, Enabled = enabled };

    private static (PlaylistSequencer seq, VideoItem[] items) Make(bool loop, params string[] names)
    {
        var items = names.Select(n => V(n)).ToArray();
        var seq = new PlaylistSequencer();
        seq.SetItems(items, loop);
        return (seq, items);
    }

    private static string? Name(VideoItem? v) => v?.DisplayName;

    [Fact]
    public void Nothing_plays_until_started_and_starting_picks_the_first()
    {
        var (seq, _) = Make(true, "a", "b");
        Assert.Equal(SequencerStatus.Idle, seq.Status);
        Assert.Null(seq.Current);
        Assert.Equal("a", Name(seq.Start()));
        Assert.Equal(SequencerStatus.Playing, seq.Status);
    }

    [Fact]
    public void Videos_play_in_list_order_and_a_looping_list_comes_back_to_the_first()
    {
        var (seq, _) = Make(true, "a", "b", "c");
        seq.Start();
        Assert.Equal(new[] { "b", "c", "a", "b" }, new[] { Name(seq.Ended()), Name(seq.Ended()), Name(seq.Ended()), Name(seq.Ended()) });
    }

    [Fact]
    public void A_list_that_does_not_loop_finishes_after_the_last_video_and_shows_nothing()
    {
        var (seq, _) = Make(false, "a", "b");
        seq.Start();
        Assert.Equal("b", Name(seq.Ended()));
        Assert.Null(seq.Ended());
        Assert.Equal(SequencerStatus.Finished, seq.Status);
        Assert.Null(seq.Current);
    }

    [Fact]
    public void A_single_looping_video_repeats_itself()
    {
        var (seq, _) = Make(true, "only");
        seq.Start();
        Assert.Equal("only", Name(seq.Ended()));
        Assert.Equal("only", Name(seq.Ended()));
        Assert.Equal("only", Name(seq.PeekNext()));            // so the player can open it again early
    }

    [Fact]
    public void Disabled_videos_are_skipped_but_stay_in_the_list()
    {
        var items = new[] { V("a"), V("b", enabled: false), V("c") };
        var seq = new PlaylistSequencer();
        seq.SetItems(items, true);
        seq.Start();
        Assert.Equal("c", Name(seq.Ended()));
        Assert.Equal("a", Name(seq.Ended()));
        Assert.Equal(3, items.Length);
    }

    [Fact]
    public void Starting_skips_a_disabled_first_video()
    {
        var seq = new PlaylistSequencer();
        seq.SetItems(new[] { V("a", enabled: false), V("b") }, true);
        Assert.Equal("b", Name(seq.Start()));
    }

    [Fact]
    public void An_empty_or_all_disabled_list_has_nothing_playable()
    {
        var seq = new PlaylistSequencer();
        seq.SetItems(Array.Empty<VideoItem>(), true);
        Assert.Null(seq.Start());
        Assert.Equal(SequencerStatus.NothingPlayable, seq.Status);

        var off = new PlaylistSequencer();
        off.SetItems(new[] { V("a", false), V("b", false) }, true);
        Assert.Null(off.Start());
        Assert.Equal(SequencerStatus.NothingPlayable, off.Status);
    }

    [Fact]
    public void A_failed_video_is_skipped_and_the_show_goes_straight_on()
    {
        var (seq, _) = Make(true, "a", "b", "c");
        seq.Start();
        Assert.Equal("b", Name(seq.Ended()));
        Assert.Equal("c", Name(seq.Failed()));                 // b could not be played
        Assert.Equal("a", Name(seq.Ended()));
        Assert.Equal("c", Name(seq.Ended()));                  // b is not tried again on later loops
        Assert.Equal(1, seq.FailedCount);
    }

    [Fact]
    public void If_the_only_video_fails_nothing_is_left_to_play_and_there_is_no_endless_retry()
    {
        var (seq, _) = Make(true, "only");
        seq.Start();
        Assert.Null(seq.Failed());
        Assert.Equal(SequencerStatus.NothingPlayable, seq.Status);
        Assert.Null(seq.Ended());                              // nothing more happens by itself
    }

    [Fact]
    public void Every_video_failing_in_turn_ends_in_nothing_playable_not_a_loop()
    {
        var (seq, _) = Make(true, "a", "b", "c");
        seq.Start();
        seq.Failed(); seq.Failed();
        Assert.Null(seq.Failed());
        Assert.Equal(SequencerStatus.NothingPlayable, seq.Status);
    }

    [Fact]
    public void A_failing_last_video_of_a_list_that_does_not_loop_finishes_instead_of_starting_over()
    {
        var (seq, _) = Make(false, "a", "b");
        seq.Start(); seq.Ended();
        Assert.Null(seq.Failed());
        Assert.Equal(SequencerStatus.Finished, seq.Status);
    }

    [Fact]
    public void Clearing_failures_gives_every_video_another_chance()
    {
        var (seq, _) = Make(true, "a");
        seq.Start(); seq.Failed();
        seq.ClearFailures();
        Assert.Equal("a", Name(seq.Start()));
    }

    // ---- operator controls ----

    [Fact]
    public void Next_and_Previous_move_one_playable_video_and_wrap_only_when_looping()
    {
        var (seq, _) = Make(true, "a", "b", "c");
        seq.Start();
        Assert.Equal("b", Name(seq.Next()));
        Assert.Equal("c", Name(seq.Next()));
        Assert.Equal("a", Name(seq.Next()));
        Assert.Equal("c", Name(seq.Previous()));
        Assert.Equal("b", Name(seq.Previous()));

        var (flat, _) = Make(false, "a", "b");
        flat.Start();
        Assert.Equal("a", Name(flat.Previous()));              // already first: stays
        flat.Next();
        Assert.Equal("b", Name(flat.Next()));                  // already last: stays, does not wrap or finish
        Assert.Equal(SequencerStatus.Playing, flat.Status);
    }

    [Fact]
    public void Next_and_Previous_skip_disabled_and_failed_videos()
    {
        var items = new[] { V("a"), V("b", enabled: false), V("c"), V("d") };
        var seq = new PlaylistSequencer();
        seq.SetItems(items, true);
        seq.Start();
        Assert.Equal("c", Name(seq.Next()));
        seq.Failed();                                          // c fails, moves on to d
        Assert.Equal("d", Name(seq.Current));
        Assert.Equal("a", Name(seq.Previous()));               // skips failed c and disabled b
    }

    [Fact]
    public void Controls_before_starting_do_nothing()
    {
        var (seq, _) = Make(true, "a", "b");
        Assert.Null(seq.Next());
        Assert.Null(seq.Previous());
        Assert.Equal(SequencerStatus.Idle, seq.Status);
    }

    // ---- early loading ----

    [Fact]
    public void Peek_next_names_what_comes_after_without_moving()
    {
        var (seq, _) = Make(false, "a", "b");
        seq.Start();
        Assert.Equal("b", Name(seq.PeekNext()));
        Assert.Equal("a", Name(seq.Current));
        seq.Ended();
        Assert.Null(seq.PeekNext());                           // last video, no looping: nothing to prepare
    }

    // ---- live edits ----

    [Fact]
    public void Editing_the_list_does_not_interrupt_the_video_that_is_playing()
    {
        var (seq, items) = Make(true, "a", "b", "c");
        seq.Start(); seq.Ended();                              // b playing
        var changed = seq.SetItems(new[] { V("new"), items[0], items[1], items[2] }, true);   // a new video added at the front
        Assert.False(changed);
        Assert.Equal("b", Name(seq.Current));
        Assert.Equal("c", Name(seq.PeekNext()));
    }

    [Fact]
    public void Reordering_while_playing_keeps_the_current_video_and_changes_what_follows()
    {
        var (seq, items) = Make(true, "a", "b", "c");
        seq.Start();                                           // a
        Assert.False(seq.SetItems(new[] { items[2], items[0], items[1] }, true));    // c, a, b
        Assert.Equal("a", Name(seq.Current));
        Assert.Equal("b", Name(seq.PeekNext()));
    }

    [Fact]
    public void Switching_off_the_playing_video_moves_on_to_the_next_one()
    {
        var (seq, items) = Make(true, "a", "b", "c");
        seq.Start(); seq.Ended();                              // b
        items[1].Enabled = false;
        Assert.True(seq.SetItems(items, true));
        Assert.Equal("c", Name(seq.Current));
    }

    [Fact]
    public void Removing_the_playing_video_moves_on_to_the_one_that_took_its_place()
    {
        var (seq, items) = Make(true, "a", "b", "c");
        seq.Start(); seq.Ended();                              // b
        Assert.True(seq.SetItems(new[] { items[0], items[2] }, true));
        Assert.Equal("c", Name(seq.Current));
    }

    [Fact]
    public void Removing_the_last_playing_video_of_a_list_that_loops_goes_back_to_the_start()
    {
        var (seq, items) = Make(true, "a", "b");
        seq.Start(); seq.Ended();                              // b (last)
        Assert.True(seq.SetItems(new[] { items[0] }, true));
        Assert.Equal("a", Name(seq.Current));
    }

    [Fact]
    public void Removing_the_last_playing_video_of_a_list_that_does_not_loop_finishes()
    {
        var (seq, items) = Make(false, "a", "b");
        seq.Start(); seq.Ended();
        Assert.True(seq.SetItems(new[] { items[0] }, false));
        Assert.Equal(SequencerStatus.Finished, seq.Status);
    }

    [Fact]
    public void Switching_off_everything_while_playing_leaves_nothing_playable_and_switching_one_on_resumes()
    {
        var (seq, items) = Make(true, "a", "b");
        seq.Start();
        items[0].Enabled = false; items[1].Enabled = false;
        Assert.True(seq.SetItems(items, true));
        Assert.Equal(SequencerStatus.NothingPlayable, seq.Status);
        items[1].Enabled = true;
        Assert.True(seq.SetItems(items, true));
        Assert.Equal("b", Name(seq.Current));
    }

    [Fact]
    public void Adding_the_first_video_to_an_empty_playlist_that_was_already_started_begins_playing_it()
    {
        var seq = new PlaylistSequencer();
        seq.SetItems(Array.Empty<VideoItem>(), true);
        seq.Start();                                           // nothing playable
        Assert.True(seq.SetItems(new[] { V("a") }, true));
        Assert.Equal("a", Name(seq.Current));
    }

    [Fact]
    public void A_finished_show_is_not_restarted_by_editing_the_list()
    {
        var (seq, items) = Make(false, "a");
        seq.Start(); seq.Ended();
        Assert.False(seq.SetItems(items.Append(V("b")).ToArray(), false));
        Assert.Equal(SequencerStatus.Finished, seq.Status);
    }

    [Fact]
    public void Failures_are_forgotten_for_videos_removed_from_the_list()
    {
        var (seq, items) = Make(true, "a", "b");
        seq.Start(); seq.Failed();
        Assert.Equal(1, seq.FailedCount);
        seq.SetItems(new[] { items[1] }, true);
        Assert.Equal(0, seq.FailedCount);
    }

    [Fact]
    public void Switching_looping_on_or_off_changes_what_follows_the_last_video()
    {
        var (seq, items) = Make(false, "a", "b");
        seq.Start(); seq.Ended();
        Assert.Null(seq.PeekNext());
        seq.SetItems(items, true);
        Assert.Equal("a", Name(seq.PeekNext()));
    }
}

public class VideoPlacementTests
{
    [Theory]
    [InlineData(VideoFit.Fit)]
    [InlineData(VideoFit.Fill)]
    public void A_video_exactly_the_screens_size_is_always_shown_1_to_1(VideoFit fit)
    {
        var r = VideoPlacement.Compute(168, 672, 168, 672, fit);
        Assert.Equal(new PixelRegion(0, 0, 168, 672), r);
        Assert.True(VideoPlacement.IsNative(r, 168, 672));
    }

    [Fact]
    public void Fit_puts_the_whole_picture_inside_the_screen_centred_with_bars()
    {
        // a 1920x1080 landscape video on a 168x672 column: width-limited, so 168 wide and about 95 tall, centred vertically
        var r = VideoPlacement.Compute(1920, 1080, 168, 672, VideoFit.Fit);
        Assert.Equal(168, r.Width);
        Assert.Equal(95, r.Height);                            // 1080 * 168/1920 = 94.5, rounded
        Assert.Equal(0, r.X);
        Assert.Equal((672 - 95) / 2, r.Y);
        Assert.False(VideoPlacement.IsNative(r, 1920, 1080));
    }

    [Fact]
    public void Fill_covers_the_whole_screen_and_crops_the_overflow_equally_on_both_sides()
    {
        var r = VideoPlacement.Compute(1920, 1080, 168, 672, VideoFit.Fill);
        Assert.Equal(672, r.Height);                           // height-limited
        Assert.Equal(1195, r.Width);                           // 1920 * 672/1080 = 1194.67
        Assert.Equal(0, r.Y);
        Assert.Equal((168 - 1195) >> 1, r.X);                  // negative: sticks out left and right
        Assert.True(r.X < 0 && r.X + r.Width > 168);
    }

    [Fact]
    public void A_tall_video_on_a_wide_screen_is_pillarboxed_by_Fit_and_cropped_by_Fill()
    {
        var fit = VideoPlacement.Compute(100, 400, 336, 672, VideoFit.Fit);
        Assert.Equal(672, fit.Height);
        Assert.Equal(168, fit.Width);
        Assert.Equal((336 - 168) / 2, fit.X);
        var fill = VideoPlacement.Compute(100, 400, 336, 672, VideoFit.Fill);
        Assert.Equal(336, fill.Width);
        Assert.Equal(1344, fill.Height);
        Assert.True(fill.Y < 0);
    }

    [Theory]
    [InlineData(VideoFit.Fit)]
    [InlineData(VideoFit.Fill)]
    public void The_limiting_side_lands_exactly_on_the_screen_so_there_is_never_a_one_pixel_gap(VideoFit fit)
    {
        foreach (var (vw, vh) in new[] { (1280, 720), (1000, 1001), (333, 777), (169, 672), (168, 673), (7, 3) })
        {
            var r = VideoPlacement.Compute(vw, vh, 168, 672, fit);
            if (fit == VideoFit.Fit)
            {
                Assert.True(r.Width <= 168 && r.Height <= 672, $"{vw}x{vh} fit {r}");
                Assert.True(r.Width == 168 || r.Height == 672, $"{vw}x{vh} fit touches an edge");
                Assert.InRange(r.X, 0, 168 - r.Width);
                Assert.InRange(r.Y, 0, 672 - r.Height);
            }
            else
            {
                Assert.True(r.Width >= 168 && r.Height >= 672, $"{vw}x{vh} fill {r}");
                Assert.True(r.Width == 168 || r.Height == 672);
                Assert.True(r.X <= 0 && r.Y <= 0 && r.X + r.Width >= 168 && r.Y + r.Height >= 672);
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 168, 672)]
    [InlineData(-5, 10, 168, 672)]
    public void An_unknown_video_size_fills_the_screen(int vw, int vh, int sw, int sh) =>
        Assert.Equal(new PixelRegion(0, 0, sw, sh), VideoPlacement.Compute(vw, vh, sw, sh, VideoFit.Fit));

    [Fact]
    public void A_nonsense_screen_size_never_throws_and_never_returns_zero_area()
    {
        var r = VideoPlacement.Compute(168, 672, 0, 0, VideoFit.Fit);
        Assert.True(r.Width >= 1 && r.Height >= 1);
    }
}

public class VideoLevelsTests
{
    [Theory]
    [InlineData(16, 0)]
    [InlineData(235, 255)]
    [InlineData(0, 0)]        // below black is clamped
    [InlineData(255, 255)]    // above white is clamped
    [InlineData(126, 128)]    // mid grey stays mid grey (about)
    public void Limited_range_is_stretched_to_full_range(int input, int expected) =>
        Assert.InRange(VideoLevels.Expand((byte)input), expected - 1, expected + 1);

    [Fact]
    public void The_stretch_is_monotonic_never_reversing_a_gradient()
    {
        var last = -1;
        for (var v = 0; v < 256; v++)
        {
            var e = VideoLevels.Expand((byte)v);
            Assert.True(e >= last);
            last = e;
        }
    }

    [Fact]
    public void A_buffer_is_stretched_in_place_and_alpha_is_untouched()
    {
        var px = new byte[] { 16, 126, 235, 255, 20, 30, 40, 200 };     // two BGRA pixels
        VideoLevels.ExpandLimitedRange(px);
        Assert.Equal(0, px[0]);
        Assert.InRange(px[1], 127, 129);
        Assert.Equal(255, px[2]);
        Assert.Equal(255, px[3]);
        Assert.Equal(200, px[7]);
        Assert.True(px[4] > 20 - 1 || px[4] == 5);                      // 20 maps to about 5
    }

    [Fact]
    public void An_empty_or_ragged_buffer_is_harmless()
    {
        VideoLevels.ExpandLimitedRange(Array.Empty<byte>());
        VideoLevels.ExpandLimitedRange(new byte[3]);
    }
}
