using SnapGit.Platform.Native;

namespace SnapGit.Platform.Shell;

public static class WindowPlacement
{
    /// <summary>
    /// Positions a window at exact physical-pixel bounds and makes it top-most.
    /// WinForms' own Bounds is unreliable for a window spanning monitors with different DPI.
    /// </summary>
    public static void PlaceTopMost(IntPtr handle, Rectangle pixelBounds) =>
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST,
            pixelBounds.X, pixelBounds.Y, pixelBounds.Width, pixelBounds.Height,
            NativeMethods.SWP_SHOWWINDOW);

    public static void BringToForeground(IntPtr handle) => NativeMethods.SetForegroundWindow(handle);

    /// <summary>
    /// Keeps a window out of screenshots and recordings (Windows 10 2004+). Returns false when unsupported;
    /// callers should then place the window outside the captured area.
    /// </summary>
    public static bool ExcludeFromCapture(IntPtr handle) =>
        NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
}
