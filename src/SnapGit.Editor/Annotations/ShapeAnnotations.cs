namespace SnapGit.Editor.Annotations;

public sealed class RectangleAnnotation : BoxAnnotation
{
    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        using var path = Geometry.RoundedRect(Rect, style.CornerRadius);

        if (style.Fill.A > 0)
        {
            using var brush = Geometry.FillBrush(Rect, style, style.Fill.WithOpacity(style.Opacity));
            g.FillPath(brush, path);
        }

        if (style.StrokeWidth > 0)
        {
            using var pen = Geometry.MakePen(style, style.Stroke.WithOpacity(style.Opacity));
            pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Miter;
            g.DrawPath(pen, path);
        }
    }
}

public sealed class EllipseAnnotation : BoxAnnotation
{
    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        if (style.Fill.A > 0)
        {
            using var brush = Geometry.FillBrush(Rect, style, style.Fill.WithOpacity(style.Opacity));
            g.FillEllipse(brush, Rect);
        }

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

/// <summary>Semi-transparent marker over text. The marker colour is <see cref="AnnotationStyle.Fill"/>.</summary>
public sealed class HighlightAnnotation : BoxAnnotation
{
    protected override bool SupportsShadow => false;
    protected override bool IsFilled => true;

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        using var brush = new SolidBrush(style.Fill.WithOpacity(style.Opacity));
        g.FillRectangle(brush, Rect);
    }
}
