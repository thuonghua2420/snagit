using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Localization;
using SnapGit.Core.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace SnapGit.App.Services;

/// <summary>Sends a library file somewhere: Windows share sheet, a folder, a program, or the clipboard.</summary>
public sealed class ShareService
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    /// <summary>Destinations that always exist (the user's own are in settings).</summary>
    public static IReadOnlyList<ShareDestination> BuiltIn { get; } =
    [
        new ShareDestination { Name = Loc.N("Share…"), Kind = ShareKind.WindowsShare },
        new ShareDestination { Name = Loc.N("Copy file to clipboard"), Kind = ShareKind.ClipboardFile },
    ];

    /// <returns>A short message describing what happened (for the status line / tray).</returns>
    public string Share(IWin32Window owner, string filePath, ShareDestination destination)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("The file no longer exists.", filePath);

        switch (destination.Kind)
        {
            case ShareKind.Folder:
                Directory.CreateDirectory(destination.Target);
                var target = Path.Combine(destination.Target, Path.GetFileName(filePath));
                File.Copy(filePath, target, overwrite: true);
                return Loc.T("Copied to {0}", destination.Target);

            case ShareKind.Program:
                var arguments = destination.Arguments.Replace("{file}", $"\"{filePath}\"", StringComparison.OrdinalIgnoreCase);
                Process.Start(new ProcessStartInfo(destination.Target, arguments) { UseShellExecute = true });
                return Loc.T("Opened with {0}", Path.GetFileNameWithoutExtension(destination.Target));

            case ShareKind.ClipboardFile:
                var files = new StringCollection { filePath };
                System.Windows.Forms.Clipboard.SetFileDropList(files);
                return Loc.T("File copied to the clipboard.");

            case ShareKind.WindowsShare:
                ShowWindowsShare(owner.Handle, filePath);
                return Loc.T("Choose where to share.");

            default:
                throw new NotSupportedException(destination.Kind.ToString());
        }
    }

    /// <summary>Reveals the file in Explorer.</summary>
    public static void ShowInExplorer(string filePath) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });

    private static void ShowWindowsShare(IntPtr window, string filePath)
    {
        try
        {
            var interop = (IDataTransferManagerInterop)WinRT.ActivationFactory.Get("Windows.ApplicationModel.DataTransfer.DataTransferManager", typeof(IDataTransferManagerInterop).GUID);
            var iid = DataTransferManagerIid;
            var pointer = interop.GetForWindow(window, ref iid);
            var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(pointer);

            manager.DataRequested += async (_, args) =>
            {
                var deferral = args.Request.GetDeferral();
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(filePath);
                    args.Request.Data.Properties.Title = Path.GetFileNameWithoutExtension(filePath);
                    args.Request.Data.SetStorageItems([file]);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Share request failed: {ex.Message}");
                    args.Request.FailWithDisplayText(ex.Message);
                }
                finally
                {
                    deferral.Complete();
                }
            };

            interop.ShowShareUIForWindow(window);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            throw new InvalidOperationException("The Windows share sheet is not available on this system.", ex);
        }
    }
}
