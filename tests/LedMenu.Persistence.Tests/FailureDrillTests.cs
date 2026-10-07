using System.Text;
using LedMenu.Core.Models;

namespace LedMenu.Persistence.Tests;

/// <summary>Things that go wrong on a real PC at an event: crashes mid-write, damaged files, locked files, junk in the folder.</summary>
public class FailureDrillTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-drill-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "settings.json");
    private string BackupDir => Path.Combine(_root, "backups");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private JsonFileStore<AppSettings> Store() => new(FilePath, BackupDir, () => new AppSettings(), AppSettings.Validate);

    private static readonly DateTime Marker = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private void SaveTwice()
    {
        Store().Save(new AppSettings { LastStartUtc = Marker });
        Store().Save(new AppSettings { LastStartUtc = Marker.AddDays(1) });   // the first becomes a backup
    }

    [Theory]
    [InlineData("")]                                  // zero bytes (power cut right after the file was created)
    [InlineData("   \r\n  ")]                         // whitespace only
    [InlineData("{")]                                 // truncated mid-write
    [InlineData("{\"SchemaVersion\": 1, \"LastStartUtc\": \"2026-10")]   // truncated inside a value
    [InlineData("null")]                              // valid JSON, no data
    [InlineData("[1,2,3]")]                           // wrong shape
    [InlineData("{\"SchemaVersion\": \"one\"}")]      // wrong type
    [InlineData("not json at all")]
    public void A_damaged_main_file_is_replaced_from_backup_and_kept_aside(string junk)
    {
        SaveTwice();
        File.WriteAllText(FilePath, junk);

        var r = Store().Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, r.Status);
        Assert.Equal(Marker, r.Value.LastStartUtc);
        Assert.NotNull(r.Message);
        Assert.Contains(Directory.GetFiles(_root), f => f.Contains("corrupt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_file_full_of_binary_zeros_is_recovered_not_crashed_on()
    {
        SaveTwice();
        File.WriteAllBytes(FilePath, new byte[4096]);          // what a bad disk sector or a half-flushed write can leave
        Assert.Equal(LoadStatus.RecoveredFromBackup, Store().Load().Status);
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_still_loads()
    {
        Store().Save(new AppSettings { LastStartUtc = Marker });
        var text = File.ReadAllText(FilePath);
        File.WriteAllText(FilePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));   // an editor re-saved it
        var r = Store().Load();
        Assert.Equal(LoadStatus.Ok, r.Status);
        Assert.Equal(Marker, r.Value.LastStartUtc);
    }

    [Fact]
    public void A_crash_during_save_leaves_a_stray_temp_file_that_never_gets_loaded_or_blocks_the_next_save()
    {
        Store().Save(new AppSettings { LastStartUtc = Marker });
        File.WriteAllText(FilePath + ".tmp", "{ half written");          // power cut between the temp write and the swap

        Assert.Equal(Marker, Store().Load().Value.LastStartUtc);          // the good main file wins
        Store().Save(new AppSettings { LastStartUtc = Marker.AddDays(2) });
        Assert.Equal(Marker.AddDays(2), Store().Load().Value.LastStartUtc);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Saving_when_the_file_is_locked_by_another_program_fails_loudly_and_leaves_the_good_file_alone()
    {
        Store().Save(new AppSettings { LastStartUtc = Marker });
        var before = File.ReadAllBytes(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))   // e.g. antivirus or an editor
        {
            Assert.ThrowsAny<IOException>(() => Store().Save(new AppSettings { LastStartUtc = Marker.AddDays(5) }));
        }

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal(Marker, Store().Load().Value.LastStartUtc);
        Store().Save(new AppSettings { LastStartUtc = Marker.AddDays(5) });   // and it works again once the lock is gone
        Assert.Equal(Marker.AddDays(5), Store().Load().Value.LastStartUtc);
    }

    [Fact]
    public void A_read_only_data_file_fails_the_save_loudly_without_damage()
    {
        Store().Save(new AppSettings { LastStartUtc = Marker });
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => Store().Save(new AppSettings { LastStartUtc = Marker.AddDays(1) }));
            Assert.Equal(Marker, Store().Load().Value.LastStartUtc);
        }
        finally { File.SetAttributes(FilePath, FileAttributes.Normal); }
    }

    [Fact]
    public void An_unwritable_backup_folder_does_not_stop_the_save_itself()
    {
        Store().Save(new AppSettings { LastStartUtc = Marker });
        File.WriteAllText(BackupDir, "a file where the backup folder should be");     // CreateDirectory will fail

        Store().Save(new AppSettings { LastStartUtc = Marker.AddDays(1) });

        Assert.Equal(Marker.AddDays(1), Store().Load().Value.LastStartUtc);
    }

    [Fact]
    public void Every_backup_damaged_too_defaults_but_never_deletes_anything()
    {
        SaveTwice();
        foreach (var b in Directory.GetFiles(BackupDir)) File.WriteAllText(b, "ruined");
        File.WriteAllText(FilePath, "ruined as well");

        var r = Store().Load();

        Assert.Equal(LoadStatus.DefaultedAfterFailure, r.Status);
        Assert.NotEmpty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Contains(Directory.GetFiles(_root), f => f.Contains("corrupt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_future_schema_version_is_refused_not_silently_downgraded()
    {
        SaveTwice();
        File.WriteAllText(FilePath, "{\"SchemaVersion\": 99}");
        var r = Store().Load();
        Assert.NotEqual(LoadStatus.Ok, r.Status);        // it is treated as unusable and the previous good copy is restored
        Assert.Equal(Marker, r.Value.LastStartUtc);
    }
}
