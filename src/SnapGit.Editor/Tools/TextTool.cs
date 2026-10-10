using SnapGit.Editor.Annotations;

namespace SnapGit.Editor.Tools;

/// <summary>Click (or drag a box) to place text or a callout; the canvas opens an in-place editor.</summary>
public sealed class TextTool(ToolKind kind, Func<TextAnnotation> factory) : EditorTool
{
    private const float DefaultWidth = 260f;

    private PointF _start;
    private TextAnnotation? _pending;
    private bool _dragging;

    public override ToolKind Kind => kind;

    public override Cursor Cursor => Cursors.IBeam;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // Clicking existing text edits it instead of stacking a new box on top.
        if (context.Document.HitTest(e.Location, 3f / Math.Max(context.Zoom, 0.01f)) is TextAnnotation existing)
        {
            context.Selection = existing;
            context.RequestTextEdit(existing, isNew: false);
            return;
        }

        _start = e.Location;
        _dragging = true;
        var style = context.GetToolStyle(kind);
        _pending = factory();
        _pending.Style = style;
        ((IDragCreatable)_pending).SetDrag(_start, new PointF(_start.X + DefaultWidth, _start.Y + style.FontSize + 16), false);
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (!_dragging || _pending is null) return;

        var box = Geometry.FromPoints(_start, e.Location);
        if (box.Width > 6 || box.Height > 6)
        {
            var end = new PointF(box.X + Math.Max(box.Width, 40f), box.Y + Math.Max(box.Height, _pending.Style.FontSize + 16));
            ((IDragCreatable)_pending).SetDrag(box.Location, end, false);
            context.Invalidate();
        }
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e)
    {
        if (!_dragging || _pending is null) return;

        _dragging = false;
        var pending = _pending;
        _pending = null;
        context.RequestTextEdit(pending, isNew: true);
    }

    public override void DrawOverlay(Graphics g, EditorContext context)
    {
        if (_pending is null) return;
        using var pen = new Pen(Color.FromArgb(160, 0, 120, 215), 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawRectangle(pen, _pending.Rect.X, _pending.Rect.Y, _pending.Rect.Width, _pending.Rect.Height);
    }
}
