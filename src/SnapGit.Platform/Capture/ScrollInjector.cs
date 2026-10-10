using System.Runtime.InteropServices;
using SnapGit.Platform.Native;

namespace SnapGit.Platform.Capture;

/// <summary>Synthesises mouse-wheel input at a screen point (used to auto-scroll the area being captured).</summary>
public static class ScrollInjector
{
    private const int WheelDelta = 120;

    /// <summary>
    /// Scrolls the content under <paramref name="screenPoint"/> by <paramref name="notches"/> wheel clicks.
    /// Positive notches scroll down (vertical) / right (horizontal).
    /// </summary>
    public static void Scroll(Point screenPoint, int notches, bool horizontal)
    {
        NativeMethods.SetCursorPos(screenPoint.X, screenPoint.Y);

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    // Wheel up is positive in Win32; "down" (towards the end of the page) is negative.
                    mouseData = unchecked((uint)(horizontal ? notches * WheelDelta : -notches * WheelDelta)),
                    dwFlags = horizontal ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL,
                },
            },
        };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>True while the key is physically down (used so Esc can stop a running capture).</summary>
    public static bool IsKeyDown(Keys key) => (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;
}
