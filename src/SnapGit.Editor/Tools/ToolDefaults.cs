using SnapGit.Editor.Annotations;

namespace SnapGit.Editor.Tools;

/// <summary>Initial style of each tool. Change defaults here, not in the tools.</summary>
internal static class ToolDefaults
{
    private static readonly Color Red = Color.FromArgb(229, 57, 53);
    private static readonly Color Yellow = Color.FromArgb(255, 235, 59);

    public static Dictionary<ToolKind, AnnotationStyle> CreateStyles() => new()
    {
        [ToolKind.Select] = new AnnotationStyle(),
        [ToolKind.Arrow] = new AnnotationStyle { Stroke = Red, StrokeWidth = 5 },
        [ToolKind.Line] = new AnnotationStyle { Stroke = Red, StrokeWidth = 4 },
        [ToolKind.Rectangle] = new AnnotationStyle { Stroke = Red, StrokeWidth = 4 },
        [ToolKind.Ellipse] = new AnnotationStyle { Stroke = Red, StrokeWidth = 4 },
        [ToolKind.Pen] = new AnnotationStyle { Stroke = Red, StrokeWidth = 4 },
        [ToolKind.Highlight] = new AnnotationStyle { Stroke = Yellow, Fill = Yellow, StrokeWidth = 0, Opacity = 0.45f },
        [ToolKind.Text] = new AnnotationStyle { Stroke = Color.FromArgb(33, 33, 33), Fill = Color.Transparent, FontSize = 28 },
        [ToolKind.Callout] = new AnnotationStyle { Stroke = Color.FromArgb(33, 33, 33), Fill = Color.FromArgb(255, 249, 196), StrokeWidth = 2, FontSize = 22, CornerRadius = 10 },
        [ToolKind.Step] = new AnnotationStyle { Stroke = Color.White, Fill = Red },
        [ToolKind.Stamp] = new AnnotationStyle { Stroke = Color.White, Fill = Color.Transparent },
        [ToolKind.Blur] = new AnnotationStyle(),
        [ToolKind.Spotlight] = new AnnotationStyle { Opacity = 0.8f },
        [ToolKind.Magnify] = new AnnotationStyle { Stroke = Color.FromArgb(33, 33, 33), StrokeWidth = 3, Shadow = true },
        [ToolKind.Eyedropper] = new AnnotationStyle(),
        [ToolKind.Fill] = new AnnotationStyle { Fill = Red },
        [ToolKind.Crop] = new AnnotationStyle(),
        [ToolKind.CutOut] = new AnnotationStyle(),
        [ToolKind.SmartMove] = new AnnotationStyle(),
    };
}
