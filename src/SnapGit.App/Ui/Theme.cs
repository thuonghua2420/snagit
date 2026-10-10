using System.Runtime.InteropServices;
using Microsoft.Win32;
using SnapGit.Core.Settings;

namespace SnapGit.App.Ui;

/// <summary>The colours of one theme. Everything that is drawn by hand (toolbars, palette, strips) takes its colours from here.</summary>
public sealed record Palette(
    Color Window,      // form background
    Color Surface,     // toolbars, panels, menus
    Color Canvas,      // the area around the picture being edited
    Color Border,
    Color Text,
    Color Muted,       // secondary text, disabled things
    Color Accent,
    Color OnAccent,     // text and icons on an accent-filled button
    Color Hover,       // translucent overlays, drawn over Surface
    Color Pressed,
    Color Selected,    // accent tint behind a checked / selected button
    Color Input);

/// <summary>Light/dark theming: palette, UI font, a modern toolbar/menu renderer, dark title bar.</summary>
public static class Theme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static readonly Palette Light = new(
        Window: Color.FromArgb(243, 243, 243), Surface: Color.FromArgb(255, 255, 255), Canvas: Color.FromArgb(222, 224, 228),
        Border: Color.FromArgb(225, 225, 225), Text: Color.FromArgb(28, 28, 28), Muted: Color.FromArgb(110, 110, 110),
        Accent: Color.FromArgb(0, 95, 184), OnAccent: Color.White, Hover: Color.FromArgb(14, 0, 0, 0), Pressed: Color.FromArgb(28, 0, 0, 0),
        Selected: Color.FromArgb(34, 0, 95, 184), Input: Color.White);

    public static readonly Palette Dark = new(
        Window: Color.FromArgb(32, 32, 32), Surface: Color.FromArgb(43, 43, 43), Canvas: Color.FromArgb(30, 30, 33),
        Border: Color.FromArgb(62, 62, 62), Text: Color.FromArgb(240, 240, 240), Muted: Color.FromArgb(160, 160, 160),
        Accent: Color.FromArgb(96, 205, 255), OnAccent: Color.FromArgb(10, 10, 10), Hover: Color.FromArgb(22, 255, 255, 255), Pressed: Color.FromArgb(38, 255, 255, 255),
        Selected: Color.FromArgb(52, 96, 205, 255), Input: Color.FromArgb(55, 55, 55));

    private static readonly ModernRenderer Renderer = new();

    public static bool IsDark { get; private set; }

    public static Palette Colors => IsDark ? Dark : Light;

    /// <summary>Foreground colour for icons drawn on the current toolbar background.</summary>
    public static Color IconColor => IsDark ? Color.FromArgb(232, 232, 236) : Color.FromArgb(50, 50, 55);

    /// <summary>Segoe UI Variable (Windows 11) when installed, otherwise Segoe UI.</summary>
    public static string FontName { get; } = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static void Configure(AppTheme theme)
    {
        IsDark = theme == AppTheme.Dark || (theme == AppTheme.System && SystemPrefersDark());

        // Every ToolStrip / menu / context menu that does not choose its own renderer follows this one (and the theme, live).
        ToolStripManager.Renderer = Renderer;
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Applies the current theme to <paramref name="form"/> and everything on it (also later, when its handle exists).</summary>
    public static void Apply(Form form)
    {
        if (form.IsHandleCreated) SetTitleBar(form);
        else form.HandleCreated += (_, _) => SetTitleBar(form);

        if (form.Font.Name != FontName) form.Font = new Font(FontName, 9f);
        form.BackColor = Colors.Window;
        form.ForeColor = Colors.Text;

        MakeAccessible(form);
        Recolor(form);
    }

    /// <summary>Themes a control tree that is created after its form (the pages hosted inside the main window): accessible names + colours.</summary>
    public static void ApplyTo(Control root)
    {
        if (root is ToolStrip own) NameItems(own.Items);
        MakeAccessible(root);
        root.BackColor = Colors.Window;
        root.ForeColor = Colors.Text;
        Recolor(root);
    }

    /// <summary>
    /// Icon-only toolbar buttons have no text, so screen readers would announce nothing: use the tooltip as the
    /// accessible name (and the text for everything else).
    /// </summary>
    private static void MakeAccessible(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is ToolStrip strip) NameItems(strip.Items);
            MakeAccessible(control);
        }
    }

    private static void NameItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripSeparator) continue;
            if (string.IsNullOrEmpty(item.AccessibleName))
                item.AccessibleName = !string.IsNullOrEmpty(item.ToolTipText) ? item.ToolTipText : item.Text;
            if (item is ToolStripDropDownItem { HasDropDownItems: true } drop) NameItems(drop.DropDownItems);
        }
    }

    /// <summary>Recolours a control (and its children) from the palette.</summary>
    public static void Recolor(Control root)
    {
        var c = Colors;
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case ToolStrip strip:
                    ThemeToolStrip(strip);
                    break;
                case Button { Tag: SwatchTag }:
                    break; // a colour chip: its background IS the colour it shows
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = c.Border;
                    button.FlatAppearance.MouseOverBackColor = IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(246, 246, 246);
                    button.BackColor = IsDark ? Color.FromArgb(58, 58, 58) : Color.White;
                    button.ForeColor = c.Text;
                    break;
                case TextBox or ComboBox or NumericUpDown or ListBox:
                    control.BackColor = c.Input;
                    control.ForeColor = c.Text;
                    break;
                case ListView list:
                    list.BackColor = IsDark ? c.Window : Color.White;
                    list.ForeColor = c.Text;
                    break;
                case TabControl or TabPage or Panel or TableLayoutPanel or FlowLayoutPanel or GroupBox or Label or CheckBox or RadioButton:
                    control.BackColor = control is Label or CheckBox or RadioButton ? Color.Transparent : c.Window;
                    control.ForeColor = control.Tag is NoteTag ? c.Muted : c.Text;
                    break;
                case ScrollBar:
                case SnapGit.Editor.Controls.EditorCanvas:
                case SnapGit.App.Controls.ToolPalette:
                case SnapGit.App.Controls.HistoryStrip:
                case SnapGit.App.Controls.TimelineBar:
                case SnapGit.App.Controls.VideoView:
                    break; // these paint themselves from the palette

                default:
                    if (IsDark || control is UserControl)
                    {
                        control.BackColor = c.Window;
                        control.ForeColor = c.Text;
                    }
                    break;
            }
            Recolor(control);
        }
    }

    /// <summary>Put this in a Label's <see cref="Control.Tag"/> to have it drawn in the quiet secondary colour (help text under a setting).</summary>
    public const string NoteTag = "note";

    /// <summary>Tag for a Button that shows a colour as its background (never recoloured by the theme).</summary>
    public const string SwatchTag = "swatch";

    /// <summary>Gives a dialog's main button the accent fill (call after <see cref="Apply"/>, which resets buttons to the plain style).</summary>
    public static void StylePrimary(Button button)
    {
        var c = Colors;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = c.Accent;
        button.ForeColor = c.OnAccent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(Math.Min(255, c.Accent.R + 18), Math.Min(255, c.Accent.G + 18), Math.Min(255, c.Accent.B + 18));
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb((int)(c.Accent.R * 0.85), (int)(c.Accent.G * 0.85), (int)(c.Accent.B * 0.85));
    }

    public static void ThemeToolStrip(ToolStrip strip)
    {
        var c = Colors;
        strip.BackColor = c.Surface;
        strip.ForeColor = c.Text;
        foreach (ToolStripItem item in strip.Items) ThemeItem(item);
    }

    private static void ThemeItem(ToolStripItem item)
    {
        var c = Colors;
        item.ForeColor = c.Text;
        switch (item)
        {
            case ToolStripComboBox combo:
                combo.BackColor = c.Input;
                combo.ForeColor = c.Text;
                combo.ComboBox.BackColor = c.Input;
                combo.ComboBox.ForeColor = c.Text;
                if (IsDark) combo.FlatStyle = FlatStyle.Flat; // the system drop-down is white-on-white otherwise
                break;
            case ToolStripTextBox box:
                box.BackColor = c.Input;
                box.ForeColor = c.Text;
                break;
            case ToolStripDropDownItem { HasDropDownItems: true } drop:
                drop.DropDown.BackColor = c.Surface;
                drop.DropDown.ImageScalingSize = new Size(16, 16);
                drop.DropDown.ForeColor = c.Text;
                foreach (ToolStripItem sub in drop.DropDownItems) ThemeItem(sub);
                break;
        }
    }

    private static void SetTitleBar(Form form)
    {
        var value = IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
