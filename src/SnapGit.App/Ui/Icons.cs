using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.CompilerServices;

namespace SnapGit.App.Ui;

/// <summary>
/// Renders the colour icons of <see cref="IconSet"/> (16 × 16 grid) to bitmaps. The toolbars ask for the exact pixel size of their slot
/// (16 px at 100 % scaling, 20 / 24 / 32 px on denser screens), so a 1 px outline is always one crisp pixel row at 100 %.
/// </summary>
public static class Icons
{
    /// <summary>The design grid: icons are authored for this size and look their best at it.</summary>
    public const int Grid = 16;

    /// <summary>Size of the bitmaps handed out by <see cref="Get"/> when no size is given (renderers redraw at the real slot size anyway).</summary>
    public const int ReferenceSize = 32;

    private static readonly object Gate = new();
    private static readonly Dictionary<(UiIcon, int, int), Bitmap> Cache = new();
    private static readonly ConditionalWeakTable<Image, Description> Described = new();

    private sealed record Description(UiIcon Icon, Color? Tint);

    /// <param name="tint">Draws the whole icon as a one-colour silhouette (white on the accent button, for instance); null = full colour.</param>
    public static Bitmap Get(UiIcon icon, Color? tint = null, int size = ReferenceSize)
    {
        var key = (icon, tint?.ToArgb() ?? 0, size);
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var bmp))
            {
                Cache[key] = bmp = Render(icon, tint, size);
                Described.Add(bmp, new Description(icon, tint));
            }
            return bmp;
        }
    }

    /// <summary>Which icon a bitmap from <see cref="Get"/> shows, so a renderer can redraw it at exactly the pixels it has.</summary>
    public static bool TryDescribe(Image? image, out UiIcon icon, out Color? tint)
    {
        icon = default;
        tint = null;
        if (image is null || !Described.TryGetValue(image, out var description)) return false;
        icon = description.Icon;
        tint = description.Tint;
        return true;
    }

    /// <summary>Greyed version for disabled buttons (colour icons turn to a soft grey, as in classic toolbars).</summary>
    public static Bitmap Disabled(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(result);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(
        [
            [.3f, .3f, .3f, 0, 0],
            [.59f, .59f, .59f, 0, 0],
            [.11f, .11f, .11f, 0, 0],
            [0, 0, 0, .55f, 0],
            [.12f, .12f, .12f, 0, 1],
        ]));
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    private static Bitmap Render(UiIcon icon, Color? tint, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(size / (float)Grid, size / (float)Grid);

        var source = icon;
        if (IconSet.Derived(icon) is { } derived)
        {
            source = derived.Source;
            if (derived.Mirror) { g.TranslateTransform(Grid, 0); g.ScaleTransform(-1, 1); }
            if (derived.Turn) { g.TranslateTransform(Grid / 2f, Grid / 2f); g.RotateTransform(90); g.TranslateTransform(-Grid / 2f, -Grid / 2f); }
        }

        foreach (var piece in IconSet.Of(source)) Draw(g, piece, tint);
        return bmp;
    }

    private static void Draw(Graphics g, Piece piece, Color? tint)
    {
        if (piece.Text is { } label) { DrawLabel(g, label, tint); return; }
        if (piece.Path is null) return;

        using var path = SvgPath.Parse(piece.Path);

        if (piece.Fill is { } fillTone)
        {
            var colour = tint ?? fillTone();
            var bounds = path.GetBounds();
            var alpha = (int)(255 * (tint is null ? piece.FillAlpha : Math.Min(piece.FillAlpha, 0.85f)));
            if (piece.Gloss && tint is null && bounds.Height > 0.5f)
            {
                using var brush = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 0.01f, Math.Max(bounds.Width, 0.1f), bounds.Height + 0.02f),
                    Color.FromArgb(alpha, Lighten(colour, 0.34f)), Color.FromArgb(alpha, Darken(colour, 0.06f)), LinearGradientMode.Vertical);
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(alpha, colour));
                g.FillPath(brush, path);
            }
        }

        if (piece.Stroke is { } strokeTone)
        {
            var colour = tint ?? strokeTone();
            using var pen = new Pen(Color.FromArgb(piece.FillAlpha < 1f && piece.Fill is null ? (int)(255 * piece.FillAlpha) : 255, colour), piece.Width)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
            };
            if (piece.Dashed) pen.DashPattern = [2f, 1.6f];
            g.DrawPath(pen, path);
        }
    }

    private static void DrawLabel(Graphics g, IconLabel label, Color? tint)
    {
        var style = (label.Bold ? FontStyle.Bold : FontStyle.Regular) | (label.Italic ? FontStyle.Italic : FontStyle.Regular);
        using var font = new Font(Theme.FontName == "Segoe UI Variable Text" ? "Segoe UI" : Theme.FontName, label.Size, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(tint ?? label.Color());
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        format.FormatFlags &= ~StringFormatFlags.LineLimit; // would make text taller than its box vanish
        g.DrawString(label.Text, font, brush, label.Box, format);
    }

    private static Color Lighten(Color c, float amount) =>
        Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * amount), (int)(c.G + (255 - c.G) * amount), (int)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, float amount) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
}
