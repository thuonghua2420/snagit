using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

/// <summary>Click to place the current stamp (<see cref="EditorContext.CurrentStamp"/>).</summary>
public sealed class StampTool : EditorTool
{
    private const float DefaultSize = 56f;

    public override ToolKind Kind => ToolKind.Stamp;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var stamp = new StampAnnotation
        {
            Kind = context.CurrentStamp,
            Style = context.GetToolStyle(ToolKind.Stamp),
            Rect = new RectangleF(e.Location.X - DefaultSize / 2, e.Location.Y - DefaultSize / 2, DefaultSize, DefaultSize),
        };
        context.Document.History.Execute(new AddAnnotationCommand(context.Document, stamp));
        context.Selection = stamp;
    }
}
