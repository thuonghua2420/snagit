using SnapGit.App.Services;
using SnapGit.Core.Diagnostics;

namespace SnapGit.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SnapGit.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance) return; // already running in the tray

        ApplicationConfiguration.Initialize();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        var startInBackground = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        using var host = new AppHost();
        Application.Run(new TrayApplicationContext(host, startInBackground));
    }

    private static void ReportCrash(Exception? ex)
    {
        Log.Error("Unhandled exception", ex);
        MessageBox.Show($"SnapGit hit an unexpected error:\n\n{ex?.Message}\n\nDetails were written to:\n{AppPaths.LogFolder}",
            "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
