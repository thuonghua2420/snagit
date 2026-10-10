namespace SnapGit.Core.Models;

/// <summary>Low-level screen access. The Win32 implementation lives in SnapGit.Platform; swap it to change the capture backend.</summary>
public interface IScreenGrabber
{
    /// <summary>Bounding rectangle of all monitors, in pixels.</summary>
    Rectangle VirtualScreen { get; }

    /// <summary>Bounds of the monitor that currently contains the mouse cursor.</summary>
    Rectangle MonitorUnderCursor { get; }

    /// <summary>Copies a screen rectangle (virtual-screen pixel coordinates) into a new bitmap.</summary>
    Bitmap Grab(Rectangle bounds, bool includeCursor);

    /// <summary>Visible top-level windows, front-most first.</summary>
    IReadOnlyList<WindowInfo> GetWindows(IntPtr excludeHandle = default);

    /// <summary>
    /// The window itself plus the nested controls under <paramref name="screenPoint"/>, outermost first
    /// (so Snagit-style "select the parent/child" can step through them).
    /// </summary>
    IReadOnlyList<WindowInfo> GetWindowChain(WindowInfo window, Point screenPoint);

    /// <summary>
    /// Captures one window with PrintWindow, so covered or partly off-screen windows are complete.
    /// Returns null when the window can't be rendered that way (the caller falls back to a screen crop).
    /// </summary>
    Bitmap? GrabWindow(IntPtr handle);
}
