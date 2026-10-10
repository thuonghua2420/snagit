namespace SnapGit.Editor.Annotations;

/// <summary>
/// Several annotations that move, copy and delete as one. Children keep their own styles; the group's own
/// style is unused. Ungrouping gives the original objects back.
/// </summary>
public sealed class GroupAnnotation : Annotation
{
    public List<Annotation> Children { get; set; } = new();

    protected override bool SupportsShadow => false;

    public override RectangleF Bounds =>
        Children.Count == 0 ? RectangleF.Empty : Children.Select(c => c.Bounds).Aggregate(RectangleF.Union);

    protected override void OnCloned() => Children = Children.Select(c => c.Clone()).ToList();

    protected override void DrawCore(Graphics g, RenderContext context, AnnotationStyle style)
    {
        foreach (var child in Children) child.Draw(g, context);
    }

    public override bool HitTest(PointF point, float tolerance) => Children.Any(c => c.HitTest(point, tolerance));

    public override void Translate(float dx, float dy)
    {
        foreach (var child in Children) child.Translate(dx, dy);
    }

    /// <summary>
    /// Restores a snapshot INTO the existing child objects (matched by id) instead of swapping them for copies:
    /// other commands in the undo history still point at those very instances.
    /// </summary>
    public override void CopyStateFrom(Annotation source)
    {
        base.CopyStateFrom(source);
        var other = (GroupAnnotation)source;

        var sameShape = other.Children.Count == Children.Count && other.Children.All(o => Children.Any(c => c.Id == o.Id));
        if (!sameShape)
        {
            Children = other.Children.Select(c => c.Clone()).ToList();
            return;
        }

        foreach (var child in Children)
            child.CopyStateFrom(other.Children.First(o => o.Id == child.Id));
    }
}
