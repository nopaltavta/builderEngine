using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using builder.Settings;

namespace builder;

/// <summary>Game-scoped settings for published games (pause menu > Settings).
/// Viewport look only: no studio, editing or update rows. Live-apply + saved.</summary>
public partial class GameSettingsWindow : Window
{
    private readonly MainWindow _main;
    private bool _ready;

    public GameSettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        MainWindow.ApplyTextModeTo(this);
        Owner = main;

        var s = AppSettings.Current;
        CheckShadows.IsChecked = s.Shadows;
        CheckMsaa.IsChecked = s.Msaa;
        CheckInvertY.IsChecked = s.InvertLookY;
        RadioShadowLowest.IsChecked = s.ShadowSize == 256;
        RadioShadowLow.IsChecked = s.ShadowSize == 512;
        RadioShadowMed.IsChecked = s.ShadowSize == 1024;
        RadioShadowHigh.IsChecked = s.ShadowSize == 2048;
        RadioShadowUltra.IsChecked = s.ShadowSize == 4096;
        FogSlider.Value = s.FogDensity;
        FogValue.Text = $"{s.FogDensity:0.000}";
        GlowSlider.Value = s.SelectionGlow;
        GlowValue.Text = $"{s.SelectionGlow:0.00}";
        ContrastSlider.Value = s.Contrast;
        ContrastValue.Text = $"{s.Contrast:0.00}";
        ShadowDistanceBox.SelectedIndex = NearestPresetIndex(s.ShadowDistance);
        foreach (var p in Models.SkyPreset.All)
        {
            var b = new Button
            {
                Content = p.Name,
                Tag = p.Name,
                ToolTip = p.Hint,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
            };
            b.Click += SkyPreset_Click;
            SkyPresetPanel.Children.Add(b);
        }
        HighlightSkyPreset();
        AvatarRSlider.Value = s.AvatarR * 255.0;
        AvatarGSlider.Value = s.AvatarG * 255.0;
        AvatarBSlider.Value = s.AvatarB * 255.0;
        SyncAvatarLabels();
        CheckBlocky.IsChecked = s.BlockyAvatar;

        Closed += (_, _) => s.Save();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => PlayOpen();
        _ready = true;
    }

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    /// <summary>Fade + grow in.</summary>
    private void PlayOpen()
    {
        Opacity = 0;
        var scale = new ScaleTransform(0.96, 0.96);
        Root.RenderTransform = scale;
        Root.RenderTransformOrigin = new Point(0.5, 0.5);
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void BlockyCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.ApplyBlockyAvatar(CheckBlocky.IsChecked == true);
    }

    private void GeneralCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.ApplyShadows(CheckShadows.IsChecked == true);
        _main.ApplyMsaa(CheckMsaa.IsChecked == true);
        _main.MsaaToggle.IsChecked = CheckMsaa.IsChecked;
        AppSettings.Current.InvertLookY = CheckInvertY.IsChecked == true;
        _main.ApplyCameraSettings();
        AppSettings.Current.Save();
    }

    private void ShadowQuality_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        int size = ReferenceEquals(sender, RadioShadowLowest) ? 256
            : ReferenceEquals(sender, RadioShadowLow) ? 512
            : ReferenceEquals(sender, RadioShadowHigh) ? 2048
            : ReferenceEquals(sender, RadioShadowUltra) ? 4096
            : 1024;
        _main.RequestShadowSize(size);
    }

    private void ShadowDistanceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ShadowDistanceBox.SelectedIndex < 0) return;
        _main.ApplyShadowDistance(AppSettings.ShadowDistancePresets[ShadowDistanceBox.SelectedIndex]);
    }

    private static int NearestPresetIndex(float v)
    {
        int best = 2;
        for (int i = 0; i < AppSettings.ShadowDistancePresets.Length; i++)
            if (Math.Abs(AppSettings.ShadowDistancePresets[i] - v) <
                Math.Abs(AppSettings.ShadowDistancePresets[best] - v)) best = i;
        return best;
    }

    private void SkyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not Button { Tag: string name }) return;
        _main.ApplySkyPreset(name);
        HighlightSkyPreset();
    }

    private void HighlightSkyPreset()
    {
        string active = _main.SkyPresetName;
        string? hint = null;
        foreach (var child in SkyPresetPanel.Children)
        {
            if (child is not Button b || b.Tag is not string name) continue;
            bool on = name == active;
            b.BorderBrush = new SolidColorBrush(on
                ? Color.FromRgb(0xE6, 0x7E, 0x22) : Color.FromRgb(0x55, 0x55, 0x55));
            b.BorderThickness = new Thickness(on ? 2 : 1);
            if (on) hint = Models.SkyPreset.ByName(name)?.Hint;
        }
        SkyPresetHint.Text = hint ?? (active == "Custom" ? "Hand-tuned sky." : "");
    }

    private void FogSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        FogValue.Text = $"{FogSlider.Value:0.000}";
        _main.ApplyFog((float)FogSlider.Value);
    }

    private void GlowSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        GlowValue.Text = $"{GlowSlider.Value:0.00}";
        _main.ApplySelectionGlow((float)GlowSlider.Value);
    }

    private void AvatarSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        var s = AppSettings.Current;
        s.AvatarR = (float)(AvatarRSlider.Value / 255.0);
        s.AvatarG = (float)(AvatarGSlider.Value / 255.0);
        s.AvatarB = (float)(AvatarBSlider.Value / 255.0);
        SyncAvatarLabels();
        _main.ApplyAvatarColor(); // live-recolors a mid-play avatar; persists on close
    }

    private void SyncAvatarLabels()
    {
        AvatarRValue.Text = $"{AvatarRSlider.Value:0}";
        AvatarGValue.Text = $"{AvatarGSlider.Value:0}";
        AvatarBValue.Text = $"{AvatarBSlider.Value:0}";
        AvatarPreview.Background = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(AvatarRSlider.Value),
            (byte)Math.Round(AvatarGSlider.Value),
            (byte)Math.Round(AvatarBSlider.Value)));
    }

    private void ContrastSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        ContrastValue.Text = $"{ContrastSlider.Value:0.00}";
        _main.ApplyContrast((float)ContrastSlider.Value);
    }
}
