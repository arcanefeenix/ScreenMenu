using System.Text;

namespace LedMenu.Core.Logging;

/// <summary>
/// Thread-safe daily-rolling text log. Logging must never throw into the caller:
/// any I/O failure is swallowed so a full disk cannot take down the operator app.
/// </summary>
public sealed class FileLog : IAppLog
{
    private readonly string _directory;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();

    public FileLog(string directory, int retainDays = 14, Func<DateTime>? now = null)
    {
        _directory = directory;
        _now = now ?? (() => DateTime.Now);
        try
        {
            Directory.CreateDirectory(directory);
            Prune(retainDays);
        }
        catch { /* logging is best-effort */ }
    }

    public string CurrentFilePath => Path.Combine(_directory, $"ledmenu-{_now():yyyy-MM-dd}.log");

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(_now().ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
              .Append(message);
            if (exception != null) sb.AppendLine().Append(exception);
            sb.AppendLine();
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(CurrentFilePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { /* never throw from the logger */ }
    }

    private void Prune(int retainDays)
    {
        var cutoff = _now().AddDays(-retainDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "ledmenu-*.log"))
        {
            try { if (File.GetLastWriteTime(file) < cutoff) File.Delete(file); }
            catch { }
        }
    }
}
