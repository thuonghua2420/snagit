using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapGit.Core.Imaging;

/// <summary>A bitmap's pixels as a flat 0xAARRGGBB int array (top-down), for algorithms that need random access.</summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new int[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public int[] Pixels { get; }

    public int this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public static PixelBuffer From(Bitmap bitmap)
    {
        var buffer = new PixelBuffer(bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < data.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer.Pixels, y * data.Width, data.Width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return buffer;
    }

    public Bitmap ToBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(Pixels, y * Width, data.Scan0 + y * data.Stride, Width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    public static int Pack(int a, int r, int g, int b) => (a << 24) | (r << 16) | (g << 8) | b;
    public static int A(int p) => (p >> 24) & 0xFF;
    public static int R(int p) => (p >> 16) & 0xFF;
    public static int G(int p) => (p >> 8) & 0xFF;
    public static int B(int p) => p & 0xFF;

    /// <summary>Largest per-channel difference (RGB only).</summary>
    public static int Distance(int p, int q) =>
        Math.Max(Math.Abs(R(p) - R(q)), Math.Max(Math.Abs(G(p) - G(q)), Math.Abs(B(p) - B(q))));
}
