using System.IO;
using LedMenu.App.Output;
using LedMenu.App.ViewModels;
using LedMenu.Core.Assets;
using LedMenu.Core.Logging;
using LedMenu.Core.Menus;
using LedMenu.Core.Screens;
using LedMenu.Persistence;
using LedMenu.Rendering;

namespace LedMenu.App.Tests;

internal sealed class FakeTransport : IVideoTransport
{
    public Dictionary<Guid, VideoScreenStatus> Status { get; } = new();
    public List<string> Calls { get; } = new();
    public event Action<Guid>? StatusChanged;
    public VideoScreenStatus? StatusOf(Guid id) => Status.GetValueOrDefault(id);
    public void Next(Guid id) => Calls.Add("next");
    public void Previous(Guid id) => Calls.Add("previous");
    public void TogglePause(Guid id) => Calls.Add("pause");
    public void Raise(Guid id) => StatusChanged?.Invoke(id);
}

/// <summary>The screens tab's logic: Menu or Video per screen, and the playlist editor, with fake import, dialogs and transport.</summary>
public class PlaylistEditorTests
{
    private sealed class Rig
    {
        public readonly ScreenLayout Layout = new();
        public readonly FakeTransport Transport = new();
        public readonly HashSet<string> MediaOnDisk = new();
        public readonly List<string> Picked = new();
        public readonly Dictionary<string, VideoItem> Importable = new();
        public readonly List<string> ImportFailures = new();
        public List<string> RemovedNames = new();
        public bool ConfirmAnswer = true;
        public int Saves;
        public IReadOnlySet<string>? LastReferenced;
        public readonly ScreensViewModel Screens;
        public readonly MenuLibrary Library = new(new MemoryStore());

        public Rig(params Screen[] screens)
        {
            foreach (var s in screens) Layout.Screens.Add(s);
            var fonts = new FontCatalog(Path.Combine(AppContext.BaseDirectory, "Fonts"));
            var assets = new AssetStore(Path.Combine(Path.GetTempPath(), "ledmenu-pe-tests"));
            var render = new MenuRenderService(fonts, assets, NullLog.Instance);
            var menus = new MenusViewModel(Library, assets, NullLog.Instance, () => null, (_, _) => true, render);
            var services = new VideoEditingServices
            {
                PickFiles = () => Picked.ToList(),
                Import = (path, _) =>
                {
                    var name = Path.GetFileName(path);
                    if (ImportFailures.Contains(name)) throw new MediaImportException($"Windows cannot play {name}.");
                    var item = Importable.TryGetValue(name, out var v) ? v : new VideoItem { FileName = "aaaaaaaaaaaa-" + name, DisplayName = name, Width = 168, Height = 672, DurationSeconds = 10 };
                    MediaOnDisk.Add(item.FileName);
                    return Task.FromResult(new MediaImportResult(new VideoItem { FileName = item.FileName, DisplayName = item.DisplayName, Width = item.Width, Height = item.Height, DurationSeconds = item.DurationSeconds }, false));
                },
                MediaExists = n => MediaOnDisk.Contains(n),
                Confirm = (_, _) => ConfirmAnswer,
                RemoveUnused = referenced => { LastReferenced = referenced; return RemovedNames; },
                Transport = Transport,
            };
            Screens = new ScreensViewModel(Layout, () => Saves++, NullLog.Instance,
                () => new CanvasInfo(new CanvasSize(1920, 1080), CanvasSource.Live, "Test"), _ => true, menus, n => MediaOnDisk.Contains(n), services);
        }

        public ScreenItemViewModel Item(int i) => Screens.Items[i];
    }

    private static Screen VideoScreen(string name = "Video", int w = 168, int h = 672, params VideoItem[] items) => new()
    {
        Name = name, X = 168, Y = 0, Width = w, Height = h, ContentKind = ScreenContentKind.Video,
        Playlist = new VideoPlaylist { Items = items.ToList() },
    };

    private static VideoItem Clip(string file, int w = 168, int h = 672, bool enabled = true) =>
        new() { FileName = file, DisplayName = file, Width = w, Height = h, DurationSeconds = 10, Enabled = enabled };

    // ---- Menu or Video ----

    [Fact]
    public void A_new_screen_shows_a_menu_and_offers_the_menu_picker_not_the_playlist_editor()
    {
        var rig = new Rig(new Screen { Name = "S", Width = 336, Height = 672 });
        var item = rig.Item(0);
        Assert.True(item.IsMenuContent);
        Assert.False(item.IsVideoContent);
        Assert.True(item.ShowsMenuPicker);
        Assert.False(item.ShowsVideoEditor);
    }

