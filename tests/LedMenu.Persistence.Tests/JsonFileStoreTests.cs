using LedMenu.Core.Models;

namespace LedMenu.Persistence.Tests;

public class JsonFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-test-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "settings.json");
    private string BackupDir => Path.Combine(_root, "backups");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private JsonFileStore<AppSettings> Store(int keep = 10) =>
        new(FilePath, BackupDir, () => new AppSettings(), AppSettings.Validate, keepBackups: keep);

    [Fact]
    public void First_load_creates_default_without_writing()
    {
        var r = Store().Load();
        Assert.Equal(LoadStatus.CreatedDefault, r.Status);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var when = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        Store().Save(new AppSettings { LastStartUtc = when });
        var r = Store().Load();
        Assert.Equal(LoadStatus.Ok, r.Status);
        Assert.Equal(when, r.Value.LastStartUtc);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Display_identities_round_trip()
    {
        var output = new LedMenu.Core.Display.DisplayIdentity
        {
            DevicePath = @"\\?\DISPLAY#MIR0001#5&1a2b&0&UID256#{e6f07b5f}",
            FriendlyName = "Mirackle VP2", DeviceName = @"\\.\DISPLAY2",
            X = 1920, Y = 0, Width = 1920, Height = 1080,
        };
        Store().Save(new AppSettings { OutputDisplay = output });
        var back = Store().Load().Value;
        Assert.Null(back.OperatorDisplay);
        Assert.Equal(output.DevicePath, back.OutputDisplay!.DevicePath);
        Assert.Equal(1920, back.OutputDisplay.X);
        Assert.Equal("Mirackle VP2", back.OutputDisplay.FriendlyName);
    }

    [Fact]
    public void Phase1_settings_file_without_display_fields_still_loads()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, "{ \"SchemaVersion\": 1, \"LastStartUtc\": \"2026-10-07T15:09:15Z\" }");
        var r = Store().Load();
        Assert.Equal(LoadStatus.Ok, r.Status);
        Assert.Null(r.Value.OutputDisplay);
    }

    [Fact]
    public void Second_save_backs_up_previous_good_file()
    {
        var s = Store();
        s.Save(new AppSettings());
        s.Save(new AppSettings { LastStartUtc = DateTime.UtcNow });
        Assert.Single(Directory.GetFiles(BackupDir));
    }

    [Fact]
    public void Corrupt_main_file_recovers_from_backup_and_preserves_corrupt_copy()
    {
        var s = Store();
        var original = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        s.Save(new AppSettings { LastStartUtc = original });
        s.Save(new AppSettings { LastStartUtc = original });   // creates a backup of the first
        File.WriteAllText(FilePath, "{ this is not json");

        var r = s.Load();

        Assert.Equal(LoadStatus.RecoveredFromBackup, r.Status);
        Assert.Equal(original, r.Value.LastStartUtc);
        Assert.NotNull(r.Message);
        Assert.Single(Directory.GetFiles(_root, "settings.json.corrupt-*"));
        // main file is usable again
        Assert.Equal(LoadStatus.Ok, Store().Load().Status);
    }

    [Fact]
    public void Corrupt_file_with_no_backup_defaults_but_never_deletes_it()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, "garbage");
        var r = Store().Load();
        Assert.Equal(LoadStatus.DefaultedAfterFailure, r.Status);
        Assert.Single(Directory.GetFiles(_root, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Missing_main_file_restores_from_backup()
    {
        var s = Store();
        s.Save(new AppSettings());
        s.Save(new AppSettings());
        File.Delete(FilePath);
        Assert.Equal(LoadStatus.RecoveredFromBackup, s.Load().Status);
    }

    [Fact]
    public void Semantically_invalid_file_is_treated_as_corrupt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, "{ \"SchemaVersion\": 999 }");
        Assert.Equal(LoadStatus.DefaultedAfterFailure, Store().Load().Status);
    }

    [Fact]
    public void Saving_invalid_data_is_refused_and_existing_file_untouched()
    {
        var s = Store();
        s.Save(new AppSettings());
        var before = File.ReadAllText(FilePath);
        Assert.Throws<InvalidOperationException>(() => s.Save(new AppSettings { SchemaVersion = 0 }));
        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Old_backups_are_pruned()
    {
        var s = Store(keep: 3);
        for (var i = 0; i < 8; i++) s.Save(new AppSettings());
        Assert.Equal(3, Directory.GetFiles(BackupDir).Length);
    }

    [Fact]
    public void Leftover_temp_file_from_power_loss_does_not_affect_load()
    {
        Store().Save(new AppSettings());
        File.WriteAllText(FilePath + ".tmp", "{ half written");
        Assert.Equal(LoadStatus.Ok, Store().Load().Status);
        Store().Save(new AppSettings()); // temp is overwritten cleanly
        Assert.Equal(LoadStatus.Ok, Store().Load().Status);
    }
}
