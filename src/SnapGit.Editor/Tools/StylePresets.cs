using SnapGit.Core.Localization;
using SnapGit.Editor.Annotations;

namespace SnapGit.Editor.Tools;

public sealed record StylePreset(string Name, Func<AnnotationStyle, AnnotationStyle> Apply);

/// <summary>Ready-made looks ("quick styles"). Applied through <see cref="EditorContext.UpdateStyle"/> so they are undoable.</summary>
public static class StylePresets
{
    public static IReadOnlyList<StylePreset> All { get; } =
    [
        new(Loc.N("Red emphasis"), s => s with { Stroke = Color.FromArgb(229, 57, 53), Fill = Color.Transparent, StrokeWidth = 4, Opacity = 1f }),
        new(Loc.N("Yellow note"), s => s with { Fill = Color.FromArgb(255, 249, 196), Stroke = Color.FromArgb(33, 33, 33), Opacity = 1f }),
        new(Loc.N("Blue info"), s => s with { Fill = Color.FromArgb(30, 136, 229), Stroke = Color.White, Opacity = 1f }),
        new(Loc.N("Green success"), s => s with { Fill = Color.FromArgb(67, 160, 71), Stroke = Color.White, Opacity = 1f }),
        new(Loc.N("Dark label"), s => s with { Fill = Color.FromArgb(33, 33, 33), Stroke = Color.White, Opacity = 1f }),
        new(Loc.N("Highlighter"), s => s with { Fill = Color.FromArgb(255, 235, 59), Stroke = Color.FromArgb(255, 235, 59), StrokeWidth = 0, Opacity = 0.45f }),
        new(Loc.N("Soft shadow"), s => s with { Shadow = true }),
        new(Loc.N("Thin outline"), s => s with { StrokeWidth = 1, Fill = Color.Transparent }),
    ];
}
