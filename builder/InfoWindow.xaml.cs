using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace builder;

/// <summary>Borderless OK notice, styled like the confirm dialog.</summary>
public partial class InfoWindow : Window
{
    public string Message
    {
        get => MessageText.Text;
        set => MessageText.Text = value;
    }

    /// <summary>Icon beside the message (resource path). Defaults to the green check.</summary>
    public string IconSource { get; set; } = "Icons/check.png";

    public InfoWindow()
    {
        InitializeComponent();
        MainWindow.ApplyTextModeTo(this);
        Loaded += (_, _) => PlayOpen();
    }

    /// <summary>Fade + grow in.</summary>
    private void PlayOpen()
    {
        Opacity = 0;
        var scale = new ScaleTransform(0.96, 0.96);
        Root.RenderTransform = scale;
        Root.RenderTransformOrigin = new Point(0.5, 0.5);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
