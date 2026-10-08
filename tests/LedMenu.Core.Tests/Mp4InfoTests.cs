using System.Text;
using LedMenu.Core.Assets;

namespace LedMenu.Core.Tests;

public class Mp4InfoTests
{
    // ---- tiny MP4 builder: just enough boxes to carry a movie header ----

    private static byte[] U32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
    private static byte[] U64(ulong v) => U32((uint)(v >> 32)).Concat(U32((uint)v)).ToArray();

    private static byte[] Box(string type, byte[] body, bool bigSize = false, bool toEnd = false)
    {
        var t = Encoding.ASCII.GetBytes(type);
        if (bigSize) return U32(1).Concat(t).Concat(U64((ulong)(16 + body.Length))).Concat(body).ToArray();
        return U32(toEnd ? 0u : (uint)(8 + body.Length)).Concat(t).Concat(body).ToArray();
    }

    private static byte[] Mvhd(uint timescale, ulong duration, int version = 0)
    {
        var body = new List<byte> { (byte)version, 0, 0, 0 };
        if (version == 1) { body.AddRange(U64(0)); body.AddRange(U64(0)); body.AddRange(U32(timescale)); body.AddRange(U64(duration)); }
        else { body.AddRange(U32(0)); body.AddRange(U32(0)); body.AddRange(U32(timescale)); body.AddRange(U32((uint)duration)); }
        body.AddRange(new byte[80]);                                             // rate, volume, matrix and the rest of the header
        return Box("mvhd", body.ToArray());
    }

    private static byte[] Ftyp() => Box("ftyp", Encoding.ASCII.GetBytes("isom").Concat(new byte[8]).ToArray());

    private static double? Read(byte[] file) => Mp4Info.TryReadDurationSeconds(new MemoryStream(file));

    [Fact]
    public void The_true_length_is_duration_divided_by_timescale_not_a_whole_number_of_seconds()
    {
        var file = Ftyp().Concat(Box("moov", Mvhd(30000, 387900))).ToArray();      // 12.93 s at 30000 ticks per second
        Assert.Equal(12.93, Read(file)!.Value, 3);
    }

    [Fact]
    public void A_movie_header_after_the_media_data_is_found_the_usual_layout_of_an_exported_file()
    {
        var file = Ftyp().Concat(Box("mdat", new byte[5000])).Concat(Box("free", new byte[40])).Concat(Box("moov", Mvhd(1000, 38500))).ToArray();
        Assert.Equal(38.5, Read(file)!.Value, 3);
    }

    [Fact]
    public void A_movie_header_before_the_media_data_is_found_too()
    {
        var file = Ftyp().Concat(Box("moov", Mvhd(600, 3620))).Concat(Box("mdat", new byte[2000])).ToArray();
        Assert.Equal(6.0333, Read(file)!.Value, 3);
    }

    [Fact]
    public void Version_1_headers_with_64_bit_times_are_read()
    {
        var file = Ftyp().Concat(Box("moov", Mvhd(90000, 8_100_000, version: 1))).ToArray();
        Assert.Equal(90.0, Read(file)!.Value, 3);
    }

    [Fact]
    public void Boxes_with_a_64_bit_size_or_a_size_of_zero_meaning_to_the_end_are_walked_correctly()
    {
        var big = Ftyp().Concat(Box("mdat", new byte[300], bigSize: true)).Concat(Box("moov", Mvhd(1000, 4000))).ToArray();
        Assert.Equal(4.0, Read(big)!.Value, 3);
        var toEnd = Ftyp().Concat(Box("moov", Mvhd(1000, 7000), toEnd: true)).ToArray();
        Assert.Equal(7.0, Read(toEnd)!.Value, 3);
    }

    [Fact]
    public void Other_boxes_inside_the_movie_box_before_the_header_are_skipped()
    {
        var inner = Box("udta", new byte[100]).Concat(Mvhd(1000, 2500)).ToArray();
        Assert.Equal(2.5, Read(Ftyp().Concat(Box("moov", inner)).ToArray())!.Value, 3);
    }

    [Fact]
    public void Anything_odd_gives_null_so_the_caller_falls_back_to_what_Windows_says()
    {
        Assert.Null(Read(Array.Empty<byte>()));
        Assert.Null(Read(new byte[] { 1, 2, 3 }));
        Assert.Null(Read(Encoding.ASCII.GetBytes("this is plainly not a video file")));
        Assert.Null(Read(Ftyp()));                                                                   // no movie box at all
        Assert.Null(Read(Ftyp().Concat(Box("moov", Mvhd(0, 5000))).ToArray()));                      // timescale zero
        Assert.Null(Read(Ftyp().Concat(Box("moov", Mvhd(1000, 0))).ToArray()));                      // no length
        Assert.Null(Read(Ftyp().Concat(Box("moov", Mvhd(1, 4_000_000_000))).ToArray()));             // longer than a week: a damaged header
        Assert.Null(Read(Ftyp().Concat(Box("moov", new byte[20])).ToArray()));                       // movie box with no header inside
    }

    [Fact]
    public void A_truncated_file_or_a_box_that_claims_to_be_bigger_than_the_file_is_refused_not_crashed_on()
    {
        var good = Ftyp().Concat(Box("moov", Mvhd(1000, 5000))).ToArray();
        for (var cut = 1; cut < good.Length; cut += 7) Read(good[..cut]);                            // every truncation point: must not throw
        var lying = Ftyp().Concat(U32(100000)).Concat(Encoding.ASCII.GetBytes("moov")).Concat(new byte[50]).ToArray();
        Assert.Null(Read(lying));
        var huge = Ftyp().Concat(U32(uint.MaxValue)).Concat(Encoding.ASCII.GetBytes("moov")).Concat(new byte[20]).ToArray();
        Assert.Null(Read(huge));
    }

    [Fact]
    public void A_missing_or_unreadable_file_gives_null()
    {
        Assert.Null(Mp4Info.TryReadDurationSeconds(Path.Combine(Path.GetTempPath(), "no-such-file-" + Guid.NewGuid() + ".mp4")));
        Assert.Null(Mp4Info.TryReadDurationSeconds(""));
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var rnd = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var junk = new byte[rnd.Next(0, 400)];
            rnd.NextBytes(junk);
            if (junk.Length > 12) { junk[4] = (byte)'m'; junk[5] = (byte)'o'; junk[6] = (byte)'o'; junk[7] = (byte)'v'; }
            Read(junk);
        }
    }
}
