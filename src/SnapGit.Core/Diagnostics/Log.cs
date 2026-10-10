using System.Text;

namespace SnapGit.Core.Diagnostics;

/// <summary>Tiny rolling-per-day file logger. Never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(level).Append(' ').Append(message);
            if (ex is not null) sb.AppendLine().Append(ex);

            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogFolder);
                var file = Path.Combine(AppPaths.LogFolder, $"snapgit-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, sb.AppendLine().ToString());
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
