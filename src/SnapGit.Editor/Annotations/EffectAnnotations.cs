using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json.Serialization;

namespace SnapGit.Editor.Annotations;

/// <summary>
/// Dims everything except <see cref="BoxAnnotation.Rect"/> to draw attention to it.
/// <see cref="AnnotationStyle.Opacity"/> = darkness, <see cref="AnnotationStyle.CornerRadius"/> rounds the hole,
/// <see cref="Ellipse"/> makes it oval.
/// </summary>
public sealed class SpotlightAnnotation : BoxAnnotation
{
    public bool Ellipse { get; set; }

    protected override bool SupportsShadow => false;
    protected override bool IsFilled => true;

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Ellipse = ((SpotlightAnnotation)source).Ellipse;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        var image = new RectangleF(0, 0, context.BaseImage.Width, context.BaseImage.Height);
        using var path = new GraphicsPath(FillMode.Alternate);
        path.AddRectangle(image);
        if (Ellipse) path.AddEllipse(Rect);
        else path.AddPath(Geometry.RoundedRect(Rect, style.CornerRadius), false);

        using var shade = new SolidBrush(Color.FromArgb((int)(255 * Math.Clamp(style.Opacity, 0f, 1f) * 0.75f), 0, 0, 0));
        g.FillPath(shade, path);
    }
}

/// <summary>Shows the area under it enlarged (<see cref="Zoom"/>×) inside an ellipse/rounded lens with a border.</summary>
public sealed class MagnifyAnnotation : BoxAnnotation
{
    public float Zoom { get; set; } = 2f;

    protected override bool IsFilled => true;

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Zoom = ((MagnifyAnnotation)source).Zoom;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        var zoom = Math.Max(1.1f, Zoom);
        var center = new PointF(Rect.Left + Rect.Width / 2, Rect.Top + Rect.Height / 2);
        var source = new RectangleF(center.X - Rect.Width / zoom / 2, center.Y - Rect.Height / zoom / 2, Rect.Width / zoom, Rect.Height / zoom);

        var state = g.Save();
        using (var lens = new GraphicsPath())
        {
            lens.AddEllipse(Rect);
            g.SetClip(lens, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(context.BaseImage, Rectangle.Round(Rect), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        g.Restore(state);

        if (style.StrokeWidth > 0)
        {
            using var pen = Geometry.MakePen(style, style.Stroke.WithOpacity(style.Opacity));
            g.DrawEllipse(pen, Rect);
        }
    }

    protected override bool ContainsInShape(RectangleF area, PointF point)
    {
        if (area.Width <= 0 || area.Height <= 0) return false;
        var nx = (point.X - (area.Left + area.Width / 2)) / (area.Width / 2);
        var ny = (point.Y - (area.Top + area.Height / 2)) / (area.Height / 2);
        return nx * nx + ny * ny <= 1;
    }
}

/// <summary>
/// A floating bitmap layer (created by Smart Move, paste, drag-and-drop). The bitmap is treated as immutable,
/// so snapshots/clones may share it.
/// </summary>
public sealed class ImageLayerAnnotation : BoxAnnotation
{
    [JsonIgnore]
    public Bitmap? Image { get; set; }

    /// <summary>Serialised form of <see cref="Image"/> (PNG, base64) used by the project file.</summary>
    public string? PngBase64
    {
        get
        {
            if (Image is null) return null;
            using var stream = new MemoryStream();
            Image.Save(stream, ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
        set
        {
            if (string.IsNullOrEmpty(value)) { Image = null; return; }
            using var stream = new MemoryStream(Convert.FromBase64String(value));
            using var loaded = new Bitmap(stream);
            Image = new Bitmap(loaded);
        }
    }

    protected override bool SupportsShadow => false;
    protected override bool IsFilled => true;

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Image = ((ImageLayerAnnotation)source).Image;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        if (Image is null) return;

        var state = g.Save();
        g.InterpolationMode = Math.Abs(Rect.Width - Image.Width) < 0.5f && Math.Abs(Rect.Height - Image.Height) < 0.5f
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (style.Opacity < 0.999f)
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(style.Opacity, 0f, 1f) });
            g.DrawImage(Image, Rectangle.Round(Rect), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, attributes);
        }
        else
        {
            g.DrawImage(Image, Rect.X, Rect.Y, Rect.Width, Rect.Height);
        }
        g.Restore(state);
    }
}
