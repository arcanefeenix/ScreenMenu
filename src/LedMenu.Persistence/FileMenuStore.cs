using LedMenu.Core.Logging;
using LedMenu.Core.Menus;

namespace LedMenu.Persistence;

/// <summary>
/// One JSON file per menu in the menus folder, each saved atomically with its own backups (see
/// <see cref="JsonFileStore{T}"/>). One damaged menu cannot affect the others. A file that cannot be read and has
/// no usable backup is left in place (renamed, never deleted) and reported; it is never replaced by an empty menu.
/// Deleting a menu moves its file to the trash folder.
/// </summary>
public sealed class FileMenuStore : IMenuStore
{
    private readonly AppPaths _paths;
    private readonly IAppLog _log;
    private readonly Dictionary<Guid, string> _fileById = new();

    public FileMenuStore(AppPaths paths, IAppLog? log = null)
    {
        _paths = paths;
        _log = log ?? NullLog.Instance;
    }

    public MenuLoadResult LoadAll()
    {
        Directory.CreateDirectory(_paths.Menus);
        _fileById.Clear();

        var menus = new List<Menu>();
        var issues = new List<MenuLoadIssue>();

        foreach (var file in Directory.EnumerateFiles(_paths.Menus, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            var result = StoreFor(file).Load();
            switch (result.Status)
            {
                case LoadStatus.Ok:
                    Accept(result.Value, file, menus);
                    break;
                case LoadStatus.RecoveredFromBackup:
                    Accept(result.Value, file, menus);
                    issues.Add(new MenuLoadIssue(name, $"\"{result.Value.Name}\" was damaged and was restored from its last good backup. {result.Message}", true));
                    break;
                case LoadStatus.DefaultedAfterFailure:
                    issues.Add(new MenuLoadIssue(name, "This menu file is damaged and no backup could restore it, so it was not loaded. " +
                                                      "The damaged file was kept next to it for recovery. " + result.Message, false));
                    break;
            }
        }
        return new MenuLoadResult(menus, issues);
    }

    public void Save(Menu menu)
    {
        if (!_fileById.TryGetValue(menu.Id, out var file))
        {
            Directory.CreateDirectory(_paths.Menus);
            file = Path.Combine(_paths.Menus, menu.Id.ToString("D") + ".json");
            _fileById[menu.Id] = file;
        }
        StoreFor(file).Save(menu);
    }

    public void Delete(Menu menu)
    {
        var file = _fileById.TryGetValue(menu.Id, out var f) ? f : Path.Combine(_paths.Menus, menu.Id.ToString("D") + ".json");
        _fileById.Remove(menu.Id);
        if (!File.Exists(file)) return;

        Directory.CreateDirectory(_paths.Trash);
        var stem = Path.GetFileNameWithoutExtension(file);
        var dest = Path.Combine(_paths.Trash, $"{stem}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
        for (var i = 1; File.Exists(dest); i++)
            dest = Path.Combine(_paths.Trash, $"{stem}.{DateTime.Now:yyyyMMdd-HHmmss-fff}-{i}.json");
        File.Move(file, dest);
        _log.Info($"Menu file moved to trash: {Path.GetFileName(dest)}");
    }

    private void Accept(Menu menu, string file, List<Menu> into)
    {
        _fileById[menu.Id] = file;
        into.Add(menu);
    }

    private JsonFileStore<Menu> StoreFor(string file) =>
        new(file, _paths.Backups, () => new Menu { Id = Guid.Empty }, MenuValidator.ValidateFile, _log);
}
