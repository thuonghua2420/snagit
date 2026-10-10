using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnapGit.App.Ui;

/// <summary>
/// Flat, modern look for every ToolStrip, menu and status bar (Windows 11 / Fluent style): no bevels or gradients,
/// rounded hover and selected states, thin separators, a filled accent button for the primary action.
/// Colours come from <see cref="Theme.Colors"/> at paint time, so a theme switch needs no rebuild.
/// </summary>
public sealed class ModernRenderer : ToolStripProfessionalRenderer
{
    /// <summary>Items with this <see cref="ToolStripItem.Tag"/> are drawn as the accent-coloured primary button.</summary>
    public const string PrimaryTag = "primary";

    private const int Radius = 3;

    public ModernRenderer() : base(new ModernColors()) => RoundedEdges = false;

    private static Palette P => Theme.Colors;

    private static bool IsPrimary(ToolStripItem item) => item.Tag is PrimaryTag;

    // ------------------------------------------------------------------ strip background / border

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(P.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(P.Border);
        var b = e.ToolStrip.ClientRectangle;
        switch (e.ToolStrip)
        {
            case ToolStripDropDown:
                e.Graphics.DrawRectangle(pen, 0, 0, b.Width - 1, b.Height - 1);
                break;
            case StatusStrip:
                e.Graphics.DrawLine(pen, 0, 0, b.Width, 0);
                break;
            default:
                e.Graphics.DrawLine(pen, 0, b.Height - 1, b.Width, b.Height - 1);
                break;
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { } // no grey gutter next to menu icons

    protected override void OnRenderGrip(ToolStripGripRenderEventArgs e) { }

    protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) { }

    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e) { }

