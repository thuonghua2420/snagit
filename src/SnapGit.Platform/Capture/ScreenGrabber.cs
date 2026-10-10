using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using SnapGit.Core.Models;
using SnapGit.Platform.Native;

namespace SnapGit.Platform.Capture;

/// <summary>GDI-based screen capture (BitBlt with CAPTUREBLT so layered windows are included).</summary>
public sealed class ScreenGrabber : IScreenGrabber
{
    public Rectangle VirtualScreen => SystemInformation.VirtualScreen;

    public Rectangle MonitorUnderCursor => Screen.FromPoint(Cursor.Position).Bounds;

    public Bitmap Grab(Rectangle bounds, bool includeCursor)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture area is empty.");

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        // Graphics.CopyFromScreen rejects SRCCOPY|CAPTUREBLT, so call BitBlt directly.
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var targetDc = g.GetHdc();
        try
        {
            if (!NativeMethods.BitBlt(targetDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Screen capture (BitBlt) failed.");
        }
        finally
        {
            g.ReleaseHdc(targetDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        if (includeCursor)
            DrawCursor(g, bounds);

        // GDI leaves the alpha channel at 0; a screenshot is opaque.
        MakeOpaque(bitmap);

        return bitmap;
    }

    private static void MakeOpaque(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var row = (byte*)data.Scan0;
                for (var y = 0; y < data.Height; y++, row += data.Stride)
                    for (var x = 3; x < data.Width * 4; x += 4)
                        row[x] = 255;
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public IReadOnlyList<WindowInfo> GetWindows(IntPtr excludeHandle = default)
    {
        var result = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (hWnd == excludeHandle) return true;
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return true;

            // Skip invisible UWP "ghost" windows and tool windows (tooltips, floating palettes).
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            var title = GetTitle(hWnd);
            if (title.Length == 0) return true;

            var bounds = GetVisibleBounds(hWnd);
            if (bounds.Width < 20 || bounds.Height < 20) return true;

            result.Add(new WindowInfo(hWnd, title, bounds));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public IReadOnlyList<WindowInfo> GetWindowChain(WindowInfo window, Point screenPoint)
    {
        var chain = new List<WindowInfo> { window };
        var parent = window.Handle;

        for (var depth = 0; depth < 12; depth++)
        {
            var point = new NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
            if (!NativeMethods.ScreenToClient(parent, ref point)) break;

            var child = NativeMethods.RealChildWindowFromPoint(parent, point);
            if (child == IntPtr.Zero || child == parent || !NativeMethods.IsWindowVisible(child)) break;
            if (!NativeMethods.GetWindowRect(child, out var rect)) break;

            var bounds = rect.ToRectangle();
            var previous = chain[^1].Bounds;
            parent = child;

            // Skip degenerate or identical rectangles (wrappers add nothing to the selection chain).
            if (bounds.Width < 12 || bounds.Height < 12 || bounds == previous) continue;
            chain.Add(new WindowInfo(child, GetTitle(child), bounds));
        }
        return chain;
    }

    public Bitmap? GrabWindow(IntPtr handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var full)) return null;
        var fullRect = full.ToRectangle();
        if (fullRect.Width < 1 || fullRect.Height < 1) return null;

        var raw = new Bitmap(fullRect.Width, fullRect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(raw))
        {
            var dc = g.GetHdc();
            bool ok;
            try { ok = NativeMethods.PrintWindow(handle, dc, NativeMethods.PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(dc); }
            if (!ok) { raw.Dispose(); return null; }
        }

        MakeOpaque(raw);
        if (IsBlank(raw)) { raw.Dispose(); return null; } // GPU-composited windows sometimes render black

        // Trim the invisible resize border/shadow like GetVisibleBounds does for screen crops.
        var visible = GetVisibleBounds(handle);
        var crop = Rectangle.Intersect(new Rectangle(visible.X - fullRect.X, visible.Y - fullRect.Y, visible.Width, visible.Height), new Rectangle(Point.Empty, raw.Size));
        if (crop.Width < 1 || crop.Height < 1 || crop.Size == raw.Size) return raw;

        var trimmed = raw.Clone(crop, PixelFormat.Format32bppArgb);
        raw.Dispose();
        return trimmed;
    }

    /// <summary>True when (nearly) every sampled pixel is the same colour — a failed render.</summary>
    private static bool IsBlank(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0).ToArgb();
        var stepX = Math.Max(1, bitmap.Width / 16);
        var stepY = Math.Max(1, bitmap.Height / 16);
        for (var y = 0; y < bitmap.Height; y += stepY)
            for (var x = 0; x < bitmap.Width; x += stepX)
                if (bitmap.GetPixel(x, y).ToArgb() != first) return false;
        return true;
    }

    /// <summary>Window rectangle without the invisible resize border/shadow that Win10+ adds.</summary>
    private static Rectangle GetVisibleBounds(IntPtr hWnd)
    {
        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT frame, Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            return frame.ToRectangle();

        return NativeMethods.GetWindowRect(hWnd, out var rect) ? rect.ToRectangle() : Rectangle.Empty;
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var length = NativeMethods.GetWindowTextLength(hWnd);
        if (length <= 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        if (NativeMethods.GetWindowText(hWnd, sb, sb.Capacity) <= 0) return string.Empty;
        return sb.ToString();
    }

    private static void DrawCursor(Graphics g, Rectangle captureBounds)
    {
        var info = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info) || (info.flags & NativeMethods.CURSOR_SHOWING) == 0) return;
        if (!NativeMethods.GetIconInfo(info.hCursor, out var icon)) return;

        try
        {
            var x = info.ptScreenPos.X - icon.xHotspot - captureBounds.X;
            var y = info.ptScreenPos.Y - icon.yHotspot - captureBounds.Y;

            var hdc = g.GetHdc();
            try { NativeMethods.DrawIconEx(hdc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }
        finally
        {
            if (icon.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(icon.hbmColor);
        }
    }
}
