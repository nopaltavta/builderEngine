using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace builder;

/// <summary>Chat-line manager for an NPC (friendly dialogue or enemy taunts).
/// Each list entry is one speech bubble; the NPC picks a random entry at runtime.
/// Storage stays the pipe-joined <see cref="Dialogue"/> string, capped at whole lines.
/// Chrome matches the studio dialogs (borderless, orange title bar, dark body).</summary>
public sealed class DialogueEditorWindow : Window
{
    private const int MaxChars = 800;
    private const int MaxLines = 12;

    private readonly ObservableCollection<string> _items = new();
    private readonly Border _root;
    private readonly ListBox _list;
    private readonly TextBox _input;
    private readonly Button _addButton;
    private readonly Button _updateButton;
    private readonly Button _deleteButton;
    private readonly Button _saveButton;
    private readonly TextBlock _counter;
    private readonly TextBlock _hint;
    private readonly TextBlock _previewBody;
    private readonly Random _random = new();
    private string? _lastPreview;

    /// <summary>Pipe-joined lines. Never cuts mid-line: trailing lines that would
    /// overflow the cap are dropped instead.</summary>
    public string Dialogue
    {
        get
        {
            var kept = _items.Take(MaxLines).ToList();
            while (kept.Count > 0 && string.Join("|", kept).Length > MaxChars)
                kept.RemoveAt(kept.Count - 1);
            return string.Join("|", kept);
        }
    }

