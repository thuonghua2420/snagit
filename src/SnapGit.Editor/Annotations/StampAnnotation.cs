using System.Drawing.Drawing2D;

namespace SnapGit.Editor.Annotations;

public enum StampKind
{
    Check,
    Cross,
    Warning,
    Info,
    Question,
    Star,
    Heart,
    Pointer,
    Bulb,
    Flag,
    Plus,
    Minus,
}

/// <summary>A built-in vector icon ("stamp"). Fill = icon colour, Stroke = glyph colour drawn on top of it.</summary>
public sealed class StampAnnotation : BoxAnnotation
{
    public StampKind Kind { get; set; } = StampKind.Check;

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Kind = ((StampAnnotation)source).Kind;
    }

    protected override bool IsFilled => true;

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        var fill = (style.Fill.A == 0 ? DefaultFill(Kind) : style.Fill).WithOpacity(style.Opacity);
        var glyph = style.Stroke.WithOpacity(style.Opacity);
        StampPainter.Draw(g, Kind, Rect, fill, glyph);
    }

    private static Color DefaultFill(StampKind kind) => kind switch
    {
        StampKind.Check => Color.FromArgb(67, 160, 71),
        StampKind.Cross => Color.FromArgb(229, 57, 53),
        StampKind.Warning => Color.FromArgb(251, 192, 45),
        StampKind.Info => Color.FromArgb(30, 136, 229),
        StampKind.Question => Color.FromArgb(142, 36, 170),
        StampKind.Star => Color.FromArgb(251, 192, 45),
        StampKind.Heart => Color.FromArgb(229, 57, 53),
        _ => Color.FromArgb(30, 136, 229),
    };
}

/// <summary>Draws the stamp glyphs on a unit square mapped onto a rectangle.</summary>
internal static class StampPainter
{
    public static void Draw(Graphics g, StampKind kind, RectangleF r, Color fill, Color glyph)
    {
        var state = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 100f, r.Height / 100f); // design space: 100 × 100
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var fillBrush = new SolidBrush(fill);
        using var glyphBrush = new SolidBrush(glyph);
        using var glyphPen = new Pen(glyph, 11f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        switch (kind)
        {
            case StampKind.Check:
                g.FillEllipse(fillBrush, 2, 2, 96, 96);
                g.DrawLines(glyphPen, [new PointF(27, 52), new PointF(44, 69), new PointF(74, 34)]);
                break;
            case StampKind.Cross:
                g.FillEllipse(fillBrush, 2, 2, 96, 96);
                g.DrawLine(glyphPen, 32, 32, 68, 68);
                g.DrawLine(glyphPen, 68, 32, 32, 68);
                break;
            case StampKind.Warning:
                g.FillPolygon(fillBrush, [new PointF(50, 5), new PointF(97, 90), new PointF(3, 90)]);
                g.DrawLine(glyphPen, 50, 36, 50, 62);
                g.FillEllipse(glyphBrush, 43.5f, 69, 13, 13);
                break;
            case StampKind.Info:
                g.FillEllipse(fillBrush, 2, 2, 96, 96);
                g.FillEllipse(glyphBrush, 43, 20, 14, 14);
                g.DrawLine(glyphPen, 50, 44, 50, 76);
                break;
            case StampKind.Question:
                g.FillEllipse(fillBrush, 2, 2, 96, 96);
                using (var font = new Font("Segoe UI", 70, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("?", font, glyphBrush, new RectangleF(0, 4, 100, 100), format);
                break;
            case StampKind.Star:
                g.FillPolygon(fillBrush, StarPoints(50, 54, 48, 20));
                break;
            case StampKind.Heart:
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(50, 90, 0, 55, 5, 12, 30, 12);
                    path.AddBezier(30, 12, 42, 12, 50, 24, 50, 32);
                    path.AddBezier(50, 32, 50, 24, 58, 12, 70, 12);
                    path.AddBezier(70, 12, 95, 12, 100, 55, 50, 90);
                    g.FillPath(fillBrush, path);
                }
                break;
            case StampKind.Pointer:
                g.FillPolygon(fillBrush, [new PointF(20, 5), new PointF(20, 80), new PointF(40, 62), new PointF(54, 95), new PointF(68, 89), new PointF(54, 57), new PointF(80, 57)]);
                break;
            case StampKind.Bulb:
                g.FillEllipse(fillBrush, 22, 5, 56, 56);
                g.FillRectangle(fillBrush, 38, 56, 24, 20);
                using (var pen = new Pen(fill, 8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, 40, 88, 60, 88);
                break;
            case StampKind.Flag:
                using (var pen = new Pen(fill, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, 22, 8, 22, 95);
                g.FillPolygon(fillBrush, [new PointF(22, 10), new PointF(90, 28), new PointF(22, 56)]);
                break;
            case StampKind.Plus:
                g.FillEllipse(fillBrush, 2, 2, 96, 96);
                g.DrawLine(glyphPen, 28, 50, 72, 50);
                g.DrawLine(glyphPen, 50, 28, 50, 72);
                break;
            case StampKind.Minus:
                g.FillEllipse(fillBrush, 2, 2, 96, 96);
                g.DrawLine(glyphPen, 28, 50, 72, 50);
                break;
        }
        g.Restore(state);
    }

    private static PointF[] StarPoints(float cx, float cy, float outer, float inner)
    {
        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : outer * 0.42f;
            var angle = -MathF.PI / 2 + i * MathF.PI / 5;
            points[i] = new PointF(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle));
        }
        return points;
    }
}
