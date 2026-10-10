using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

public sealed class PenTool : EditorTool
{
    private PenAnnotation? _stroke;

    public override ToolKind Kind => ToolKind.Pen;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        _stroke = new PenAnnotation { Style = context.GetToolStyle(ToolKind.Pen) };
        _stroke.Points.Add(e.Location);
        context.Selection = null;
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (_stroke is null) return;

        // Skip sub-pixel jitter to keep strokes light.
        if (Geometry.Distance(_stroke.Points[^1], e.Location) < 1.5f) return;
        _stroke.Points.Add(e.Location);
        context.Invalidate();
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e)
    {
        if (_stroke is null) return;

        var finished = _stroke;
        _stroke = null;
        context.Document.History.Execute(new AddAnnotationCommand(context.Document, finished));
        context.Selection = finished;
    }

    public override bool KeyDown(EditorContext context, Keys key)
    {
        if (key != Keys.Escape || _stroke is null) return false;
        _stroke = null;
        context.Invalidate();
        return true;
    }

    public override void DrawOverlay(Graphics g, EditorContext context) => _stroke?.Draw(g, context.RenderContext);
}
