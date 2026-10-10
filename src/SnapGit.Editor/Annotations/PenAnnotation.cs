namespace SnapGit.Editor.Annotations;

/// <summary>Freehand stroke.</summary>
public sealed class PenAnnotation : Annotation
{
    public List<PointF> Points { get; set; } = new();

    public override RectangleF Bounds
    {
        get
        {
            if (Points.Count == 0) return RectangleF.Empty;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in Points)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }
    }

    protected override void OnCloned() => Points = new List<PointF>(Points);

    public override void Translate(float dx, float dy)
    {
        for (var i = 0; i < Points.Count; i++)
            Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
    }

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Points = new List<PointF>(((PenAnnotation)source).Points);
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        var reach = tolerance + Math.Max(Style.StrokeWidth, 2f) / 2;
        if (Points.Count == 1) return Geometry.Distance(point, Points[0]) <= reach;

        for (var i = 1; i < Points.Count; i++)
            if (Geometry.DistanceToSegment(point, Points[i - 1], Points[i]) <= reach)
                return true;
        return false;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        if (Points.Count == 0) return;
        var color = style.Stroke.WithOpacity(style.Opacity);
        var width = Math.Max(style.StrokeWidth, 1f);

        if (Points.Count == 1)
        {
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, Points[0].X - width / 2, Points[0].Y - width / 2, width, width);
            return;
        }

        using var pen = Geometry.MakePen(style, color, width);
        g.DrawLines(pen, Points.ToArray());
    }
}
