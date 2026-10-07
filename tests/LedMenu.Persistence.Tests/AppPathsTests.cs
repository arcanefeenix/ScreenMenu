using LedMenu.Persistence;

namespace LedMenu.Persistence.Tests;

public class AppPathsTests
{
    [Fact]
    public void EnsureCreated_makes_expected_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ledmenu-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = new AppPaths(root);
            p.EnsureCreated();
            Assert.True(Directory.Exists(p.Logs));
            Assert.True(Directory.Exists(p.Backups));
            Assert.True(Directory.Exists(p.Assets));
            Assert.Equal(Path.Combine(root, "settings.json"), p.SettingsFile);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Default_root_is_under_AppData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(appData, AppPaths.Default().Root);
    }
}

public class AppPathsOverrideTests
{
    [Fact]
    public void Without_an_override_data_lives_under_the_roaming_app_data_folder()
    {
        var p = AppPaths.Resolve(null);
        Assert.EndsWith(Path.Combine("LedMenu"), p.Root);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), p.Root);
        Assert.Equal(p.Root, AppPaths.Resolve("  ").Root);
    }

    [Fact]
    public void An_override_moves_everything_and_tolerates_quotes_and_spaces()
    {
        var dir = Path.Combine(Path.GetTempPath(), "led menu data");
        var p = AppPaths.Resolve("\"" + dir + "\"");
        Assert.Equal(dir, p.Root);
        Assert.Equal(Path.Combine(dir, "menus"), p.Menus);
        Assert.Equal(Path.Combine(dir, "settings.json"), p.SettingsFile);
        Assert.Equal(Path.Combine(dir, "logs"), p.Logs);
    }
}

public class AppPathsInstanceKeyTests
{
    [Fact]
    public void The_same_folder_always_gives_the_same_key_however_it_is_written()
    {
        var a = AppPaths.Resolve(Path.Combine(Path.GetTempPath(), "LedData")).InstanceKey;
        var b = AppPaths.Resolve(Path.Combine(Path.GetTempPath(), "LedData") + Path.DirectorySeparatorChar).InstanceKey;
        var c = AppPaths.Resolve(Path.Combine(Path.GetTempPath(), "LEDDATA")).InstanceKey;
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Different_folders_give_different_keys_so_a_test_copy_can_run_beside_the_real_one() =>
        Assert.NotEqual(AppPaths.Resolve(Path.Combine(Path.GetTempPath(), "one")).InstanceKey,
                        AppPaths.Resolve(Path.Combine(Path.GetTempPath(), "two")).InstanceKey);

    [Fact]
    public void The_key_is_safe_to_put_in_a_lock_name()
    {
        var k = AppPaths.Resolve(Path.Combine(Path.GetTempPath(), "x")).InstanceKey;
        Assert.Matches("^[0-9A-F]{16}$", k);
    }
}
