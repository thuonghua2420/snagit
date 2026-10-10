using System.Drawing.Drawing2D;

namespace SnapGit.Editor.Annotations;

/// <summary>
/// Speech-bubble callout: a text box with a pointer ("tail") to <see cref="Tail"/> (image coordinates).
/// Fill = bubble colour, Stroke = text colour, border drawn when StrokeWidth &gt; 0 using <see cref="BorderColor"/>.
/// </summary>
public sealed class CalloutAnnotation : TextAnnotation
{
    public PointF Tail { get; set; }

    /// <summary>Bubble outline colour.</summary>
    public Color BorderColor { get; set; } = Color.FromArgb(120, 120, 120);

    public override RectangleF Bounds => RectangleF.Union(Rect, new RectangleF(Tail.X, Tail.Y, 0, 0));

    public override IReadOnlyList<PointF> HandlePoints => [.. base.HandlePoints, Tail];

    public override void SetDrag(PointF start, PointF end, bool constrain)
    {
        base.SetDrag(start, end, constrain);
        Tail = new PointF(Rect.Left + Rect.Width * 0.25f, Rect.Bottom + Math.Max(24f, Rect.Height * 0.5f));
    }

    public override void MoveHandle(int index, PointF to)
    {
        if (index == 8) Tail = to;
        else base.MoveHandle(index, to);
    }

    public override void Translate(float dx, float dy)
    {
        base.Translate(dx, dy);
        Tail = new PointF(Tail.X + dx, Tail.Y + dy);
    }

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        var other = (CalloutAnnotation)source;
        Tail = other.Tail;
        BorderColor = other.BorderColor;
    }

    public override bool HitTest(PointF point, float tolerance)
    {
        if (RectangleF.Inflate(Rect, tolerance, tolerance).Contains(point)) return true;
        return Geometry.DistanceToSegment(point, Geometry.Midpoint(new PointF(Rect.Left, Rect.Top), new PointF(Rect.Right, Rect.Bottom)), Tail) <= tolerance + 4;
    }

    protected override void DrawBackground(Graphics g, AnnotationStyle style)
    {
        using var path = BuildBubblePath(style.CornerRadius);

        var fill = style.Fill.A == 0 ? Color.White : style.Fill;
        using (var brush = Geometry.FillBrush(path.GetBounds(), style, fill.WithOpacity(style.Opacity)))
            g.FillPath(brush, path);

        if (style.StrokeWidth > 0)
        {
            using var pen = Geometry.MakePen(style, BorderColor.WithOpacity(style.Opacity), Math.Min(style.StrokeWidth, 6f));
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Rounded rectangle with a triangular pointer inserted into the side nearest to <see cref="Tail"/>.</summary>
    private GraphicsPath BuildBubblePath(float cornerRadius)
    {
        var r = Rect;
        var radius = Math.Clamp(cornerRadius <= 0 ? 8f : cornerRadius, 0, Math.Min(r.Width, r.Height) / 2);
        var points = new List<PointF>();

        // Which side does the tail sit on? (compare against the rectangle's centre lines)
        var center = new PointF(r.Left + r.Width / 2, r.Top + r.Height / 2);
        var dx = (Tail.X - center.X) / (r.Width / 2);
        var dy = (Tail.Y - center.Y) / (r.Height / 2);
        var tailInside = Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1;
        var side = tailInside ? -1 : Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? 1 : 3) : (dy > 0 ? 2 : 0); // 0 top,1 right,2 bottom,3 left

        void Arc(float cx, float cy, float startAngle)
        {
            for (var i = 0; i <= 6; i++)
            {
                var a = (startAngle + 90f * i / 6f) * MathF.PI / 180f;
                points.Add(new PointF(cx + radius * MathF.Cos(a), cy + radius * MathF.Sin(a)));
            }
        }

        // Tail base: 2 points on the side, centred on the tail's projection (kept away from the corners).
        PointF[] TailBase(bool horizontalSide, float sideStart, float sideEnd, float fixedCoord, float projected)
        {
            var half = Math.Min(14f, (sideEnd - sideStart) / 4);
            var c = Math.Clamp(projected, sideStart + radius + half, sideEnd - radius - half);
            return horizontalSide
                ? [new PointF(c - half, fixedCoord), new PointF(c + half, fixedCoord)]
                : [new PointF(fixedCoord, c - half), new PointF(fixedCoord, c + half)];
        }

        Arc(r.Left + radius, r.Top + radius, 180);                       // top-left corner
        if (side == 0) { var b = TailBase(true, r.Left, r.Right, r.Top, Tail.X); points.AddRange([b[0], Tail, b[1]]); }
        Arc(r.Right - radius, r.Top + radius, 270);                      // top-right
        if (side == 1) { var b = TailBase(false, r.Top, r.Bottom, r.Right, Tail.Y); points.AddRange([b[0], Tail, b[1]]); }
        Arc(r.Right - radius, r.Bottom - radius, 0);                     // bottom-right
        if (side == 2) { var b = TailBase(true, r.Left, r.Right, r.Bottom, Tail.X); points.AddRange([b[1], Tail, b[0]]); }
        Arc(r.Left + radius, r.Bottom - radius, 90);                     // bottom-left
        if (side == 3) { var b = TailBase(false, r.Top, r.Bottom, r.Left, Tail.Y); points.AddRange([b[1], Tail, b[0]]); }

        var path = new GraphicsPath();
        path.AddPolygon(points.ToArray());
        return path;
    }
}
