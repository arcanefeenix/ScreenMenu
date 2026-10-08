using System.Text.Json;
using LedMenu.Core.Assets;
using LedMenu.Core.Screens;

namespace LedMenu.Persistence.Tests;

public class ScreenLayoutMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-mig-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "screens.json");
    private string BackupDir => Path.Combine(_root, "backups");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private JsonFileStore<ScreenLayout> Store() => new(FilePath, BackupDir, () => new ScreenLayout(), ScreenLayout.ValidateFile,
        versionOf: l => l.SchemaVersion, currentVersion: ScreenLayout.CurrentSchemaVersion, upgrade: ScreenLayout.Upgrade);

    private const string V1 = "{\"SchemaVersion\":1,\"Name\":\"Default\",\"Screens\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"Wall\",\"X\":0,\"Y\":0,\"Width\":336,\"Height\":672,\"Enabled\":true,\"AssignedMenuId\":\"22222222-2222-2222-2222-222222222222\"}]}";

    private void WriteV1()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, V1);
    }

    [Fact]
    public void An_old_file_is_kept_aside_then_upgraded_and_rewritten_in_the_new_format()
    {
        WriteV1();

        var r = Store().Load();

        Assert.Equal(LoadStatus.Migrated, r.Status);
        Assert.Equal(2, r.Value.SchemaVersion);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), r.Value.Screens[0].AssignedMenuId);

        var kept = Directory.GetFiles(BackupDir, "screens.premigration-v1-to-v2-*.bak");
        Assert.Single(kept);
        Assert.Equal(V1, File.ReadAllText(kept[0]));                        // byte-for-byte the original

        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));      // and the main file is now version 2
        Assert.Equal(2, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void Loading_the_upgraded_file_again_changes_nothing_and_keeps_no_extra_copies()
    {
        WriteV1();
        Store().Load();
        var again = Store().Load();
        Assert.Equal(LoadStatus.Ok, again.Status);
        Assert.Single(Directory.GetFiles(BackupDir, "*.bak"));
    }

    [Fact]
    public void A_pre_migration_copy_is_never_mistaken_for_an_ordinary_backup_or_pruned()
    {
        WriteV1();
        Store().Load();
        for (var i = 0; i < 15; i++)
            Store().Save(new ScreenLayout { Name = "edit " + i });            // plenty of saves, so ordinary backups are pruned to ten
        Assert.Single(Directory.GetFiles(BackupDir, "*.bak"));               // the original v1 copy is still there
        Assert.True(Directory.GetFiles(BackupDir, "screens.*.json").Length <= 10);

        // damage the main file: recovery must restore a version 2 backup, not the old v1 copy
        File.WriteAllText(FilePath, "ruined");
        var r = Store().Load();
        Assert.Equal(LoadStatus.RecoveredFromBackup, r.Status);
        Assert.Equal(2, r.Value.SchemaVersion);
    }

    [Fact]
    public void If_a_copy_of_the_old_file_cannot_be_made_the_old_file_is_left_untouched()
    {
        WriteV1();
        File.WriteAllText(BackupDir, "a file where the backup folder must go");     // CreateDirectory / copy will fail

        var r = Store().Load();

        Assert.Equal(V1, File.ReadAllText(FilePath));                              // never overwritten without a copy
        Assert.Equal(1, r.Value.SchemaVersion);                                    // and not upgraded in memory either
        Assert.NotEqual(LoadStatus.Migrated, r.Status);
    }

    [Fact]
    public void An_old_backup_restored_after_damage_is_upgraded_too()
    {
        WriteV1();
        Directory.CreateDirectory(BackupDir);
        File.Copy(FilePath, Path.Combine(BackupDir, "screens.20260101-000000-000.json"));
        File.WriteAllText(FilePath, "ruined");

        var r = Store().Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, r.Status);
        Assert.Equal(2, r.Value.SchemaVersion);
        Assert.Equal("Wall", r.Value.Screens[0].Name);
    }

    [Fact]
    public void A_store_without_migration_settings_behaves_exactly_as_before()
    {
        WriteV1();
        var plain = new JsonFileStore<ScreenLayout>(FilePath, BackupDir, () => new ScreenLayout(), ScreenLayout.ValidateFile);
        var r = plain.Load();
        Assert.Equal(LoadStatus.Ok, r.Status);
        Assert.Equal(1, r.Value.SchemaVersion);
        Assert.False(Directory.Exists(BackupDir));
    }
}

