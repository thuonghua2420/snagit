using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnapGit.Core.Imaging;

[Flags]
public enum EdgeSides
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,
    All = Top | Right | Bottom | Left,
}

/// <summary>
/// Pure image operations: every method returns a NEW bitmap (the caller owns it) and never touches its input.
/// No UI, so everything here is unit-tested.
/// </summary>
public static class ImageOps
{
    public static Bitmap Clone(Bitmap source) => new(source);

    public static Bitmap RotateFlip(Bitmap source, RotateFlipType type)
    {
        var copy = Clone(source);
        copy.RotateFlip(type);
        return copy;
    }

    /// <summary>High-quality resize.</summary>
    public static Bitmap Scale(Bitmap source, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
        return result;
    }

    /// <summary>Scales down (never up) so neither side exceeds the limits; keeps the aspect ratio.</summary>
    public static Bitmap ScaleToFit(Bitmap source, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height));
        return scale >= 1.0 ? Clone(source) : Scale(source, (int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale));
    }

    /// <summary>New canvas of the given size; the source is placed at <paramref name="offset"/> (may be negative = crop).</summary>
    public static Bitmap ResizeCanvas(Bitmap source, int width, int height, Point offset, Color background)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(background);
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImageUnscaled(source, offset);
        return result;
    }

    public static Bitmap Crop(Bitmap source, Rectangle area)
    {
        area.Intersect(new Rectangle(Point.Empty, source.Size));
        if (area.Width < 1 || area.Height < 1) throw new ArgumentException("Crop area is outside the image.", nameof(area));
        return source.Clone(area, PixelFormat.Format32bppArgb);
    }

    /// <summary>
    /// Bounds of the content once uniform margins (same colour as the top-left pixel, within <paramref name="tolerance"/>)
    /// are removed. Returns the whole image when it is uniform or has no margin.
    /// </summary>
    public static Rectangle FindContentBounds(Bitmap source, int tolerance = 8)
    {
        var buffer = PixelBuffer.From(source);
        var background = buffer[0, 0];
        bool IsBackground(int x, int y) => PixelBuffer.Distance(buffer[x, y], background) <= tolerance;

        int top = 0, bottom = buffer.Height - 1, left = 0, right = buffer.Width - 1;

        bool RowIsBackground(int y) { for (var x = 0; x < buffer.Width; x++) if (!IsBackground(x, y)) return false; return true; }
        bool ColumnIsBackground(int x, int y0, int y1) { for (var y = y0; y <= y1; y++) if (!IsBackground(x, y)) return false; return true; }

        while (top <= bottom && RowIsBackground(top)) top++;
        if (top > bottom) return new Rectangle(0, 0, buffer.Width, buffer.Height); // nothing but background
        while (bottom > top && RowIsBackground(bottom)) bottom--;
        while (left < right && ColumnIsBackground(left, top, bottom)) left++;
        while (right > left && ColumnIsBackground(right, top, bottom)) right--;

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    public static Bitmap AddBorder(Bitmap source, int thickness, Color color)
    {
        thickness = Math.Max(0, thickness);
        var result = ResizeCanvas(source, source.Width + thickness * 2, source.Height + thickness * 2, new Point(thickness, thickness), color);
        return result;
    }

    /// <summary>Soft drop shadow behind the image; the canvas grows to fit it.</summary>
    public static Bitmap AddShadow(Bitmap source, int offset, int blur, Color shadowColor, Color background)
    {
        blur = Math.Max(1, blur);
        var pad = blur * 2;
        var width = source.Width + pad * 2 + Math.Abs(offset);
        var height = source.Height + pad * 2 + Math.Abs(offset);
        var origin = new Point(pad + Math.Max(0, -offset), pad + Math.Max(0, -offset));

        // Shadow mask: a solid rectangle, blurred by down/up-scaling.
        using var mask = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(mask))
        using (var brush = new SolidBrush(shadowColor))
            g.FillRectangle(brush, origin.X + offset, origin.Y + offset, source.Width, source.Height);

        var factor = Math.Max(2, blur);
        using var small = Scale(mask, Math.Max(1, width / factor), Math.Max(1, height / factor));
        using var smoothSmall = BoxBlur(small, 1);
        using var blurred = Scale(smoothSmall, width, height);

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.Clear(background);
            g.DrawImageUnscaled(blurred, 0, 0);
            g.DrawImageUnscaled(source, origin);
        }
        return result;
    }

    /// <summary>Jagged "torn paper" edges on the chosen sides (outside becomes transparent). Deterministic for a given seed.</summary>
    public static Bitmap TornEdge(Bitmap source, int amplitude, int tooth, EdgeSides sides, int seed = 7)
    {
        amplitude = Math.Max(1, amplitude);
        tooth = Math.Max(2, tooth);
        var rng = new Random(seed);
        float w = source.Width, h = source.Height;

        PointF Jag(float x, float y, float dx, float dy) => new(x + dx * (float)rng.NextDouble() * amplitude, y + dy * (float)rng.NextDouble() * amplitude);

        var points = new List<PointF>();
        // Walk the outline clockwise; each side either stays straight or gets inset random points.
        points.Add(new PointF(0, 0));
        if (sides.HasFlag(EdgeSides.Top)) for (float x = 0; x < w; x += tooth) points.Add(Jag(x, 0, 0, 1));
        points.Add(new PointF(w, 0));
        if (sides.HasFlag(EdgeSides.Right)) for (float y = 0; y < h; y += tooth) points.Add(Jag(w, y, -1, 0));
        points.Add(new PointF(w, h));
        if (sides.HasFlag(EdgeSides.Bottom)) for (float x = w; x > 0; x -= tooth) points.Add(Jag(x, h, 0, -1));
        points.Add(new PointF(0, h));
        if (sides.HasFlag(EdgeSides.Left)) for (float y = h; y > 0; y -= tooth) points.Add(Jag(0, y, 1, 0));

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddPolygon(points.ToArray());
        g.SetClip(path);
        g.DrawImageUnscaled(source, 0, 0);
        return result;
    }

    /// <summary>
    /// Removes a strip and joins the two halves. <paramref name="horizontalStrip"/> = remove rows
    /// [from, to); otherwise remove columns [from, to).
    /// </summary>
    public static Bitmap CutOut(Bitmap source, int from, int to, bool horizontalStrip)
    {
        var limit = horizontalStrip ? source.Height : source.Width;
        var lo = Math.Clamp(Math.Min(from, to), 0, limit);
        var hi = Math.Clamp(Math.Max(from, to), 0, limit);
        from = lo;
        to = hi;
        if (to - from >= limit) throw new ArgumentException("Cannot cut out the whole image.");
        if (to == from) return Clone(source);

        var removed = to - from;
        var result = horizontalStrip
            ? new Bitmap(source.Width, source.Height - removed, PixelFormat.Format32bppArgb)
            : new Bitmap(source.Width - removed, source.Height, PixelFormat.Format32bppArgb);

        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        if (horizontalStrip)
        {
            g.DrawImage(source, new Rectangle(0, 0, source.Width, from), new Rectangle(0, 0, source.Width, from), GraphicsUnit.Pixel);
            g.DrawImage(source, new Rectangle(0, from, source.Width, source.Height - to), new Rectangle(0, to, source.Width, source.Height - to), GraphicsUnit.Pixel);
        }
        else
        {
            g.DrawImage(source, new Rectangle(0, 0, from, source.Height), new Rectangle(0, 0, from, source.Height), GraphicsUnit.Pixel);
            g.DrawImage(source, new Rectangle(from, 0, source.Width - to, source.Height), new Rectangle(to, 0, source.Width - to, source.Height), GraphicsUnit.Pixel);
        }
        return result;
    }

    public static Bitmap ReplaceColor(Bitmap source, Color from, Color to, int tolerance)
    {
        var buffer = PixelBuffer.From(source);
        var target = from.ToArgb();
        var replacement = to.ToArgb();
        for (var i = 0; i < buffer.Pixels.Length; i++)
        {
            if (PixelBuffer.Distance(buffer.Pixels[i], target) > tolerance) continue;
            // keep the original alpha so anti-aliased edges and transparency survive
            buffer.Pixels[i] = (buffer.Pixels[i] & unchecked((int)0xFF000000)) | (replacement & 0x00FFFFFF);
        }
        return buffer.ToBitmap();
    }

    public static Bitmap FloodFill(Bitmap source, Point start, Color fill, int tolerance)
    {
        var buffer = PixelBuffer.From(source);
        if (start.X < 0 || start.Y < 0 || start.X >= buffer.Width || start.Y >= buffer.Height) return buffer.ToBitmap();

        var seed = buffer[start.X, start.Y];
        var replacement = fill.ToArgb();
        if (PixelBuffer.Distance(seed, replacement) == 0 && PixelBuffer.A(seed) == fill.A) return buffer.ToBitmap();

        var visited = new bool[buffer.Pixels.Length];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((start.X, start.Y));

        bool Matches(int x, int y) => !visited[y * buffer.Width + x] && PixelBuffer.Distance(buffer[x, y], seed) <= tolerance;

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (!Matches(x, y)) continue;

            // scanline: extend left/right, then queue the rows above and below
            int left = x, right = x;
            while (left > 0 && Matches(left - 1, y)) left--;
            while (right < buffer.Width - 1 && Matches(right + 1, y)) right++;

            for (var i = left; i <= right; i++)
            {
                visited[y * buffer.Width + i] = true;
                buffer[i, y] = replacement;
            }

            for (var i = left; i <= right; i++)
            {
                if (y > 0 && Matches(i, y - 1)) stack.Push((i, y - 1));
                if (y < buffer.Height - 1 && Matches(i, y + 1)) stack.Push((i, y + 1));
            }
        }
        return buffer.ToBitmap();
    }

    public static Bitmap Grayscale(Bitmap source) => MapPixels(source, p =>
    {
        var y = (PixelBuffer.R(p) * 299 + PixelBuffer.G(p) * 587 + PixelBuffer.B(p) * 114) / 1000;
        return PixelBuffer.Pack(PixelBuffer.A(p), y, y, y);
    });

    public static Bitmap Invert(Bitmap source) => MapPixels(source, p =>
        PixelBuffer.Pack(PixelBuffer.A(p), 255 - PixelBuffer.R(p), 255 - PixelBuffer.G(p), 255 - PixelBuffer.B(p)));

    public static Bitmap Sepia(Bitmap source) => MapPixels(source, p =>
    {
        int r = PixelBuffer.R(p), g = PixelBuffer.G(p), b = PixelBuffer.B(p);
        return PixelBuffer.Pack(PixelBuffer.A(p),
            Math.Min(255, (int)(r * .393 + g * .769 + b * .189)),
            Math.Min(255, (int)(r * .349 + g * .686 + b * .168)),
            Math.Min(255, (int)(r * .272 + g * .534 + b * .131)));
    });

    /// <param name="brightness">-100..100</param>
    /// <param name="contrast">-100..100</param>
    public static Bitmap BrightnessContrast(Bitmap source, int brightness, int contrast)
    {
        var factor = (259.0 * (contrast + 255)) / (255.0 * (259 - contrast));
        var shift = brightness * 2.55;
        int Map(int v) => (int)Math.Clamp(factor * (v - 128) + 128 + shift, 0, 255);

        var lookup = Enumerable.Range(0, 256).Select(Map).ToArray();
        return MapPixels(source, p => PixelBuffer.Pack(PixelBuffer.A(p), lookup[PixelBuffer.R(p)], lookup[PixelBuffer.G(p)], lookup[PixelBuffer.B(p)]));
    }

    /// <summary>Flattens a UI screenshot into a simplified look: smooth, then quantise colours (removes noise, keeps layout).</summary>
    public static Bitmap Simplify(Bitmap source, int levels = 5)
    {
        levels = Math.Clamp(levels, 2, 16);
        using var smooth = BoxBlur(source, 1);
        var step = 255.0 / (levels - 1);
        int Quantize(int v) => (int)(Math.Round(v / step) * step);
        return MapPixels(smooth, p => PixelBuffer.Pack(PixelBuffer.A(p), Quantize(PixelBuffer.R(p)), Quantize(PixelBuffer.G(p)), Quantize(PixelBuffer.B(p))));
    }

    /// <summary>3×3-style box blur repeated <paramref name="radius"/> times.</summary>
    public static Bitmap BoxBlur(Bitmap source, int radius)
    {
        var src = PixelBuffer.From(source);
        var dst = new PixelBuffer(src.Width, src.Height);
        for (var pass = 0; pass < Math.Max(1, radius); pass++)
        {
            for (var y = 0; y < src.Height; y++)
            {
                for (var x = 0; x < src.Width; x++)
                {
                    int a = 0, r = 0, g = 0, b = 0, n = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var yy = y + dy;
                        if (yy < 0 || yy >= src.Height) continue;
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var xx = x + dx;
                            if (xx < 0 || xx >= src.Width) continue;
                            var p = src[xx, yy];
                            a += PixelBuffer.A(p); r += PixelBuffer.R(p); g += PixelBuffer.G(p); b += PixelBuffer.B(p);
                            n++;
                        }
                    }
                    dst[x, y] = PixelBuffer.Pack(a / n, r / n, g / n, b / n);
                }
            }
            (src, dst) = (dst, src);
        }
        return src.ToBitmap();
    }

    /// <summary>
    /// Fills <paramref name="area"/> by interpolating between the pixels just outside it (horizontally and vertically,
    /// averaged). Hides the hole left by Smart Move on flat or gradient backgrounds.
    /// </summary>
    public static Bitmap FillRegionSmart(Bitmap source, Rectangle area)
    {
        var buffer = PixelBuffer.From(source);
        area.Intersect(new Rectangle(0, 0, buffer.Width, buffer.Height));
        if (area.IsEmpty) return buffer.ToBitmap();

        int Edge(int x, int y) => buffer[Math.Clamp(x, 0, buffer.Width - 1), Math.Clamp(y, 0, buffer.Height - 1)];
        static int Lerp(int p, int q, double t)
        {
            int Ch(int a, int b) => (int)Math.Round(a + (b - a) * t);
            return PixelBuffer.Pack(255, Ch(PixelBuffer.R(p), PixelBuffer.R(q)), Ch(PixelBuffer.G(p), PixelBuffer.G(q)), Ch(PixelBuffer.B(p), PixelBuffer.B(q)));
        }

        var result = new int[area.Width * area.Height];
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var horizontal = Lerp(Edge(area.Left - 1, y), Edge(area.Right, y), (x - area.Left + 0.5) / area.Width);
                var vertical = Lerp(Edge(x, area.Top - 1), Edge(x, area.Bottom), (y - area.Top + 0.5) / area.Height);
                result[(y - area.Top) * area.Width + (x - area.Left)] = Lerp(horizontal, vertical, 0.5);
            }
        }

        for (var y = 0; y < area.Height; y++)
            for (var x = 0; x < area.Width; x++)
                buffer[area.Left + x, area.Top + y] = result[y * area.Width + x];
        return buffer.ToBitmap();
    }

    /// <summary>
    /// Average colour along the border of <paramref name="area"/> (just outside it): a cheap background guess
    /// used to patch the hole left by Smart Move.
    /// </summary>
    public static Color SampleSurroundingColor(Bitmap source, Rectangle area)
    {
        var buffer = PixelBuffer.From(source);
        long r = 0, g = 0, b = 0, n = 0;

        void Add(int x, int y)
        {
            if (x < 0 || y < 0 || x >= buffer.Width || y >= buffer.Height) return;
            var p = buffer[x, y];
            r += PixelBuffer.R(p); g += PixelBuffer.G(p); b += PixelBuffer.B(p); n++;
        }

        for (var x = area.Left - 1; x <= area.Right; x++) { Add(x, area.Top - 1); Add(x, area.Bottom); }
        for (var y = area.Top; y < area.Bottom; y++) { Add(area.Left - 1, y); Add(area.Right, y); }
        return n == 0 ? Color.White : Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
    }

    /// <summary>
    /// Bounding box of the "object" at <paramref name="point"/>: region-grow over pixels that differ from the local
    /// background colour (estimated from the surroundings of the seed). Used by Smart Move's click-to-select.
    /// </summary>
    public static Rectangle DetectObjectBounds(Bitmap source, Point point, int tolerance = 24, int maxPixels = 4_000_000)
    {
        var buffer = PixelBuffer.From(source);
        if (point.X < 0 || point.Y < 0 || point.X >= buffer.Width || point.Y >= buffer.Height) return Rectangle.Empty;

        // Background = the most common colour in a ring around the seed.
        var votes = new Dictionary<int, int>();
        for (var dy = -40; dy <= 40; dy += 4)
            for (var dx = -40; dx <= 40; dx += 4)
            {
                int x = point.X + dx, y = point.Y + dy;
                if (x < 0 || y < 0 || x >= buffer.Width || y >= buffer.Height) continue;
                var key = buffer[x, y] & 0x00F8F8F8; // coarse bucket
                votes[key] = votes.GetValueOrDefault(key) + 1;
            }
        var background = votes.Count == 0 ? buffer[point.X, point.Y] : votes.MaxBy(v => v.Value).Key;

        bool IsForeground(int x, int y) => PixelBuffer.Distance(buffer[x, y], background) > tolerance;
        if (!IsForeground(point.X, point.Y)) return Rectangle.Empty;

        var seen = new bool[buffer.Pixels.Length];
        var queue = new Queue<(int, int)>();
        queue.Enqueue((point.X, point.Y));
        seen[point.Y * buffer.Width + point.X] = true;
        int minX = point.X, maxX = point.X, minY = point.Y, maxY = point.Y, count = 0;

        while (queue.Count > 0 && count < maxPixels)
        {
            var (x, y) = queue.Dequeue();
            count++;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);

            // 8-neighbourhood with a small gap bridge (2px) so text strokes join into one object
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= buffer.Width || ny >= buffer.Height) continue;
                    var idx = ny * buffer.Width + nx;
                    if (seen[idx] || !IsForeground(nx, ny)) continue;
                    seen[idx] = true;
                    queue.Enqueue((nx, ny));
                }
        }
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Bitmap MapPixels(Bitmap source, Func<int, int> map)
    {
        var buffer = PixelBuffer.From(source);
        for (var i = 0; i < buffer.Pixels.Length; i++) buffer.Pixels[i] = map(buffer.Pixels[i]);
        return buffer.ToBitmap();
    }
}
