using SnapGit.Editor.Annotations;

namespace SnapGit.Editor.Tools;

/// <summary>The one place that maps a <see cref="ToolKind"/> to its tool. Register new tools here.</summary>
public static class ToolFactory
{
    public static IEditorTool Create(ToolKind kind) => kind switch
    {
        ToolKind.Select => new SelectTool(),
        ToolKind.Arrow => new DragShapeTool(kind, () => new ArrowAnnotation()),
        ToolKind.Line => new DragShapeTool(kind, () => new LineAnnotation()),
        ToolKind.Rectangle => new DragShapeTool(kind, () => new RectangleAnnotation()),
        ToolKind.Ellipse => new DragShapeTool(kind, () => new EllipseAnnotation()),
        ToolKind.Highlight => new DragShapeTool(kind, () => new HighlightAnnotation()),
        ToolKind.Blur => new DragShapeTool(kind, () => new BlurAnnotation()),
        ToolKind.Spotlight => new DragShapeTool(kind, () => new SpotlightAnnotation()),
        ToolKind.Magnify => new DragShapeTool(kind, () => new MagnifyAnnotation()),
        ToolKind.Pen => new PenTool(),
        ToolKind.Text => new TextTool(kind, () => new TextAnnotation()),
        ToolKind.Callout => new TextTool(kind, () => new CalloutAnnotation()),
        ToolKind.Step => new StepTool(),
        ToolKind.Stamp => new StampTool(),
        ToolKind.Eyedropper => new EyedropperTool(),
        ToolKind.Fill => new FillTool(),
        ToolKind.Crop => new CropTool(),
        ToolKind.CutOut => new CutOutTool(),
        ToolKind.SmartMove => new SmartMoveTool(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No tool registered."),
    };
}
