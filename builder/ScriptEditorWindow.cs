using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace builder;

/// <summary>Lua code editor for a part (Play-mode scripts). Chrome matches the
/// studio dialogs (borderless, orange title bar, dark body). Save commits via
/// ShowDialog()==true; the caller owns undo + persistence.</summary>
public sealed class ScriptEditorWindow : Window
{
    public const int MaxChars = 8000;

    private readonly TextBox _code;
    private readonly TextBlock _counter;
    private readonly TextBlock _help;

    /// <summary>Trimmed code (capped whole, never mid-line... capped at MaxChars).</summary>
    public string Code => _code.Text.Length > MaxChars ? _code.Text[..MaxChars] : _code.Text;

    private static readonly System.Collections.Generic.Dictionary<string, string> Snippets = new()
    {
        ["Door (touch to open)"] =
            "-- Touch to slide open, closes after 3 seconds\n" +
            "function onTouch(player)\n" +
            "  part.Position = {x=part.Position.x, y=part.Position.y + 4, z=part.Position.z}\n" +
            "  game:playSound(\"stomp\", 0.8)\n" +
            "  game:after(3, function()\n" +
            "    part.Position = {x=part.Position.x, y=part.Position.y - 4, z=part.Position.z}\n" +
            "  end)\n" +
            "end\n",
        ["Blinking lamp"] =
            "-- Gentle glow pulse\n" +
            "function onTick(dt)\n" +
            "  part.Transparency = 0.3 + 0.2 * math.sin(game.time * 3)\n" +
            "end\n",
        ["Touch counter"] =
            "-- Counts every touch in Output\n" +
            "n = 0\n" +
            "function onTouch(player)\n" +
            "  n = n + 1\n" +
            "  game:log(\"touched \" .. n .. \" times\")\n" +
            "  game:playSound(\"stomp\", 0.5)\n" +
            "end\n",
        ["Launch pad"] =
            "-- Touch to launch upward\n" +
            "function onTouch(player)\n" +
            "  game:teleportPlayer(part.Position.x, part.Position.y + 10, part.Position.z)\n" +
            "end\n",
        ["Kill zone"] =
            "-- Touch = ouch\n" +
            "function onTouch(player)\n" +
            "  game:damagePlayer(100)\n" +
            "  game:playSound(\"stomp\", 1)\n" +
            "end\n",
        ["Ball dropper"] =
            "-- Drops a ball above every 2 seconds\n" +
            "t = 0\n" +
            "function onTick(dt)\n" +
            "  t = t + dt\n" +
            "  if t >= 2 then\n" +
            "    t = 0\n" +
            "    game:createPart{shape=\"Ball\",\n" +
            "      position={x=part.Position.x, y=part.Position.y + 5, z=part.Position.z}}\n" +
            "  end\n" +
            "end\n",
    };

