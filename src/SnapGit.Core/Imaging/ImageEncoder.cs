using System.Drawing.Imaging;

namespace SnapGit.Core.Imaging;

public static class ImageEncoder
{
    public static string GetExtension(ImageFileFormat format) => format switch
    {
        ImageFileFormat.Jpeg => ".jpg",
        ImageFileFormat.Bmp => ".bmp",
        ImageFileFormat.Gif => ".gif",
        _ => ".png",
    };

    /// <summary>Maps a file extension to a format; unknown extensions fall back to PNG.</summary>
    public static ImageFileFormat FromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ImageFileFormat.Jpeg,
        ".bmp" => ImageFileFormat.Bmp,
        ".gif" => ImageFileFormat.Gif,
        _ => ImageFileFormat.Png,
    };

    public static void Save(Bitmap image, string path, ImageFileFormat format, int jpegQuality = 90)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // JPEG/BMP have no alpha: flatten onto white (otherwise transparent areas turn black).
        using var flattened = format == ImageFileFormat.Png ? null : FlattenOnWhite(image);
        var source = flattened ?? image;

        switch (format)
        {
            case ImageFileFormat.Jpeg:
                using (var parameters = JpegParameters(jpegQuality))
                    source.Save(path, JpegCodec(), parameters);
                break;
            case ImageFileFormat.Bmp:
                source.Save(path, ImageFormat.Bmp);
                break;
            case ImageFileFormat.Gif:
                source.Save(path, ImageFormat.Gif);
                break;
            default:
                source.Save(path, ImageFormat.Png);
                break;
        }
    }

    public static void Save(Bitmap image, Stream stream, ImageFileFormat format, int jpegQuality = 90)
    {
        if (format == ImageFileFormat.Jpeg)
        {
            using var flattened = FlattenOnWhite(image);
            using var parameters = JpegParameters(jpegQuality);
            flattened.Save(stream, JpegCodec(), parameters);
        }
        else
        {
            image.Save(stream, format switch { ImageFileFormat.Bmp => ImageFormat.Bmp, ImageFileFormat.Gif => ImageFormat.Gif, _ => ImageFormat.Png });
        }
    }

    private static ImageCodecInfo JpegCodec() => ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private static EncoderParameters JpegParameters(int quality)
    {
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 1, 100));
        return parameters;
    }

    public static Bitmap FlattenOnWhite(Bitmap source)
    {
        var flat = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(flat);
        g.Clear(Color.White);
        g.DrawImageUnscaled(source, 0, 0);
        return flat;
    }

    /// <summary>
    /// Loads an image fully into memory and releases the file, so it can be overwritten/deleted afterwards
    /// (GDI+ otherwise keeps the file locked for the lifetime of the Bitmap).
    /// </summary>
    public static Bitmap LoadUnlocked(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var loaded = new Bitmap(stream);
        return new Bitmap(loaded); // deep copy, detached from the stream
    }
}
