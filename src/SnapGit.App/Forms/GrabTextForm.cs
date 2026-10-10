using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;

namespace SnapGit.App.Forms;

/// <summary>Shows the text recognised in a capture (Grab Text). Editable; can be re-scanned with another language.</summary>
public sealed class GrabTextForm : Form
{
    private readonly AppHost _host;
    private readonly Bitmap _image;
    private readonly TextBox _text = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 11f), AcceptsReturn = true, WordWrap = false };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(12, 8, 0, 0) };

    public GrabTextForm(AppHost host, Bitmap image, OcrOutput? initial)
    {
        _host = host;
        _image = image;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = Loc.T("Grab Text");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 460);
        MinimumSize = new Size(420, 280);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 0), WrapContents = false };
        var copy = new Button { Text = Loc.T("Copy"), AutoSize = true };
        copy.Click += (_, _) => CopyAll();
        var rescan = new Button { Text = Loc.T("Re-scan"), AutoSize = true };
        rescan.Click += async (_, _) => await ScanAsync();
        var close = new Button { Text = Loc.T("Close"), AutoSize = true };
        close.Click += (_, _) => Close();

        foreach (var lang in _host.Ocr.AvailableLanguages) _language.Items.Add(new LanguageItem(lang));
        var wanted = _host.Settings.Current.OcrLanguage;
        _language.SelectedIndex = Math.Max(0, Array.FindIndex(_language.Items.Cast<LanguageItem>().ToArray(), l => l.Language.Tag.Equals(wanted, StringComparison.OrdinalIgnoreCase)));

        bar.Controls.AddRange([copy, new Label { Text = Loc.T("Language"), AutoSize = true, Margin = new Padding(12, 8, 2, 0) }, _language, rescan, close, _status]);

        Controls.Add(_text);
        Controls.Add(bar);

        if (initial is not null) Display(initial);
        Theme.Apply(this);
        FormClosed += (_, _) => _image.Dispose();
    }

    private void Display(OcrOutput result)
    {
        _text.Text = result.Text;
        _status.Text = string.IsNullOrWhiteSpace(result.Text)
            ? Loc.T("No text found.")
            : Loc.T("{0} line(s), language {1}", result.Lines.Count, result.LanguageTag);
    }

    public async Task ScanAsync()
    {
        _status.Text = Loc.T("Reading…");
        try
        {
            var tag = (_language.SelectedItem as LanguageItem)?.Language.Tag;
            Display(await _host.Ocr.RecognizeAsync(_image, tag));
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void CopyAll()
    {
        if (_text.Text.Length == 0) return;
        Clipboard.SetText(_text.SelectionLength > 0 ? _text.SelectedText : _text.Text);
        _status.Text = Loc.T("Copied.");
    }

    private sealed record LanguageItem(OcrLanguage Language)
    {
        public override string ToString() => $"{Language.DisplayName} ({Language.Tag})";
    }
}
