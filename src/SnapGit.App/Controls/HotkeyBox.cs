using SnapGit.Core.Hotkeys;

namespace SnapGit.App.Controls;

/// <summary>Read-only text box that records the next key combination the user presses. Backspace/Delete clears it.</summary>
public sealed class HotkeyBox : TextBox
{
    private HotkeyGesture? _gesture;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = SystemColors.Window;
    }

    public HotkeyGesture? Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            Text = value?.ToString() ?? Core.Localization.Loc.T("(none)");
        }
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Swallow everything (incl. Alt+..., Tab) while capturing.
        HandleKey(keyData);
        return true;
    }

    private void HandleKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return; // modifier alone

        if (keyData is Keys.Back or Keys.Delete)
        {
            Gesture = null;
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (keyData.HasFlag(Keys.Control)) modifiers |= HotkeyModifiers.Control;
        if (keyData.HasFlag(Keys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (keyData.HasFlag(Keys.Shift)) modifiers |= HotkeyModifiers.Shift;

        Gesture = new HotkeyGesture(modifiers, key);
    }

    // Windows only delivers key-UP for PrintScreen, so KeyDown/ProcessCmdKey never sees it.
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.PrintScreen) HandleKey(e.KeyData);
    }
}
