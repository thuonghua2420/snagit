using System.Drawing.Imaging;

namespace SnapGit.Core.Imaging;

/// <summary>Reads an animated GIF into frames (with their delays) and writes a sub-range back: used by "Trim".</summary>
public static class GifTrimmer
{
    private const int PropertyTagFrameDelay = 0x5100;

    /// <summary>Decodes every frame as a full, composited bitmap. The caller disposes the bitmaps.</summary>
    public static List<GifFrame> ReadFrames(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path)); // don't keep the file locked
        using var gif = Image.FromStream(stream);
        var dimension = new FrameDimension(gif.FrameDimensionsList[0]);
        var count = gif.GetFrameCount(dimension);

        // Delays are stored in 1/100 s, one 4-byte entry per frame.
        var delays = new int[count];
        try
        {
            var item = gif.GetPropertyItem(PropertyTagFrameDelay);
            for (var i = 0; i < count && item?.Value is { } value && i * 4 + 3 < value.Length; i++)
                delays[i] = BitConverter.ToInt32(value, i * 4) * 10;
        }
        catch (ArgumentException)
        {
            // no delay table: fall through to the default below
        }

        var frames = new List<GifFrame>(count);
        for (var i = 0; i < count; i++)
        {
            gif.SelectActiveFrame(dimension, i);
            frames.Add(new GifFrame(new Bitmap(gif), delays[i] > 0 ? delays[i] : 100));
        }
        return frames;
    }

    /// <summary>Total playing time of the frames in milliseconds.</summary>
    public static int TotalMilliseconds(IEnumerable<GifFrame> frames) => frames.Sum(f => f.DelayMs);

    /// <summary>Writes frames [<paramref name="first"/>..<paramref name="last"/>] (inclusive) of <paramref name="source"/> to <paramref name="target"/>.</summary>
    public static int Trim(string source, string target, int first, int last)
    {
        var frames = ReadFrames(source);
        try
        {
            first = Math.Clamp(first, 0, frames.Count - 1);
            last = Math.Clamp(last, first, frames.Count - 1);
            var kept = frames.GetRange(first, last - first + 1);
            GifEncoder.Write(target, kept);
            return kept.Count;
        }
        finally
        {
            foreach (var frame in frames) frame.Image.Dispose();
        }
    }
}
