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
