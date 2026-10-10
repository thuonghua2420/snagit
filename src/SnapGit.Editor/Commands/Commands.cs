using SnapGit.Editor.Annotations;

namespace SnapGit.Editor.Commands;

public sealed class AddAnnotationCommand(EditorDocument document, Annotation annotation) : IUndoableCommand
{
    private int _index = -1;

    public string Name => "Add";

    public void Do() => _index = document.InsertAnnotation(annotation, _index);

    public void Undo() => document.RemoveAnnotation(annotation);
}

public sealed class RemoveAnnotationCommand(EditorDocument document, Annotation annotation) : IUndoableCommand
{
    private int _index;

    public string Name => "Delete";

    public void Do() => _index = document.RemoveAnnotation(annotation);

    public void Undo() => document.InsertAnnotation(annotation, _index);
}

/// <summary>Records a before/after snapshot of one annotation (move, resize, restyle, edit text).</summary>
public sealed class ModifyAnnotationCommand : IUndoableCommand
{
    private readonly Annotation _target;
    private readonly Annotation _before;
    private readonly Annotation _after;

    /// <param name="before">Snapshot (Clone) taken BEFORE the change.</param>
    public ModifyAnnotationCommand(Annotation target, Annotation before)
    {
        _target = target;
        _before = before;
        _after = target.Clone(); // the change has already been applied live
    }

    public string Name => "Edit";

    public void Do() => _target.CopyStateFrom(_after);

    public void Undo() => _target.CopyStateFrom(_before);
}

/// <summary>Several commands that undo/redo as one step (multi-selection edits, Smart Move...).</summary>
public sealed class CompositeCommand(string name, IReadOnlyList<IUndoableCommand> commands) : IUndoableCommand
{
    public string Name => name;

    public void Do()
    {
        foreach (var command in commands) command.Do();
    }

    public void Undo()
    {
        for (var i = commands.Count - 1; i >= 0; i--) commands[i].Undo();
    }
}

/// <summary>Replaces several annotations by one <see cref="GroupAnnotation"/> placed where the top-most member was.</summary>
public sealed class GroupCommand : IUndoableCommand
{
    private readonly EditorDocument _document;
    private readonly List<(Annotation Member, int Index)> _members;

    public GroupCommand(EditorDocument document, IEnumerable<Annotation> members)
    {
        _document = document;
        _members = members.Select(m => (m, document.Annotations.ToList().IndexOf(m))).Where(t => t.Item2 >= 0).OrderBy(t => t.Item2).ToList();
        if (_members.Count < 2) throw new ArgumentException("A group needs at least two annotations.", nameof(members));
        Group = new GroupAnnotation { Children = _members.Select(t => t.Member).ToList() };
    }

    public GroupAnnotation Group { get; }

    public string Name => "Group";

    public void Do()
    {
        foreach (var (member, _) in _members) _document.RemoveAnnotation(member);
        // After removing the members, everything that was below the top-most member sits at 0..(top - members + 1).
        _document.InsertAnnotation(Group, _members[^1].Index - (_members.Count - 1));
    }

    public void Undo()
    {
        _document.RemoveAnnotation(Group);
        foreach (var (member, index) in _members) _document.InsertAnnotation(member, index); // ascending: restores the old order
    }
}

/// <summary>Dissolves a group back into its children (same z-position).</summary>
public sealed class UngroupCommand(EditorDocument document, GroupAnnotation group) : IUndoableCommand
{
    private int _index;

    public string Name => "Ungroup";

    public void Do()
    {
        _index = document.RemoveAnnotation(group);
        for (var i = 0; i < group.Children.Count; i++) document.InsertAnnotation(group.Children[i], _index + i);
    }

    public void Undo()
    {
        foreach (var child in group.Children) document.RemoveAnnotation(child);
        document.InsertAnnotation(group, _index);
    }
}

/// <summary>Moves annotations in the z-order. <paramref name="steps"/> &gt; 0 = towards the front; use int.MaxValue / int.MinValue for front/back.</summary>
public sealed class ReorderCommand : IUndoableCommand
{
    private readonly EditorDocument _document;
    private readonly IReadOnlyList<Annotation> _targets;
    private readonly int _steps;
    private List<Annotation>? _before;

    public ReorderCommand(EditorDocument document, IEnumerable<Annotation> targets, int steps)
    {
        _document = document;
        _targets = targets.ToList();
        _steps = steps;
    }

    public string Name => "Arrange";

    public void Do()
    {
        _before = _document.Annotations.ToList();
        _document.ReorderAnnotations(_targets, _steps);
    }

    public void Undo()
    {
        if (_before is not null) _document.SetAnnotationOrder(_before);
    }
}

/// <summary>
/// Replaces the base image. Either shifts annotations by <c>-shift</c> (crop, canvas resize) or, with
/// <c>flatten</c>, expects the new image to already contain the annotations and clears them (rotate, effects, cut-out).
/// </summary>
public sealed class ReplaceImageCommand : IUndoableCommand
{
    private readonly EditorDocument _document;
    private readonly Bitmap _before;
    private readonly Bitmap _after;
    private readonly Point _shift;
    private readonly bool _flatten;
    private List<Annotation>? _removed;

    public ReplaceImageCommand(EditorDocument document, Bitmap newImage, string name, Point shift = default, bool flatten = false)
    {
        _document = document;
        _before = document.Image;
        _after = newImage;
        _shift = shift;
        _flatten = flatten;
        Name = name;
    }

    public string Name { get; }

    public void Do()
    {
        _document.ReplaceImage(_after);
        if (_flatten) _removed = _document.RemoveAllAnnotations();
        else _document.TranslateAll(-_shift.X, -_shift.Y);
    }

    public void Undo()
    {
        _document.ReplaceImage(_before);
        if (_flatten) _document.RestoreAnnotations(_removed ?? []);
        else _document.TranslateAll(_shift.X, _shift.Y);
    }
}

/// <summary>Crops the base image; annotations are shifted so they stay attached to the same pixels.</summary>
public sealed class CropCommand : IUndoableCommand
{
    private readonly ReplaceImageCommand _inner;

    public CropCommand(EditorDocument document, Rectangle area)
    {
        area.Intersect(new Rectangle(Point.Empty, document.Image.Size));
        if (area.Width < 1 || area.Height < 1)
            throw new ArgumentException("Crop area is outside the image.", nameof(area));

        var cropped = document.Image.Clone(area, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        _inner = new ReplaceImageCommand(document, cropped, "Crop", area.Location);
    }

    public string Name => "Crop";

    public void Do() => _inner.Do();

    public void Undo() => _inner.Undo();
}
