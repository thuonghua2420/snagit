using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SnapGit.App.Ui;

/// <summary>App icon drawn in code so no binary asset is needed yet (replace with a real .ico later).</summary>
public static class AppIcon
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var back = new SolidBrush(Color.FromArgb(0, 120, 215));
            using var path = RoundedRect(new RectangleF(1, 1, 30, 30), 7);
            g.FillPath(back, path);

            using var pen = new Pen(Color.White, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            // Four corner brackets = "capture region".
            const float a = 8, b = 24, l = 6;
            g.DrawLines(pen, [new PointF(a, a + l), new PointF(a, a), new PointF(a + l, a)]);
            g.DrawLines(pen, [new PointF(b - l, a), new PointF(b, a), new PointF(b, a + l)]);
            g.DrawLines(pen, [new PointF(a, b - l), new PointF(a, b), new PointF(a + l, b)]);
            g.DrawLines(pen, [new PointF(b - l, b), new PointF(b, b), new PointF(b, b - l)]);
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, 13, 13, 6, 6);
        }

        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
