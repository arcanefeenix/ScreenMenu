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

public class ScreenLayoutStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ledmenu-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private JsonFileStore<LedMenu.Core.Screens.ScreenLayout> Store() =>
        new(Path.Combine(_root, "screens.json"), Path.Combine(_root, "backups"),
            () => new LedMenu.Core.Screens.ScreenLayout(), LedMenu.Core.Screens.ScreenLayout.ValidateFile);

    [Fact]
    public void Screens_round_trip_exactly_including_menu_reference_and_disabled_state()
    {
        var menu = Guid.NewGuid();
        var layout = new LedMenu.Core.Screens.ScreenLayout { Name = "Dual 2x2" };
        layout.Screens.Add(new() { Name = "Food Menu", X = 0, Y = 0, Width = 336, Height = 672, AssignedMenuId = menu });
        layout.Screens.Add(new() { Name = "Drink Menu", X = 336, Y = 0, Width = 336, Height = 672, Enabled = false });
        Store().Save(layout);

        var back = Store().Load();
        Assert.Equal(LoadStatus.Ok, back.Status);
        Assert.Equal("Dual 2x2", back.Value.Name);
        Assert.Equal(2, back.Value.Screens.Count);
        var food = back.Value.Screens[0];
        Assert.Equal((layout.Screens[0].Id, "Food Menu", 0, 0, 336, 672, true, (Guid?)menu),
                     (food.Id, food.Name, food.X, food.Y, food.Width, food.Height, food.Enabled, food.AssignedMenuId));
        Assert.False(back.Value.Screens[1].Enabled);
        Assert.Null(back.Value.Screens[1].AssignedMenuId);
        Assert.Equal(new[] { "Food Menu", "Drink Menu" }, back.Value.Screens.Select(s => s.Name));   // order preserved
    }

    [Fact]
    public void Out_of_range_screens_are_saved_and_restored_unchanged()
    {
        var layout = new LedMenu.Core.Screens.ScreenLayout();
        layout.Screens.Add(new() { Name = "Outside", X = 4000, Y = 3000, Width = 5000, Height = 4000 });
        layout.Screens.Add(new() { Name = "Broken", X = -10, Y = -20, Width = 0, Height = -1 });
        Store().Save(layout);

        var back = Store().Load().Value.Screens;
        Assert.Equal((4000, 3000, 5000, 4000), (back[0].X, back[0].Y, back[0].Width, back[0].Height));
        Assert.Equal((-10, -20, 0, -1), (back[1].X, back[1].Y, back[1].Width, back[1].Height));
    }

    [Fact]
    public void Corrupt_screen_file_recovers_from_backup()
    {
        var s = Store();
        var layout = new LedMenu.Core.Screens.ScreenLayout();
        layout.Screens.Add(new() { Name = "Keep me", Width = 336, Height = 672 });
        s.Save(layout);
        s.Save(layout);
        File.WriteAllText(Path.Combine(_root, "screens.json"), "{ nope");
        var r = s.Load();
        Assert.Equal(LoadStatus.RecoveredFromBackup, r.Status);
        Assert.Equal("Keep me", r.Value.Screens.Single().Name);
    }

    [Fact]
    public void Screen_file_and_settings_file_are_independent()
    {
        var paths = new AppPaths(_root);
        Assert.NotEqual(paths.SettingsFile, paths.ScreensFile);
    }
}

