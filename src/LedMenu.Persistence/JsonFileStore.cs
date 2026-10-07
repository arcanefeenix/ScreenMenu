using System.Text.Json;
using LedMenu.Core.Logging;

namespace LedMenu.Persistence;

public enum LoadStatus
{
    /// <summary>Main file loaded normally.</summary>
    Ok,
    /// <summary>No file existed; defaults were created (first run).</summary>
    CreatedDefault,
    /// <summary>Main file was unusable; a backup was restored.</summary>
    RecoveredFromBackup,
    /// <summary>Main file and every backup were unusable; defaults were used. The corrupt file is preserved.</summary>
    DefaultedAfterFailure,
}

public sealed record LoadResult<T>(T Value, LoadStatus Status, string? Message);

/// <summary>
/// JSON persistence for one file with crash-safe writes and backup recovery.
/// Save: write temp -> flush -> re-read and validate -> back up current good file -> atomically replace.
/// Load: main file, else newest valid backup, else defaults. A corrupt file is renamed, never deleted.
/// </summary>
public sealed class JsonFileStore<T> where T : class
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly string _backupDir;
    private readonly Func<T> _createDefault;
    private readonly Func<T?, string?> _validate;
    private readonly IAppLog _log;
    private readonly int _keepBackups;
    private readonly object _gate = new();

    public JsonFileStore(string path, string backupDir, Func<T> createDefault,
        Func<T?, string?>? validate = null, IAppLog? log = null, int keepBackups = 10)
    {
        _path = path;
        _backupDir = backupDir;
        _createDefault = createDefault;
        _validate = validate ?? (v => v is null ? "File is empty." : null);
        _log = log ?? NullLog.Instance;
        _keepBackups = keepBackups;
    }

    private string Stem => Path.GetFileNameWithoutExtension(_path);

    public LoadResult<T> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                var recovered = TryRestoreFromBackups();
                if (recovered != null)
                {
                    _log.Warn($"{Stem}: main file missing; restored from backup.");
                    return new(recovered, LoadStatus.RecoveredFromBackup, "The data file was missing; the last backup was restored.");
                }
                return new(_createDefault(), LoadStatus.CreatedDefault, null);
            }

            var problem = TryRead(_path, out var value);
            if (problem == null) return new(value!, LoadStatus.Ok, null);

            _log.Error($"{Stem}: main file unusable ({problem}).");
            PreserveCorrupt();

            var fromBackup = TryRestoreFromBackups();
            if (fromBackup != null)
            {
                _log.Warn($"{Stem}: recovered from backup.");
                return new(fromBackup, LoadStatus.RecoveredFromBackup,
                    $"The data file was damaged ({problem}). The most recent good backup was restored.");
            }

            _log.Error($"{Stem}: no usable backup; using defaults. Corrupt file preserved.");
            return new(_createDefault(), LoadStatus.DefaultedAfterFailure,
                $"The data file was damaged ({problem}) and no backup could be restored. Defaults are in use; the damaged file was kept.");
        }
    }

    public void Save(T value)
    {
        lock (_gate)
        {
            var invalid = _validate(value);
            if (invalid != null) throw new InvalidOperationException($"Refusing to save invalid data: {invalid}");

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";

            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(fs, value, Options);
                fs.Flush(flushToDisk: true);
            }

            var problem = TryRead(tmp, out _);
            if (problem != null)
            {
                TryDelete(tmp);
                throw new IOException($"Written file failed validation: {problem}");
            }

            if (File.Exists(_path))
            {
                if (TryRead(_path, out _) == null) BackupCurrent();
                File.Replace(tmp, _path, null);
            }
            else
            {
                File.Move(tmp, _path);
            }
        }
    }

    private string? TryRead(string file, out T? value)
    {
        value = null;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            value = JsonSerializer.Deserialize<T>(fs, Options);
            return _validate(value);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private void BackupCurrent()
    {
        try
        {
            Directory.CreateDirectory(_backupDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var dest = Path.Combine(_backupDir, $"{Stem}.{stamp}.json");
            for (var i = 1; File.Exists(dest); i++)
                dest = Path.Combine(_backupDir, $"{Stem}.{stamp}-{i}.json");
            File.Copy(_path, dest);
            foreach (var old in BackupFiles().Skip(_keepBackups)) TryDelete(old);
        }
        catch (Exception ex)
        {
            _log.Warn($"{Stem}: could not create backup.", ex);
        }
    }

    /// <summary>Newest first.</summary>
    private IEnumerable<string> BackupFiles() =>
        Directory.Exists(_backupDir)
            ? Directory.EnumerateFiles(_backupDir, $"{Stem}.*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    private T? TryRestoreFromBackups()
    {
        foreach (var backup in BackupFiles())
        {
            if (TryRead(backup, out var value) == null)
            {
                try { File.Copy(backup, _path, overwrite: true); }
                catch (Exception ex) { _log.Warn($"{Stem}: restored data in memory but could not rewrite main file.", ex); }
                return value;
            }
            _log.Warn($"{Stem}: backup {Path.GetFileName(backup)} is also unusable.");
        }
        return null;
    }

    private void PreserveCorrupt()
    {
        try
        {
            var dest = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}";
            File.Move(_path, dest);
            _log.Warn($"{Stem}: corrupt file preserved as {Path.GetFileName(dest)}.");
        }
        catch (Exception ex)
        {
            _log.Warn($"{Stem}: could not preserve corrupt file.", ex);
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
