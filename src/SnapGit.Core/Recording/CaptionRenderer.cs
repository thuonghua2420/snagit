using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SnapGit.Core.Recording;

/// <summary>Draws a <see cref="VideoCaption"/> onto a frame of a given size: the editor preview and the exported overlay use the same code.</summary>
public static class CaptionRenderer
{
    public static void Draw(Graphics g, Size frame, VideoCaption caption)
    {
        if (string.IsNullOrWhiteSpace(caption.Text)) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var fontPixels = Math.Max(8f, frame.Height * caption.SizePercent / 100f);
        var margin = frame.Height * 0.04f;
        var box = new RectangleF(margin, margin, frame.Width - margin * 2, frame.Height - margin * 2);

        using var family = new FontFamily("Segoe UI");
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = caption.Position switch
            {
                CaptionPosition.Top => StringAlignment.Near,
                CaptionPosition.Center => StringAlignment.Center,
                _ => StringAlignment.Far,
            },
        };
        format.FormatFlags &= ~StringFormatFlags.LineLimit; // would make text vanish when it is taller than its box

        using var path = new GraphicsPath();
        path.AddString(caption.Text, family, (int)FontStyle.Bold, fontPixels, box, format);

        using var outline = new Pen(Color.FromArgb(220, 0, 0, 0), Math.Max(2f, fontPixels / 9f)) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(Color.FromArgb(caption.ColorArgb));
        g.DrawPath(outline, path);
        g.FillPath(fill, path);
    }

    /// <summary>A transparent frame-sized picture with the caption on it (what gets overlaid on the video).</summary>
    public static Bitmap Render(Size frame, VideoCaption caption)
    {
        var bitmap = new Bitmap(frame.Width, frame.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        Draw(g, frame, caption);
        return bitmap;
    }
}
