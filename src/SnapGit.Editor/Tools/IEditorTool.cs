namespace SnapGit.Editor.Tools;

/// <summary>Mouse event in IMAGE coordinates (the canvas already converted from screen/view space).</summary>
public sealed record ToolMouseEventArgs(PointF Location, MouseButtons Button, Keys Modifiers, int Clicks = 1)
{
    public bool Shift => Modifiers.HasFlag(Keys.Shift);
    public bool Ctrl => Modifiers.HasFlag(Keys.Control);
}

public interface IEditorTool
{
    ToolKind Kind { get; }
    Cursor Cursor { get; }

    void OnActivated(EditorContext context);
    void OnDeactivated(EditorContext context);

    void MouseDown(EditorContext context, ToolMouseEventArgs e);
    void MouseMove(EditorContext context, ToolMouseEventArgs e);
    void MouseUp(EditorContext context, ToolMouseEventArgs e);

    /// <summary>
    /// <paramref name="keyData"/> = key code | modifiers (as in <c>KeyEventArgs.KeyData</c>).
    /// Returns true when the key was consumed (e.g. Enter/Esc in the crop tool).
    /// </summary>
    bool KeyDown(EditorContext context, Keys keyData);

    /// <summary>Draws transient feedback (preview shape, crop frame) in image coordinates.</summary>
    void DrawOverlay(Graphics g, EditorContext context);
}

/// <summary>No-op implementation so concrete tools only override what they need.</summary>
public abstract class EditorTool : IEditorTool
{
    public abstract ToolKind Kind { get; }
    public virtual Cursor Cursor => Cursors.Cross;

    public virtual void OnActivated(EditorContext context) { }
    public virtual void OnDeactivated(EditorContext context) { }
    public virtual void MouseDown(EditorContext context, ToolMouseEventArgs e) { }
    public virtual void MouseMove(EditorContext context, ToolMouseEventArgs e) { }
    public virtual void MouseUp(EditorContext context, ToolMouseEventArgs e) { }
    public virtual bool KeyDown(EditorContext context, Keys keyData) => false;
    public virtual void DrawOverlay(Graphics g, EditorContext context) { }
}
