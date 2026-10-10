using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;
using SnapGit.Editor.Persistence;
using SnapGit.Editor.Tools;

namespace SnapGit.Editor;

public enum AlignMode
{
    Left,
    Center,
    Right,
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// Mutable editing session shared by the canvas, the tools and the toolbar:
/// active tool, per-tool style, selection (multi), and view-related requests. Holds no WinForms controls.
/// </summary>
public sealed class EditorContext
{
    private readonly Dictionary<ToolKind, AnnotationStyle> _toolStyles = ToolDefaults.CreateStyles();
    private readonly Dictionary<ToolKind, IEditorTool> _tools = new();
    private readonly List<Annotation> _selected = new();

    public EditorContext(EditorDocument document)
    {
        Document = document;
        Tool = GetTool(ToolKind.Arrow);
        ActiveTool = ToolKind.Arrow;
        PreviousTool = ToolKind.Arrow;
    }

    public EditorDocument Document { get; }

    public RenderContext RenderContext => new(Document.Image);

    /// <summary>Current view zoom (1 = 100%). Tools use it to keep hit areas a constant size on screen.</summary>
    public float Zoom { get; set; } = 1f;

    public ToolKind ActiveTool { get; private set; }

    /// <summary>The tool that was active before the current one (eyedropper returns to it).</summary>
    public ToolKind PreviousTool { get; private set; }

    public IEditorTool Tool { get; private set; }

    /// <summary>Which stamp the Stamp tool places.</summary>
    public StampKind CurrentStamp { get; set; } = StampKind.Check;

    /// <summary>All selected annotations, bottom-most first.</summary>
    public IReadOnlyList<Annotation> Selected => _selected;

    /// <summary>The primary selection (the most recently selected); null when nothing is selected.</summary>
    public Annotation? Selection
    {
        get => _selected.Count > 0 ? _selected[^1] : null;
        set => SetSelection(value is null ? [] : [value]);
    }

    public bool IsSelected(Annotation annotation) => _selected.Contains(annotation);

    public void SetSelection(IEnumerable<Annotation> annotations)
    {
        var next = annotations.Distinct().ToList();
        if (next.SequenceEqual(_selected)) return;

        _selected.Clear();
        _selected.AddRange(next);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleSelection(Annotation annotation)
    {
        var next = _selected.ToList();
        if (!next.Remove(annotation)) next.Add(annotation);
        SetSelection(next);
    }

    public void SelectAll() => SetSelection(Document.Annotations);

    /// <summary>Style the active tool will use for new annotations (and that the toolbar edits).</summary>
    public AnnotationStyle CurrentStyle => Selection?.Style ?? _toolStyles[ActiveTool];

    public event EventHandler? SelectionChanged;
    public event EventHandler? ToolChanged;
    public event EventHandler? StyleChanged;

    /// <summary>The canvas should repaint.</summary>
    public event EventHandler? Invalidated;

    /// <summary>The canvas should show an in-place text editor for this annotation.</summary>
    public event EventHandler<TextEditRequest>? TextEditRequested;

    public void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);

