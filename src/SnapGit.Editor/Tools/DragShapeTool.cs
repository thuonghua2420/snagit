using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

/// <summary>Generic "drag from A to B" tool used by arrow, line, rectangle, ellipse, highlight and blur.</summary>
public sealed class DragShapeTool(ToolKind kind, Func<Annotation> factory) : EditorTool
{
    private Annotation? _preview;
    private PointF _start;

    public override ToolKind Kind => kind;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        _start = e.Location;
        _preview = factory();
        _preview.Style = context.GetToolStyle(kind);
        ((IDragCreatable)_preview).SetDrag(_start, _start, e.Shift);
        context.Selection = null;
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (_preview is null) return;
        ((IDragCreatable)_preview).SetDrag(_start, e.Location, e.Shift);
        context.Invalidate();
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e)
    {
        if (_preview is null) return;

        var created = _preview;
        _preview = null;

        if (!((IDragCreatable)created).IsDegenerate)
        {
            context.Document.History.Execute(new AddAnnotationCommand(context.Document, created));
            context.Selection = created;
        }
        context.Invalidate();
    }

    public override bool KeyDown(EditorContext context, Keys key)
    {
        if (key != Keys.Escape || _preview is null) return false;
        _preview = null;
        context.Invalidate();
        return true;
    }

    public override void DrawOverlay(Graphics g, EditorContext context) => _preview?.Draw(g, context.RenderContext);
}