    [Fact]
    public void Choosing_Video_saves_at_once_and_swaps_the_picker_for_the_playlist_editor()
    {
        var rig = new Rig(new Screen { Name = "S", Width = 168, Height = 672 });
        var item = rig.Item(0);
        var seen = new List<string?>();
        item.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        item.IsVideoContent = true;

        Assert.Equal(ScreenContentKind.Video, item.Model.ContentKind);
        Assert.True(item.ShowsVideoEditor);
        Assert.False(item.ShowsMenuPicker);
        Assert.True(rig.Saves >= 1);
        Assert.Contains(nameof(item.ShowsVideoEditor), seen);
        Assert.Contains(nameof(item.IsMenuContent), seen);
    }

    [Fact]
    public void Switching_between_Menu_and_Video_loses_nothing_either_way()
    {
        var menuId = Guid.NewGuid();
        var rig = new Rig(new Screen { Name = "S", Width = 168, Height = 672, AssignedMenuId = menuId, Playlist = new VideoPlaylist { Items = { Clip("a.mp4") }, Loop = false, Fit = VideoFit.Fill } });
        var item = rig.Item(0);
        item.IsVideoContent = true;
        item.IsMenuContent = true;
        item.IsVideoContent = true;
        item.IsMenuContent = true;

        Assert.Equal(menuId, item.Model.AssignedMenuId);
        Assert.Single(item.Model.Playlist.Items);
        Assert.False(item.Model.Playlist.Loop);
        Assert.Equal(VideoFit.Fill, item.Model.Playlist.Fit);
    }

    [Fact]
    public void Choosing_the_kind_that_is_already_chosen_does_not_save_again()
    {
        var rig = new Rig(new Screen { Name = "S", Width = 168, Height = 672 });
        var before = rig.Saves;
        rig.Item(0).IsMenuContent = true;
        rig.Item(0).IsVideoContent = false;
        Assert.Equal(before, rig.Saves);
    }

