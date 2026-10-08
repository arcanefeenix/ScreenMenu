using System.Security.Cryptography;
using LedMenu.Core.Assets;
using LedMenu.Core.Logging;
using LedMenu.Core.Screens;

namespace LedMenu.Persistence;

public sealed class MediaImportException : Exception
{
    public MediaImportException(string message) : base(message) { }
}

public sealed record MediaImportResult(VideoItem Item, bool AlreadyPresent);

/// <summary>
/// Copies videos into the application's own media folder so that moving or deleting the original never breaks a playlist.
/// Playlists keep only the returned file name. Files are named from their content hash, so importing the same video twice
/// reuses the first copy. Large files are streamed (never loaded whole into memory), and a file is only kept if Windows can
/// actually open it: a video that cannot be played is rejected at import, with a reason, instead of failing at the event.
/// </summary>
public sealed class MediaStore
{
    private readonly string _directory;
    private readonly IVideoProbe _probe;
    private readonly IAppLog _log;

    public MediaStore(string directory, IVideoProbe probe, IAppLog? log = null)
    {
        _directory = directory;
        _probe = probe;
        _log = log ?? NullLog.Instance;
    }

    public string FolderPath => _directory;

    public async Task<MediaImportResult> ImportAsync(string sourcePath, CancellationToken cancel = default)
    {
        FileInfo info;
        try { info = new FileInfo(sourcePath); }
        catch (Exception ex) { throw new MediaImportException("That file could not be opened: " + ex.Message); }

        if (!info.Exists) throw new MediaImportException("That file does not exist.");
        if (info.Length == 0) throw new MediaImportException("That file is empty.");
        if (info.Length > MediaRules.MaxBytes)
            throw new MediaImportException($"That file is {info.Length / (1024L * 1024 * 1024)} GB; the limit is {MediaRules.MaxBytes / (1024L * 1024 * 1024)} GB.");

        VideoContainer kind;
        string hash;
        try
        {
            var head = new byte[16];
            using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var read = fs.Read(head, 0, head.Length);
            kind = MediaRules.Detect(head.AsSpan(0, read))
                   ?? throw new MediaImportException("That does not look like a video file (MP4 with H.264 is recommended; WMV, AVI and MKV may also work).");
            fs.Position = 0;
            hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, cancel));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MediaImportException("That file could not be read: " + ex.Message);
        }

        var name = MediaRules.BuildFileName(hash, Path.GetFileNameWithoutExtension(sourcePath), kind);
        var dest = Path.Combine(_directory, name);
        Directory.CreateDirectory(_directory);

        // The same content imported under another name reuses the copy already here (checked by hash, not just by name).
        var prefix = name[..name.IndexOf('-')] + "-*";
        string? existingName = null;
        foreach (var existing in Directory.EnumerateFiles(_directory, prefix))
        {
            if (existing.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            if (new FileInfo(existing).Length != info.Length) continue;
            using var efs = File.OpenRead(existing);
            if (Convert.ToHexString(await SHA256.HashDataAsync(efs, cancel)) == hash) { existingName = Path.GetFileName(existing); break; }
        }

        var alreadyThere = existingName != null;
        var storedName = existingName ?? name;
        var storedPath = Path.Combine(_directory, storedName);

        if (!alreadyThere)
        {
            var free = TryFreeSpace(_directory);
            if (free != null && free < info.Length + 200L * 1024 * 1024)
                throw new MediaImportException($"Not enough free disk space: the video needs {info.Length / (1024 * 1024)} MB and about {Math.Max(0, free.Value / (1024 * 1024))} MB is free.");

            var tmp = dest + ".tmp";
            try
            {
                using (var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await src.CopyToAsync(dst, 1 << 20, cancel);
                    dst.Flush(flushToDisk: true);
                }
                File.Move(tmp, dest, overwrite: true);
            }
            catch (OperationCanceledException) { TryDelete(tmp); throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(tmp);
                throw new MediaImportException("The video could not be copied into the program's media folder: " + ex.Message);
            }
        }

        VideoProbeResult probe;
        try { probe = await _probe.ProbeAsync(storedPath, cancel); }
        catch (OperationCanceledException) { if (!alreadyThere) TryDelete(storedPath); throw; }
        catch (Exception ex) { probe = VideoProbeResult.Failure(ex.Message); }

        if (!probe.Ok || probe.Width <= 0 || probe.Height <= 0)
        {
            if (!alreadyThere) TryDelete(storedPath);      // never keep a file that cannot be played
            _log.Warn($"Video rejected at import: {Path.GetFileName(sourcePath)}: {probe.Problem ?? "no picture found"}");
            throw new MediaImportException(
                "Windows cannot play that video" + (string.IsNullOrWhiteSpace(probe.Problem) ? "." : $" ({probe.Problem})") +
                " MP4 with H.264 video is the safest format; try re-exporting it that way.");
        }

        // Windows reports whole seconds (12.93 s shows as 12.0 s); the file's own header knows the real length
        var seconds = Mp4Info.TryReadDurationSeconds(storedPath) ?? probe.DurationSeconds;
        _log.Info($"Video imported: {storedName} {probe.Width}x{probe.Height}, {seconds:0.00} s" + (alreadyThere ? " (already present)" : ""));
        var item = new VideoItem
        {
            FileName = storedName,
            DisplayName = Path.GetFileName(sourcePath),
            Width = probe.Width,
            Height = probe.Height,
            DurationSeconds = seconds,
        };
        return new MediaImportResult(item, alreadyThere);
    }

    /// <summary>Full path of a stored video, or null if the name is unsafe or the file is missing.</summary>
    public string? Resolve(string? fileName)
    {
        if (!MediaRules.IsSafeFileName(fileName)) return null;
        var path = Path.Combine(_directory, fileName!);
        return File.Exists(path) ? path : null;
    }

    public bool Exists(string? fileName) => Resolve(fileName) != null;

    /// <summary>Removes media files no playlist refers to. Returns the names removed. Never touches anything that is referenced.</summary>
    public IReadOnlyList<string> RemoveUnused(IReadOnlySet<string> referencedNames)
    {
        var removed = new List<string>();
        if (!Directory.Exists(_directory)) return removed;
        foreach (var f in Directory.EnumerateFiles(_directory))
        {
            var name = Path.GetFileName(f);
            if (referencedNames.Contains(name)) continue;
            try { File.Delete(f); removed.Add(name); }
            catch (Exception ex) { _log.Warn($"Could not remove unused media {name}.", ex); }
        }
        return removed;
    }

    private static long? TryFreeSpace(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return null; }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
