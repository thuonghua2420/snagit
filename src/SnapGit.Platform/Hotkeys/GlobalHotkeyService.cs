using SnapGit.Core.Diagnostics;
using SnapGit.Core.Hotkeys;
using SnapGit.Platform.Native;

namespace SnapGit.Platform.Hotkeys;

/// <summary>
/// RegisterHotKey-based global hotkeys. Must be created on the UI thread: the hidden message window
/// delivers WM_HOTKEY there, so <see cref="HotkeyPressed"/> is always raised on the UI thread.
/// </summary>
public sealed class GlobalHotkeyService : IHotkeyService, IDisposable
{
    private sealed class MessageWindow : NativeWindow
    {
        private readonly GlobalHotkeyService _owner;

        public MessageWindow(GlobalHotkeyService owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE: invisible, message-only
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
                _owner.OnHotkey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    private readonly MessageWindow _window;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private int _nextId = 0x5A00;

    public GlobalHotkeyService() => _window = new MessageWindow(this);

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public IReadOnlyList<HotkeyAction> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings)
    {
        UnregisterAll();

        var failed = new List<HotkeyAction>();
        foreach (var (action, gesture) in bindings)
        {
            if (!gesture.IsValid) continue;

            var id = _nextId++;
            var ok = NativeMethods.RegisterHotKey(_window.Handle, id,
                (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)gesture.Key);

            if (ok)
            {
                _registered[id] = action;
            }
            else
            {
                Log.Warn($"RegisterHotKey failed for {action} ({gesture}); likely in use by another application.");
                failed.Add(action);
            }
        }
        return failed;
    }

    private void OnHotkey(int id)
    {
        if (_registered.TryGetValue(id, out var action))
            HotkeyPressed?.Invoke(this, action);
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }
}
