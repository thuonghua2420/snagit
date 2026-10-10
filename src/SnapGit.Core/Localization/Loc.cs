using System.Globalization;

namespace SnapGit.Core.Localization;

/// <summary>
/// Minimal localisation: the English text IS the key (<c>Loc.T("Save")</c>), so code stays readable and a missing
/// translation simply shows English. Vietnamese lives in <see cref="Vietnamese"/>. A unit test scans the source tree
/// and fails when a <c>Loc.T("...")</c> literal has no Vietnamese entry.
/// </summary>
public static class Loc
{
    private static IReadOnlyDictionary<string, string> _table = new Dictionary<string, string>();

    public static string Language { get; private set; } = "en";

    /// <summary>Languages offered in Settings: (code, display name in its own language).</summary>
    public static IReadOnlyList<(string Code, string Name)> Available { get; } =
    [
        ("auto", "Auto"),
        ("en", "English"),
        ("vi", "Tiếng Việt"),
    ];

    /// <param name="language">"auto" follows the Windows display language.</param>
    public static void SetLanguage(string? language)
    {
        var code = string.IsNullOrWhiteSpace(language) || language == "auto"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : language;

        Language = code;
        _table = code == "vi" ? Vietnamese.Table : new Dictionary<string, string>();
    }

    public static string T(string english) => _table.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>
    /// Marks a literal as translatable without translating it now (for static tables); display it later with
    /// <see cref="T(string)"/>. The localisation test treats <c>Loc.N("...")</c> exactly like <c>Loc.T("...")</c>.
    /// </summary>
    public static string N(string english) => english;

    public static string T(string english, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);
}