    public DialogueEditorWindow(string dialogue)
    {
        Title = "NPC chat";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MainWindow.ApplyTextModeTo(this);
        FontSize = 12;
        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));

        var body = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        var field = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
        var deep = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var edge = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var text = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        var dim = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var accent = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));

        _root = new Border { Background = body };
        var dock = new DockPanel();
        _root.Child = dock;

        // Custom title bar: drag to move (same as Settings / Confirm).
        var titleBar = new Grid { Height = 38, Background = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
        var title = new TextBlock
        {
            Text = "NPC chat", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
        };
        titleBar.Children.Add(title);
        var close = new Button
        {
            Content = "✕", Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Close (Esc)",
            Style = TitleCloseStyle(),
        };
        close.Click += (_, _) => DialogResult = false;
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);

        var content = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        dock.Children.Add(content);

        content.Children.Add(new TextBlock
        {
            Text = "Each entry is one speech bubble. In Play mode the NPC greets you on approach, then says a random entry (never twice in a row) while you stay in Chat Range.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = text,
        });

        // Preview bubble + dice button.
        var previewRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        var dice = DarkButton("🎲 Test", 72, primary: false);
        dice.Margin = new Thickness(8, 0, 0, 0);
        dice.VerticalAlignment = VerticalAlignment.Center;
        dice.Click += (_, _) => PreviewRandom();
        DockPanel.SetDock(dice, Dock.Right);
        previewRow.Children.Add(dice);
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(253, 253, 253)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 8, 6),
        };
        _previewBody = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.Black), Text = "…" };
        bubble.Child = _previewBody;
        previewRow.Children.Add(bubble);
        content.Children.Add(previewRow);

        // The line list.
        _list = new ListBox
        {
            Height = 200, Background = deep, Foreground = text,
            BorderBrush = edge, BorderThickness = new Thickness(1),
            FontSize = 13, SelectionMode = SelectionMode.Single,
            Style = DialogListBoxStyle(),
            ItemContainerStyle = ListItemStyle(),
        };
        _list.ItemsSource = _items;
        _list.SelectionChanged += (_, _) => RefreshButtons();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; } };
        _list.MouseDoubleClick += (_, _) => LoadSelectedIntoInput();
        content.Children.Add(_list);

        // Input row: text + Add.
        var inputRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        _addButton = DarkButton("Add", 72, primary: false);
        _addButton.Margin = new Thickness(8, 0, 0, 0);
        _addButton.Click += (_, _) => AddLine();
        DockPanel.SetDock(_addButton, Dock.Right);
        inputRow.Children.Add(_addButton);
        _input = new TextBox
        {
            Background = field, Foreground = Brushes.White, BorderBrush = edge, BorderThickness = new Thickness(1),
            CaretBrush = Brushes.White, Padding = new Thickness(6, 4, 6, 4),
            Style = DialogTextBoxStyle(),
        };
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddLine(); e.Handled = true; } };
        _input.TextChanged += (_, _) => RefreshButtons();
        inputRow.Children.Add(_input);
        content.Children.Add(inputRow);

        // Edit row: Update / Delete selected.
        var editRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _updateButton = DarkButton("Update selected", 120, primary: false);
        _updateButton.Margin = new Thickness(0, 0, 8, 0);
        _updateButton.Click += (_, _) => UpdateSelected();
        _deleteButton = DarkButton("Delete", 80, primary: false);
        _deleteButton.Click += (_, _) => DeleteSelected();
        editRow.Children.Add(_updateButton);
        editRow.Children.Add(_deleteButton);
        content.Children.Add(editRow);

        _hint = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x80)), TextWrapping = TextWrapping.Wrap };
        content.Children.Add(_hint);

        _counter = new TextBlock { Margin = new Thickness(0, 2, 0, 0), Foreground = dim };
        content.Children.Add(_counter);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = DarkButton("Cancel", 80, primary: false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        _saveButton = DarkButton("Save", 80, primary: true);
        _saveButton.IsDefault = true;
        _saveButton.Click += (_, _) => { if (_items.Count > 0) DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_saveButton);
        content.Children.Add(buttons);

        Content = _root;
        Loaded += (_, _) => { PlayOpen(); _input.Focus(); };

        foreach (var line in dialogue.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (_items.Count >= MaxLines) break;
            // Legacy over-long file? Keep whole leading lines that fit, drop the rest.
            string probe = _items.Count == 0 ? line : string.Join("|", _items) + "|" + line;
            if (probe.Length > MaxChars) break;
            _items.Add(line);
        }
        RefreshButtons();
        UpdateCounter();
    }

    /// <summary>Studio dialog button: blue primary or dark secondary.
    /// Fully explicit template so the app-wide Fluent theme can't recolor it.</summary>
    private static Style? _buttonStyle;

    private static Button DarkButton(string label, double minWidth, bool primary)
    {
        _buttonStyle ??= DialogButtonStyle();
        return new Button
        {
            Content = label, MinWidth = minWidth, Padding = new Thickness(0, 4, 0, 4),
            Background = primary
                ? new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22))
                : new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            Foreground = Brushes.White,
            BorderThickness = primary ? new Thickness(0) : new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Style = _buttonStyle,
        };
    }

    private static Style DialogButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty, BindParent("Background"));
        border.SetBinding(Border.BorderBrushProperty, BindParent("BorderBrush"));
        border.SetBinding(Border.BorderThicknessProperty, BindParent("BorderThickness"));
        border.SetBinding(Border.PaddingProperty, BindParent("Padding"));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, BindParent("Content"));
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, BindParent("ContentTemplate"));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(TextElement.ForegroundProperty, BindParent("Foreground"));
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), "Bd"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8, "Bd"));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Bd"));
        template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    /// <summary>Dark text field with a blue focus ring. Explicit template so the
    /// app-wide theme can't paint it white.</summary>
    private static Style DialogTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty, BindParent("Background"));
        border.SetBinding(Border.BorderBrushProperty, BindParent("BorderBrush"));
        border.SetBinding(Border.BorderThicknessProperty, BindParent("BorderThickness"));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetBinding(FrameworkElement.MarginProperty, BindParent("Padding"));
        host.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        template.VisualTree = border;
        var focused = new Trigger { Property = UIElement.IsFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), "Bd"));
        template.Triggers.Add(focused);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55, "Bd"));
        template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    /// <summary>Dark list box. Explicit template so the app-wide theme can't paint it white.</summary>
    private static Style DialogListBoxStyle()
    {
        var style = new Style(typeof(ListBox));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(ListBox));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty, BindParent("Background"));
        border.SetBinding(Border.BorderBrushProperty, BindParent("BorderBrush"));
        border.SetBinding(Border.BorderThicknessProperty, BindParent("BorderThickness"));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.PaddingProperty, new Thickness(1));
        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        scroll.AppendChild(items);
        border.AppendChild(scroll);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Binding BindParent(string path) =>
        new Binding(path) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };

    /// <summary>Transparent ✕ with a red hover, like the Settings title bar.</summary>
    private static Style TitleCloseStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.WidthProperty, 36.0));
        style.Setters.Add(new Setter(Control.HeightProperty, 24.0));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)), "Bd"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        template.Triggers.Add(hover);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    /// <summary>Dark rows with studio-blue selection. Explicit template so the
    /// app-wide theme can't swallow the highlight.</summary>
    private static Style ListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4))));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetBinding(Border.BackgroundProperty, BindParent("Background"));
        border.SetBinding(Border.PaddingProperty, BindParent("Padding"));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, BindParent("Content"));
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, BindParent("ContentTemplate"));
        presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, BindParent("HorizontalContentAlignment"));
        presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty, BindParent("VerticalContentAlignment"));
        border.AppendChild(presenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        var active = new MultiTrigger();
        active.Conditions.Add(new Condition(Selector.IsSelectedProperty, true));
        active.Conditions.Add(new Condition(Selector.IsSelectionActiveProperty, true));
        active.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22))));
        active.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(active);
        var inactive = new MultiTrigger();
        inactive.Conditions.Add(new Condition(Selector.IsSelectedProperty, true));
        inactive.Conditions.Add(new Condition(Selector.IsSelectionActiveProperty, false));
        inactive.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46))));
        inactive.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(inactive);
        return style;
    }

    /// <summary>Fade + grow in, like the confirmation dialog.</summary>
    private void PlayOpen()
    {
        Opacity = 0;
        var scale = new ScaleTransform(0.96, 0.96);
        _root.RenderTransform = scale;
        _root.RenderTransformOrigin = new Point(0.5, 0.5);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void Say(string msg) => _hint.Text = msg;

    private bool Validate(string text, int ignoreIndex = -1)
    {
        if (string.IsNullOrWhiteSpace(text)) { Say("Type a line first."); return false; }
        if (text.Contains('|')) { Say("'|' separates stored lines — remove it from the text."); return false; }
        for (int i = 0; i < _items.Count; i++)
            if (i != ignoreIndex && _items[i].Equals(text, StringComparison.OrdinalIgnoreCase))
            { Say("That line is already in the list."); return false; }
        return true;
    }

    private bool Fits(string candidate, int ignoreIndex = -1)
    {
        var probe = _items.ToList();
        if (ignoreIndex >= 0) probe[ignoreIndex] = candidate; else probe.Add(candidate);
        if (probe.Count > MaxLines) { Say($"Keep it to {MaxLines} lines — delete one first."); return false; }
        if (string.Join("|", probe).Length > MaxChars) { Say("Character limit reached — shorten or delete a line."); return false; }
        return true;
    }

    private void AddLine()
    {
        string text = _input.Text.Trim();
        if (!Validate(text) || !Fits(text)) return;
        _items.Add(text);
        _input.Clear();
        _list.SelectedIndex = _items.Count - 1;
        _list.ScrollIntoView(_list.SelectedItem);
        Say("");
        RefreshButtons();
        UpdateCounter();
        _input.Focus();
    }

    private void UpdateSelected()
    {
        int i = _list.SelectedIndex;
        if (i < 0) { Say("Select a line first."); return; }
        string text = _input.Text.Trim();
        if (!Validate(text, i) || !Fits(text, i)) return;
        _items[i] = text;
        Say("");
        RefreshButtons();
        UpdateCounter();
        _input.Focus();
    }

    private void DeleteSelected()
    {
        int i = _list.SelectedIndex;
        if (i < 0) { Say("Select a line first."); return; }
        _items.RemoveAt(i);
        if (_items.Count > 0) _list.SelectedIndex = Math.Min(i, _items.Count - 1);
        Say("");
        RefreshButtons();
        UpdateCounter();
    }

    private void LoadSelectedIntoInput()
    {
        if (_list.SelectedItem is string s)
        {
            _input.Text = s;
            _input.Focus();
            _input.SelectAll();
        }
    }

    private void PreviewRandom()
    {
        if (_items.Count == 0) { Say("Add a line first."); return; }
        int pick = _random.Next(_items.Count);
        if (_items.Count > 1 && _items.Any(s => s != _lastPreview))
            while (_items[pick] == _lastPreview) pick = _random.Next(_items.Count);
        _lastPreview = _items[pick];
        _previewBody.Text = _lastPreview;
        Say("");
    }

    private void RefreshButtons()
    {
        bool hasSelection = _list.SelectedIndex >= 0;
        bool hasInput = !string.IsNullOrWhiteSpace(_input.Text);
        _addButton.IsEnabled = hasInput;
        _updateButton.IsEnabled = hasSelection && hasInput;
        _deleteButton.IsEnabled = hasSelection;
        _saveButton.IsEnabled = _items.Count > 0;
    }

    private void UpdateCounter() =>
        _counter.Text = $"{_items.Count} line{(_items.Count == 1 ? "" : "s")} · {Dialogue.Length}/{MaxChars} chars";
}
