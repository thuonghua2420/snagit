using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace SnapGit.Core.Imaging;

public enum CombineLayout
{
    Vertical,
    Horizontal,
    Grid,
}

/// <summary>Combines several images into one, and builds "step by step" documents. Pure functions, unit-tested.</summary>
public static class ImageComposer
{
    public static Bitmap Combine(IReadOnlyList<Bitmap> images, CombineLayout layout, int spacing, Color background, int gridColumns = 2)
    {
        if (images.Count == 0) throw new ArgumentException("Nothing to combine.", nameof(images));
        spacing = Math.Max(0, spacing);

        var cells = layout switch
        {
            CombineLayout.Vertical => images.Select((_, i) => (Col: 0, Row: i)).ToList(),
            CombineLayout.Horizontal => images.Select((_, i) => (Col: i, Row: 0)).ToList(),
            _ => images.Select((_, i) => (Col: i % Math.Max(1, gridColumns), Row: i / Math.Max(1, gridColumns))).ToList(),
        };

        var columns = cells.Max(c => c.Col) + 1;
        var rows = cells.Max(c => c.Row) + 1;
        var columnWidths = new int[columns];
        var rowHeights = new int[rows];
        for (var i = 0; i < images.Count; i++)
        {
            columnWidths[cells[i].Col] = Math.Max(columnWidths[cells[i].Col], images[i].Width);
            rowHeights[cells[i].Row] = Math.Max(rowHeights[cells[i].Row], images[i].Height);
        }

        var width = columnWidths.Sum() + spacing * (columns - 1);
        var height = rowHeights.Sum() + spacing * (rows - 1);
        var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(background);

        for (var i = 0; i < images.Count; i++)
        {
            var x = columnWidths.Take(cells[i].Col).Sum() + spacing * cells[i].Col;
            var y = rowHeights.Take(cells[i].Row).Sum() + spacing * cells[i].Row;
            g.DrawImageUnscaled(images[i], x, y);
        }
        return result;
    }

    /// <summary>
    /// A vertical "how-to" document: optional title, then each image preceded by a numbered caption.
    /// Images wider than <paramref name="pageWidth"/> are scaled down to fit.
    /// </summary>
    public static Bitmap BuildStepDocument(IReadOnlyList<(Bitmap Image, string Caption)> steps, string? title, int pageWidth = 1000)
    {
        if (steps.Count == 0) throw new ArgumentException("Nothing to document.", nameof(steps));

        const int margin = 40;
        const int gap = 28;
        var contentWidth = pageWidth - margin * 2;

        using var titleFont = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
        using var captionFont = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measureBitmap = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(measureBitmap);

        var layout = new List<(Bitmap Image, bool Owned, string Caption, SizeF CaptionSize)>();
        foreach (var (image, caption) in steps)
        {
            var scaled = image.Width > contentWidth ? ImageOps.Scale(image, contentWidth, (int)Math.Round(image.Height * (double)contentWidth / image.Width)) : image;
            var size = measure.MeasureString(caption, captionFont, contentWidth - 56);
            layout.Add((scaled, !ReferenceEquals(scaled, image), caption, size));
        }

        SizeF titleSize = string.IsNullOrWhiteSpace(title) ? SizeF.Empty : measure.MeasureString(title, titleFont, contentWidth);
        var height = margin + (titleSize.Height > 0 ? titleSize.Height + gap : 0)
                     + layout.Sum(l => Math.Max(l.CaptionSize.Height, 36) + 12 + l.Image.Height + gap) + margin - gap;

        var result = new Bitmap(pageWidth, (int)Math.Ceiling(height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            float y = margin;
            if (titleSize.Height > 0)
            {
                g.DrawString(title, titleFont, Brushes.Black, new RectangleF(margin, y, contentWidth, titleSize.Height));
                y += titleSize.Height + gap;
            }

            using var badge = new SolidBrush(Color.FromArgb(0, 120, 215));
            using var badgeFont = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            for (var i = 0; i < layout.Count; i++)
            {
                var (image, _, caption, size) = layout[i];
                var rowHeight = Math.Max(size.Height, 36);

                g.FillEllipse(badge, margin, y, 36, 36);
                g.DrawString((i + 1).ToString(CultureInfo.InvariantCulture), badgeFont, Brushes.White, new RectangleF(margin, y, 36, 36), center);
                g.DrawString(caption, captionFont, Brushes.Black, new RectangleF(margin + 52, y + (rowHeight - size.Height) / 2, contentWidth - 56, size.Height));
                y += rowHeight + 12;

                g.DrawImageUnscaled(image, margin, (int)y);
                using var border = new Pen(Color.FromArgb(200, 200, 200));
                g.DrawRectangle(border, margin, (int)y, image.Width - 1, image.Height - 1);
                y += image.Height + gap;
            }
        }

        foreach (var l in layout.Where(l => l.Owned)) l.Image.Dispose();
        return result;
    }
}