    public void SetTool(ToolKind kind)
    {
        if (kind == ActiveTool) return;

        Tool.OnDeactivated(this);
        PreviousTool = ActiveTool;
        ActiveTool = kind;
        Tool = GetTool(kind);
        if (kind != ToolKind.Select) SetSelection([]);
        Tool.OnActivated(this);

        ToolChanged?.Invoke(this, EventArgs.Empty);
        StyleChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public AnnotationStyle GetToolStyle(ToolKind kind) => _toolStyles[kind];

    /// <summary>Takes over the user's style choices (per tool) and stamp from the editing session of the previous picture.</summary>
    public void CopyPreferencesFrom(EditorContext other)
    {
        foreach (var (kind, style) in other._toolStyles) _toolStyles[kind] = style;
        CurrentStamp = other.CurrentStamp;
    }

    /// <summary>Applies a style change to the active tool and to every selected annotation (one undo step).</summary>
    public void UpdateStyle(Func<AnnotationStyle, AnnotationStyle> change)
    {
        _toolStyles[ActiveTool] = change(_toolStyles[ActiveTool]);

        if (_selected.Count > 0)
        {
            var commands = new List<IUndoableCommand>();
            foreach (var annotation in _selected)
            {
                var before = annotation.Clone();
                annotation.Style = change(annotation.Style);
                commands.Add(new ModifyAnnotationCommand(annotation, before));
            }
            Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Style", commands));
        }

        StyleChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void DeleteSelection()
    {
        if (_selected.Count == 0) return;
        var commands = _selected.Select(a => (IUndoableCommand)new RemoveAnnotationCommand(Document, a)).ToList();
        SetSelection([]);
        Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Delete", commands));
    }

    /// <summary>Moves the selection towards the front (<paramref name="steps"/> &gt; 0) or back; int.MaxValue / MinValue = front / back.</summary>
    public void Arrange(int steps)
    {
        if (_selected.Count == 0) return;
        Document.History.Execute(new ReorderCommand(Document, _selected, steps));
    }

    /// <summary>Duplicates the selection slightly offset and selects the copies.</summary>
    public void DuplicateSelection(float offset = 16f)
    {
        if (_selected.Count == 0) return;

        var copies = new List<Annotation>();
        var commands = new List<IUndoableCommand>();
        foreach (var original in _selected)
        {
            var copy = original.Clone();
            copy.Id = Guid.NewGuid();
            copy.Translate(offset, offset);
            copies.Add(copy);
            commands.Add(new AddAnnotationCommand(Document, copy));
        }
        Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Duplicate", commands));
        SetSelection(copies);
    }

    /// <summary>Groups the selected annotations (2+) so they move/copy/delete together; the group becomes the selection.</summary>
    public bool GroupSelection()
    {
        if (_selected.Count < 2) return false;
        var command = new GroupCommand(Document, _selected);
        Document.History.Execute(command);
        SetSelection([command.Group]);
        return true;
    }

    /// <summary>Dissolves every selected group; the former members become the selection.</summary>
    public bool UngroupSelection()
    {
        var groups = _selected.OfType<GroupAnnotation>().ToList();
        if (groups.Count == 0) return false;

        var commands = groups.Select(g => (IUndoableCommand)new UngroupCommand(Document, g)).ToList();
        var freed = groups.SelectMany(g => g.Children).ToList();
        Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Ungroup", commands));
        SetSelection(freed);
        return true;
    }

    /// <summary>Aligns the selected annotations to their common edge/centre.</summary>
    public bool Align(AlignMode mode)
    {
        if (_selected.Count < 2) return false;

        var union = _selected.Select(a => a.Bounds).Aggregate(RectangleF.Union);
        return MoveEach(a =>
        {
            var b = a.Bounds;
            return mode switch
            {
                AlignMode.Left => (union.Left - b.Left, 0f),
                AlignMode.Center => (union.Left + union.Width / 2 - (b.Left + b.Width / 2), 0f),
                AlignMode.Right => (union.Right - b.Right, 0f),
                AlignMode.Top => (0f, union.Top - b.Top),
                AlignMode.Middle => (0f, union.Top + union.Height / 2 - (b.Top + b.Height / 2)),
                _ => (0f, union.Bottom - b.Bottom),
            };
        }, "Align");
    }

    /// <summary>Spreads three or more annotations so the gaps between them are equal (outermost two stay put).</summary>
    public bool Distribute(bool horizontal)
    {
        if (_selected.Count < 3) return false;

        var ordered = _selected.OrderBy(a => horizontal ? a.Bounds.Left : a.Bounds.Top).ToList();
        var first = ordered[0].Bounds;
        var last = ordered[^1].Bounds;
        var span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        var sizes = ordered.Sum(a => horizontal ? a.Bounds.Width : a.Bounds.Height);
        var gap = (span - sizes) / (ordered.Count - 1);

        var targets = new Dictionary<Annotation, float>();
        var cursor = horizontal ? first.Left : first.Top;
        foreach (var annotation in ordered)
        {
            targets[annotation] = cursor;
            cursor += (horizontal ? annotation.Bounds.Width : annotation.Bounds.Height) + gap;
        }

        return MoveEach(a => horizontal ? (targets[a] - a.Bounds.Left, 0f) : (0f, targets[a] - a.Bounds.Top), "Distribute");
    }

    private bool MoveEach(Func<Annotation, (float Dx, float Dy)> offset, string name)
    {
        var commands = new List<IUndoableCommand>();
        foreach (var annotation in _selected)
        {
            var (dx, dy) = offset(annotation);
            if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) continue;

            var before = annotation.Clone();
            annotation.Translate(dx, dy);
            commands.Add(new ModifyAnnotationCommand(annotation, before));
        }

        if (commands.Count == 0) return false;
        Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(name, commands));
        return true;
    }

