using System.Drawing.Drawing2D;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SnapGit.App.Services;

/// <summary>Builds library thumbnails for every item kind (image, project preview, GIF first frame, video poster frame).</summary>
public static class ThumbnailLoader
{
    public static readonly Size Size = new(176, 112);

    /// <summary>Returns the picture scaled into a <see cref="Size"/> tile, or null when the file can't be read.</summary>
    public static async Task<Bitmap?> LoadAsync(LibraryItem item)
    {
        try
        {
            using var source = await LoadSourceAsync(item);
            return source is null ? null : Compose(source, item.Kind);
        }
        catch
        {
            return null; // unreadable / in use: keep the placeholder
        }
    }

    /// <summary>The favourite star, drawn over a copy of the cached tile (so toggling it never needs a re-read).</summary>
    public static Bitmap WithFavoriteBadge(Bitmap tile)
    {
        var copy = new Bitmap(tile);
        using var g = Graphics.FromImage(copy);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var gold = new SolidBrush(Color.FromArgb(251, 192, 45));
        using var outline = new Pen(Color.FromArgb(160, 90, 60, 0), 1.2f);
        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? 10f : 4.2f;
            var angle = -MathF.PI / 2 + i * MathF.PI / 5;
            points[i] = new PointF(Size.Width - 16 + radius * MathF.Cos(angle), 16 + radius * MathF.Sin(angle));
        }
        g.FillPolygon(gold, points);
        g.DrawPolygon(outline, points);
        return copy;
    }

    private static async Task<Bitmap?> LoadSourceAsync(LibraryItem item)
    {
        switch (item.Kind)
        {
            case LibraryItemKind.Project:
                return await Task.Run(() => SnapProjectFile.ReadPreview(item.FilePath));

            case LibraryItemKind.Video:
                var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                using (var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 256))
                {
                    if (thumbnail is null || thumbnail.Size == 0) return null;
                    using var stream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(thumbnail);
                    using var loaded = new Bitmap(stream);
                    return new Bitmap(loaded);
                }

            default:
                return await Task.Run(() => ImageEncoder.LoadUnlocked(item.FilePath));
        }
    }

    private static Bitmap Compose(Bitmap source, LibraryItemKind kind)
    {
        var scale = Math.Min((float)Size.Width / source.Width, (float)Size.Height / source.Height);
        var w = Math.Max(1, (int)(source.Width * scale));
        var h = Math.Max(1, (int)(source.Height * scale));

        var tile = new Bitmap(Size.Width, Size.Height);
        using var g = Graphics.FromImage(tile);
        g.Clear(Color.FromArgb(232, 232, 236));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle((Size.Width - w) / 2, (Size.Height - h) / 2, w, h);
        g.DrawImage(source, rect);

        if (kind == LibraryItemKind.Video)
        {
            using var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
            g.FillEllipse(shade, Size.Width / 2 - 20, Size.Height / 2 - 20, 40, 40);
            g.FillPolygon(Brushes.White, [new Point(Size.Width / 2 - 6, Size.Height / 2 - 11), new Point(Size.Width / 2 - 6, Size.Height / 2 + 11), new Point(Size.Width / 2 + 12, Size.Height / 2)]);
        }
        else if (kind == LibraryItemKind.Animation)
        {
            using var back = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
            using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
            g.FillRectangle(back, 4, Size.Height - 20, 32, 16);
            g.DrawString("GIF", font, Brushes.White, 7, Size.Height - 19);
        }
        return tile;
    }
}
