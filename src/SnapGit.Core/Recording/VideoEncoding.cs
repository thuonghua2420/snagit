namespace SnapGit.Core.Recording;

/// <summary>Size of the recorded video relative to the area that is recorded.</summary>
public enum VideoResolution
{
    /// <summary>Exactly the pixels of the recorded area (sharpest for small areas, no scaling).</summary>
    Original,

    /// <summary>Fits inside 1920 × 1080 (Full HD): large areas are scaled down, small ones up (at most 2×).</summary>
    FullHd,

    /// <summary>Fits inside 1280 × 720 (smaller files).</summary>
    Hd,
}

/// <summary>How many bits the encoder may spend per pixel. Screen content (text, thin lines) needs far more than camera video.</summary>
public enum VideoQuality
{
    Standard,
    High,
    Best,
}

/// <summary>The numbers behind a recording: output size and bitrate. Pure, so it is unit-tested.</summary>
public static class VideoEncoding
{
    /// <summary>Upscaling beyond this factor only makes a bigger, blurrier file.</summary>
    public const double MaxUpscale = 2.0;

    /// <summary>Output size for a recorded <paramref name="area"/> (always even: H.264 needs it).</summary>
    public static Size OutputSize(Size area, VideoResolution resolution)
    {
        var native = new Size(Even(area.Width), Even(area.Height));
        var bounds = resolution switch
        {
            VideoResolution.FullHd => new Size(1920, 1080),
            VideoResolution.Hd => new Size(1280, 720),
            _ => Size.Empty,
        };
        if (bounds.IsEmpty || native.Width < 2 || native.Height < 2) return native;

        var scale = Math.Min((double)bounds.Width / native.Width, (double)bounds.Height / native.Height);
        scale = Math.Min(scale, MaxUpscale);
        return new Size(Even((int)Math.Round(native.Width * scale)), Even((int)Math.Round(native.Height * scale)));
    }

    /// <summary>Bits per second for the given picture size and frame rate.</summary>
    public static uint Bitrate(Size output, int framesPerSecond, VideoQuality quality)
    {
        var bitsPerPixel = quality switch
        {
            VideoQuality.Standard => 0.10,
            VideoQuality.Best => 0.30,
            _ => 0.18,
        };
        var bitrate = output.Width * (double)output.Height * Math.Clamp(framesPerSecond, 5, 60) * bitsPerPixel;
        return (uint)Math.Clamp(bitrate, 2_000_000, 50_000_000);
    }

    private static int Even(int value) => Math.Max(2, value & ~1);
}
