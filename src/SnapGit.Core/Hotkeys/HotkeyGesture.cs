namespace SnapGit.Core.Hotkeys;

/// <summary>Modifier flags. Values match the Win32 MOD_* constants used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A key plus modifiers, e.g. "Ctrl+Shift+PrintScreen". Round-trips through <see cref="ToString"/>/<see cref="TryParse"/>.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, Keys Key)
{
    public bool IsValid => Key != Keys.None;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((char)Key).ToString() : Key.ToString());
        return string.Join("+", parts);
    }

    private static bool TryParseKey(string text, out Keys key)
    {
        key = Keys.None;

        // "1" means the digit key, NOT Keys value 1 (LButton): Enum.TryParse would happily accept any number.
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = (Keys)text[0]; // Keys.D0..D9 share the ASCII codes of '0'..'9'
            return true;
        }

        if (char.IsAsciiDigit(text[0]) || text[0] == '-') return false;
        return Enum.TryParse(text, ignoreCase: true, out key) && key != Keys.None && Enum.IsDefined(key);
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        Keys key = Keys.None;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= HotkeyModifiers.Control; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win":
                case "windows": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key != Keys.None || !TryParseKey(raw, out var parsed))
                        return false;
                    key = parsed;
                    break;
            }
        }

        if (key == Keys.None) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }
}