    // ------------------------------------------------------------------ buttons

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => DrawButton(e.Graphics, e.Item, (e.Item as ToolStripButton)?.Checked == true);

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => DrawButton(e.Graphics, e.Item, false);

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e) => DrawButton(e.Graphics, e.Item, false);

    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e) => DrawButton(e.Graphics, e.Item, false);

    private static void DrawButton(Graphics g, ToolStripItem item, bool isChecked)
    {
        var bounds = new Rectangle(1, 1, item.Width - 2, item.Height - 2);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (IsPrimary(item))
        {
            var accent = P.Accent;
            var fill = item.Pressed ? Shade(accent, -0.2f) : item.Selected ? Shade(accent, -0.1f) : accent;
            Round(g, bounds, fill);
            return;
        }

        if (!item.Enabled) return;
        if (isChecked)
        {
            Round(g, bounds, P.Selected);
            Outline(g, bounds, P.Accent);       // a pressed-in tool: soft tint + 1 px accent outline
        }
        if (item.Pressed) { Round(g, bounds, P.Pressed); Outline(g, bounds, Color.FromArgb(120, P.Accent)); }
        else if (item.Selected)
        {
            Round(g, bounds, P.Hover);
            if (!isChecked) Outline(g, bounds, Color.FromArgb(90, P.Accent));
        }
    }

    private static void Outline(Graphics g, Rectangle bounds, Color color)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(bounds, Radius);
        using var pen = new Pen(color, 1f);
        g.DrawPath(pen, path);
        g.SmoothingMode = old;
    }

    // ------------------------------------------------------------------ menus

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Enabled) return;

        var inDropDown = item.Owner is ToolStripDropDown;
        var bounds = inDropDown ? new Rectangle(4, 1, item.Width - 8, item.Height - 2) : new Rectangle(1, 2, item.Width - 2, item.Height - 4);
        if (item.Pressed && !inDropDown) Round(e.Graphics, bounds, P.Pressed);
        else if (item.Selected || item.Pressed) Round(e.Graphics, bounds, P.Hover);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(P.Border);
        var b = e.Item.ContentRectangle;
        if (e.Vertical)
        {
            var x = b.Left + b.Width / 2;
            e.Graphics.DrawLine(pen, x, 4, x, e.Item.Height - 5);
        }
        else
        {
            var y = b.Top + b.Height / 2;
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(P.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        g.DrawLines(pen, [new PointF(cx - 4f, cy), new PointF(cx - 1.2f, cy + 3f), new PointF(cx + 4.2f, cy - 3.2f)]);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ArrowRectangle;
        var cx = r.Left + r.Width / 2f;
        var cy = r.Top + r.Height / 2f;
        using var pen = new Pen(e.Item is not null && IsPrimary(e.Item) ? P.OnAccent : e.Item?.Enabled == false ? P.Muted : P.Text, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        PointF[] chevron = e.Direction switch
        {
            ArrowDirection.Right => [new PointF(cx - 1.5f, cy - 3.5f), new PointF(cx + 1.8f, cy), new PointF(cx - 1.5f, cy + 3.5f)],
            ArrowDirection.Left => [new PointF(cx + 1.5f, cy - 3.5f), new PointF(cx - 1.8f, cy), new PointF(cx + 1.5f, cy + 3.5f)],
            ArrowDirection.Up => [new PointF(cx - 3.5f, cy + 1.5f), new PointF(cx, cy - 1.8f), new PointF(cx + 3.5f, cy + 1.5f)],
            _ => [new PointF(cx - 3.2f, cy - 1.5f), new PointF(cx, cy + 1.7f), new PointF(cx + 3.2f, cy - 1.5f)],
        };
        g.DrawLines(pen, chevron);
    }

    // ------------------------------------------------------------------ text and images

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = IsPrimary(e.Item) ? P.OnAccent : e.Item.Enabled ? e.Item is ToolStripStatusLabel ? P.Muted : P.Text : P.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null) return;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // Our own icons are redrawn at exactly the pixels of their slot (1 px outlines stay 1 px at any DPI) instead of being resampled.
        var rect = e.ImageRectangle;
        var image = e.Image;
        if (Icons.TryDescribe(e.Image, out var icon, out var tint) && rect.Width > 0)
        {
            var side = Math.Min(rect.Width, rect.Height);
            rect = new Rectangle(rect.X + (rect.Width - side) / 2, rect.Y + (rect.Height - side) / 2, side, side);
            image = Icons.Get(icon, tint, side);
        }

        if (e.Item.Enabled)
        {
            g.DrawImage(image, rect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            return;
        }

        using var copy = new Bitmap(image);
        using var grey = Icons.Disabled(copy);
        g.DrawImage(grey, rect, 0, 0, grey.Width, grey.Height, GraphicsUnit.Pixel);
    }

    // ------------------------------------------------------------------ helpers

    private static void Round(Graphics g, Rectangle bounds, Color color)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(bounds, Radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    internal static GraphicsPath RoundedRectangle(Rectangle r, int radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Shade(Color color, float amount) => amount < 0
        ? Color.FromArgb(color.A, (int)(color.R * (1 + amount)), (int)(color.G * (1 + amount)), (int)(color.B * (1 + amount)))
        : Color.FromArgb(color.A, (int)(color.R + (255 - color.R) * amount), (int)(color.G + (255 - color.G) * amount), (int)(color.B + (255 - color.B) * amount));

    /// <summary>The colour table covers the few things the base renderer still asks for (menu surfaces).</summary>
    private sealed class ModernColors : ProfessionalColorTable
    {
        private static Palette P => Theme.Colors;

        public ModernColors() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => P.Surface;
        public override Color ImageMarginGradientBegin => P.Surface;
        public override Color ImageMarginGradientMiddle => P.Surface;
        public override Color ImageMarginGradientEnd => P.Surface;
        public override Color MenuBorder => P.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuStripGradientBegin => P.Surface;
        public override Color MenuStripGradientEnd => P.Surface;
        public override Color ToolStripGradientBegin => P.Surface;
        public override Color ToolStripGradientMiddle => P.Surface;
        public override Color ToolStripGradientEnd => P.Surface;
        public override Color StatusStripGradientBegin => P.Surface;
        public override Color StatusStripGradientEnd => P.Surface;
        public override Color SeparatorDark => P.Border;
        public override Color SeparatorLight => P.Border;
    }
}
