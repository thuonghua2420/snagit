using System.Drawing.Drawing2D;

namespace SnapGit.Editor.Annotations;

public enum BlurMode
{
    Pixelate,
    Blur,
}

/// <summary>Obscures a region of the underlying screenshot (hide passwords, faces, etc.).</summary>
public sealed class BlurAnnotation : BoxAnnotation
{
    public BlurMode Mode { get; set; } = BlurMode.Pixelate;

    /// <summary>Pixelate: block size in px. Blur: downsample factor (bigger = blurrier).</summary>
    public int Strength { get; set; } = 10;

    protected override bool SupportsShadow => false;
    protected override bool IsFilled => true;

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        var other = (BlurAnnotation)source;
        Mode = other.Mode;
        Strength = other.Strength;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        var target = Rectangle.Intersect(Rectangle.Round(Rect), new Rectangle(Point.Empty, context.BaseImage.Size));
        if (target.Width < 2 || target.Height < 2) return;

        var factor = Math.Max(2, Strength);
        var smallW = Math.Max(1, target.Width / factor);
        var smallH = Math.Max(1, target.Height / factor);

        using var small = new Bitmap(smallW, smallH);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.PixelOffsetMode = PixelOffsetMode.Half;
            sg.DrawImage(context.BaseImage, new Rectangle(0, 0, smallW, smallH), target, GraphicsUnit.Pixel);
        }

        var state = g.Save();
        g.SetClip(target, CombineMode.Intersect);
        g.InterpolationMode = Mode == BlurMode.Pixelate ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, target, 0, 0, smallW, smallH, GraphicsUnit.Pixel);
        g.Restore(state);
    }
}
