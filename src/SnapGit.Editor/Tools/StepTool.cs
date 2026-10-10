using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

/// <summary>Click to drop the next numbered step marker.</summary>
public sealed class StepTool : EditorTool
{
    public override ToolKind Kind => ToolKind.Step;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var step = new StepAnnotation
        {
            Style = context.GetToolStyle(ToolKind.Step),
            Center = e.Location,
            Number = context.NextStepNumber(),
        };
        context.Document.History.Execute(new AddAnnotationCommand(context.Document, step));
        context.Selection = step;
    }
}
