using System.Security.Cryptography;
using LedMenu.Core.Assets;
using LedMenu.Core.Logging;

namespace LedMenu.Persistence;

public sealed class AssetImportException : Exception
{
    public AssetImportException(string message) : base(message) { }
}

public sealed record AssetImportResult(string FileName, bool AlreadyPresent);

/// <summary>
/// Copies images into the application's own assets folder so that moving or deleting the original never
/// breaks a menu. Menus keep only the returned file name. Files are named from their content hash, so importing
/// the same picture twice reuses the first copy, and nothing outside the assets folder can be referenced.
/// </summary>
public sealed class AssetStore
{
    private readonly string _directory;
    private readonly IAppLog _log;

    public AssetStore(string directory, IAppLog? log = null)
    {
        _directory = directory;
        _log = log ?? NullLog.Instance;
    }

    public AssetImportResult Import(string sourcePath)
    {
        FileInfo info;
        try { info = new FileInfo(sourcePath); }
        catch (Exception ex) { throw new AssetImportException("That file could not be opened: " + ex.Message); }

        if (!info.Exists) throw new AssetImportException("That file does not exist.");
        if (info.Length == 0) throw new AssetImportException("That file is empty.");
        if (info.Length > AssetRules.MaxBytes)
            throw new AssetImportException($"That file is {info.Length / (1024 * 1024)} MB; the limit is {AssetRules.MaxBytes / (1024 * 1024)} MB.");

        byte[] bytes;
        try { bytes = File.ReadAllBytes(sourcePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new AssetImportException("That file could not be read: " + ex.Message); }

        var kind = AssetRules.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, 16)))
                   ?? throw new AssetImportException("That is not a PNG, JPEG, BMP or GIF image.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var name = AssetRules.BuildFileName(hash, Path.GetFileNameWithoutExtension(sourcePath), kind);
        var dest = Path.Combine(_directory, name);

        Directory.CreateDirectory(_directory);

        // The same picture imported under another file name reuses the copy that is already here.
        var prefix = name[..name.IndexOf('-')] + "-*";
        foreach (var existing in Directory.EnumerateFiles(_directory, prefix))
        {
            if (new FileInfo(existing).Length == bytes.Length && File.ReadAllBytes(existing).AsSpan().SequenceEqual(bytes))
                return new AssetImportResult(Path.GetFileName(existing), true);
        }

        var tmp = dest + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, dest, overwrite: true);
        _log.Info($"Asset imported: {name} ({bytes.Length:N0} bytes) from {Path.GetFileName(sourcePath)}");
        return new AssetImportResult(name, false);
    }

    /// <summary>Full path of an asset, or null if the name is unsafe or the file is missing.</summary>
    public string? Resolve(string? fileName)
    {
        if (!AssetRules.IsSafeFileName(fileName)) return null;
        var path = Path.Combine(_directory, fileName!);
        return File.Exists(path) ? path : null;
    }

    public bool Exists(string? fileName) => Resolve(fileName) != null;
}
