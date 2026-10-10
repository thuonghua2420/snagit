using System.Drawing.Drawing2D;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Commands;

namespace SnapGit.Editor.Tools;

/// <summary>
/// Click to select (Ctrl/Shift = add/remove), drag empty space to marquee-select, drag to move, drag a handle to resize
/// a single selection, double-click text to edit, arrow keys nudge.
/// </summary>
public sealed class SelectTool : EditorTool
{
    private enum Mode { None, Move, Handle, Marquee }

    private const float HandleReachPx = 8f;
    private const float HitTolerancePx = 5f;

    private Mode _mode;
    private List<(Annotation Target, Annotation Before)> _drag = new();
    private int _handleIndex = -1;
    private PointF _last;
    private PointF _marqueeStart;
    private RectangleF _marquee;
    private List<Annotation> _marqueeBase = new();
    private bool _moved;

    public override ToolKind Kind => ToolKind.Select;

    public override Cursor Cursor => Cursors.Default;

    public override void MouseDown(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var zoom = Math.Max(context.Zoom, 0.01f);
        _last = e.Location;
        _moved = false;
        _handleIndex = -1;
        _mode = Mode.None;
        _drag = new();

        if (e.Clicks >= 2 && context.Document.HitTest(e.Location, HitTolerancePx / zoom) is TextAnnotation text)
        {
            context.Selection = text;
            context.RequestTextEdit(text, isNew: false);
            return;
        }

        // 1. Handle of a single selection?
        if (context.Selected.Count == 1)
        {
            var selected = context.Selected[0];
            var handles = selected.HandlePoints;
            for (var i = 0; i < handles.Count; i++)
            {
                if (Geometry.Distance(handles[i], e.Location) <= HandleReachPx / zoom)
                {
                    _mode = Mode.Handle;
                    _handleIndex = i;
                    _drag = [(selected, selected.Clone())];
                    return;
                }
            }
        }

        // 2. Any annotation under the pointer?
        var hit = context.Document.HitTest(e.Location, HitTolerancePx / zoom);
        var additive = e.Ctrl || e.Shift;

        if (hit is not null)
        {
            if (additive)
            {
                context.ToggleSelection(hit);
                if (!context.IsSelected(hit)) return; // removed from the selection: nothing to drag
            }
            else if (!context.IsSelected(hit))
            {
                context.Selection = hit;
            }

            _mode = Mode.Move;
            _drag = context.Selected.Select(a => (a, a.Clone())).ToList();
            return;
        }

        // 3. Empty space: start a marquee.
        _marqueeBase = additive ? context.Selected.ToList() : new List<Annotation>();
        if (!additive) context.SetSelection([]);
        _mode = Mode.Marquee;
        _marqueeStart = e.Location;
        _marquee = new RectangleF(e.Location, SizeF.Empty);
    }

    public override void MouseMove(EditorContext context, ToolMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        switch (_mode)
        {
            case Mode.Handle:
                _drag[0].Target.MoveHandle(_handleIndex, e.Location);
                break;

            case Mode.Move:
                var dx = e.Location.X - _last.X;
                var dy = e.Location.Y - _last.Y;
                foreach (var (target, _) in _drag) target.Translate(dx, dy);
                break;

            case Mode.Marquee:
                _marquee = Geometry.FromPoints(_marqueeStart, e.Location);
                var inside = context.Document.Annotations.Where(a => a.Bounds.IntersectsWith(_marquee));
                context.SetSelection(_marqueeBase.Concat(inside));
                context.Invalidate();
                return;

            default:
                return;
        }

        _last = e.Location;
        _moved = true;
        context.Document.NotifyChanged();
    }

    public override void MouseUp(EditorContext context, ToolMouseEventArgs e)
    {
        if (_mode is Mode.Move or Mode.Handle && _moved)
        {
            var commands = _drag.Select(d => (IUndoableCommand)new ModifyAnnotationCommand(d.Target, d.Before)).ToList();
            context.Document.History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Move", commands));
        }

        if (_mode == Mode.Marquee) context.Invalidate();

        _mode = Mode.None;
        _drag = new();
        _handleIndex = -1;
        _marquee = RectangleF.Empty;
    }

    public override bool KeyDown(EditorContext context, Keys keyData)
    {
        if (context.Selected.Count == 0) return false;

        var step = keyData.HasFlag(Keys.Shift) ? 10f : 1f;
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Delete:
                context.DeleteSelection();
                return true;
            case Keys.Escape:
                context.SetSelection([]);
                return true;
            case Keys.Left: context.Nudge(-step, 0); return true;
            case Keys.Right: context.Nudge(step, 0); return true;
            case Keys.Up: context.Nudge(0, -step); return true;
            case Keys.Down: context.Nudge(0, step); return true;
            default: return false;
        }
    }

    public override void DrawOverlay(Graphics g, EditorContext context)
    {
        if (_mode != Mode.Marquee || _marquee.Width < 1 || _marquee.Height < 1) return;

        using var fill = new SolidBrush(Color.FromArgb(40, 0, 120, 215));
        using var pen = new Pen(Color.FromArgb(200, 0, 120, 215), 1f / Math.Max(context.Zoom, 0.01f)) { DashStyle = DashStyle.Dash };
        g.FillRectangle(fill, _marquee);
        g.DrawRectangle(pen, _marquee.X, _marquee.Y, _marquee.Width, _marquee.Height);
    }
}
