namespace LedMenu.Core.Assets;

/// <summary>
/// Reads a video's true length straight from an MP4/MOV header. Windows reports lengths rounded down to whole seconds
/// (a 12.93 s clip shows as 12.0 s), which is fine for playing but wrong for showing the operator. The length is stored in the
/// movie header box ("moov" then "mvhd") as a count of ticks and a number of ticks per second. Anything unusual returns null,
/// and the caller falls back to what Windows says.
/// </summary>
public static class Mp4Info
{
    private const int MaxMoovBytes = 16 * 1024 * 1024;

    public static double? TryReadDurationSeconds(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return TryReadDurationSeconds(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static double? TryReadDurationSeconds(Stream stream)
    {
        if (!stream.CanSeek) return null;
        var length = stream.Length;
        long position = 0;
        var header = new byte[16];

        // walk the top-level boxes: each starts with a 4-byte size and a 4-byte type
        for (var guard = 0; guard < 10_000 && position + 8 <= length; guard++)
        {
            stream.Position = position;
            if (stream.Read(header, 0, 8) < 8) return null;
            long size = ReadUInt32(header, 0);
            var type = (header[4], header[5], header[6], header[7]);
            long headerSize = 8;

            if (size == 1)                                   // a 64-bit size follows
            {
                if (stream.Read(header, 8, 8) < 8) return null;
                size = unchecked((long)ReadUInt64(header, 8));
                headerSize = 16;
            }
            else if (size == 0) size = length - position;    // the box runs to the end of the file
            if (size < headerSize || position + size > length) return null;

            if (type == ((byte)'m', (byte)'o', (byte)'o', (byte)'v'))
            {
                var contentLength = size - headerSize;
                if (contentLength <= 0 || contentLength > MaxMoovBytes) return null;
                var content = new byte[contentLength];
                stream.Position = position + headerSize;
                if (ReadFully(stream, content) < content.Length) return null;
                return FindMovieHeaderDuration(content);
            }

            position += size;
        }
        return null;
    }

    /// <summary>Looks through the boxes directly inside "moov" for "mvhd" and works out the length.</summary>
    private static double? FindMovieHeaderDuration(byte[] moov)
    {
        var p = 0;
        while (p + 8 <= moov.Length)
        {
            long size = ReadUInt32(moov, p);
            var isMvhd = moov[p + 4] == 'm' && moov[p + 5] == 'v' && moov[p + 6] == 'h' && moov[p + 7] == 'd';
            var headerSize = 8;
            if (size == 1) { if (p + 16 > moov.Length) return null; size = unchecked((long)ReadUInt64(moov, p + 8)); headerSize = 16; }
            else if (size == 0) size = moov.Length - p;
            if (size < headerSize || p + size > moov.Length) return null;

            if (isMvhd)
            {
                var body = p + headerSize;
                var version = moov[body];
                uint timescale; ulong duration;
                if (version == 1)                            // 64-bit times: version/flags(4) created(8) modified(8) timescale(4) duration(8)
                {
                    if (body + 32 > moov.Length) return null;
                    timescale = ReadUInt32(moov, body + 20);
                    duration = ReadUInt64(moov, body + 24);
                }
                else                                         // version/flags(4) created(4) modified(4) timescale(4) duration(4)
                {
                    if (body + 20 > moov.Length) return null;
                    timescale = ReadUInt32(moov, body + 12);
                    duration = ReadUInt32(moov, body + 16);
                }
                if (timescale == 0 || duration == 0 || duration == uint.MaxValue || duration == ulong.MaxValue) return null;
                var seconds = (double)duration / timescale;
                return seconds is > 0 and < 7 * 24 * 3600 ? seconds : null;      // anything past a week is a damaged header
            }
            p += (int)size;
        }
        return null;
    }

    private static uint ReadUInt32(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);

    private static ulong ReadUInt64(byte[] b, int i) => (ulong)ReadUInt32(b, i) << 32 | ReadUInt32(b, i + 4);

    private static int ReadFully(Stream s, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = s.Read(buffer, total, buffer.Length - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
