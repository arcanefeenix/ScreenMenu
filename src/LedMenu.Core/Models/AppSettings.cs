namespace LedMenu.Core.Models;

/// <summary>Application-level settings. Display, screen and menu data live in their own files.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>UTC time of the last successful startup; useful when diagnosing an event installation.</summary>
    public DateTime? LastStartUtc { get; set; }

    /// <summary>Returns null when valid, otherwise a human-readable problem description.</summary>
    public static string? Validate(AppSettings? settings)
    {
        if (settings is null) return "Settings file is empty.";
        if (settings.SchemaVersion < 1) return $"Invalid schema version {settings.SchemaVersion}.";
        if (settings.SchemaVersion > CurrentSchemaVersion)
            return $"Settings were written by a newer version (schema {settings.SchemaVersion}).";
        return null;
    }
}
