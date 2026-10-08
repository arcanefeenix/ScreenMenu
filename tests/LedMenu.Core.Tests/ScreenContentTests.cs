using System.Text.Json;
using LedMenu.Core.Assets;
using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

namespace LedMenu.Core.Tests;

public class ScreenContentTests
{
    private static Screen VideoScreen(params VideoItem[] items) => new()
    {
        Name = "Video", X = 168, Y = 0, Width = 168, Height = 672, ContentKind = ScreenContentKind.Video,
        Playlist = new VideoPlaylist { Items = items.ToList() },
    };

    private static VideoItem Clip(string file = "abc123abc123-clip.mp4", int w = 168, int h = 672, bool enabled = true) =>
        new() { FileName = file, DisplayName = file, Width = w, Height = h, DurationSeconds = 10, Enabled = enabled };

    [Fact]
    public void A_new_screen_shows_a_menu_with_an_empty_looping_playlist()
    {
        var s = new Screen();
        Assert.Equal(ScreenContentKind.Menu, s.ContentKind);
        Assert.False(s.ShowsVideo);
        Assert.Empty(s.Playlist.Items);
        Assert.True(s.Playlist.Loop);
        Assert.Equal(VideoFit.Fit, s.Playlist.Fit);
    }

    [Fact]
    public void Schema_version_is_2_and_content_round_trips_as_readable_text()
    {
        Assert.Equal(2, ScreenLayout.CurrentSchemaVersion);
        var layout = new ScreenLayout { Screens = { VideoScreen(Clip()) } };
        var json = JsonSerializer.Serialize(layout);
        Assert.Contains("\"Video\"", json);                    // the kind is written as a word, not a number
        var back = JsonSerializer.Deserialize<ScreenLayout>(json)!;
        Assert.Equal(ScreenContentKind.Video, back.Screens[0].ContentKind);
        Assert.Equal("abc123abc123-clip.mp4", back.Screens[0].Playlist.Items[0].FileName);
        Assert.Null(ScreenLayout.ValidateFile(back));
    }

    [Fact]
    public void Computed_helper_values_are_not_written_into_the_file()
    {
        var json = JsonSerializer.Serialize(new ScreenLayout { Screens = { VideoScreen(Clip()) } });
        Assert.DoesNotContain("ShowsVideo", json);
        Assert.DoesNotContain("Playable", json);
    }

