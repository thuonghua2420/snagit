using System.Drawing.Drawing2D;

namespace SnapGit.Editor.Annotations;

internal static class Geometry
{
    public static RectangleF FromPoints(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static float Distance(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        var lengthSq = dx * dx + dy * dy;
        if (lengthSq < 1e-6f) return Distance(p, a);

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0f, 1f);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    public static Color WithOpacity(this Color color, float opacity) =>
        Color.FromArgb((int)Math.Clamp(color.A * opacity, 0, 255), color);

    /// <summary>Constrains <paramref name="end"/> so the segment start→end snaps to 45° steps.</summary>
    public static PointF SnapAngle(PointF start, PointF end)
    {
        float dx = end.X - start.X, dy = end.Y - start.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f) return end;

        var step = MathF.PI / 4;
        var angle = MathF.Round(MathF.Atan2(dy, dx) / step) * step;
        return new PointF(start.X + length * MathF.Cos(angle), start.Y + length * MathF.Sin(angle));
    }

    /// <summary>Pen honouring colour/width/dash from the style. Caller disposes.</summary>
    public static Pen MakePen(AnnotationStyle style, Color color, float? width = null)
    {
        var pen = new Pen(color, Math.Max(width ?? style.StrokeWidth, 0.5f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        switch (style.Dash)
        {
            case LineDash.Dash: pen.DashStyle = DashStyle.Dash; break;
            case LineDash.Dot: pen.DashStyle = DashStyle.Dot; break;
        }
        return pen;
    }

    /// <summary>
    /// Brush for an annotation's interior: solid <paramref name="fill"/>, or a gradient towards
    /// <see cref="AnnotationStyle.GradientTo"/> when one is set. Caller disposes.
    /// </summary>
    public static Brush FillBrush(RectangleF bounds, AnnotationStyle style, Color fill)
    {
        if (style.GradientTo.A > 0 && bounds.Width > 0.5f && bounds.Height > 0.5f)
            return new LinearGradientBrush(bounds, fill, style.GradientTo.WithOpacity(style.Opacity), style.GradientAngle, isAngleScaleable: true);
        return new SolidBrush(fill);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius < 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Point on a quadratic Bézier.</summary>
    public static PointF Quadratic(PointF a, PointF control, PointF b, float t)
    {
        var u = 1 - t;
        return new PointF(u * u * a.X + 2 * u * t * control.X + t * t * b.X, u * u * a.Y + 2 * u * t * control.Y + t * t * b.Y);
    }

    public static PointF Midpoint(PointF a, PointF b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    public static PointF Normalize(float dx, float dy)
    {
        var length = MathF.Sqrt(dx * dx + dy * dy);
        return length < 1e-6f ? new PointF(1, 0) : new PointF(dx / length, dy / length);
    }
}
