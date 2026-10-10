using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor;

/// <summary>
/// The model being edited: a base screenshot plus an ordered list of annotations (index 0 = bottom).
/// All user-visible mutations go through <see cref="History"/> so they can be undone.
/// </summary>
public sealed class EditorDocument : IDisposable
{
    private readonly List<Annotation> _annotations = new();
    private int _savedVersion;
    private int _version;

    public EditorDocument(Bitmap image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        History.Changed += (_, _) =>
        {
            _version++;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public Bitmap Image { get; private set; }

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public CommandHistory History { get; } = new();

    /// <summary>Raised whenever the document content changed (including undo/redo).</summary>
    public event EventHandler? Changed;

    /// <summary>True when there are changes since construction or the last <see cref="MarkSaved"/>.</summary>
    public bool IsModified => _version != _savedVersion;

    public void MarkSaved() => _savedVersion = _version;

    /// <summary>Tools call this during live (not yet committed) edits so views repaint.</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Loads annotations without recording history (used when opening a project file).</summary>
    public void LoadAnnotations(IEnumerable<Annotation> annotations)
    {
        _annotations.Clear();
        _annotations.AddRange(annotations);
    }

    /// <summary>Returns the index it was inserted at.</summary>
    internal int InsertAnnotation(Annotation annotation, int index)
    {
        if (index < 0 || index > _annotations.Count) index = _annotations.Count;
        _annotations.Insert(index, annotation);
        return index;
    }

    /// <summary>Returns the index it was removed from (-1 if it was not in the document).</summary>
    internal int RemoveAnnotation(Annotation annotation)
    {
        var index = _annotations.IndexOf(annotation);
        if (index >= 0) _annotations.RemoveAt(index);
        return index;
    }

    internal List<Annotation> RemoveAllAnnotations()
    {
        var removed = _annotations.ToList();
        _annotations.Clear();
        return removed;
    }

    internal void RestoreAnnotations(IEnumerable<Annotation> annotations)
    {
        _annotations.Clear();
        _annotations.AddRange(annotations);
    }

    internal void SetAnnotationOrder(IReadOnlyList<Annotation> order)
    {
        _annotations.Clear();
        _annotations.AddRange(order);
    }

    /// <summary>Moves the targets by <paramref name="steps"/> positions towards the front (positive) or back (negative), keeping their relative order.</summary>
    internal void ReorderAnnotations(IReadOnlyList<Annotation> targets, int steps)
    {
        var moving = _annotations.Where(targets.Contains).ToList(); // preserve current relative order
        if (moving.Count == 0 || steps == 0) return;

        var rest = _annotations.Where(a => !moving.Contains(a)).ToList();
        // Index (within `rest`) just after which the block currently sits.
        var anchor = _annotations.FindIndex(moving.Contains); // everything before the first mover is a non-mover

        int target;
        if (steps == int.MaxValue) target = rest.Count;
        else if (steps == int.MinValue) target = 0;
        else target = Math.Clamp(anchor + steps, 0, rest.Count);

        rest.InsertRange(target, moving);
        _annotations.Clear();
        _annotations.AddRange(rest);
    }

    internal void ReplaceImage(Bitmap image) => Image = image;

    internal void TranslateAll(float dx, float dy)
    {
        foreach (var annotation in _annotations) annotation.Translate(dx, dy);
    }

    /// <summary>Annotation under <paramref name="point"/> (top-most first), or null.</summary>
    public Annotation? HitTest(PointF point, float tolerance)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
            if (_annotations[i].HitTest(point, tolerance))
                return _annotations[i];
        return null;
    }

    /// <summary>
    /// Runs an image operation (rotate, effect, cut-out, canvas resize...) as one undoable step.
    /// With <paramref name="flatten"/> the operation receives the rendered image (base + annotations) and the
    /// annotations are consumed; otherwise it receives only the base image and annotations are shifted by <paramref name="shift"/>.
    /// The operation must return a NEW bitmap and must not dispose its input.
    /// </summary>
    public void ApplyImageOperation(string name, Func<Bitmap, Bitmap> operation, bool flatten, Point shift = default)
    {
        using var rendered = flatten ? Render() : null;
        var result = operation(rendered ?? Image);
        History.Execute(new ReplaceImageCommand(this, result, name, shift, flatten));
    }

    /// <summary>Colour of the flattened result (base + annotations) at an image point, or null when outside the image.</summary>
    public Color? PixelAt(PointF point)
    {
        int x = (int)Math.Floor(point.X), y = (int)Math.Floor(point.Y);
        if (x < 0 || y < 0 || x >= Image.Width || y >= Image.Height) return null;

        using var rendered = Render();
        return rendered.GetPixel(x, y);
    }

    /// <summary>Draws image + annotations onto <paramref name="g"/> in image coordinates.</summary>
    public void Paint(Graphics g)
    {
        var context = new RenderContext(Image);
        foreach (var annotation in _annotations)
            annotation.Draw(g, context);
    }

    /// <summary>Flattens everything into a new bitmap (caller owns it).</summary>
    public Bitmap Render()
    {
        var result = new Bitmap(Image.Width, Image.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        // Keep default (linear) compositing: HighQuality would gamma-correct blends and make the exported
        // image differ from what the canvas shows.
        g.DrawImageUnscaled(Image, 0, 0);
        Paint(g);
        return result;
    }

    public void Dispose() => Image.Dispose();
}
