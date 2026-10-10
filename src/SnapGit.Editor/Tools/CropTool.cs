using System.Drawing.Drawing2D;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

/// <summary>Drag a frame, then press Enter (or double-click) to crop; Esc cancels.</summary>
public sealed class CropTool : EditorTool
{
    private PointF _start;
    private RectangleF _frame;
    private bool _dragging;

    public override ToolKind Kind => ToolKind.Crop;

    public override void OnDeactivated(EditorContext context) => _frame = RectangleF.Empty;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (e.Clicks >= 2 && _frame.Contains(e.Location))
        {
            Apply(context);
            return;
        }

        _start = e.Location;
        _frame = RectangleF.Empty;
        _dragging = true;
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (!_dragging) return;
        _frame = Geometry.FromPoints(_start, e.Location);
        context.Invalidate();
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e) => _dragging = false;

    public override bool KeyDown(EditorContext context, Keys key)
    {
        switch (key)
        {
            case Keys.Enter:
                Apply(context);
                return true;
            case Keys.Escape:
                _frame = RectangleF.Empty;
                context.Invalidate();
                return true;
            default:
                return false;
        }
    }

    public override void DrawOverlay(Graphics g, EditorContext context)
    {
        if (_frame.Width < 1 || _frame.Height < 1) return;

        var image = new Rectangle(Point.Empty, context.Document.Image.Size);
        var state = g.Save();
        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.SetClip(_frame, CombineMode.Exclude);
            g.FillRectangle(dim, image);
        }
        g.Restore(state);

        using var pen = new Pen(Color.White, 1.5f / Math.Max(context.Zoom, 0.01f)) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, _frame.X, _frame.Y, _frame.Width, _frame.Height);
    }

    private void Apply(EditorContext context)
    {
        var area = Rectangle.Round(_frame);
        _frame = RectangleF.Empty;
        if (area.Width < 2 || area.Height < 2) return;

        context.Selection = null;
        context.Document.History.Execute(new CropCommand(context.Document, area));
    }
}
