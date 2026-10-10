using System.Drawing.Drawing2D;

namespace SnapGit.Editor.Annotations;

/// <summary>
/// Straight arrow, or a curved one once the middle handle is dragged (<see cref="Bend"/> = Bézier control point).
/// Head shape / double-headed come from the style.
/// </summary>
public sealed class ArrowAnnotation : LineAnnotation
{
    private const int CurveSamples = 24;

    /// <summary>Control point of the curve; null = straight.</summary>
    public PointF? Bend { get; set; }

    private PointF Control => Bend ?? Geometry.Midpoint(Start, End);

    public override RectangleF Bounds
    {
        get
        {
            var box = Geometry.FromPoints(Start, End);
            return Bend is { } b ? RectangleF.Union(box, new RectangleF(b.X, b.Y, 0, 0)) : box;
        }
    }

    public override IReadOnlyList<PointF> HandlePoints => [Start, End, Control];

    public override void SetDrag(PointF start, PointF end, bool constrain)
    {
        base.SetDrag(start, end, constrain);
        Bend = null;
    }

    public override void MoveHandle(int index, PointF to)
    {
        if (index == 2)
        {
            // Dropping the handle back onto the straight line makes the arrow straight again.
            Bend = Geometry.DistanceToSegment(to, Start, End) < 3f ? null : to;
            return;
        }
        base.MoveHandle(index, to);
    }

    public override void Translate(float dx, float dy)
    {
        base.Translate(dx, dy);
        if (Bend is { } b) Bend = new PointF(b.X + dx, b.Y + dy);
    }

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Bend = ((ArrowAnnotation)source).Bend;
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        var reach = tolerance + Math.Max(Style.StrokeWidth, 2f) / 2;
        if (Bend is null) return Geometry.DistanceToSegment(point, Start, End) <= reach;

        var previous = Start;
        for (var i = 1; i <= CurveSamples; i++)
        {
            var next = Geometry.Quadratic(Start, Control, End, i / (float)CurveSamples);
            if (Geometry.DistanceToSegment(point, previous, next) <= reach) return true;
            previous = next;
        }
        return false;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        var length = Geometry.Distance(Start, End);
        if (length < 1f) return;

        var width = Math.Max(style.StrokeWidth, 1f);
        var color = style.Stroke.WithOpacity(style.Opacity);
        var control = Control;

        var headLength = Math.Min(length * 0.6f, width * 3.5f + 10f);
        var hasEndHead = style.Head != ArrowHead.None;
        var hasStartHead = hasEndHead && style.DoubleHeaded;

        var endDir = Geometry.Normalize(End.X - control.X, End.Y - control.Y);
        var startDir = Geometry.Normalize(Start.X - control.X, Start.Y - control.Y);

        // Stop the shaft where the (filled) head begins so the stroke doesn't poke through the tip.
        var shaftInset = style.Head is ArrowHead.Triangle or ArrowHead.Diamond ? headLength * 0.85f : style.Head == ArrowHead.Circle ? headLength * 0.4f : 0f;
        var shaftEnd = hasEndHead ? new PointF(End.X - endDir.X * shaftInset, End.Y - endDir.Y * shaftInset) : End;
        var shaftStart = hasStartHead ? new PointF(Start.X - startDir.X * shaftInset, Start.Y - startDir.Y * shaftInset) : Start;

        using (var pen = Geometry.MakePen(style, color, width))
        {
            if (Bend is null)
            {
                g.DrawLine(pen, shaftStart, shaftEnd);
            }
            else
            {
                var c1 = new PointF(shaftStart.X + 2f / 3f * (control.X - shaftStart.X), shaftStart.Y + 2f / 3f * (control.Y - shaftStart.Y));
                var c2 = new PointF(shaftEnd.X + 2f / 3f * (control.X - shaftEnd.X), shaftEnd.Y + 2f / 3f * (control.Y - shaftEnd.Y));
                g.DrawBezier(pen, shaftStart, c1, c2, shaftEnd);
            }
        }

        if (hasEndHead) DrawHead(g, style, color, width, End, endDir, headLength);
        if (hasStartHead) DrawHead(g, style, color, width, Start, startDir, headLength);
    }

    private static void DrawHead(Graphics g, AnnotationStyle style, Color color, float width, PointF tip, PointF dir, float length)
    {
        var half = length * 0.45f;
        var baseCenter = new PointF(tip.X - dir.X * length, tip.Y - dir.Y * length);
        var left = new PointF(baseCenter.X - dir.Y * half, baseCenter.Y + dir.X * half);
        var right = new PointF(baseCenter.X + dir.Y * half, baseCenter.Y - dir.X * half);

        using var brush = new SolidBrush(color);
        switch (style.Head)
        {
            case ArrowHead.Triangle:
                g.FillPolygon(brush, [tip, left, right]);
                break;
            case ArrowHead.Diamond:
                var back = new PointF(tip.X - dir.X * length, tip.Y - dir.Y * length);
                var mid = new PointF(tip.X - dir.X * length / 2, tip.Y - dir.Y * length / 2);
                var l = new PointF(mid.X - dir.Y * half * 0.8f, mid.Y + dir.X * half * 0.8f);
                var r = new PointF(mid.X + dir.Y * half * 0.8f, mid.Y - dir.X * half * 0.8f);
                g.FillPolygon(brush, [tip, l, back, r]);
                break;
            case ArrowHead.Circle:
                var radius = length * 0.35f;
                g.FillEllipse(brush, tip.X - dir.X * radius - radius, tip.Y - dir.Y * radius - radius, radius * 2, radius * 2);
                break;
            case ArrowHead.Open:
                using (var pen = Geometry.MakePen(style with { Dash = LineDash.Solid }, color, width))
                {
                    g.DrawLine(pen, left, tip);
                    g.DrawLine(pen, right, tip);
                }
                break;
        }
    }
}
