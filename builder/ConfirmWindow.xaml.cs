using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace builder;

/// <summary>Borderless Yes/No confirmation, styled like the settings dialog.</summary>
public partial class ConfirmWindow : Window
{
    public string Message
    {
        get => MessageText.Text;
        set => MessageText.Text = value;
    }

    public ConfirmWindow()
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

    // Dismissal is synchronous on purpose: fading out across Close() races the
    // dialog lifetime (InvalidOperationException setting DialogResult late).
    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
