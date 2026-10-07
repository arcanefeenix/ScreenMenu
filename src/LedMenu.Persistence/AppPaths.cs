namespace LedMenu.Persistence;

/// <summary>Resolves the application-data layout. The root is injectable so tests never touch real data.</summary>
public sealed class AppPaths
{
    public AppPaths(string root) => Root = root;

    /// <summary>Environment variable that moves all program data (menus, settings, logs) to another folder.</summary>
    public const string DataDirVariable = "LEDMENU_DATA_DIR";

    /// <summary>Normally <c>%APPDATA%\LedMenu</c>; the <see cref="DataDirVariable"/> setting overrides it (portable use, clean-start tests).</summary>
    public static AppPaths Default() => Resolve(Environment.GetEnvironmentVariable(DataDirVariable));

    public static AppPaths Resolve(string? overrideDir) => new(!string.IsNullOrWhiteSpace(overrideDir)
        ? Path.GetFullPath(overrideDir.Trim().Trim('"'))
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedMenu"));

    public string Root { get; }

    /// <summary>
    /// Short stable key for this data folder. The single-instance lock uses it, so two copies sharing one data folder
    /// are refused (they would overwrite each other) while a copy with its own folder, such as a test run, can start.
    /// </summary>
    public string InstanceKey
    {
        get
        {
            var normal = Path.GetFullPath(Root).TrimEnd('\\', '/').ToUpperInvariant();
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normal));
            return Convert.ToHexString(hash, 0, 8);
        }
    }
    public string Logs => Path.Combine(Root, "logs");
    public string Backups => Path.Combine(Root, "backups");
    public string Assets => Path.Combine(Root, "assets");
    public string Media => Path.Combine(Root, "media");
    public string Menus => Path.Combine(Root, "menus");
    public string Trash => Path.Combine(Root, "trash");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string ScreensFile => Path.Combine(Root, "screens.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Media);
        Directory.CreateDirectory(Menus);
        Directory.CreateDirectory(Trash);
    }
}
