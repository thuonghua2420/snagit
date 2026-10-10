using System.Globalization;
using System.Text;

namespace SnapGit.Core.Library;

/// <summary>
/// Turns a naming pattern such as <c>"{title} {yyyy-MM-dd HH-mm-ss}"</c> into a file name (without extension).
/// Tokens: <c>{title}</c>, <c>{counter}</c> / <c>{counter:000}</c>, <c>{user}</c>, <c>{machine}</c>;
/// any other token is a .NET date format applied to the capture time.
/// </summary>
public static class NamePattern
{
    public const string Default = "{title} {yyyy-MM-dd HH-mm-ss}";

    /// <summary>What <c>{title}</c> becomes when the capture has no title (e.g. a region capture).</summary>
    public const string UntitledName = "SnapGit";

    public static string Format(string? pattern, DateTime now, string? title = null, int counter = 1)
    {
        var template = string.IsNullOrWhiteSpace(pattern) ? Default : pattern;
        var result = new StringBuilder();

        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                result.Append(template[i]);
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                result.Append(template, i, template.Length - i); // unmatched brace: keep literally
                break;
            }

            result.Append(Expand(template[(i + 1)..close], now, title, counter));
            i = close;
        }

        return Sanitize(result.ToString());
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        // Line breaks/tabs (e.g. from a window title) become spaces; other invalid characters are dropped.
        var spaced = new string(name.Select(c => c is '\r' or '\n' or '\t' ? ' ' : c).ToArray());
        var cleaned = string.Join(' ', new string(spaced.Where(c => !invalid.Contains(c)).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        if (cleaned.Length > 120) cleaned = cleaned[..120].TrimEnd();
        return cleaned.Length == 0 ? "capture" : cleaned;
    }

    private static string Expand(string token, DateTime now, string? title, int counter)
    {
        var lower = token.ToLowerInvariant();
        if (lower == "title") return string.IsNullOrWhiteSpace(title) ? UntitledName : title.Trim();
        if (lower == "user") return Environment.UserName;
        if (lower == "machine") return Environment.MachineName;
        if (lower == "counter") return counter.ToString(CultureInfo.InvariantCulture);
        if (lower.StartsWith("counter:", StringComparison.Ordinal))
            return counter.ToString(token["counter:".Length..], CultureInfo.InvariantCulture);

        try { return now.ToString(token, CultureInfo.InvariantCulture); }
        catch (FormatException) { return "{" + token + "}"; }
    }
}
