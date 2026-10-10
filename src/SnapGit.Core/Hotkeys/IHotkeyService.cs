namespace SnapGit.Core.Hotkeys;

/// <summary>Registers system-wide hotkeys and raises an event when one is pressed (on the UI thread).</summary>
public interface IHotkeyService
{
    event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>
    /// Replaces every registered hotkey with <paramref name="bindings"/>.
    /// Returns the actions that could not be registered (e.g. already taken by another app).
    /// </summary>
    IReadOnlyList<HotkeyAction> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings);
}