public class MediaStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-media-" + Guid.NewGuid().ToString("N"));
    private string MediaDir => Path.Combine(_root, "media");
    private string SourceDir => Path.Combine(_root, "source");

    public MediaStoreTests() => Directory.CreateDirectory(SourceDir);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class FakeProbe : IVideoProbe
    {
        public VideoProbeResult Result { get; set; } = new(true, 168, 672, 12.5, null);
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public Func<string, Task<VideoProbeResult>>? Custom { get; set; }
        public Task<VideoProbeResult> ProbeAsync(string path, CancellationToken cancel = default)
        {
            Calls++; LastPath = path;
            Assert.True(File.Exists(path), "the probe must be given the stored copy, which must already exist");
            return Custom?.Invoke(path) ?? Task.FromResult(Result);
        }
    }

    private static byte[] Mp4Bytes(int size = 4000, int seed = 1)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        b[0] = 0; b[1] = 0; b[2] = 0; b[3] = 0x20; b[4] = 0x66; b[5] = 0x74; b[6] = 0x79; b[7] = 0x70;   // ....ftyp
        return b;
    }

    private string Source(string name, byte[] bytes)
    {
        var p = Path.Combine(SourceDir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private MediaStore Store(IVideoProbe probe) => new(MediaDir, probe);

    [Fact]
    public async Task A_good_video_is_copied_probed_and_described()
    {
        var probe = new FakeProbe { Result = new(true, 168, 672, 12.5, null) };
        var bytes = Mp4Bytes();
        var result = await Store(probe).ImportAsync(Source("My Promo.mp4", bytes));

        Assert.False(result.AlreadyPresent);
        var item = result.Item;
        Assert.Equal("My Promo.mp4", item.DisplayName);
        Assert.Equal((168, 672), (item.Width, item.Height));
        Assert.Equal(12.5, item.DurationSeconds);
        Assert.True(item.Enabled);
        Assert.True(MediaRules.IsSafeFileName(item.FileName));
        Assert.EndsWith(".mp4", item.FileName);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(MediaDir, item.FileName)));
        Assert.Equal(Path.Combine(MediaDir, item.FileName), probe.LastPath);       // the stored copy was probed, not the original
        Assert.Empty(Directory.GetFiles(MediaDir, "*.tmp"));
    }

    [Fact]
    public async Task The_original_can_be_moved_or_deleted_afterwards_without_harm()
    {
        var src = Source("clip.mp4", Mp4Bytes());
        var result = await Store(new FakeProbe()).ImportAsync(src);
        File.Delete(src);
        Assert.NotNull(Store(new FakeProbe()).Resolve(result.Item.FileName));
    }

    [Fact]
    public async Task The_same_video_under_another_name_reuses_the_first_copy()
    {
        var store = Store(new FakeProbe());
        var bytes = Mp4Bytes(seed: 5);
        var a = await store.ImportAsync(Source("one.mp4", bytes));
        var b = await store.ImportAsync(Source("copy of one.mp4", bytes));
        Assert.True(b.AlreadyPresent);
        Assert.Equal(a.Item.FileName, b.Item.FileName);
        Assert.Single(Directory.GetFiles(MediaDir));
        Assert.Equal("copy of one.mp4", b.Item.DisplayName);                       // the new entry shows the name it was imported under
    }

    [Fact]
    public async Task Different_videos_never_collide()
    {
        var store = Store(new FakeProbe());
        var a = await store.ImportAsync(Source("a.mp4", Mp4Bytes(seed: 1)));
        var b = await store.ImportAsync(Source("a.mp4", Mp4Bytes(seed: 2)));       // same file name, different content
        Assert.NotEqual(a.Item.FileName, b.Item.FileName);
        Assert.Equal(2, Directory.GetFiles(MediaDir).Length);
    }

    [Fact]
    public async Task A_video_Windows_cannot_play_is_rejected_with_a_reason_and_leaves_nothing_behind()
    {
        var probe = new FakeProbe { Result = VideoProbeResult.Failure("a video format this PC cannot decode") };
        var ex = await Assert.ThrowsAsync<MediaImportException>(() => Store(probe).ImportAsync(Source("bad.mp4", Mp4Bytes())));
        Assert.Contains("cannot play", ex.Message);
        Assert.Contains("cannot decode", ex.Message);
        Assert.Contains("H.264", ex.Message);
        Assert.Empty(Directory.GetFiles(MediaDir));
    }

    [Fact]
    public async Task Rejecting_a_duplicate_does_not_delete_the_good_copy_that_was_already_stored()
    {
        var good = new FakeProbe();
        var bytes = Mp4Bytes(seed: 9);
        var first = await Store(good).ImportAsync(Source("a.mp4", bytes));

        var bad = new FakeProbe { Result = VideoProbeResult.Failure("now it fails") };     // e.g. a codec was removed
        await Assert.ThrowsAsync<MediaImportException>(() => Store(bad).ImportAsync(Source("again.mp4", bytes)));

        Assert.True(File.Exists(Path.Combine(MediaDir, first.Item.FileName)));
    }

    [Fact]
    public async Task A_probe_that_returns_a_zero_sized_picture_counts_as_failure()
    {
        var probe = new FakeProbe { Result = new(true, 0, 0, 5, null) };
        await Assert.ThrowsAsync<MediaImportException>(() => Store(probe).ImportAsync(Source("x.mp4", Mp4Bytes())));
        Assert.Empty(Directory.GetFiles(MediaDir));
    }

    [Fact]
    public async Task A_probe_that_throws_is_reported_as_a_rejection_not_a_crash()
    {
        var probe = new FakeProbe { Custom = _ => throw new InvalidOperationException("decoder exploded") };
        var ex = await Assert.ThrowsAsync<MediaImportException>(() => Store(probe).ImportAsync(Source("x.mp4", Mp4Bytes())));
        Assert.Contains("decoder exploded", ex.Message);
        Assert.Empty(Directory.GetFiles(MediaDir));
    }

    [Theory]
    [InlineData("image.mp4", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0 })]      // a PNG named .mp4
    [InlineData("program.mp4", new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0 })]                  // an .exe named .mp4
    [InlineData("notes.mp4", new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x77, 0x6F, 0x72, 0x6C, 0x64, 0x21 })]
    public async Task Files_that_are_not_videos_are_refused_whatever_their_name_says(string name, byte[] bytes)
    {
        var probe = new FakeProbe();
        var ex = await Assert.ThrowsAsync<MediaImportException>(() => Store(probe).ImportAsync(Source(name, bytes)));
        Assert.Contains("does not look like a video", ex.Message);
        Assert.Equal(0, probe.Calls);
        Assert.False(Directory.Exists(MediaDir) && Directory.GetFiles(MediaDir).Length > 0);
    }

    [Fact]
    public async Task Missing_empty_and_locked_sources_give_clear_messages()
    {
        var store = Store(new FakeProbe());
        var missing = await Assert.ThrowsAsync<MediaImportException>(() => store.ImportAsync(Path.Combine(SourceDir, "nope.mp4")));
        Assert.Contains("does not exist", missing.Message);

        var empty = await Assert.ThrowsAsync<MediaImportException>(() => store.ImportAsync(Source("empty.mp4", Array.Empty<byte>())));
        Assert.Contains("empty", empty.Message);

        var locked = Source("locked.mp4", Mp4Bytes());
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = await Assert.ThrowsAsync<MediaImportException>(() => store.ImportAsync(locked));
            Assert.Contains("could not be read", ex.Message);
        }
    }

    [Fact]
    public async Task Cancelling_during_the_probe_leaves_nothing_behind()
    {
        using var cts = new CancellationTokenSource();
        var probe = new FakeProbe { Custom = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(probe).ImportAsync(Source("c.mp4", Mp4Bytes()), cts.Token));
        Assert.Empty(Directory.GetFiles(MediaDir));
    }

    [Fact]
    public async Task A_large_file_is_streamed_and_arrives_intact()
    {
        var bytes = Mp4Bytes(size: 12 * 1024 * 1024, seed: 3);                       // 12 MB: several copy buffers
        var result = await Store(new FakeProbe()).ImportAsync(Source("big.mp4", bytes));
        var stored = Path.Combine(MediaDir, result.Item.FileName);
        Assert.Equal(bytes.Length, new FileInfo(stored).Length);
        Assert.True(File.ReadAllBytes(stored).AsSpan().SequenceEqual(bytes));
    }

    [Theory]
    [InlineData("..\\x.mp4")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("a/b.mp4")]
    [InlineData(null)]
    public void Resolve_never_leaves_the_media_folder(string? name) => Assert.Null(Store(new FakeProbe()).Resolve(name));

    [Fact]
    public async Task Cleanup_removes_only_files_no_playlist_uses()
    {
        var store = Store(new FakeProbe());
        var keep = await store.ImportAsync(Source("keep.mp4", Mp4Bytes(seed: 1)));
        var drop = await store.ImportAsync(Source("drop.mp4", Mp4Bytes(seed: 2)));

        var removed = store.RemoveUnused(new HashSet<string> { keep.Item.FileName });

        Assert.Equal(new[] { drop.Item.FileName }, removed);
        Assert.True(store.Exists(keep.Item.FileName));
        Assert.False(store.Exists(drop.Item.FileName));
    }
}
