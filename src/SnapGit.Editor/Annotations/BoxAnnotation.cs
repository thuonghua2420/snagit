namespace SnapGit.Editor.Annotations;

/// <summary>Base for rectangular annotations with 8 resize handles.</summary>
public abstract class BoxAnnotation : Annotation, IDragCreatable
{
    private const float MinSize = 4f;

    public RectangleF Rect { get; set; }

    public override RectangleF Bounds => Rect;

    public bool IsDegenerate => Rect.Width < MinSize && Rect.Height < MinSize;

    public override IReadOnlyList<PointF> HandlePoints
    {
        get
        {
            var r = Rect;
            var cx = r.Left + r.Width / 2;
            var cy = r.Top + r.Height / 2;
            return
            [
                new(r.Left, r.Top), new(cx, r.Top), new(r.Right, r.Top),
                new(r.Right, cy),
                new(r.Right, r.Bottom), new(cx, r.Bottom), new(r.Left, r.Bottom),
                new(r.Left, cy),
            ];
        }
    }

    public virtual void SetDrag(PointF start, PointF end, bool constrain)
    {
        if (constrain)
        {
            var size = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
            end = new PointF(start.X + Math.Sign(end.X - start.X) * size, start.Y + Math.Sign(end.Y - start.Y) * size);
        }
        Rect = Geometry.FromPoints(start, end);
    }

    public override void MoveHandle(int index, PointF to)
    {
        float l = Rect.Left, t = Rect.Top, r = Rect.Right, b = Rect.Bottom;
        switch (index)
        {
            case 0: l = to.X; t = to.Y; break;
            case 1: t = to.Y; break;
            case 2: r = to.X; t = to.Y; break;
            case 3: r = to.X; break;
            case 4: r = to.X; b = to.Y; break;
            case 5: b = to.Y; break;
            case 6: l = to.X; b = to.Y; break;
            case 7: l = to.X; break;
        }

        // Clamp instead of normalising so the handle being dragged keeps its meaning.
        if (index is 0 or 6 or 7) l = Math.Min(l, r - 1);
        if (index is 2 or 3 or 4) r = Math.Max(r, l + 1);
        if (index is 0 or 1 or 2) t = Math.Min(t, b - 1);
        if (index is 4 or 5 or 6) b = Math.Max(b, t + 1);

        Rect = RectangleF.FromLTRB(l, t, r, b);
    }

    public override void Translate(float dx, float dy) => Rect = new RectangleF(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Rect = ((BoxAnnotation)source).Rect;
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        var reach = tolerance + Style.StrokeWidth / 2;
        var outer = RectangleF.Inflate(Rect, reach, reach);
        if (!ContainsInShape(outer, point)) return false;
        if (IsFilled) return true;

        var inner = RectangleF.Inflate(Rect, -reach, -reach);
        return inner.Width <= 0 || inner.Height <= 0 || !ContainsInShape(inner, point);
    }

    /// <summary>True when clicking anywhere inside selects it (filled shapes, text, effects).</summary>
    protected virtual bool IsFilled => Style.Fill.A > 0;

    protected virtual bool ContainsInShape(RectangleF area, PointF point) => area.Contains(point);
}
