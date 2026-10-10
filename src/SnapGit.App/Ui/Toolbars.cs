namespace SnapGit.App.Ui;

/// <summary>
/// Builders for the app's toolbars so every page has the same compact proportions: 16 px colour icons in 24 px buttons,
/// a 26 px bar, hairline separators, tooltips (= accessible names), and one accent button for the primary action.
/// </summary>
public static class Toolbars
{
    /// <summary>Logical icon size; the renderer draws the icon at the real pixel size of the slot (16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %).</summary>
    public const int IconPixels = 16;

    public static ToolStrip NewBar(DockStyle dock) => new()
    {
        Dock = dock,
        GripStyle = ToolStripGripStyle.Hidden,
        ImageScalingSize = new Size(IconPixels, IconPixels),
        Padding = new Padding(3, 1, 3, 1),
        CanOverflow = true,
    };

    /// <summary>Icon button; give <paramref name="text"/> only where the icon alone would not be clear.</summary>
    public static ToolStripButton Button(UiIcon icon, string tip, Action? onClick = null, string? text = null)
    {
        var button = new ToolStripButton(Icons.Get(icon))
        {
            ToolTipText = tip,
            AccessibleName = tip,
            DisplayStyle = text is null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Text = text ?? string.Empty,
        };
        Shape(button, text is not null);
        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Icon button that stays pressed (bold, mute, crop…): the pressed state is the tinted, outlined button.</summary>
    public static ToolStripButton Toggle(UiIcon icon, string tip, Action<bool>? onChanged = null)
    {
        var button = Button(icon, tip);
        button.CheckOnClick = true;
        button.CheckedChanged += (_, _) => onChanged?.Invoke(button.Checked);
        return button;
    }

    /// <summary>Icon button that opens a menu.</summary>
    public static ToolStripDropDownButton Menu(UiIcon icon, string tip, string? text = null)
    {
        var menu = new ToolStripDropDownButton(Icons.Get(icon))
        {
            ToolTipText = tip,
            AccessibleName = tip,
            DisplayStyle = text is null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Text = text ?? string.Empty,
            ShowDropDownArrow = true,
        };
        Shape(menu, text is not null);
        menu.DropDown.ImageScalingSize = new Size(IconPixels, IconPixels);
        return menu;
    }

    /// <summary>The one accent-filled button of a page (Capture, Export): white icon + text on the accent colour.</summary>
    public static ToolStripDropDownButton PrimaryMenu(UiIcon icon, string text, string tip)
    {
        var menu = new ToolStripDropDownButton(Icons.Get(icon, Theme.Colors.OnAccent))
        {
            ToolTipText = tip,
            AccessibleName = tip,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Text = text,
            ShowDropDownArrow = true,
            Tag = ModernRenderer.PrimaryTag,
        };
        Shape(menu, withText: true);
        menu.Padding = new Padding(6, 4, 4, 4);
        menu.DropDown.ImageScalingSize = new Size(IconPixels, IconPixels);
        return menu;
    }

    public static ToolStripButton Primary(UiIcon icon, string text, string tip, Action onClick)
    {
        var button = new ToolStripButton(Icons.Get(icon, Theme.Colors.OnAccent))
        {
            ToolTipText = tip,
            AccessibleName = tip,
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Text = text,
            Tag = ModernRenderer.PrimaryTag,
        };
        Shape(button, withText: true);
        button.Padding = new Padding(6, 4, 8, 4);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static ToolStripSeparator Separator() => new() { Margin = new Padding(3, 0, 3, 0) };

    private static void Shape(ToolStripItem item, bool withText)
    {
        item.Padding = withText ? new Padding(4, 4, 6, 4) : new Padding(4);
        item.Margin = new Padding(0, 1, 0, 1);
        item.AutoSize = true;
    }

    /// <summary>A menu item with an icon (dropdowns draw them at 16 px).</summary>
    public static ToolStripMenuItem Item(string text, UiIcon? icon, Action onClick, Keys shortcut = Keys.None, string? shortcutText = null)
    {
        var item = new ToolStripMenuItem(text, icon is { } i ? Icons.Get(i) : null);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        if (shortcutText is not null) item.ShortcutKeyDisplayString = shortcutText;
        item.Click += (_, _) => onClick();
        return item;
    }
}
