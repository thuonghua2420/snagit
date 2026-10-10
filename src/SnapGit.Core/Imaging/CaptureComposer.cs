using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnapGit.Core.Imaging;

/// <summary>Builds the final image of the free-hand and multi-area capture modes from a screen snapshot. Pure and unit-tested.</summary>
public static class CaptureComposer
{
    /// <summary>
    /// Cuts the polygon <paramref name="path"/> (snapshot coordinates) out of <paramref name="snapshot"/>.
    /// The result is the polygon's bounding box; everything outside the polygon is transparent.
    /// </summary>
    public static Bitmap FreeHand(Bitmap snapshot, IReadOnlyList<Point> path, out Rectangle bounds)
    {
        if (path.Count < 3) throw new ArgumentException("A free-hand selection needs at least 3 points.", nameof(path));

        var points = path.Select(p => new PointF(p.X, p.Y)).ToArray();
        using var polygon = new GraphicsPath();
        polygon.AddPolygon(points);

        bounds = Rectangle.Intersect(Rectangle.Ceiling(polygon.GetBounds()), new Rectangle(Point.Empty, snapshot.Size));
        if (bounds.Width < 1 || bounds.Height < 1) throw new ArgumentException("The selection is outside the screen.", nameof(path));

        var result = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(-bounds.X, -bounds.Y);
        g.SetClip(polygon);
        g.DrawImageUnscaled(snapshot, 0, 0);
        return result;
    }

    /// <summary>
    /// Several rectangles combined into one image the size of their bounding box, keeping their relative
    /// positions; the gaps between them are transparent.
    /// </summary>
    public static Bitmap MultiArea(Bitmap snapshot, IReadOnlyList<Rectangle> regions, out Rectangle bounds)
    {
        var screen = new Rectangle(Point.Empty, snapshot.Size);
        var valid = regions.Select(r => Rectangle.Intersect(r, screen)).Where(r => r.Width > 0 && r.Height > 0).ToList();
        if (valid.Count == 0) throw new ArgumentException("No usable regions.", nameof(regions));

        bounds = valid.Aggregate(Rectangle.Union);
        var result = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        foreach (var region in valid)
            g.DrawImage(snapshot, new Rectangle(region.X - bounds.X, region.Y - bounds.Y, region.Width, region.Height), region, GraphicsUnit.Pixel);
        return result;
    }
}
