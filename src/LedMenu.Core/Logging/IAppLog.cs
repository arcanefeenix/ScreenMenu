namespace LedMenu.Core.Logging;

public enum LogLevel { Info, Warn, Error }

/// <summary>Minimal diagnostic log. Use for significant events only, never per-frame.</summary>
public interface IAppLog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

public static class AppLogExtensions
{
    public static void Info(this IAppLog log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this IAppLog log, string message, Exception? ex = null) => log.Write(LogLevel.Warn, message, ex);
    public static void Error(this IAppLog log, string message, Exception? ex = null) => log.Write(LogLevel.Error, message, ex);
}

/// <summary>Discards everything. Used where no log is configured.</summary>
public sealed class NullLog : IAppLog
{
    public static readonly NullLog Instance = new();
    public void Write(LogLevel level, string message, Exception? exception = null) { }
}