    [Fact]
    public void A_screen_showing_video_does_not_count_as_using_its_remembered_menu()
    {
        var menu = SampleMenus.FestivalFood();
        var rig = new Rig(new Screen { Name = "Menu screen", Width = 168, Height = 672, AssignedMenuId = menu.Id },
                          new Screen { Name = "Video screen", Width = 168, Height = 672, AssignedMenuId = menu.Id, ContentKind = ScreenContentKind.Video });
        rig.Library.Add(menu);
        var users = rig.Screens.Items.Select(i => i).Count();
        Assert.Equal(2, users);
        // the menu library asks which screens use a menu (to warn before deleting it): only the screen that really shows it
        var screensUsing = typeof(MenusViewModel).GetProperty("ScreensUsing")!.GetValue(rig.Screens.GetType().GetField("_menus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(rig.Screens)) as Func<Guid, IReadOnlyList<string>>;
        Assert.NotNull(screensUsing);
        Assert.Equal(new[] { "Menu screen" }, screensUsing!(menu.Id));
    }

    // ---- importing ----

    [Fact]
    public async Task Importing_adds_each_video_to_the_end_in_the_order_chosen_saves_and_says_how_many()
    {
        var rig = new Rig(VideoScreen(items: Clip("existing.mp4")));
        rig.MediaOnDisk.Add("existing.mp4");
        var editor = rig.Item(0).Playlist!;
        var saves = rig.Saves;

        await editor.ImportFilesAsync(new[] { @"C:\x\one.mp4", @"C:\x\two.mp4" });

        Assert.Equal(new[] { "existing.mp4", "aaaaaaaaaaaa-one.mp4", "aaaaaaaaaaaa-two.mp4" }, rig.Layout.Screens[0].Playlist.Items.Select(i => i.FileName));
        Assert.Equal(3, editor.Items.Count);
        Assert.True(rig.Saves > saves);
        Assert.Contains("Added 2 videos", editor.Message);
        Assert.False(editor.IsImporting);
        Assert.Equal("", editor.ImportStatus);
        Assert.False(editor.HasNoVideos);
    }

    [Fact]
    public async Task A_file_that_cannot_be_used_is_reported_by_name_and_the_others_are_still_added()
    {
        var rig = new Rig(VideoScreen());
        rig.ImportFailures.Add("bad.mp4");
        var editor = rig.Item(0).Playlist!;

        await editor.ImportFilesAsync(new[] { @"C:\x\good.mp4", @"C:\x\bad.mp4", @"C:\x\also-good.mp4" });

        Assert.Equal(2, rig.Layout.Screens[0].Playlist.Items.Count);
        Assert.Contains("Added 2 videos", editor.Message);
        Assert.Contains("bad.mp4", editor.Message);
        Assert.Contains("Windows cannot play", editor.Message);
        Assert.True(editor.HasMessage);
    }

    [Fact]
    public async Task When_nothing_could_be_imported_nothing_is_saved_and_the_reason_is_shown()
    {
        var rig = new Rig(VideoScreen());
        rig.ImportFailures.Add("bad.mp4");
        var saves = rig.Saves;
        var editor = rig.Item(0).Playlist!;
        await editor.ImportFilesAsync(new[] { @"C:\x\bad.mp4" });
        Assert.Empty(rig.Layout.Screens[0].Playlist.Items);
        Assert.Equal(saves, rig.Saves);
        Assert.Contains("Not added", editor.Message);
        Assert.DoesNotContain("Added", editor.Message!.Replace("Not added", ""));
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_does_nothing()
    {
        var rig = new Rig(VideoScreen());
        var saves = rig.Saves;
        await rig.Item(0).Playlist!.ImportAsync();                 // the fake dialog returns no files
        Assert.Equal(saves, rig.Saves);
        Assert.Null(rig.Item(0).Playlist!.Message);
    }

    [Fact]
    public async Task A_dialog_that_throws_is_reported_not_crashed_on()
    {
        var rig = new Rig(VideoScreen());
        var editor = new PlaylistEditorViewModel(rig.Item(0), new VideoEditingServices
        {
            PickFiles = () => throw new InvalidOperationException("no dialog today"),
            Import = (_, _) => throw new NotSupportedException(), MediaExists = _ => true, Confirm = (_, _) => true, RemoveUnused = _ => Array.Empty<string>(),
        });
        await editor.ImportAsync();
        Assert.Contains("no dialog today", editor.Message);
    }

    // ---- editing the list ----

    private static Rig ThreeClips(out ScreenItemViewModel item)
    {
        var rig = new Rig(VideoScreen(items: new[] { Clip("a.mp4"), Clip("b.mp4"), Clip("c.mp4") }));
        foreach (var n in new[] { "a.mp4", "b.mp4", "c.mp4" }) rig.MediaOnDisk.Add(n);
        item = rig.Item(0);
        item.Playlist!.RefreshWarnings();
        return rig;
    }

    private static string Order(Rig rig) => string.Join(",", rig.Layout.Screens[0].Playlist.Items.Select(i => i.FileName[0]));

    [Fact]
    public void Videos_move_up_and_down_in_the_saved_playlist_and_the_ends_cannot_move_off_the_list()
    {
        var rig = ThreeClips(out var item);
        var rows = item.Playlist!.Items;
        Assert.False(rows[0].MoveUpCommand.CanExecute(null));
        Assert.False(rows[2].MoveDownCommand.CanExecute(null));

        rows[2].MoveUpCommand.Execute(null);                       // a, c, b
        Assert.Equal("a,c,b", Order(rig));
        Assert.Equal("a,c,b", string.Join(",", rows.Select(r => r.Model.FileName[0])));
        rows[0].MoveDownCommand.Execute(null);                     // c, a, b
        Assert.Equal("c,a,b", Order(rig));
        Assert.False(rows[0].MoveUpCommand.CanExecute(null));      // the buttons track the new positions
        Assert.True(rows[2].MoveUpCommand.CanExecute(null));
    }

    [Fact]
    public void Removing_a_video_takes_it_off_the_playlist_but_leaves_its_file_alone()
    {
        var rig = ThreeClips(out var item);
        item.Playlist!.Items[1].RemoveCommand.Execute(null);
        Assert.Equal("a,c", Order(rig));
        Assert.Equal(2, item.Playlist.Items.Count);
        Assert.Contains("b.mp4", rig.MediaOnDisk);                 // nothing deleted from the media folder
        Assert.Empty(rig.RemovedNames);
    }

    [Fact]
    public void Switching_a_video_off_and_on_is_saved_and_keeps_its_place()
    {
        var rig = ThreeClips(out var item);
        var saves = rig.Saves;
        item.Playlist!.Items[1].Enabled = false;
        Assert.False(rig.Layout.Screens[0].Playlist.Items[1].Enabled);
        Assert.True(rig.Saves > saves);
        item.Playlist.Items[1].Enabled = true;
        Assert.True(rig.Layout.Screens[0].Playlist.Items[1].Enabled);
        Assert.Equal("a,b,c", Order(rig));
    }

    [Fact]
    public void Loop_and_fit_settings_are_saved_and_fit_and_fill_are_exclusive()
    {
        var rig = ThreeClips(out var item);
        var editor = item.Playlist!;
        Assert.True(editor.Loop); Assert.True(editor.IsFit); Assert.False(editor.IsFill);

        editor.Loop = false;
        Assert.False(rig.Layout.Screens[0].Playlist.Loop);

        editor.IsFill = true;
        Assert.Equal(VideoFit.Fill, rig.Layout.Screens[0].Playlist.Fit);
        Assert.False(editor.IsFit);
        editor.IsFit = true;
        Assert.Equal(VideoFit.Fit, rig.Layout.Screens[0].Playlist.Fit);
        Assert.False(editor.IsFill);
        editor.IsFit = false;                                      // unticking the one that is chosen changes nothing
        Assert.Equal(VideoFit.Fit, rig.Layout.Screens[0].Playlist.Fit);
    }

    // ---- warnings ----

    [Fact]
    public void A_video_of_the_wrong_shape_or_with_its_file_missing_carries_a_plain_warning()
    {
        var rig = new Rig(VideoScreen(items: new[] { Clip("fits.mp4"), Clip("wide.mp4", 1920, 1080), Clip("gone.mp4") }));
        rig.MediaOnDisk.Add("fits.mp4"); rig.MediaOnDisk.Add("wide.mp4");
        var rows = rig.Item(0).Playlist!.Items;
        rig.Item(0).Playlist!.RefreshWarnings();

        Assert.False(rows[0].HasWarning);
        Assert.Contains("1920×1080 on 168×672", rows[1].Warning);
        Assert.Contains("scaled to fit", rows[1].Warning);
        Assert.Contains("missing", rows[2].Warning);
    }

    [Fact]
    public void Warnings_follow_the_screens_size_and_the_fit_choice()
    {
        var rig = new Rig(VideoScreen(items: new[] { Clip("tall.mp4") }));
        rig.MediaOnDisk.Add("tall.mp4");
        var item = rig.Item(0);
        item.Playlist!.RefreshWarnings();
        var row = item.Playlist!.Items[0];
        Assert.False(row.HasWarning);

        item.Width = 336;                                           // the screen is made wider: the 168-wide clip no longer matches
        Assert.True(row.HasWarning);
        Assert.Contains("scaled to fit", row.Warning);
        item.Playlist.IsFill = true;
        Assert.Contains("scaled and cropped", row.Warning);
        item.Width = 168;
        Assert.False(row.HasWarning);
    }

    [Fact]
    public void Each_row_shows_size_and_true_length_in_plain_words()
    {
        var rig = new Rig(VideoScreen(items: new[] { new VideoItem { FileName = "x.mp4", DisplayName = "Fall Drinks.mp4", Width = 168, Height = 672, DurationSeconds = 38.5 }, new VideoItem { FileName = "y.mp4", DisplayName = "", Width = 0, Height = 0, DurationSeconds = 0 } }));
        var rows = rig.Item(0).Playlist!.Items;
        Assert.Equal("Fall Drinks.mp4", rows[0].DisplayName);
        Assert.Equal("168×672 · 0:39", rows[0].Detail);
        Assert.Equal("y.mp4", rows[1].DisplayName);                 // falls back to the stored name
        Assert.Equal("size unknown · ?", rows[1].Detail);
    }

    // ---- transport ----

    [Fact]
    public void The_transport_buttons_do_nothing_until_a_player_exists_then_pass_the_operators_press_on()
    {
        var rig = ThreeClips(out var item);
        var editor = item.Playlist!;
        var id = item.Model.Id;
        Assert.False(editor.NextCommand.CanExecute(null));
        Assert.StartsWith("Not playing", editor.LiveStatus);

        rig.Transport.Status[id] = new VideoScreenStatus(VideoPlayerState.Playing, "b.mp4", 2, 3, null);
        rig.Transport.Raise(id);
        Assert.True(editor.NextCommand.CanExecute(null));
        Assert.Equal("Playing 2 of 3: b.mp4", editor.LiveStatus);

        editor.NextCommand.Execute(null);
        editor.PreviousCommand.Execute(null);
        editor.PlayPauseCommand.Execute(null);
        Assert.Equal(new[] { "next", "previous", "pause" }, rig.Transport.Calls);
    }

    [Fact]
    public void The_status_line_describes_every_state_in_words_and_shows_problems()
    {
        var rig = ThreeClips(out var item);
        var editor = item.Playlist!;
        var id = item.Model.Id;
        void Set(VideoPlayerState s, string? problem = null) { rig.Transport.Status[id] = new VideoScreenStatus(s, "a.mp4", 1, 3, problem); rig.Transport.Raise(id); }

        Set(VideoPlayerState.Paused);
        Assert.Equal("Paused on 1 of 3: a.mp4", editor.LiveStatus);
        Assert.Equal("▶ Resume", editor.PlayPauseText);
        Set(VideoPlayerState.Playing);
        Assert.Equal("⏸ Pause", editor.PlayPauseText);
        Set(VideoPlayerState.Opening);
        Assert.Contains("Opening", editor.LiveStatus);
        Set(VideoPlayerState.Finished);
        Assert.Contains("Finished", editor.LiveStatus);
        Set(VideoPlayerState.NothingPlayable, "\"c.mp4\" was skipped: its file is missing.");
        Assert.Contains("Nothing on this screen can be played", editor.LiveStatus);
        Assert.Contains("c.mp4", editor.LiveStatus);
        Set(VideoPlayerState.Idle);
        Assert.StartsWith("Not playing", editor.LiveStatus);
        Assert.False(editor.PlayPauseCommand.CanExecute(null));
    }

    [Fact]
    public void Status_changes_on_other_screens_do_not_disturb_this_screens_line()
    {
        var rig = new Rig(VideoScreen("A", items: Clip("a.mp4")), VideoScreen("B", items: Clip("b.mp4")));
        var a = rig.Item(0); var b = rig.Item(1);
        rig.Transport.Status[b.Model.Id] = new VideoScreenStatus(VideoPlayerState.Playing, "b.mp4", 1, 1, null);
        rig.Transport.Raise(b.Model.Id);
        Assert.StartsWith("Not playing", a.Playlist!.LiveStatus);
        Assert.Equal("Playing 1 of 1: b.mp4", b.Playlist!.LiveStatus);
    }

    // ---- cleaning up the media folder ----

    [Fact]
    public void Cleaning_up_passes_every_name_any_playlist_uses_including_screens_currently_showing_menus()
    {
        var showsMenu = new Screen { Name = "M", Width = 168, Height = 672, Playlist = new VideoPlaylist { Items = { Clip("kept-for-later.mp4") } } };
        var rig = new Rig(showsMenu, VideoScreen("V", items: Clip("playing.mp4")));
        rig.RemovedNames = new List<string> { "orphan1.mp4", "orphan2.mp4" };

        rig.Screens.CleanUpVideoFiles();

        Assert.Contains("kept-for-later.mp4", rig.LastReferenced!);
        Assert.Contains("playing.mp4", rig.LastReferenced!);
        Assert.Equal("Removed 2 unused video files.", rig.Screens.VideoMessage);
    }

    [Fact]
    public void Cleaning_up_asks_first_and_does_nothing_if_the_answer_is_no()
    {
        var rig = new Rig(VideoScreen());
        rig.ConfirmAnswer = false;
        rig.Screens.CleanUpVideoFiles();
        Assert.Null(rig.LastReferenced);
        Assert.Null(rig.Screens.VideoMessage);
    }

    [Fact]
    public void Cleaning_up_with_nothing_to_remove_says_so()
    {
        var rig = new Rig(VideoScreen());
        rig.Screens.CleanUpVideoFiles();
        Assert.Equal("There were no unused video files.", rig.Screens.VideoMessage);
    }

    [Fact]
    public void Where_video_editing_is_not_available_a_screen_has_no_playlist_editor_and_never_offers_one()
    {
        var fonts = new FontCatalog(Path.Combine(AppContext.BaseDirectory, "Fonts"));
        var assets = new AssetStore(Path.Combine(Path.GetTempPath(), "ledmenu-pe-tests"));
        var menus = new MenusViewModel(new MenuLibrary(new MemoryStore()), assets, NullLog.Instance, () => null, (_, _) => true, new MenuRenderService(fonts, assets, NullLog.Instance));
        var layout = new ScreenLayout { Screens = { new Screen { Name = "S", Width = 168, Height = 672, ContentKind = ScreenContentKind.Video } } };
        var vm = new ScreensViewModel(layout, () => { }, NullLog.Instance, () => new CanvasInfo(new CanvasSize(1920, 1080), CanvasSource.Live, "T"), _ => true, menus);
        Assert.Null(vm.Items[0].Playlist);
        Assert.False(vm.Items[0].ShowsVideoEditor);
        Assert.False(vm.CanManageVideo);
    }
}
