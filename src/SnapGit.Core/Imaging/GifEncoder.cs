namespace SnapGit.Core.Imaging;

/// <summary>One animation frame: the full image and how long it stays on screen.</summary>
public sealed record GifFrame(Bitmap Image, int DelayMs);

/// <summary>
/// Animated GIF writer (GIF89a): global median-cut palette (255 colours + 1 transparent), frame differencing
/// (only the changed rectangle is stored — a big win for screen recordings), LZW compression, infinite loop.
/// </summary>
public static class GifEncoder
{
    private const int TransparentIndex = 255;

    public static void Write(string path, IReadOnlyList<GifFrame> frames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp)) Write(stream, frames);
        File.Move(temp, path, overwrite: true);
    }

    public static void Write(Stream output, IReadOnlyList<GifFrame> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("A GIF needs at least one frame.", nameof(frames));

        int width = frames[0].Image.Width, height = frames[0].Image.Height;
        var buffers = frames.Select(f => PixelBuffer.From(f.Image.Size == new Size(width, height) ? f.Image : Resize(f.Image, width, height))).ToList();

        var palette = BuildPalette(buffers);
        var lookup = new Dictionary<int, byte>();

        using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("GIF89a"u8.ToArray());
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((byte)0xF7); // global colour table, 8 bits/colour, 256 entries
        w.Write((byte)0);
        w.Write((byte)0);
        for (var i = 0; i < 256; i++)
        {
            var c = i < palette.Length ? palette[i] : 0;
            w.Write((byte)PixelBuffer.R(c));
            w.Write((byte)PixelBuffer.G(c));
            w.Write((byte)PixelBuffer.B(c));
        }

        // Netscape loop extension (0 = forever)
        w.Write([0x21, 0xFF, 0x0B]);
        w.Write("NETSCAPE2.0"u8.ToArray());
        w.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        byte[]? previous = null;
        for (var f = 0; f < frames.Count; f++)
        {
            var indices = Quantize(buffers[f], palette, lookup);

            // Changed rectangle compared with the previous frame.
            int minX = 0, minY = 0, maxX = width - 1, maxY = height - 1;
            var data = indices;
            if (previous is not null)
            {
                minX = width; minY = height; maxX = -1; maxY = -1;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        if (indices[y * width + x] != previous[y * width + x])
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }

                if (maxX < 0) { minX = minY = 0; maxX = maxY = 0; } // identical: one transparent pixel keeps the timing

                var cropW = maxX - minX + 1;
                var cropH = maxY - minY + 1;
                data = new byte[cropW * cropH];
                for (var y = 0; y < cropH; y++)
                    for (var x = 0; x < cropW; x++)
                    {
                        var i = (minY + y) * width + (minX + x);
                        data[y * cropW + x] = indices[i] == previous[i] ? (byte)TransparentIndex : indices[i];
                    }
            }

            var rectW = maxX - minX + 1;
            var rectH = maxY - minY + 1;
            var transparent = previous is not null;

            // Graphic control extension: disposal = "do not dispose", optional transparency, delay in 1/100 s
            w.Write([0x21, 0xF9, 0x04]);
            w.Write((byte)(0x04 | (transparent ? 0x01 : 0x00)));
            w.Write((ushort)Math.Max(2, frames[f].DelayMs / 10));
            w.Write((byte)TransparentIndex);
            w.Write((byte)0);

            // Image descriptor
            w.Write((byte)0x2C);
            w.Write((ushort)minX);
            w.Write((ushort)minY);
            w.Write((ushort)rectW);
            w.Write((ushort)rectH);
            w.Write((byte)0);

            w.Write((byte)8); // LZW minimum code size
            WriteSubBlocks(w, LzwCompress(data, 8));
            w.Write((byte)0);

            previous = indices;
        }
        w.Write((byte)0x3B);
    }

    // ------------------------------------------------------------------ palette

    private static int[] BuildPalette(List<PixelBuffer> frames)
    {
        // Sample at most ~250k pixels spread over all frames.
        var total = frames.Sum(f => (long)f.Pixels.Length);
        var step = (int)Math.Max(1, total / 250_000);
        var samples = new List<int>();
        var counter = 0;
        foreach (var frame in frames)
            foreach (var pixel in frame.Pixels)
                if (counter++ % step == 0) samples.Add(pixel & 0x00FFFFFF);

        // Median cut into 255 boxes.
        var boxes = new List<List<int>> { samples };
        while (boxes.Count < 255)
        {
            var biggest = boxes.Select((b, i) => (b, i)).Where(t => t.b.Count > 1).OrderByDescending(t => Range(t.b)).FirstOrDefault();
            if (biggest.b is null || Range(biggest.b) == 0) break;

            var box = biggest.b;
            var channel = WidestChannel(box);
            box.Sort((x, y) => Channel(x, channel).CompareTo(Channel(y, channel)));
            var half = box.Count / 2;
            boxes[biggest.i] = box.GetRange(0, half);
            boxes.Add(box.GetRange(half, box.Count - half));
        }

        return boxes.Select(Average).ToArray();
    }

    private static int Channel(int pixel, int channel) => channel switch { 0 => PixelBuffer.R(pixel), 1 => PixelBuffer.G(pixel), _ => PixelBuffer.B(pixel) };

    private static int Range(List<int> box)
    {
        var best = 0;
        for (var c = 0; c < 3; c++)
        {
            int min = 255, max = 0;
            foreach (var p in box) { var v = Channel(p, c); if (v < min) min = v; if (v > max) max = v; }
            best = Math.Max(best, max - min);
        }
        return best * (int)Math.Min(box.Count, 1000) / 4; // weight by population so large flat areas get split first
    }

    private static int WidestChannel(List<int> box)
    {
        int bestChannel = 0, bestRange = -1;
        for (var c = 0; c < 3; c++)
        {
            int min = 255, max = 0;
            foreach (var p in box) { var v = Channel(p, c); if (v < min) min = v; if (v > max) max = v; }
            if (max - min > bestRange) { bestRange = max - min; bestChannel = c; }
        }
        return bestChannel;
    }

    private static int Average(List<int> box)
    {
        long r = 0, g = 0, b = 0;
        foreach (var p in box) { r += PixelBuffer.R(p); g += PixelBuffer.G(p); b += PixelBuffer.B(p); }
        var n = Math.Max(1, box.Count);
        return PixelBuffer.Pack(255, (int)(r / n), (int)(g / n), (int)(b / n));
    }

    private static byte[] Quantize(PixelBuffer frame, int[] palette, Dictionary<int, byte> cache)
    {
        var indices = new byte[frame.Pixels.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            var pixel = frame.Pixels[i] & 0x00FFFFFF;
            if (!cache.TryGetValue(pixel, out var index))
            {
                index = Nearest(palette, pixel);
                cache[pixel] = index;
            }
            indices[i] = index;
        }
        return indices;
    }

    private static byte Nearest(int[] palette, int pixel)
    {
        int r = PixelBuffer.R(pixel), g = PixelBuffer.G(pixel), b = PixelBuffer.B(pixel);
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Length && i < TransparentIndex; i++)
        {
            int dr = r - PixelBuffer.R(palette[i]), dg = g - PixelBuffer.G(palette[i]), db = b - PixelBuffer.B(palette[i]);
            var d = dr * dr * 3 + dg * dg * 4 + db * db * 2; // perceptual-ish weights
            if (d < bestDistance) { bestDistance = d; best = i; if (d == 0) break; }
        }
        return (byte)best;
    }

    private static Bitmap Resize(Bitmap source, int width, int height) => ImageOps.Scale(source, width, height);

    // ------------------------------------------------------------------ LZW

    private static void WriteSubBlocks(BinaryWriter w, byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 255)
        {
            var length = Math.Min(255, data.Length - offset);
            w.Write((byte)length);
            w.Write(data, offset, length);
        }
    }

    private static byte[] LzwCompress(byte[] pixels, int minCodeSize)
    {
        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;

        var output = new List<byte>(pixels.Length / 2);
        var current = 0;
        var bits = 0;

        void Emit(int code, int size)
        {
            current |= code << bits;
            bits += size;
            while (bits >= 8)
            {
                output.Add((byte)(current & 0xFF));
                current >>= 8;
                bits -= 8;
            }
        }

        var dictionary = new Dictionary<int, int>();
        var codeSize = minCodeSize + 1;
        var nextCode = endCode + 1;

        Emit(clearCode, codeSize);

        int prefix = pixels[0];
        for (var i = 1; i < pixels.Length; i++)
        {
            var symbol = pixels[i];
            var key = (prefix << 8) | symbol;
            if (dictionary.TryGetValue(key, out var existing))
            {
                prefix = existing;
                continue;
            }

            Emit(prefix, codeSize);

            if (nextCode < 4096)
            {
                dictionary[key] = nextCode++;
                if (nextCode - 1 == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clearCode, codeSize);
                dictionary.Clear();
                codeSize = minCodeSize + 1;
                nextCode = endCode + 1;
            }
            prefix = symbol;
        }

        Emit(prefix, codeSize);
        Emit(endCode, codeSize);
        if (bits > 0) output.Add((byte)(current & 0xFF));
        return output.ToArray();
    }
}
