namespace SnapGit.Core.Hotkeys;

/// <summary>Every action that can be bound to a global hotkey. Add new values at the END (persisted by name).</summary>
public enum HotkeyAction
{
    CaptureRegion,
    CaptureWindow,
    CaptureFullScreen,
    OpenLibrary,
    CaptureScrolling,
    GrabText,
    CaptureFreehand,
    CaptureMultiArea,
    RecordGif,
    RecordVideo,
    RepeatLastCapture,
}
