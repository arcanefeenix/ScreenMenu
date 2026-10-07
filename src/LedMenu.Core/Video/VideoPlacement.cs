using LedMenu.Core.Calibration;
using LedMenu.Core.Screens;

namespace LedMenu.Core.Video;

/// <summary>Where a video's picture goes inside its screen. The rectangle may be smaller than the screen (Fit leaves black bars) or larger (Fill crops).</summary>
public static class VideoPlacement
{
    /// <summary>
    /// A video exactly the screen's size is always placed 1:1, whatever the fit setting says, so a clip made for the screen is never resampled.
    /// Fit scales the whole picture to fit inside the screen and centres it; Fill scales it to cover the whole screen and centres it
    /// (what sticks out is cropped by the screen's own edge). Coordinates are whole pixels; centring rounds down.
    /// An unknown video size fills the screen.
    /// </summary>
    public static PixelRegion Compute(int videoWidth, int videoHeight, int screenWidth, int screenHeight, VideoFit fit)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return new PixelRegion(0, 0, Math.Max(screenWidth, 1), Math.Max(screenHeight, 1));
        if (videoWidth <= 0 || videoHeight <= 0 || (videoWidth == screenWidth && videoHeight == screenHeight))
            return new PixelRegion(0, 0, screenWidth, screenHeight);

        var scaleX = (double)screenWidth / videoWidth;
        var scaleY = (double)screenHeight / videoHeight;
        var scale = fit == VideoFit.Fill ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);

        var w = Math.Max(1, (int)Math.Round(videoWidth * scale, MidpointRounding.AwayFromZero));
        var h = Math.Max(1, (int)Math.Round(videoHeight * scale, MidpointRounding.AwayFromZero));
        // the limiting side lands exactly on the screen's size (rounding must not leave a one-pixel gap or overflow)
        if (fit == VideoFit.Fit) { if (scaleX <= scaleY) w = screenWidth; else h = screenHeight; }
        else { if (scaleX >= scaleY) w = screenWidth; else h = screenHeight; }

        return new PixelRegion((screenWidth - w) >> 1, (screenHeight - h) >> 1, w, h);
    }

    /// <summary>True when the picture is shown without any scaling.</summary>
    public static bool IsNative(PixelRegion dest, int videoWidth, int videoHeight) =>
        dest.Width == videoWidth && dest.Height == videoHeight;
}

/// <summary>
/// Windows hands over limited-range ("video range") pictures without stretching 16-235 to 0-255, which would leave whites grey
/// and blacks lifted on the wall. This stretches them back, in place. Measured against ffmpeg's own decode of real clips the remaining error is about 1/255.
/// </summary>
public static class VideoLevels
{
    private static readonly byte[] Lut = BuildLut();

    private static byte[] BuildLut()
    {
        var lut = new byte[256];
        for (var v = 0; v < 256; v++) lut[v] = (byte)Math.Clamp((int)Math.Round((v - 16) * 255.0 / 219.0), 0, 255);
        return lut;
    }

    /// <summary>Stretches the colour channels of a BGRA buffer. The alpha channel is left alone.</summary>
    public static void ExpandLimitedRange(byte[] bgra)
    {
        var lut = Lut;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            bgra[i] = lut[bgra[i]];
            bgra[i + 1] = lut[bgra[i + 1]];
            bgra[i + 2] = lut[bgra[i + 2]];
        }
    }

    public static byte Expand(byte v) => Lut[v];
}