    [Fact]
    public void A_version_1_file_without_the_new_fields_still_reads_as_a_menu_screen_and_upgrades_cleanly()
    {
        const string v1 = "{\"SchemaVersion\":1,\"Name\":\"Default\",\"Screens\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Old\",\"X\":0,\"Y\":0,\"Width\":336,\"Height\":672,\"Enabled\":true,\"AssignedMenuId\":\"22222222-2222-2222-2222-222222222222\"}]}";
        var layout = JsonSerializer.Deserialize<ScreenLayout>(v1)!;
        Assert.Null(ScreenLayout.ValidateFile(layout));         // version 1 is still a valid file
        ScreenLayout.Upgrade(layout);
        Assert.Equal(2, layout.SchemaVersion);
        Assert.Equal(ScreenContentKind.Menu, layout.Screens[0].ContentKind);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), layout.Screens[0].AssignedMenuId);   // nothing lost
        Assert.NotNull(layout.Screens[0].Playlist);
    }

    [Fact]
    public void A_file_from_a_newer_version_is_still_refused()
    {
        var layout = new ScreenLayout { SchemaVersion = 3 };
        Assert.NotNull(ScreenLayout.ValidateFile(layout));
    }

    [Theory]
    [InlineData("\"ContentKind\":\"Hologram\"")]
    [InlineData("\"ContentKind\":7")]
    public void An_unknown_content_kind_is_a_damaged_file_not_silently_a_menu(string fragment)
    {
        var json = "{\"SchemaVersion\":2,\"Screens\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\"," + fragment + "}]}";
        string? problem;
        try { problem = ScreenLayout.ValidateFile(JsonSerializer.Deserialize<ScreenLayout>(json)); }
        catch (JsonException) { problem = "unreadable"; }
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("..\\evil.mp4")]
    [InlineData("C:\\Windows\\x.mp4")]
    [InlineData("sub/dir.mp4")]
    [InlineData("")]
    public void A_playlist_entry_that_could_point_outside_the_media_folder_makes_the_file_invalid(string file)
    {
        var layout = new ScreenLayout { Screens = { VideoScreen(Clip(file)) } };
        Assert.NotNull(ScreenLayout.ValidateFile(layout));
    }

    [Fact]
    public void Two_videos_with_the_same_id_on_one_screen_make_the_file_invalid()
    {
        var a = Clip("aaaaaaaaaaaa-a.mp4"); var b = Clip("bbbbbbbbbbbb-b.mp4"); b.Id = a.Id;
        Assert.NotNull(ScreenLayout.ValidateFile(new ScreenLayout { Screens = { VideoScreen(a, b) } }));
    }

    [Fact]
    public void Cloning_a_screen_copies_its_playlist_so_edits_to_the_copy_do_not_leak_back()
    {
        var s = VideoScreen(Clip());
        var copy = s.Clone();
        copy.Playlist.Items[0].Enabled = false;
        copy.Playlist.Items.Add(Clip("zzzzzzzzzzzz-z.mp4"));
        Assert.True(s.Playlist.Items[0].Enabled);
        Assert.Single(s.Playlist.Items);
    }

    [Fact]
    public void Playable_means_enabled_and_in_order()
    {
        var p = new VideoPlaylist { Items = { Clip("a-1.mp4"), Clip("b-2.mp4", enabled: false), Clip("c-3.mp4") } };
        Assert.Equal(new[] { "a-1.mp4", "c-3.mp4" }, p.Playable.Select(i => i.FileName));
    }

    // ---- validator ----

    [Fact]
    public void An_empty_video_screen_warns_and_a_screen_of_only_disabled_videos_says_so()
    {
        var empty = VideoScreen();
        var off = VideoScreen(Clip(enabled: false));
        var issues = ScreenValidator.Validate(new[] { empty, off }, new CanvasSize(1920, 1080), null, _ => true);
        Assert.Contains(issues, i => i.ScreenId == empty.Id && i.Code == IssueCode.EmptyPlaylist && i.Message.Contains("no videos yet"));
        Assert.Contains(issues, i => i.ScreenId == off.Id && i.Code == IssueCode.EmptyPlaylist && i.Message.Contains("switched off"));
        Assert.DoesNotContain(issues, i => i.Code == IssueCode.EmptyPlaylist && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void A_video_missing_from_the_media_folder_warns_and_is_kept()
    {
        var s = VideoScreen(Clip("gone-gone.mp4"));
        var issues = ScreenValidator.Validate(new[] { s }, new CanvasSize(1920, 1080), null, name => name != "gone-gone.mp4");
        Assert.Contains(issues, i => i.Code == IssueCode.MissingMedia && i.Severity == IssueSeverity.Warning);
        Assert.Single(s.Playlist.Items);
    }

    [Fact]
    public void A_video_of_the_exact_screen_size_raises_no_size_warning_and_a_different_one_does()
    {
        var exact = VideoScreen(Clip(w: 168, h: 672));
        var other = VideoScreen(Clip(w: 1920, h: 1080));
        var issues = ScreenValidator.Validate(new[] { exact, other }, new CanvasSize(1920, 1080), null, _ => true);
        Assert.DoesNotContain(issues, i => i.ScreenId == exact.Id && i.Code == IssueCode.VideoSizeMismatch);
        Assert.Contains(issues, i => i.ScreenId == other.Id && i.Code == IssueCode.VideoSizeMismatch && i.Message.Contains("1920×1080") && i.Message.Contains("168×672"));
    }

    [Fact]
    public void A_video_screen_is_not_blamed_for_a_missing_menu_it_does_not_use()
    {
        var s = VideoScreen(Clip());
        s.AssignedMenuId = Guid.NewGuid();                       // a kept assignment to a menu that no longer exists
        var issues = ScreenValidator.Validate(new[] { s }, new CanvasSize(1920, 1080), new HashSet<Guid>(), _ => true);
        Assert.DoesNotContain(issues, i => i.Code == IssueCode.MissingMenu);
    }

    [Fact]
    public void Video_screens_take_part_in_geometry_checks_like_any_other()
    {
        var s = VideoScreen(Clip()); s.X = 1800;
        var issues = ScreenValidator.Validate(new[] { s }, new CanvasSize(1920, 1080), null, _ => true);
        Assert.Contains(issues, i => i.Code == IssueCode.OutsideCanvas && i.Severity == IssueSeverity.Error);
    }

    // ---- drawing plan ----

    [Fact]
    public void A_video_screen_never_shows_the_menu_it_still_remembers()
    {
        var menu = Guid.NewGuid();
        var s = VideoScreen(Clip()); s.AssignedMenuId = menu;
        var m = new Screen { Name = "Menu", X = 0, Y = 0, Width = 168, Height = 672, AssignedMenuId = menu };
        var plan = CalibrationPlan.Build(new[] { m, s }, new CanvasSize(1920, 1080));
        Assert.Equal(menu, plan.Drawn[0].MenuId);
        Assert.False(plan.Drawn[0].IsVideo);
        Assert.Null(plan.Drawn[1].MenuId);
        Assert.True(plan.Drawn[1].IsVideo);
    }

    // ---- media rules ----

    private static byte[] Bytes(params int[] b) => b.Select(x => (byte)x).ToArray();

    [Fact]
    public void Containers_are_recognised_by_content()
    {
        Assert.Equal(VideoContainer.Mp4, MediaRules.Detect(Bytes(0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D)));   // ....ftypisom
        Assert.Equal(VideoContainer.Mp4, MediaRules.Detect(Bytes(0, 0, 0, 8, 0x6D, 0x6F, 0x6F, 0x76, 0, 0, 0, 0)));                   // ....moov
        Assert.Equal(VideoContainer.Asf, MediaRules.Detect(Bytes(0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9)));
        Assert.Equal(VideoContainer.Avi, MediaRules.Detect(Bytes(0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x41, 0x56, 0x49, 0x20)));      // RIFF....AVI
        Assert.Equal(VideoContainer.Matroska, MediaRules.Detect(Bytes(0x1A, 0x45, 0xDF, 0xA3, 0, 0)));
    }

    [Fact]
    public void Things_that_are_not_video_containers_are_refused()
    {
        Assert.Null(MediaRules.Detect(Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0)));     // a PNG image
        Assert.Null(MediaRules.Detect(Bytes(0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0)));                   // an .exe renamed to .mp4
        Assert.Null(MediaRules.Detect(Array.Empty<byte>()));
        Assert.Null(MediaRules.Detect(Bytes(1, 2, 3)));
        Assert.Null(MediaRules.Detect(System.Text.Encoding.ASCII.GetBytes("not a video file at all")));
    }

    [Fact]
    public void Media_file_names_are_content_based_clean_and_safe()
    {
        var n = MediaRules.BuildFileName("ABCDEF0123456789", "My Promo (final) v2!", VideoContainer.Mp4);
        Assert.Equal("abcdef012345-My_Promo__final__v2.mp4", n);
        Assert.True(MediaRules.IsSafeFileName(n));
        Assert.StartsWith("abcdef012345-video", MediaRules.BuildFileName("ABCDEF0123456789", "", VideoContainer.Asf));
    }

    [Theory]
    [InlineData(0, "?")]
    [InlineData(9.4, "0:09")]
    [InlineData(95, "1:35")]
    [InlineData(3725, "1:02:05")]
    public void Durations_read_naturally(double seconds, string expected) => Assert.Equal(expected, MediaRules.FormatDuration(seconds));
}
