using System.Drawing.Drawing2D;
using SnapGit.App.Ui;

namespace SnapGit.App.Controls;

public sealed record NavItem(UiIcon Icon, string Text);

/// <summary>
/// Vertical navigation list (16 px colour icon + label, 30 px rows) for the Settings dialog. The selected entry has an accent
/// outline on a soft tint; arrow keys move the selection; screen readers see a list of items.
/// </summary>
public sealed class SideNav : Control
{
    private const int ItemHeight = 30, Pad = 8, IconSize = 16;

    private readonly List<NavItem> _items = [];
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedIndexChanged;

    public SideNav()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    public void Add(NavItem item)
    {
        _items.Add(item);
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1));
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal int Count => _items.Count;

    private float K => DeviceDpi / 96f;

    private int S(int value) => (int)Math.Round(value * K);

    private Rectangle ItemRect(int index) => new(S(4), S(4) + index * S(ItemHeight + 1), Width - S(8) - 1, S(ItemHeight));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var palette = Theme.Colors;
        g.Clear(palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (var i = 0; i < _items.Count; i++)
        {
            var rect = ItemRect(i);
            var selected = i == _selected;

            if (selected || i == _hover || (Focused && selected))
            {
                using var path = ModernRenderer.RoundedRectangle(rect, S(3));
                if (selected) { using var tint = new SolidBrush(palette.Selected); g.FillPath(tint, path); }
                else { using var hover = new SolidBrush(palette.Hover); g.FillPath(hover, path); }
                using var pen = new Pen(selected ? palette.Accent : Color.FromArgb(90, palette.Accent), 1f);
                g.DrawPath(pen, path);
            }

            var size = S(IconSize);
            g.DrawImage(Icons.Get(_items[i].Icon, null, size), rect.X + S(Pad), rect.Y + (rect.Height - size) / 2, size, size);
            TextRenderer.DrawText(g, _items[i].Text, Font, new Rectangle(rect.X + S(Pad) + size + S(8), rect.Y, rect.Width - S(Pad) - size - S(12), rect.Height),
                palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        using var border = new Pen(palette.Border);
        g.DrawLine(border, Width - 1, 0, Width - 1, Height);
    }

    private int IndexAt(Point p)
    {
        for (var i = 0; i < _items.Count; i++)
            if (ItemRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        if (index == _hover) return;
        _hover = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var index = IndexAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0) SelectedIndex = index;
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up: SelectedIndex = _selected - 1; e.Handled = true; break;
            case Keys.Down: SelectedIndex = _selected + 1; e.Handled = true; break;
            case Keys.Home: SelectedIndex = 0; e.Handled = true; break;
            case Keys.End: SelectedIndex = _items.Count - 1; e.Handled = true; break;
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override AccessibleObject CreateAccessibilityInstance() => new NavAccessibleObject(this);

    private sealed class NavAccessibleObject(SideNav owner) : ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner._items.Count;

        public override AccessibleObject GetChild(int index) =>
            index >= 0 && index < owner._items.Count ? new ItemAccessibleObject(owner, index) : base.GetChild(index)!;
    }

    private sealed class ItemAccessibleObject(SideNav owner, int index) : AccessibleObject
    {
        public override string? Name { get => owner._items[index].Text; set { } }

        public override AccessibleRole Role => AccessibleRole.ListItem;

        public override AccessibleObject Parent => owner.AccessibilityObject;

        public override Rectangle Bounds => owner.RectangleToScreen(owner.ItemRect(index));

        public override AccessibleStates State =>
            AccessibleStates.Focusable | AccessibleStates.Selectable | (index == owner._selected ? AccessibleStates.Selected | AccessibleStates.Focused : AccessibleStates.None);

        public override void DoDefaultAction() => owner.SelectedIndex = index;
    }
}
