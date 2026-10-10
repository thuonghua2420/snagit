using SnapGit.App.Controls;
using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Localization;
using SnapGit.Editor;
using SnapGit.Editor.Controls;

namespace SnapGit.App.Forms;

/// <summary>
/// The main window's pages (editor / library / video editor), swapping the editor's document in place, and what "close" means
/// for a window that is the whole app.
/// </summary>
public sealed partial class EditorForm
{
    // ================================================================== pages

    /// <summary>Brings a page to the front; everything the other pages own is hidden (their state is kept).</summary>
    internal void ShowPage(MainPage page)
    {
        if (page == MainPage.Library) EnsureLibrary();
        if (page == MainPage.Video && _video is null) page = MainPage.Editor;

        _page = page;
        if (page != MainPage.Library) _lastWorkPage = page;

        var editor = page == MainPage.Editor;
        _canvas.Visible = editor;
        _history.Visible = editor;
        _toolBox.Visible = editor;
        _styleBar.Visible = editor;
        _actionBar.Visible = editor;
        _statusBar.Visible = editor;
        foreach (var menu in _editorMenus) menu.Visible = editor;

        if (_library is not null) _library.Visible = page == MainPage.Library;
        if (_video is not null) _video.Visible = page == MainPage.Video;

        UpdateTitle();
        if (editor) _canvas.Focus();
        else if (page == MainPage.Library) _library!.Focus();
    }

    /// <summary>The library page: every capture so far.</summary>
    public void ShowLibraryPage() => ShowPage(MainPage.Library);

    public void ShowEditorPage() => ShowPage(MainPage.Editor);

    /// <summary>True when there is an image or a video to go back to (the library has a "Back" button).</summary>
    public bool CanGoBack => _hasWork || _video is not null;

    /// <summary>Back from the library to whatever was being worked on.</summary>
    public void GoBack() => ShowPage(_lastWorkPage == MainPage.Video && _video is not null ? MainPage.Video : MainPage.Editor);

    private void EnsureLibrary()
    {
        if (_library is not null) return;
        _library = new LibraryView(_host) { Visible = false };
        Controls.Add(_library);
        Controls.SetChildIndex(_library, 0);
    }

    // ================================================================== replacing the document

    /// <summary>
    /// Loads a document into the editor (and shows it). Unsaved edits to the current picture are asked about first;
    /// returns false — and the new document is NOT taken over — when the user cancels.
    /// </summary>
    public bool OpenDocument(EditorDocument document, string? filePath, bool isProject)
    {
        if (!ConfirmKeepOrDiscardChanges()) return false;
        SwapDocument(document, filePath, isProject);
        _hasWork = true;
        ShowPage(MainPage.Editor);
        return true;
    }

    /// <summary>Test seam: answers "Save changes to …?" without a dialog (the argument is the picture's name).</summary>
    internal Func<string, DialogResult>? SaveChangesPrompt { get; set; }

    /// <summary>"Save changes to …?" — false when the user cancels (or saving failed).</summary>
    private bool ConfirmKeepOrDiscardChanges()
    {
        if (!_document.IsModified) return true;

        var answer = SaveChangesPrompt?.Invoke(DisplayName) ?? MessageBox.Show(this, Loc.T("Save changes to \"{0}\"?", DisplayName), "SnapGit",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer != DialogResult.Cancel && (answer != DialogResult.Yes || Save());
    }

    private void SwapDocument(EditorDocument document, string? filePath, bool isProject)
    {
        var oldCanvas = _canvas;
        var oldDocument = _document;
        var oldContext = _context;

        DiscardRecovery();
        _recoveryPath = RecoveryService.NewPath();

        _document = document;
        _sourcePath = filePath;
        _projectPath = isProject ? filePath : null;
        _pasteCount = 0;

        _context = new EditorContext(document);
        _context.CopyPreferencesFrom(oldContext); // colours, widths, fonts the user chose carry over from picture to picture
        _canvas = new EditorCanvas(_context) { Dock = DockStyle.Fill, Visible = _page == MainPage.Editor, BackColor = Theme.Colors.Canvas };

        Controls.Remove(oldCanvas);
        Controls.Add(_canvas);
        Controls.SetChildIndex(_canvas, 0);
        oldCanvas.Dispose();
        oldDocument.Dispose();

        WireDocument();
        OnToolChanged();
        OnDocumentChanged();
        UpdateStatus();
        _history.Invalidate();
    }

    /// <summary>A white canvas: what the editor shows when nothing has been opened.</summary>
    internal static EditorDocument BlankDocument()
    {
        var bitmap = new Bitmap(960, 600);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.White);
        return new EditorDocument(bitmap);
    }

    // ================================================================== video

    /// <summary>Opens a video on the video page. Edits to the video that is open now are asked about first (cancel = nothing changes).</summary>
    public async Task<bool> OpenVideoAsync(string path)
    {
        if (_video is { } open && string.Equals(Path.GetFullPath(open.SourcePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            ShowPage(MainPage.Video);
            return true;
        }
        if (_video is { } current && !current.TryClose()) return false;

        var page = new VideoEditorPage(_host, path) { Visible = false };
        Controls.Add(page);
        Controls.SetChildIndex(page, 0);
        if (_video is { } old)
        {
            Controls.Remove(old);
            old.Dispose();
        }
        _video = page;
        ShowPage(MainPage.Video);
        await page.OpenAsync();
        return true;
    }

    // ================================================================== keyboard guard

    /// <summary>Remembers every shortcut of the editor's menus, so they can be switched off while another page is in front.</summary>
    private void CollectEditorShortcuts(IEnumerable<ToolStripItem> items)
    {
        foreach (var item in items)
        {
            if (item is not ToolStripMenuItem menu) continue;
            if (menu.ShortcutKeys != Keys.None) _editorShortcuts.Add(menu.ShortcutKeys);
            CollectEditorShortcuts(menu.DropDownItems.Cast<ToolStripItem>());
        }
    }

    // ================================================================== closing = hiding

    /// <summary>
    /// The window is the whole app, which lives in the tray: closing it asks about unsaved work, then hides it and goes back to
    /// the blank state (a later capture or "Open library" brings it back).
    /// </summary>
    private bool HideInsteadOfClose()
    {
        if (!ConfirmKeepOrDiscardChanges()) return false;
        if (_video is { } video && !video.TryClose()) return false;

        if (_video is { } open)
        {
            Controls.Remove(open);
            open.Dispose();
            _video = null;
        }
        SwapDocument(BlankDocument(), null, false);
        _hasWork = false;
        _lastWorkPage = MainPage.Editor;
        ShowPage(MainPage.Library);
        Hide();
        return true;
    }
}
