using System.Drawing.Drawing2D;
using SnapGit.App.Ui;
using SnapGit.Core.Localization;

namespace SnapGit.App.Controls;

/// <summary>One tool in the palette.</summary>
public sealed record PaletteTool(string Id, UiIcon Icon, string Name, string Shortcut);

/// <summary>
/// The editor's tool box, in the compact classic form: ONE column of 24 px buttons with 16 px colour icons (a 26 px wide strip),
/// hairline dividers between groups. The selected tool has a 1 px accent outline on a soft tint, the hovered one a lighter outline.
/// When the window is too short for one column it switches to two by itself. Tooltips name the tool and its key; arrow keys move
/// between tools (Enter/Space picks); every button is exposed to screen readers.
/// </summary>
public sealed class ToolPalette : Control
{
    private const int Button = 24, Pad = 1, GroupGap = 7, TopPad = 3, IconSize = 16;

    private readonly List<PaletteTool> _tools = [];
    private readonly List<int> _groupStarts = [];
    private readonly ToolTip _tip = new() { InitialDelay = 350, ReshowDelay = 100 };
    private string _selected = string.Empty;
    private int _hover = -1;
    private int _focus;
    private int _columns = 1;

    public event Action<string>? ToolChosen;

    public ToolPalette()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Dock = DockStyle.Left;
        AccessibleName = Loc.T("Tools");
        AccessibleRole = AccessibleRole.ToolBar;
    }

    internal IReadOnlyList<PaletteTool> Tools => _tools;

    internal int Columns => _columns;

    public string Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            var index = _tools.FindIndex(t => t.Id == value);
            if (index >= 0) _focus = index;
            Invalidate();
        }
    }

    /// <summary>Adds a group; groups are separated by a divider and start on a new row.</summary>
    public void AddGroup(IEnumerable<PaletteTool> tools)
    {
        _groupStarts.Add(_tools.Count);
        _tools.AddRange(tools);
        FitColumns();
        Invalidate();
    }

    private float K => DeviceDpi / 96f;

    private int S(int value) => (int)Math.Round(value * K);

    private int PreferredWidth => S(Pad) * 2 + _columns * S(Button) + 1;

    /// <summary>Height one column of buttons needs.</summary>
    private int HeightFor(int columns)
    {
        var rows = 0;
        for (var g = 0; g < _groupStarts.Count; g++)
        {
            var count = (g + 1 < _groupStarts.Count ? _groupStarts[g + 1] : _tools.Count) - _groupStarts[g];
            rows += (count + columns - 1) / columns;
        }
        return S(TopPad) * 2 + rows * S(Button) + Math.Max(0, _groupStarts.Count - 1) * S(GroupGap);
    }

    /// <summary>One column when everything fits, two when the window is too short.</summary>
    private void FitColumns()
    {
        var columns = Height > 0 && HeightFor(1) > Height ? 2 : 1;
        _columns = columns;
        var width = PreferredWidth;
        if (Width != width) Width = width;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var before = _columns;
        FitColumns();
        if (before != _columns) Invalidate();
    }

    private Rectangle ButtonRect(int index)
    {
        var group = _groupStarts.FindLastIndex(start => start <= index);
        var row = 0;
        for (var g = 0; g < group; g++)
        {
            var count = (g + 1 < _groupStarts.Count ? _groupStarts[g + 1] : _tools.Count) - _groupStarts[g];
            row += (count + _columns - 1) / _columns;
        }
        var within = index - _groupStarts[group];
        row += within / _columns;
        var column = within % _columns;

        return new Rectangle(S(Pad) + column * S(Button), S(TopPad) + row * S(Button) + group * S(GroupGap), S(Button), S(Button));
    }

    private int IndexAt(Point point)
    {
        for (var i = 0; i < _tools.Count; i++)
            if (ButtonRect(i).Contains(point)) return i;
        return -1;
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var palette = Theme.Colors;
        g.Clear(palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var border = new Pen(palette.Border)) g.DrawLine(border, Width - 1, 0, Width - 1, Height);

        // hairline dividers between groups
        using (var divider = new Pen(palette.Border))
            for (var group = 1; group < _groupStarts.Count; group++)
            {
                var first = ButtonRect(_groupStarts[group]);
                var y = first.Top - S(GroupGap) / 2;
                g.DrawLine(divider, S(Pad) + S(3), y, Width - S(Pad) - S(4), y);
            }

        for (var i = 0; i < _tools.Count; i++)
        {
            var rect = ButtonRect(i);
            var isSelected = _tools[i].Id == _selected;

            if (isSelected || i == _hover || (Focused && i == _focus))
            {
                using var path = ModernRenderer.RoundedRectangle(rect, S(3));
                if (isSelected) { using var tint = new SolidBrush(palette.Selected); g.FillPath(tint, path); }
                if (i == _hover) { using var hover = new SolidBrush(palette.Hover); g.FillPath(hover, path); }
                var outline = isSelected ? palette.Accent : Color.FromArgb(Focused && i == _focus ? 255 : 110, palette.Accent);
                using var pen = new Pen(outline, 1f);
                g.DrawPath(pen, path);
            }

            // exactly the pixels of the slot: a 1 px outline of the icon is one crisp pixel row
            var size = S(IconSize);
            g.DrawImage(Icons.Get(_tools[i].Icon, null, size), rect.X + (rect.Width - size) / 2, rect.Y + (rect.Height - size) / 2, size, size);
        }
    }

    // ------------------------------------------------------------------ input

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        if (index == _hover) return;
        _hover = index;
        _tip.SetToolTip(this, index < 0 ? string.Empty : $"{_tools[index].Name}  ({_tools[index].Shortcut})");
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == -1) return;
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        var index = IndexAt(e.Location);
        if (index >= 0) Choose(index);
    }

    private void Choose(int index)
    {
        _focus = index;
        ToolChosen?.Invoke(_tools[index].Id);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_tools.Count == 0) return;

        var next = e.KeyCode switch
        {
            Keys.Left => _focus - 1,
            Keys.Right => _focus + 1,
            Keys.Up => _focus - _columns,
            Keys.Down => _focus + _columns,
            Keys.Home => 0,
            Keys.End => _tools.Count - 1,
            _ => -1,
        };

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Choose(_focus);
            e.Handled = true;
        }
        else if (next >= 0)
        {
            _focus = Math.Clamp(next, 0, _tools.Count - 1);
            e.Handled = true;
            Invalidate();
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    // ------------------------------------------------------------------ accessibility

    protected override AccessibleObject CreateAccessibilityInstance() => new PaletteAccessibleObject(this);

    private sealed class PaletteAccessibleObject(ToolPalette owner) : ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner._tools.Count;

        public override AccessibleObject GetChild(int index) =>
            index >= 0 && index < owner._tools.Count ? new ToolAccessibleObject(owner, index) : base.GetChild(index)!;
    }

    private sealed class ToolAccessibleObject(ToolPalette owner, int index) : AccessibleObject
    {
        private PaletteTool Tool => owner._tools[index];

        public override string? Name { get => Tool.Name; set { } }

        public override string KeyboardShortcut => Tool.Shortcut;

        public override AccessibleRole Role => AccessibleRole.RadioButton;

        public override AccessibleObject Parent => owner.AccessibilityObject;

        public override Rectangle Bounds => owner.RectangleToScreen(owner.ButtonRect(index));

        public override AccessibleStates State =>
            AccessibleStates.Focusable | AccessibleStates.Selectable | (Tool.Id == owner._selected ? AccessibleStates.Checked | AccessibleStates.Selected : AccessibleStates.None);

        public override string DefaultAction => Loc.T("Select");

        public override void DoDefaultAction() => owner.Choose(index);
    }
}
