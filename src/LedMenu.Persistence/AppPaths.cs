namespace LedMenu.Persistence;

/// <summary>Resolves the application-data layout. The root is injectable so tests never touch real data.</summary>
public sealed class AppPaths
{
    public AppPaths(string root) => Root = root;

    public static AppPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LedMenu"));

    public string Root { get; }
    public string Logs => Path.Combine(Root, "logs");
    public string Backups => Path.Combine(Root, "backups");
    public string Assets => Path.Combine(Root, "assets");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Assets);
    }
}
