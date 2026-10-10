using System.Drawing.Drawing2D;
using SnapGit.Core.Imaging;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

/// <summary>Click a pixel: its colour becomes the outline colour (Shift or right-click: fill colour), then the previous tool returns.</summary>
public sealed class EyedropperTool : EditorTool
{
    public override ToolKind Kind => ToolKind.Eyedropper;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        var picked = context.Document.PixelAt(e.Location);
        if (picked is null) return;

        var asFill = e.Shift || e.Button == MouseButtons.Right;
        var color = picked.Value;

        context.SetTool(context.PreviousTool == ToolKind.Eyedropper ? ToolKind.Arrow : context.PreviousTool);
        context.UpdateStyle(s => asFill ? s with { Fill = color } : s with { Stroke = color });
    }
}

/// <summary>Paint-bucket: flood-fills the base image region under the click with the fill colour.</summary>
public sealed class FillTool : EditorTool
{
    private const int Tolerance = 32;

    public override ToolKind Kind => ToolKind.Fill;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var style = context.GetToolStyle(ToolKind.Fill);
        var color = style.Fill.A > 0 ? style.Fill : style.Stroke;
        var point = Point.Round(e.Location);

        if (point.X < 0 || point.Y < 0 || point.X >= context.Document.Image.Width || point.Y >= context.Document.Image.Height) return;
        context.Document.ApplyImageOperation("Fill", image => ImageOps.FloodFill(image, point, color.WithAlpha(style.Opacity), Tolerance), flatten: false);
    }
}

internal static class ColorExtensions
{
    public static Color WithAlpha(this Color color, float opacity) => Color.FromArgb((int)Math.Clamp(color.A * opacity, 0, 255), color);
}

/// <summary>
/// Drag a band across the image, then Enter (or double-click) to remove it and join the remaining parts.
/// The band is vertical (columns) when the drag is mostly horizontal, horizontal (rows) when mostly vertical.
/// </summary>
public sealed class CutOutTool : EditorTool
{
    private PointF _start;
    private Rectangle _band;
    private bool _horizontalStrip;
    private bool _dragging;

    public override ToolKind Kind => ToolKind.CutOut;

    public override void OnDeactivated(EditorContext context) => _band = Rectangle.Empty;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (e.Clicks >= 2 && _band.Contains(Point.Round(e.Location)))
        {
            Apply(context);
            return;
        }

        _start = e.Location;
        _band = Rectangle.Empty;
        _dragging = true;
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (!_dragging) return;

        var image = context.Document.Image;
        float dx = Math.Abs(e.Location.X - _start.X), dy = Math.Abs(e.Location.Y - _start.Y);
        _horizontalStrip = dy > dx;

        _band = _horizontalStrip
            ? Rectangle.FromLTRB(0, (int)Math.Min(_start.Y, e.Location.Y), image.Width, (int)Math.Max(_start.Y, e.Location.Y))
            : Rectangle.FromLTRB((int)Math.Min(_start.X, e.Location.X), 0, (int)Math.Max(_start.X, e.Location.X), image.Height);
        _band.Intersect(new Rectangle(Point.Empty, image.Size));
        context.Invalidate();
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e) => _dragging = false;

    public override bool KeyDown(EditorContext context, Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Enter:
                Apply(context);
                return true;
            case Keys.Escape:
                _band = Rectangle.Empty;
                context.Invalidate();
                return true;
            default:
                return false;
        }
    }

    public override void DrawOverlay(Graphics g, EditorContext context)
    {
        if (_band.Width < 1 || _band.Height < 1) return;

        using var shade = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(200, 229, 57, 53), Color.FromArgb(90, 255, 255, 255));
        g.FillRectangle(shade, _band);
        using var pen = new Pen(Color.FromArgb(229, 57, 53), 1.5f / Math.Max(context.Zoom, 0.01f)) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, _band);
    }

    private void Apply(EditorContext context)
    {
        var band = _band;
        var horizontal = _horizontalStrip;
        _band = Rectangle.Empty;

        var span = horizontal ? band.Height : band.Width;
        var total = horizontal ? context.Document.Image.Height : context.Document.Image.Width;
        if (span < 1 || span >= total) { context.Invalidate(); return; }

        context.SetSelection([]);
        var (from, to) = horizontal ? (band.Top, band.Bottom) : (band.Left, band.Right);
        context.Document.ApplyImageOperation("Cut out", image => ImageOps.CutOut(image, from, to, horizontal), flatten: true);
    }
}

/// <summary>
/// Click an object (auto-detected) or drag a rectangle: the pixels become a movable layer and the hole is filled
/// from the surroundings. Afterwards the Select tool is active so the layer can be dragged.
/// </summary>
public sealed class SmartMoveTool : EditorTool
{
    private const float ClickSlopPx = 4f;

    private PointF _start;
    private RectangleF _drag;
    private bool _dragging;

    public override ToolKind Kind => ToolKind.SmartMove;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _start = e.Location;
        _drag = RectangleF.Empty;
        _dragging = true;
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (!_dragging) return;
        _drag = Geometry.FromPoints(_start, e.Location);
        context.Invalidate();
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;

        var image = context.Document.Image;
        var area = Rectangle.Empty;

        if (Geometry.Distance(_start, e.Location) < ClickSlopPx / Math.Max(context.Zoom, 0.01f))
        {
            area = ImageOps.DetectObjectBounds(image, Point.Round(_start));
        }
        else
        {
            area = Rectangle.Round(_drag);
        }

        _drag = RectangleF.Empty;
        area.Intersect(new Rectangle(Point.Empty, image.Size));
        if (area.Width < 3 || area.Height < 3) { context.Invalidate(); return; }

        Lift(context, area);
    }

    private static void Lift(EditorContext context, Rectangle area)
    {
        var document = context.Document;
        var layer = new ImageLayerAnnotation
        {
            Image = document.Image.Clone(area, System.Drawing.Imaging.PixelFormat.Format32bppArgb),
            Rect = area,
        };
        var patched = ImageOps.FillRegionSmart(document.Image, area);

        document.History.Execute(new CompositeCommand("Smart move",
        [
            new ReplaceImageCommand(document, patched, "Smart move"),
            new AddAnnotationCommand(document, layer),
        ]));

        context.SetTool(ToolKind.Select);
        context.Selection = layer;
    }

    public override void DrawOverlay(Graphics g, EditorContext context)
    {
        if (!_dragging || _drag.Width < 1 || _drag.Height < 1) return;
        using var pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f / Math.Max(context.Zoom, 0.01f)) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, _drag.X, _drag.Y, _drag.Width, _drag.Height);
    }
}