    public ScriptEditorWindow(string code, string partName)
    {
        Title = "Script — " + partName;
        Width = 600;
        Height = 540;
        MinWidth = 420;
        MinHeight = 360;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MainWindow.ApplyTextModeTo(this);
        FontSize = 12;
        Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));

        var body = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
        var field = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var edge = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var dim = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var accent = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));

        var root = new Border { Background = body, BorderBrush = edge, BorderThickness = new Thickness(1) };
        var dock = new DockPanel();
        root.Child = dock;
        Content = root;

        var titleBar = new Grid { Height = 38, Background = accent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "Script — " + partName,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        Grid.SetColumn(title, 0);
        titleBar.Children.Add(title);
        var close = new Button
        {
            Content = "✕", Width = 36, Height = 24, Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Arrow,
        };
        close.Click += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 12, 12),
        };
        var helpBtn = new Button
        {
            Content = "API help", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0),
            Background = field, Foreground = Brushes.White, BorderBrush = edge, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        var clearBtn = new Button
        {
            Content = "Clear", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0),
            Background = field, Foreground = Brushes.White, BorderBrush = edge, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        var cancelBtn = new Button
        {
            Content = "Cancel", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0),
            Background = field, Foreground = Brushes.White, BorderBrush = edge, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };
        var saveBtn = new Button
        {
            Content = "Save", Padding = new Thickness(16, 4, 16, 4),
            Background = accent, Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, IsDefault = true,
        };
        bottom.Children.Add(helpBtn);
        bottom.Children.Add(clearBtn);
        bottom.Children.Add(cancelBtn);
        bottom.Children.Add(saveBtn);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);

        var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 4) };
        _counter = new TextBlock { Foreground = dim, FontSize = 11 };
        status.Children.Add(_counter);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(status);

        _help = new TextBlock
        {
            Text = CheatSheet,
            Foreground = dim,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 4, 12, 4),
            Visibility = Visibility.Collapsed,
            FontFamily = new FontFamily("Consolas"),
        };
        var helpScroll = new ScrollViewer
        {
            Content = _help,
            MaxHeight = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
        };
        DockPanel.SetDock(helpScroll, Dock.Bottom);
        dock.Children.Add(helpScroll);
        var snipBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 8, 12, 0),
        };
        var snipBox = new ComboBox { Width = 220, Background = field, Foreground = Brushes.White };
        foreach (string key in Snippets.Keys) snipBox.Items.Add(key);
        snipBox.SelectedIndex = 0;
        var snipBtn = new Button
        {
            Content = "Insert snippet", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0),
            Background = field, Foreground = Brushes.White, BorderBrush = edge, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "Paste a starter script at the cursor",
        };
        void InsertSelectedSnippet()
        {
            if (_code == null) return; // still constructing (initial SelectedIndex=0)
            if (snipBox.SelectedItem is string key && Snippets.TryGetValue(key, out var snippet))
            {
                _code.SelectedText = snippet;
                _code.Focus();
            }
        }
        snipBtn.Click += (_, _) => InsertSelectedSnippet();
        // Picking a snippet pastes it right away (at the cursor, like the
        // button): a dropdown that only arms the button reads as broken.
        snipBox.SelectionChanged += (_, _) => InsertSelectedSnippet();
        snipBar.Children.Add(snipBox);
        snipBar.Children.Add(snipBtn);
        DockPanel.SetDock(snipBar, Dock.Top);
        dock.Children.Add(snipBar);

        _code = new TextBox
        {
            Text = code ?? "",
            Background = field,
            Foreground = Brushes.White,
            BorderBrush = edge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(12, 8, 12, 0),
            Padding = new Thickness(8, 6, 8, 6),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        SpellCheck.SetIsEnabled(_code, false);
        dock.Children.Add(_code);

        _code.TextChanged += (_, _) => UpdateCounter();
        helpBtn.Click += (_, _) =>
        {
            bool show = helpScroll.Visibility != Visibility.Visible;
            helpScroll.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        };
        clearBtn.Click += (_, _) => { _code.Text = ""; };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        saveBtn.Click += (_, _) => { DialogResult = true; Close(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                DialogResult = true; Close(); e.Handled = true;
            }
        };
        Loaded += (_, _) => { _code.Focus(); _code.CaretIndex = _code.Text.Length; };
        UpdateCounter();
    }

    private void UpdateCounter()
    {
        _counter.Text = $"{_code.Text.Length} / {MaxChars} chars · Ctrl+S saves · empty script = disabled";
    }

    private const string CheatSheet =
        "EVENTS (global functions, no 'local'!)\n" +
        "  onTick(dt)         — every frame while playing\n" +
        "  onTouch(player)    — when the avatar touches this part\n" +
        "PART (this part, or game:findPart(\"Name\"))\n" +
        "  part.Position      — {x=, y=, z=} table, read/write\n" +
        "  part.Color         — {r=, g=, b=} 0-1 table, read/write\n" +
        "  part.Size          — read/write (recreates physics)\n" +
        "  part.Transparency  — 0-1 · part.Anchored — true/false\n" +
        "  part.CanCollide — true/false (ghost when off)\n" +
        "  part.Visible       — true/false · part.Name — text\n" +
        "  part:isTouched()   — avatar overlap right now\n" +
        "  part:destroy()     — remove (restored when play stops)\n" +
        "GAME\n" +
        "  game.time · game:log(\"hi\") · print(...) → Output\n" +
        "  game:playSound(\"stomp\" [, 0.8])\n" +
        "  game:damagePlayer(25) · game:healPlayer(10)\n" +
        "  game:teleportPlayer(x, y, z)\n" +
        "  game:createPart{shape=\"Ball\", position={x=0,y=5,z=0}}\n" +
        "  game:after(2.5, function() ... end)\n" +
        "  game:wait(1.5) — pause here, resume later (loops forever safely)\n" +
        "RULES\n" +
        "  No files, network, os, require. Infinite loops are killed\n" +
        "  (script disabled, message in Output). Errors never stop play.";
}
