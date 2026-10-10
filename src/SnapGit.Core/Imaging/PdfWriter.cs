using System.Globalization;
using System.Text;

namespace SnapGit.Core.Imaging;

/// <summary>
/// Minimal PDF writer: one image per page (JPEG/DCT streams), page size = image size at 96 DPI.
/// No third-party dependency; the output opens in every PDF reader.
/// </summary>
public static class PdfWriter
{
    public static void Write(string path, IReadOnlyList<Bitmap> pages, int jpegQuality = 90)
    {
        if (pages.Count == 0) throw new ArgumentException("A PDF needs at least one page.", nameof(pages));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp)) Write(stream, pages, jpegQuality);
        File.Move(temp, path, overwrite: true);
    }

    public static void Write(Stream output, IReadOnlyList<Bitmap> pages, int jpegQuality = 90)
    {
        var offsets = new List<long>();
        var latin1 = Encoding.Latin1;

        void Raw(string text)
        {
            var bytes = latin1.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        void BeginObject(int number)
        {
            offsets.Add(output.Position);
            Raw($"{number} 0 obj\n");
        }

        // Object numbering: 1 catalog, 2 pages tree, then per page: page, content, image.
        Raw("%PDF-1.4\n%âãÏÓ\n");

        BeginObject(1);
        Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 3} 0 R"));
        BeginObject(2);
        Raw($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var pageObject = 3 + i * 3;
            var contentObject = pageObject + 1;
            var imageObject = pageObject + 2;

            // 96 DPI pixels -> 72 DPI points
            var widthPt = (page.Width * 0.75).ToString("0.##", CultureInfo.InvariantCulture);
            var heightPt = (page.Height * 0.75).ToString("0.##", CultureInfo.InvariantCulture);

            BeginObject(pageObject);
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt} {heightPt}] /Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>\nendobj\n");

            var content = $"q {widthPt} 0 0 {heightPt} 0 0 cm /Im0 Do Q";
            BeginObject(contentObject);
            Raw($"<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

            byte[] jpeg;
            using (var flat = Flatten(page))
            using (var ms = new MemoryStream())
            {
                ImageEncoder.Save(flat, ms, ImageFileFormat.Jpeg, jpegQuality);
                jpeg = ms.ToArray();
            }

            BeginObject(imageObject);
            Raw($"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            output.Write(jpeg, 0, jpeg.Length);
            Raw("\nendstream\nendobj\n");
        }

        var xrefPosition = output.Position;
        var count = offsets.Count + 1;
        Raw($"xref\n0 {count}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Raw($"{offset:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {count} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");
    }

    /// <summary>JPEG has no alpha: composite on white so transparent areas don't turn black.</summary>
    private static Bitmap Flatten(Bitmap source)
    {
        var flat = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(flat);
        g.Clear(Color.White);
        g.DrawImageUnscaled(source, 0, 0);
        return flat;
    }
}
