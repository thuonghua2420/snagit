using System.Drawing.Drawing2D;

using SnapGit.App.Ui;

namespace SnapGit.App.Controls;

/// <summary>Swatch grid shown inside a drop-down.</summary>
internal sealed class PaletteGrid : Control
{
    private const int Cell = 22;
    private const int Gap = 3;
    private const int Columns = 8;

    private static readonly Color[] Colors = BuildPalette();
    private int _hover = -1;

    public PaletteGrid()
    {
        DoubleBuffered = true;
        var rows = (Colors.Length + Columns - 1) / Columns;
        Size = new Size(Columns * (Cell + Gap) + Gap, rows * (Cell + Gap) + Gap);
    }

    public event EventHandler<Color>? ColorPicked;

    private static Color[] BuildPalette()
    {
        Color[] hues =
        [
            Color.FromArgb(229, 57, 53), Color.FromArgb(251, 140, 0), Color.FromArgb(253, 216, 53), Color.FromArgb(67, 160, 71),
            Color.FromArgb(0, 172, 193), Color.FromArgb(30, 136, 229), Color.FromArgb(142, 36, 170), Color.FromArgb(216, 27, 96),
        ];

        var list = new List<Color>(hues);
        list.AddRange(hues.Select(h => Blend(h, Color.Black, 0.35f)));
        list.AddRange(hues.Select(h => Blend(h, Color.White, 0.55f)));
        list.AddRange([Color.White, Color.FromArgb(224, 224, 224), Color.FromArgb(176, 176, 176), Color.FromArgb(128, 128, 128),
            Color.FromArgb(80, 80, 80), Color.FromArgb(48, 48, 48), Color.FromArgb(24, 24, 24), Color.Black]);
        return list.ToArray();
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    private Rectangle CellRect(int index) =>
        new(Gap + index % Columns * (Cell + Gap), Gap + index / Columns * (Cell + Gap), Cell, Cell);

    private int IndexAt(Point p)
    {
        for (var i = 0; i < Colors.Length; i++)
            if (CellRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var palette = Theme.Colors;
        for (var i = 0; i < Colors.Length; i++)
        {
            var r = CellRect(i);
            using var fill = new SolidBrush(Colors[i]);
            e.Graphics.FillEllipse(fill, r);
            using var border = new Pen(palette.Border, 1f);
            e.Graphics.DrawEllipse(border, r);
            if (i != _hover) continue;
            using var ring = new Pen(palette.Accent, 2f);
            e.Graphics.DrawEllipse(ring, Rectangle.Inflate(r, 2, 2));
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index == _hover) return;
        _hover = index;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);
        if (index >= 0) ColorPicked?.Invoke(this, Colors[index]);
    }
}

/// <summary>Toolbar button showing the current colour with a palette drop-down (optionally with a "no colour" entry).</summary>
public sealed class ColorPickerButton : ToolStripDropDownButton
{
    private Color _color = Color.Red;

    public ColorPickerButton(string caption, bool allowTransparent)
    {
        Text = caption;
        ToolTipText = caption;
        DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        TextImageRelation = TextImageRelation.ImageBeforeText;
        ShowDropDownArrow = true;
        Padding = new Padding(4, 4, 2, 4);
        DropDown.BackColor = Theme.Colors.Surface;
        DropDown.ImageScalingSize = new Size(16, 16);

        var grid = new PaletteGrid();
        grid.ColorPicked += (_, color) =>
        {
            DropDown.Close();
            Color = color;
        };
        DropDown.Items.Add(new ToolStripControlHost(grid) { Padding = Padding.Empty, Margin = Padding.Empty, AutoSize = true });

        if (allowTransparent)
            DropDown.Items.Add("No color", null, (_, _) => Color = Color.Transparent);
        DropDown.Items.Add("More colors...", null, (_, _) => PickCustom());

        UpdateSwatch();
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            SetColorSilently(value);
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised only when the user picked a colour (not when synced programmatically).</summary>
    public event EventHandler? ColorChanged;

    public void SetColorSilently(Color value)
    {
        _color = value;
        UpdateSwatch();
    }

    private void PickCustom()
    {
        using var dialog = new ColorDialog { Color = _color.A == 0 ? Color.White : _color, FullOpen = true };
        if (dialog.ShowDialog() == DialogResult.OK) Color = dialog.Color;
    }

    private void UpdateSwatch()
    {
        var old = Image;
        var swatch = new Bitmap(40, 40);
        using (var g = Graphics.FromImage(swatch))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var chip = new Rectangle(5, 5, 30, 30);
            if (_color.A == 0)
            {
                using var white = new SolidBrush(Color.White);
                g.FillEllipse(white, chip);
                using var red = new Pen(Color.FromArgb(220, 50, 50), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(red, 11, 29, 29, 11);
            }
            else
            {
                using var fill = new SolidBrush(_color);
                g.FillEllipse(fill, chip);
            }
            using var ring = new Pen(Color.FromArgb(Theme.IsDark ? 160 : 110, Theme.IsDark ? Color.White : Color.Black), 1.6f);
            g.DrawEllipse(ring, chip);
        }
        Image = swatch;
        old?.Dispose();
    }
}