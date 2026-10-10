namespace SnapGit.Core.Models;

public sealed record OcrLine(string Text, RectangleF Bounds);

/// <param name="Text">All recognised text, lines joined with newlines (empty if nothing was found).</param>
public sealed record OcrOutput(string Text, IReadOnlyList<OcrLine> Lines, string LanguageTag);

public sealed record OcrLanguage(string Tag, string DisplayName);

/// <summary>Optical character recognition. The Windows implementation lives in SnapGit.Platform.</summary>
public interface IOcrService
{
    /// <summary>Languages that have an OCR engine installed on this machine.</summary>
    IReadOnlyList<OcrLanguage> AvailableLanguages { get; }

    /// <param name="languageTag">BCP-47 tag (e.g. "vi", "en-US"); null/empty = the user's profile languages.</param>
    /// <exception cref="InvalidOperationException">No OCR engine is available for the language.</exception>
    Task<OcrOutput> RecognizeAsync(Bitmap image, string? languageTag = null, CancellationToken cancellation = default);
}
