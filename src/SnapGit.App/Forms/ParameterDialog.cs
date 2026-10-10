using SnapGit.App.Ui;
using SnapGit.Core.Localization;

namespace SnapGit.App.Forms;

/// <summary>
/// A small data-driven dialog (number/text/combo/colour/checkbox fields) so image operations don't each need
/// their own form. Build it with the Add* methods, show it, then read values by key.
/// </summary>
public sealed class ParameterDialog : Form
{
    private readonly TableLayoutPanel _table;
    private readonly Dictionary<string, Control> _fields = new();
    private readonly Dictionary<string, Color> _colors = new();

    public ParameterDialog(string title)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
            Dock = DockStyle.Fill,
        };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(_table);
    }

    public ParameterDialog AddLabel(string text)
    {
        var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, 8) };
        _table.Controls.Add(label);
        _table.SetColumnSpan(label, 2);
        return this;
    }

    public ParameterDialog AddNumber(string key, string label, decimal value, decimal min, decimal max, int decimals = 0)
    {
        var box = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = Math.Clamp(value, min, max), Width = 110 };
        return Add(key, label, box);
    }

    public ParameterDialog AddText(string key, string label, string value, int width = 220)
    {
        return Add(key, label, new TextBox { Text = value, Width = width });
    }

    public ParameterDialog AddMultiline(string key, string label, string value, int height = 150)
    {
        var box = new TextBox { Text = value, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 360, Height = height };
        return Add(key, label, box);
    }

    public ParameterDialog AddCombo(string key, string label, IEnumerable<string> items, int selected = 0)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        combo.Items.AddRange(items.Cast<object>().ToArray());
        if (combo.Items.Count > 0) combo.SelectedIndex = Math.Clamp(selected, 0, combo.Items.Count - 1);
        return Add(key, label, combo);
    }

    public ParameterDialog AddCheck(string key, string label, bool value)
    {
        var check = new CheckBox { Text = label, Checked = value, AutoSize = true };
        _fields[key] = check;
        _table.Controls.Add(check);
        _table.SetColumnSpan(check, 2);
        return this;
    }

    public ParameterDialog AddColor(string key, string label, Color value)
    {
        _colors[key] = value;
        var button = new Button { Width = 110, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = value, Tag = Theme.SwatchTag };
        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _colors[key], FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _colors[key] = dialog.Color;
            button.BackColor = dialog.Color;
        };
        return Add(key, label, button);
    }

    private ParameterDialog Add(string key, string label, Control control)
    {
        _fields[key] = control;
        _table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
        _table.Controls.Add(control);
        return this;
    }

    public int Int(string key) => (int)((NumericUpDown)_fields[key]).Value;

    public double Double(string key) => (double)((NumericUpDown)_fields[key]).Value;

    public string String(string key) => _fields[key].Text;

    public int Index(string key) => ((ComboBox)_fields[key]).SelectedIndex;

    public bool Bool(string key) => ((CheckBox)_fields[key]).Checked;

    public Color ColorValue(string key) => _colors[key];

    /// <summary>Adds OK/Cancel and shows the dialog; true when the user confirmed.</summary>
    public bool Prompt(IWin32Window? owner)
    {
        var ok = new Button { Text = Loc.T("OK"), DialogResult = DialogResult.OK, Width = 96, Height = 32 };
        var cancel = new Button { Text = Loc.T("Cancel"), DialogResult = DialogResult.Cancel, Width = 96, Height = 32 };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        _table.Controls.Add(buttons);
        _table.SetColumnSpan(buttons, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Theme.Apply(this);
        Theme.StylePrimary(ok);
        return ShowDialog(owner) == DialogResult.OK;
    }
}
