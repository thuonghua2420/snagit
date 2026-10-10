using System.Drawing.Drawing2D;

namespace SnapGit.Editor.Annotations;

/// <summary>Text box. Text colour = Stroke, background = Fill (optionally a gradient), optional text outline.</summary>
public class TextAnnotation : BoxAnnotation
{
    protected const float Padding = 8f;

    public string Text { get; set; } = string.Empty;

    protected override bool IsFilled => true; // clicking anywhere in the box selects it

    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        Text = ((TextAnnotation)source).Text;
    }

    /// <summary>Grows the box height so all text is visible at the current width.</summary>
    public void FitHeight(Graphics g)
    {
        using var font = CreateFont(Style);
        var size = g.MeasureString(Text.Length == 0 ? " " : Text, font,
            new SizeF(Math.Max(1f, Rect.Width - Padding * 2), 100000f), StringFormat.GenericTypographic);
        var height = size.Height + Padding * 2;
        Rect = new RectangleF(Rect.X, Rect.Y, Rect.Width, Math.Max(height, font.Height + Padding * 2));
    }

    public static Font CreateFont(AnnotationStyle style)
    {
        var fontStyle = (style.Bold ? FontStyle.Bold : FontStyle.Regular) | (style.Italic ? FontStyle.Italic : FontStyle.Regular);
        return new Font(style.FontFamily, Math.Max(style.FontSize, 6f), fontStyle, GraphicsUnit.Pixel);
    }

    protected static StringFormat CreateFormat(AnnotationStyle style)
    {
        var format = new StringFormat(StringFormat.GenericTypographic);
        // LineLimit makes GDI+ draw NOTHING when the box is shorter than one line (e.g. after dragging a handle).
        format.FormatFlags &= ~StringFormatFlags.LineLimit;
        format.Alignment = style.Align switch
        {
            TextAlign.Center => StringAlignment.Center,
            TextAlign.Right => StringAlignment.Far,
            _ => StringAlignment.Near,
        };
        return format;
    }

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        DrawBackground(g, style);
        DrawText(g, style);
    }

    protected virtual void DrawBackground(Graphics g, AnnotationStyle style)
    {
        if (style.Fill.A == 0) return;
        using var path = Geometry.RoundedRect(Rect, style.CornerRadius);
        using var back = Geometry.FillBrush(Rect, style, style.Fill.WithOpacity(style.Opacity));
        g.FillPath(back, path);
    }

    protected void DrawText(Graphics g, AnnotationStyle style)
    {
        if (Text.Length == 0) return;

        using var font = CreateFont(style);
        using var brush = new SolidBrush(style.Stroke.WithOpacity(style.Opacity));
        using var format = CreateFormat(style);
        var area = RectangleF.Inflate(Rect, -Padding, -Padding);

        if (style.OutlineWidth > 0 && style.OutlineColor.A > 0)
        {
            // Outlined text is drawn as a vector path: stroke first (twice as wide, half of it hidden), fill on top.
            using var path = new GraphicsPath();
            path.AddString(Text, font.FontFamily, (int)font.Style, font.Size, area, format);
            using var outline = new Pen(style.OutlineColor.WithOpacity(style.Opacity), style.OutlineWidth * 2) { LineJoin = LineJoin.Round };
            g.DrawPath(outline, path);
            g.FillPath(brush, path);
            return;
        }

        var previous = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.DrawString(Text, font, brush, area, format);
        g.TextRenderingHint = previous;
    }
}
