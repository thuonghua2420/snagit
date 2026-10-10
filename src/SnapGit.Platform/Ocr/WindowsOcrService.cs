using System.Drawing.Imaging;
using SnapGit.Core.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapGit.Platform.Ocr;

/// <summary>OCR through the built-in Windows engine (offline; languages = installed language packs).</summary>
public sealed class WindowsOcrService : IOcrService
{
    public IReadOnlyList<OcrLanguage> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => new OcrLanguage(l.LanguageTag, l.DisplayName)).ToList();

    public async Task<OcrOutput> RecognizeAsync(Bitmap image, string? languageTag = null, CancellationToken cancellation = default)
    {
        var engine = CreateEngine(languageTag)
            ?? throw new InvalidOperationException(string.IsNullOrWhiteSpace(languageTag)
                ? "No OCR language is installed. Add a language pack in Windows Settings > Time & language."
                : $"No OCR engine for \"{languageTag}\". Install that language pack in Windows Settings > Time & language.");

        // The engine rejects huge images and reads small text badly: keep the longest side within limits,
        // and enlarge tiny captures.
        var max = (int)OcrEngine.MaxImageDimension;
        var scale = 1.0;
        if (Math.Max(image.Width, image.Height) > max) scale = (double)max / Math.Max(image.Width, image.Height);
        else if (image.Height < 120 && image.Width < 1200) scale = 3.0;
        else if (image.Height < 300) scale = 2.0;

        using var prepared = Math.Abs(scale - 1.0) < 0.001 ? new Bitmap(image) : SnapGit.Core.Imaging.ImageOps.Scale(image, (int)(image.Width * scale), (int)(image.Height * scale));

        using var memory = new MemoryStream();
        prepared.Save(memory, ImageFormat.Png);
        memory.Position = 0;

        using var stream = memory.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation);
        using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellation);

        var result = await engine.RecognizeAsync(software).AsTask(cancellation);

        var lines = result.Lines.Select(line =>
        {
            // Union of the word boxes, mapped back to the original image's coordinates.
            var words = line.Words.Select(w => w.BoundingRect).ToList();
            var left = words.Min(r => r.Left);
            var top = words.Min(r => r.Top);
            var right = words.Max(r => r.Right);
            var bottom = words.Max(r => r.Bottom);
            var box = RectangleF.FromLTRB((float)(left / scale), (float)(top / scale), (float)(right / scale), (float)(bottom / scale));
            return new SnapGit.Core.Models.OcrLine(line.Text, box);
        }).ToList();

        var rows = OcrLayout.MergeRows(lines);
        return new OcrOutput(string.Join(Environment.NewLine, rows.Select(l => l.Text)), rows, engine.RecognizerLanguage.LanguageTag);
    }

    private static OcrEngine? CreateEngine(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            var language = new Language(languageTag);
            return OcrEngine.IsLanguageSupported(language) ? OcrEngine.TryCreateFromLanguage(language) : null;
        }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }
}
