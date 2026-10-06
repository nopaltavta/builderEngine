using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using builder.Viewport;

namespace builder;

/// <summary>Waypoint loop manager for a moving platform. "Add waypoint here"
/// captures the platform's current position (move it with the Move tool first).
/// The platform loops 1-2-3-1 at Move speed in Play mode; marker balls show
/// the track in edit mode. Chrome matches the studio dialogs.</summary>
public sealed class WaypointEditorWindow : Window
{
    private readonly SceneObject _platform;
    private readonly ObservableCollection<string> _items = new();
    private readonly List<OpenTK.Mathematics.Vector3> _points = new();
    private readonly Border _root;
    private readonly ListBox _list;
    private readonly TextBlock _counter;
    private readonly TextBlock _hint;

    /// <summary>Saved track (copy). Empty clears the loop (ping-pong fallback).</summary>
    public List<OpenTK.Mathematics.Vector3> Waypoints { get; } = new();

    /// <summary>True when Save closed the dialog (vs Cancel/X/Escape).</summary>
    public bool Accepted { get; private set; }

    public WaypointEditorWindow(SceneObject platform)
    {
        _platform = platform;
        Title = "Mover waypoints";
        Width = 420;
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
        var deep = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var edge = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var text = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        var dim = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var accent = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));

        _root = new Border { Background = body };
        var dock = new DockPanel();
        _root.Child = dock;

        var titleBar = new Grid { Height = 38, Background = accent };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
        var title = new TextBlock
        {
            Text = "Mover waypoints", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
        };
        titleBar.Children.Add(title);
        var close = new Button
        {
            Content = "✕", Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "Close (Esc)",
            Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(0), Width = 36, Height = 24,
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);

        var content = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        dock.Children.Add(content);

        content.Children.Add(new TextBlock
        {
            Text = "Move the platform with the Move tool, then capture each stop. Play mode loops 1-2-3-1 at Move speed (one point holds, empty keeps ping-pong).",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = text,
        });

        _list = new ListBox
        {
            Height = 180, Background = deep, Foreground = text,
            BorderBrush = edge, BorderThickness = new Thickness(1),
            FontSize = 13, SelectionMode = SelectionMode.Single,
        };
        _list.ItemsSource = _items;
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; } };
        content.Children.Add(_list);

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var capture = DarkButton("Add waypoint here");
        capture.Click += (_, _) => CaptureHere();
        var up = DarkButton("Up", 56);
        up.Margin = new Thickness(8, 0, 0, 0);
        up.Click += (_, _) => MoveSelected(-1);
        var down = DarkButton("Down", 56);
        down.Margin = new Thickness(8, 0, 0, 0);
        down.Click += (_, _) => MoveSelected(1);
        var delete = DarkButton("Delete", 72);
        delete.Margin = new Thickness(8, 0, 0, 0);
        delete.Click += (_, _) => DeleteSelected();
        addRow.Children.Add(capture);
        addRow.Children.Add(up);
        addRow.Children.Add(down);
        addRow.Children.Add(delete);
        content.Children.Add(addRow);

        _hint = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x80)), TextWrapping = TextWrapping.Wrap };
        content.Children.Add(_hint);
        _counter = new TextBlock { Margin = new Thickness(0, 2, 0, 0), Foreground = dim };
        content.Children.Add(_counter);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = DarkButton("Cancel", 80);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        var save = DarkButton("Save", 80);
        save.IsDefault = true;
        save.Click += (_, _) => { Waypoints.Clear(); Waypoints.AddRange(_points); Accepted = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        content.Children.Add(buttons);

        Content = _root;
        Loaded += (_, _) => PlayOpen();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        foreach (var w in platform.Waypoints)
        {
            if (_points.Count >= SceneObject.MaxWaypoints) break;
            _points.Add(w);
            _items.Add(Label(_points.Count, w));
        }
        RefreshCounter();
    }

    private static string Label(int n, OpenTK.Mathematics.Vector3 w) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0}, {2:0.0}, {3:0.0}", n, w.X, w.Y, w.Z);

    private static Button DarkButton(string label, double minWidth = 72) => new()
    {
        Content = label,
        MinWidth = minWidth,
        Padding = new Thickness(0, 4, 0, 4),
        Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        BorderThickness = new Thickness(1),
    };

    private void CaptureHere()
    {
        if (_points.Count >= SceneObject.MaxWaypoints)
        {
            _hint.Text = $"Keep it to {SceneObject.MaxWaypoints} waypoints — delete one first.";
            return;
        }
        var p = _platform.Position;
        _points.Add(p);
        _items.Add(Label(_points.Count, p));
        _list.SelectedIndex = _items.Count - 1;
        _list.ScrollIntoView(_list.SelectedItem);
        _hint.Text = "";
        RefreshCounter();
    }

    private void MoveSelected(int dir)
    {
        int i = _list.SelectedIndex;
        int j = i + dir;
        if (i < 0 || j < 0 || j >= _points.Count) return;
        (_points[i], _points[j]) = (_points[j], _points[i]);
        (_items[i], _items[j]) = (_items[j], _items[i]);
        Renumber();
        _list.SelectedIndex = j;
    }

    private void DeleteSelected()
    {
        int i = _list.SelectedIndex;
        if (i < 0) return;
        _points.RemoveAt(i);
        _items.RemoveAt(i);
        Renumber();
        if (_items.Count > 0) _list.SelectedIndex = Math.Min(i, _items.Count - 1);
        RefreshCounter();
    }

    private void Renumber()
    {
        for (int i = 0; i < _points.Count; i++) _items[i] = Label(i + 1, _points[i]);
    }

    private void RefreshCounter() =>
        _counter.Text = $"{_points.Count} waypoint{(_points.Count == 1 ? "" : "s")}";

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
}
