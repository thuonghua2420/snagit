namespace SnapGit.Editor.Annotations;

public enum LineDash
{
    Solid,
    Dash,
    Dot,
}

public enum ArrowHead
{
    Triangle,
    Open,
    Circle,
    Diamond,
    None,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Appearance shared by all annotations. Immutable: change it with <c>style with { ... }</c>.
/// Semantics are uniform so one toolbar drives every tool:
/// <list type="bullet">
/// <item><see cref="Stroke"/> = outline / line / arrow / pen / text / step-number colour</item>
/// <item><see cref="Fill"/> = interior colour (shape fill, highlight colour, text background, step circle)</item>
/// </list>
/// Properties that don't apply to an annotation type are simply ignored by it.
/// </summary>
public sealed record AnnotationStyle
{
    public Color Stroke { get; init; } = Color.FromArgb(229, 57, 53);
    public Color Fill { get; init; } = Color.Transparent;
    public float StrokeWidth { get; init; } = 4f;

    /// <summary>0..1, applied on top of the colours' own alpha.</summary>
    public float Opacity { get; init; } = 1f;

    public bool Shadow { get; init; }

    public LineDash Dash { get; init; } = LineDash.Solid;

    /// <summary>Rounded corners for rectangles, callouts and spotlights (image pixels).</summary>
    public float CornerRadius { get; init; }

    public ArrowHead Head { get; init; } = ArrowHead.Triangle;

    /// <summary>Arrows only: head at both ends.</summary>
    public bool DoubleHeaded { get; init; }

    public string FontFamily { get; init; } = "Segoe UI";

    /// <summary>Font size in pixels (image space, so it does not depend on screen DPI).</summary>
    public float FontSize { get; init; } = 24f;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>Second fill colour: when not transparent the interior is a gradient from <see cref="Fill"/> to this.</summary>
    public Color GradientTo { get; init; } = Color.Transparent;

    /// <summary>Gradient direction in degrees: 0 = left→right, 90 = top→bottom.</summary>
    public float GradientAngle { get; init; } = 90f;

    /// <summary>Outline drawn around text (callouts and text boxes); needs <see cref="OutlineWidth"/> &gt; 0.</summary>
    public Color OutlineColor { get; init; } = Color.White;

    public float OutlineWidth { get; init; }
}
