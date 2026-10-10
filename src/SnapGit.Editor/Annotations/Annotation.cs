using System.Text.Json.Serialization;

namespace SnapGit.Editor.Annotations;

/// <summary>What an annotation needs from its surroundings while drawing.</summary>
/// <param name="BaseImage">The untouched screenshot (needed by effects such as blur/pixelate).</param>
public sealed record RenderContext(Bitmap BaseImage);

/// <summary>
/// An editable object drawn on top of the screenshot, in IMAGE pixel coordinates.
/// To add a new kind: derive from this (or <see cref="BoxAnnotation"/> / <see cref="LineAnnotation"/>),
/// implement <see cref="DrawCore"/>, <see cref="HitTest"/>, <see cref="Translate"/>, <see cref="CopyStateFrom"/>,
/// then add a tool. See .claude/skills/snapgit-add-annotation-tool.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RectangleAnnotation), "rectangle")]
[JsonDerivedType(typeof(EllipseAnnotation), "ellipse")]
[JsonDerivedType(typeof(HighlightAnnotation), "highlight")]
[JsonDerivedType(typeof(LineAnnotation), "line")]
[JsonDerivedType(typeof(ArrowAnnotation), "arrow")]
[JsonDerivedType(typeof(PenAnnotation), "pen")]
[JsonDerivedType(typeof(StepAnnotation), "step")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(CalloutAnnotation), "callout")]
[JsonDerivedType(typeof(StampAnnotation), "stamp")]
[JsonDerivedType(typeof(BlurAnnotation), "blur")]
[JsonDerivedType(typeof(SpotlightAnnotation), "spotlight")]
[JsonDerivedType(typeof(MagnifyAnnotation), "magnify")]
[JsonDerivedType(typeof(ImageLayerAnnotation), "image")]
[JsonDerivedType(typeof(GroupAnnotation), "group")]
public abstract class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AnnotationStyle Style { get; set; } = new();

    public abstract RectangleF Bounds { get; }

    /// <summary>Resize handles (image coordinates). Empty = not resizable.</summary>
    public virtual IReadOnlyList<PointF> HandlePoints => Array.Empty<PointF>();

    protected virtual bool SupportsShadow => true;

    public void Draw(Graphics g, RenderContext context)
    {
        if (Style.Shadow && SupportsShadow)
        {
            var shadowColor = Color.FromArgb(120, 0, 0, 0);
            var shadowStyle = Style with
            {
                Stroke = shadowColor,
                Fill = Style.Fill.A == 0 ? Style.Fill : shadowColor,
                GradientTo = Color.Transparent, // a shadow is one flat colour
                OutlineWidth = 0,
                Shadow = false,
            };

            var state = g.Save();
            g.TranslateTransform(3, 3);
            DrawCore(g, context, shadowStyle);
            g.Restore(state);
        }

        DrawCore(g, context, Style);
    }

    protected abstract void DrawCore(Graphics g, RenderContext context, AnnotationStyle style);

    /// <param name="tolerance">Extra reach in image pixels (already divided by zoom by the caller).</param>
    public abstract bool HitTest(PointF point, float tolerance);

    public abstract void Translate(float dx, float dy);

    public virtual void MoveHandle(int index, PointF to) { }

    /// <summary>Overwrites this annotation's mutable state with <paramref name="source"/>'s (same concrete type). Used by undo/redo.</summary>
    public virtual void CopyStateFrom(Annotation source) => Style = source.Style;

    /// <summary>Deep snapshot (keeps the same <see cref="Id"/>).</summary>
    public Annotation Clone()
    {
        var copy = (Annotation)MemberwiseClone();
        copy.OnCloned();
        return copy;
    }

    protected virtual void OnCloned() { }
}

/// <summary>Annotations that are created by dragging from one point to another.</summary>
public interface IDragCreatable
{
    /// <param name="constrain">Shift held: square for boxes, 45° steps for lines.</param>
    void SetDrag(PointF start, PointF end, bool constrain);

    /// <summary>True when the drag is too small to keep.</summary>
    bool IsDegenerate { get; }
}
