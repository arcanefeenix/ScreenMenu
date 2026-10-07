using LedMenu.Core.Logging;

namespace LedMenu.Core.Tests;

public class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ledmenu-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Writes_level_message_and_exception()
    {
        var log = new FileLog(_dir);
        log.Info("hello");
        log.Error("boom", new InvalidOperationException("bad"));
        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains("[INFO] hello", text);
        Assert.Contains("[ERROR] boom", text);
        Assert.Contains("InvalidOperationException", text);
    }

    [Fact]
    public void Appends_across_instances()
    {
        new FileLog(_dir).Info("one");
        var second = new FileLog(_dir);
        second.Info("two");
        var text = File.ReadAllText(second.CurrentFilePath);
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [Fact]
    public void Prunes_old_files_on_startup()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "ledmenu-2000-01-01.log");
        File.WriteAllText(old, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        _ = new FileLog(_dir, retainDays: 14);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Never_throws_when_directory_is_unusable()
    {
        // A path under an existing *file* cannot be created as a directory.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var log = new FileLog(Path.Combine(blocker, "logs"));
        var ex = Record.Exception(() => log.Info("still fine"));
        Assert.Null(ex);
    }
}