    /// <summary>The selection as JSON (for the clipboard); null when nothing is selected.</summary>
    public string? SerializeSelection() =>
        _selected.Count == 0 ? null : AnnotationJson.Serialize(Document.Annotations.Where(_selected.Contains));

    /// <summary>Adds copies of previously serialised annotations (new ids, nudged by <paramref name="offset"/>) and selects them.</summary>
    public IReadOnlyList<Annotation> PasteSerialized(string json, float offset = 16f)
    {
        var pasted = AnnotationJson.Deserialize(json);
        if (pasted.Count == 0) return pasted;

        var commands = new List<IUndoableCommand>();
        foreach (var annotation in pasted)
        {
            RenewIds(annotation);
            annotation.Translate(offset, offset);
            commands.Add(new AddAnnotationCommand(Document, annotation));
        }
        Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Paste", commands));
        SetSelection(pasted);
        return pasted;
    }

    private static void RenewIds(Annotation annotation)
    {
        annotation.Id = Guid.NewGuid();
        if (annotation is GroupAnnotation group)
            foreach (var child in group.Children) RenewIds(child);
    }

    /// <summary>Moves the selection by a few pixels (arrow keys).</summary>
    public void Nudge(float dx, float dy)
    {
        if (_selected.Count == 0) return;

        var commands = new List<IUndoableCommand>();
        foreach (var annotation in _selected)
        {
            var before = annotation.Clone();
            annotation.Translate(dx, dy);
            commands.Add(new ModifyAnnotationCommand(annotation, before));
        }
        Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Nudge", commands));
    }

    /// <summary>Adds a floating image layer (paste / drop) centred in the image, and selects it.</summary>
    public ImageLayerAnnotation AddImageLayer(Bitmap image, PointF? center = null)
    {
        var target = center ?? new PointF(Document.Image.Width / 2f, Document.Image.Height / 2f);

        // Never larger than the canvas.
        var scale = Math.Min(1f, Math.Min(Document.Image.Width / (float)image.Width, Document.Image.Height / (float)image.Height));
        var size = new SizeF(image.Width * scale, image.Height * scale);

        var layer = new ImageLayerAnnotation
        {
            Image = new Bitmap(image),
            Rect = new RectangleF(target.X - size.Width / 2, target.Y - size.Height / 2, size.Width, size.Height),
        };
        Document.History.Execute(new AddAnnotationCommand(Document, layer));
        Selection = layer;
        return layer;
    }

    /// <summary>Next free step number (max existing + 1).</summary>
    public int NextStepNumber() =>
        Document.Annotations.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;

    public void RequestTextEdit(TextAnnotation annotation, bool isNew) =>
        TextEditRequested?.Invoke(this, new TextEditRequest(annotation, isNew));

    /// <summary>Completes an in-place text edit started by <see cref="RequestTextEdit"/>.</summary>
    public void CommitText(TextEditRequest request, string text, Graphics measure)
    {
        var annotation = request.Annotation;

        if (request.IsNew)
        {
            if (string.IsNullOrWhiteSpace(text)) return; // nothing typed: discard
            annotation.Text = text;
            annotation.FitHeight(measure);
            Document.History.Execute(new AddAnnotationCommand(Document, annotation));
            Selection = annotation;
            return;
        }

        var before = annotation.Clone();
        if (string.IsNullOrWhiteSpace(text))
        {
            Selection = null;
            Document.History.Execute(new RemoveAnnotationCommand(Document, annotation));
            return;
        }

        if (text == annotation.Text) return;
        annotation.Text = text;
        annotation.FitHeight(measure);
        Document.History.Execute(new ModifyAnnotationCommand(annotation, before));
    }

    private IEditorTool GetTool(ToolKind kind)
    {
        if (!_tools.TryGetValue(kind, out var tool))
            _tools[kind] = tool = ToolFactory.Create(kind);
        return tool;
    }
}

public sealed record TextEditRequest(TextAnnotation Annotation, bool IsNew);
