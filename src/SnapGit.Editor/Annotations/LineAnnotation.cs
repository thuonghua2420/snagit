namespace SnapGit.Editor.Annotations;

public class LineAnnotation : Annotation, IDragCreatable
{
    public PointF Start { get; set; }
    public PointF End { get; set; }

    public override RectangleF Bounds => Geometry.FromPoints(Start, End);

    public bool IsDegenerate => Geometry.Distance(Start, End) < 4f;

    public override IReadOnlyList<PointF> HandlePoints => [Start, End];

    public virtual void SetDrag(PointF start, PointF end, bool constrain)
    {
        Start = start;
        End = constrain ? Geometry.SnapAngle(start, end) : end;
    }

    public override void MoveHandle(int index, PointF to)
    {
        if (index == 0) Start = to;
        else if (index == 1) End = to;
    }

    public override void Translate(float dx, float dy)
    {
        Start = new PointF(Start.X + dx, Start.Y + dy);
        End = new PointF(End.X + dx, End.Y + dy);
    }

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        var other = (LineAnnotation)source;
        Start = other.Start;
        End = other.End;
    }

    public override bool HitTest(PointF point, float tolerance) =>
        Geometry.DistanceToSegment(point, Start, End) <= tolerance + Math.Max(Style.StrokeWidth, 2f) / 2;

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        using var pen = Geometry.MakePen(style, style.Stroke.WithOpacity(style.Opacity), Math.Max(style.StrokeWidth, 1f));
        g.DrawLine(pen, Start, End);
    }
}
