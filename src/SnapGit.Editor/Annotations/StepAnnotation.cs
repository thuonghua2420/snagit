namespace SnapGit.Editor.Annotations;

/// <summary>Numbered circle ("Step" tool). Circle colour = Fill, number colour = Stroke.</summary>
public sealed class StepAnnotation : Annotation
{
    public PointF Center { get; set; }
    public int Number { get; set; } = 1;
    public float Diameter { get; set; } = 36f;

    public override RectangleF Bounds =>
        new(Center.X - Diameter / 2, Center.Y - Diameter / 2, Diameter, Diameter);

    public override void Translate(float dx, float dy) => Center = new PointF(Center.X + dx, Center.Y + dy);

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        var other = (StepAnnotation)source;
        Center = other.Center;
        Number = other.Number;
        Diameter = other.Diameter;
    }

    public override bool HitTest(PointF point, float tolerance) =>
        Geometry.Distance(point, Center) <= Diameter / 2 + tolerance;

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        using (var fill = new SolidBrush(style.Fill.WithOpacity(style.Opacity)))
            g.FillEllipse(fill, Bounds);

        var text = Number.ToString();
        using var font = new Font(style.FontFamily, Diameter * (text.Length > 1 ? 0.42f : 0.55f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(style.Stroke.WithOpacity(style.Opacity));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // Pixel-grid text smoothing looks muddy on coloured discs; AntiAlias is cleaner here.
        var previous = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.DrawString(text, font, brush, Bounds, format);
        g.TextRenderingHint = previous;
    }
}
