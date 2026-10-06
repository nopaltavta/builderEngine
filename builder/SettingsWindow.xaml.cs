using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using builder.Settings;

namespace builder;

/// <summary>Live-apply settings dialog. Changes hit the main window immediately; saved on close.</summary>
public partial class SettingsWindow : Window
{
    private readonly MainWindow _main;
    private bool _ready;

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        MainWindow.ApplyTextModeTo(this);
        Owner = main;
        RegisterNav();
        BuildSkyButtons();
        RefreshControls();

        Closed += (_, _) => AppSettings.Current.Save();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => PlayOpen();
        Closing += OnClosingAnimate;
        _ready = true;
    }

    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly Dictionary<string, UIElement> _navPanels = new();
    private readonly Dictionary<string, Image> _navIcons = new();

    /// <summary>Left-rail nav: buttons switch panels, icons load from SettingsIcons/.</summary>
    private void RegisterNav()
    {
        foreach (var child in NavPanel.Children)
            if (child is Button b && b.Tag is string key)
                _navButtons[key] = b;
        _navPanels["Window"] = PanelWindow;
        _navPanels["Camera"] = PanelCamera;
        _navPanels["Viewport"] = PanelViewport;
        _navPanels["Editing"] = PanelEditing;
        _navPanels["Physics"] = PanelPhysics;
        _navPanels["Player"] = PanelPlayer;
        _navPanels["Sound"] = PanelSound;
        _navPanels["Sky"] = PanelSky;
        _navPanels["Updates"] = PanelUpdates;
        _navIcons["Window"] = NavIconWindow;
        _navIcons["Camera"] = NavIconCamera;
        _navIcons["Viewport"] = NavIconViewport;
        _navIcons["Editing"] = NavIconEditing;
        _navIcons["Physics"] = NavIconPhysics;
        _navIcons["Player"] = NavIconPlayer;
        _navIcons["Sound"] = NavIconSound;
        _navIcons["Sky"] = NavIconSky;
        _navIcons["Updates"] = NavIconUpdate;
        foreach (var kv in _navIcons)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "SettingsIcons",
                    kv.Key.ToLowerInvariant() == "updates" ? "update.png" : kv.Key.ToLowerInvariant() + ".png");
                if (File.Exists(path))
                    kv.Value.Source = new BitmapImage(new Uri(path));
            }
            catch { /* missing icon: text-only nav item */ }
        }
        SelectNav("Viewport");
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string key) SelectNav(key);
    }

    private void SelectNav(string key)
    {
        foreach (var kv in _navPanels)
            kv.Value.Visibility = kv.Key == key ? Visibility.Visible : Visibility.Collapsed;
        foreach (var kv in _navButtons)
            kv.Value.Background = kv.Key == key
                ? new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22))
                : Brushes.Transparent;
    }

    /// <summary>Sky preset buttons (built once; HighlightStudioSky refreshes).</summary>
    private void BuildSkyButtons()
    {
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
            b.Click += SkyStudio_Click;
            SkyStudioPanel.Children.Add(b);
        }
    }

    /// <summary>Re-sync every control from AppSettings (ctor + Reset to Default).</summary>
    private void RefreshControls()
    {
        _ready = false;
        var s = AppSettings.Current;
        RadioWindowed.IsChecked = !s.IsBorderless;
        RadioBorderless.IsChecked = s.IsBorderless;
        RadioDark.IsChecked = s.DarkTheme;
        RadioLight.IsChecked = !s.DarkTheme;
        SensitivitySlider.Value = s.LookSensitivity;
        SpeedSlider.Value = s.MoveSpeed;
        SensitivityValue.Text = $"{s.LookSensitivity:0.00}";
        SpeedValue.Text = $"{s.MoveSpeed:0}";
        LookSmoothSlider.Value = s.LookSmoothing;
        MoveSmoothSlider.Value = s.MoveSmoothing;
        LookSmoothValue.Text = $"{s.LookSmoothing:0}";
        MoveSmoothValue.Text = $"{s.MoveSmoothing:0}";
        FovSlider.Value = s.Fov;
        FovValue.Text = $"{s.Fov:0}";
        ZoomSpeedSlider.Value = s.ZoomSpeed;
        ZoomSpeedValue.Text = $"{s.ZoomSpeed:0.00}";
        InvertYCheck.IsChecked = s.InvertLookY;
        
        CheckVSync.IsChecked = _main.VSyncToggle.IsChecked;
        CheckMsaa.IsChecked = _main.MsaaToggle.IsChecked;
        CheckShadows.IsChecked = _main.ShadowsToggle.IsChecked;
        CheckWaterRefl.IsChecked = s.WaterReflections;
        RadioTextSharp.IsChecked = s.TextMode == "Sharp";
        RadioTextSmooth.IsChecked = s.TextMode == "Smooth";
        RadioTextGray.IsChecked = s.TextMode == "Gray";
        RadioShadowLowest.IsChecked = s.ShadowSize == 256;
        RadioGL46.IsChecked = !(s.GlMajor == 4 && s.GlMinor == 0);
        RadioGL40.IsChecked = s.GlMajor == 4 && s.GlMinor == 0;        RadioShadowLow.IsChecked = s.ShadowSize == 512;
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
        SnapDefaultCheck.IsChecked = s.SnapEnabled;
        SurfaceDragCheck.IsChecked = s.DragOnSurfaces;
        DragCollideCheck.IsChecked = s.DragCollision;
        GravitySlider.Value = s.GravityStrength;
        GravityValue.Text = $"{s.GravityStrength:0}";
        FrictionSlider.Value = s.Friction;
        FrictionValue.Text = $"{s.Friction:0.00}";
        WalkSlider.Value = s.PlayerWalkSpeed;
        WalkValue.Text = $"{s.PlayerWalkSpeed:0}";
        JumpSlider.Value = s.PlayerJumpPower;
        JumpValue.Text = $"{s.PlayerJumpPower:0}";
        ZoomSlider.Value = s.PlayerZoom;
        ZoomValue.Text = $"{s.PlayerZoom:0}";
        SpawnHeightSlider.Value = s.PlayerSpawnHeight;
        SpawnHeightValue.Text = $"{s.PlayerSpawnHeight:0}";
        CoyoteSlider.Value = s.PlayerCoyote;
        CoyoteValue.Text = $"{s.PlayerCoyote:0.00}";
        AvatarRSlider.Value = s.AvatarR * 255.0;
        AvatarGSlider.Value = s.AvatarG * 255.0;
        AvatarBSlider.Value = s.AvatarB * 255.0;
        SyncAvatarLabels();
        CheckBlocky.IsChecked = s.BlockyAvatar;
        CheckUpdatesCheck.IsChecked = s.CheckForUpdates;
        ManifestBox.Text = s.UpdateManifestUrl;
        HighlightStudioSky();
        SoundCheck.IsChecked = s.SoundEnabled;
        SoundVolumeSlider.Value = s.SoundVolume * 100.0;
        SoundVolumeValue.Text = $"{s.SoundVolume * 100.0:0}";
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

    private bool _closeAnimated;

    /// <summary>Fade out, then really close (skipped for owner-shutdown fast path).</summary>
    public void CloseImmediate()
    {
        _closeAnimated = true;
        Close();
    }

    private void OnClosingAnimate(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeAnimated) return;
        e.Cancel = true;
        _closeAnimated = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    /// <summary>Refresh the window-mode radios (e.g. after Esc exits borderless).</summary>
    public void SyncWindowMode()
    {
        _ready = false;
        RadioWindowed.IsChecked = !AppSettings.Current.IsBorderless;
        RadioBorderless.IsChecked = AppSettings.Current.IsBorderless;
        _ready = true;
    }

    private void WindowMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        // Sender-based: during a switch the sibling may still read checked.
        _main.ApplyWindowMode(ReferenceEquals(sender, RadioBorderless) ? "Borderless" : "Windowed");
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.SetTheme(ReferenceEquals(sender, RadioDark));
    }

    private void CameraSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        var s = AppSettings.Current;
        s.LookSensitivity = (float)SensitivitySlider.Value;
        s.MoveSpeed = (float)SpeedSlider.Value;
        s.LookSmoothing = (float)LookSmoothSlider.Value;
        s.MoveSmoothing = (float)MoveSmoothSlider.Value;
        SensitivityValue.Text = $"{s.LookSensitivity:0.00}";
        SpeedValue.Text = $"{s.MoveSpeed:0}";
        LookSmoothValue.Text = $"{s.LookSmoothing:0}";
        MoveSmoothValue.Text = $"{s.MoveSmoothing:0}";
        _main.ApplyCameraSettings();
    }

    private void FovSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        FovValue.Text = $"{FovSlider.Value:0}";
        _main.ApplyFov((float)FovSlider.Value);
    }

    private void FogSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        FogValue.Text = $"{FogSlider.Value:0.000}";
        _main.ApplyFog((float)FogSlider.Value);
    }

    private void GravitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        AppSettings.Current.GravityStrength = (float)GravitySlider.Value;
        GravityValue.Text = $"{AppSettings.Current.GravityStrength:0}";
    }

    private void PlayerSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        var s = AppSettings.Current;
        s.PlayerWalkSpeed = (float)WalkSlider.Value;
        s.PlayerJumpPower = (float)JumpSlider.Value;
        s.PlayerZoom = (float)ZoomSlider.Value;
        s.PlayerSpawnHeight = (float)SpawnHeightSlider.Value;
        s.PlayerCoyote = (float)CoyoteSlider.Value;
        WalkValue.Text = $"{s.PlayerWalkSpeed:0}";
        JumpValue.Text = $"{s.PlayerJumpPower:0}";
        ZoomValue.Text = $"{s.PlayerZoom:0}";
        SpawnHeightValue.Text = $"{s.PlayerSpawnHeight:0}";
        CoyoteValue.Text = $"{s.PlayerCoyote:0.00}";
        _main.ApplyPlayerSettings(); // live: drive reads these every frame
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

    private void BlockyCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.ApplyBlockyAvatar(CheckBlocky.IsChecked == true);
    }

    private void ZoomSpeedSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        AppSettings.Current.ZoomSpeed = (float)ZoomSpeedSlider.Value;
        ZoomSpeedValue.Text = $"{AppSettings.Current.ZoomSpeed:0.00}";
    }

    private void InvertYCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.InvertLookY = InvertYCheck.IsChecked == true;
        _main.ApplyCameraSettings();
    }

    private void GlowSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        GlowValue.Text = $"{GlowSlider.Value:0.00}";
        _main.ApplySelectionGlow((float)GlowSlider.Value);
    }

    private void ContrastSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        ContrastValue.Text = $"{ContrastSlider.Value:0.00}";
        _main.ApplyContrast((float)ContrastSlider.Value);
    }

    private void SnapDefaultCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _main.ApplySnapDefault(SnapDefaultCheck.IsChecked == true);
    }

    private void SurfaceDragCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.DragOnSurfaces = SurfaceDragCheck.IsChecked == true;
        AppSettings.Current.Save();
    }

    private void DragCollideCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.DragCollision = DragCollideCheck.IsChecked == true;
        AppSettings.Current.Save();
    }

    private void FrictionSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        FrictionValue.Text = $"{FrictionSlider.Value:0.00}";
        _main.ApplyFriction((float)FrictionSlider.Value);
    }

    private void RenderSystem_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool fallback = ReferenceEquals(sender, RadioGL40);
        AppSettings.Current.GlMajor = 4;
        AppSettings.Current.GlMinor = fallback ? 0 : 6;
        AppSettings.Current.Save();
    }

    private void ShadowQuality_Checked(object sender, RoutedEventArgs e)    {
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

    private void ViewportCheck_Click(object sender, RoutedEventArgs e)
    {
        _main.ApplyVsync(CheckVSync.IsChecked == true);
        _main.VSyncToggle.IsChecked = CheckVSync.IsChecked;
        _main.ApplyMsaa(CheckMsaa.IsChecked == true);
        _main.MsaaToggle.IsChecked = CheckMsaa.IsChecked;
        _main.ApplyShadows(CheckShadows.IsChecked == true);
        _main.ShadowsToggle.IsChecked = CheckShadows.IsChecked;
        _main.ApplyWaterReflections(CheckWaterRefl.IsChecked == true);
    }

    private void TextMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        string mode = RadioTextGray.IsChecked == true ? "Gray"
            : RadioTextSmooth.IsChecked == true ? "Smooth" : "Sharp";
        _main.ApplyTextMode(mode);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Apply closes (everything already applies + saves live).</summary>
    private void Apply_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Restore shipped defaults across every studio setting, then re-sync.</summary>
    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var d = new AppSettings();
        var s = AppSettings.Current;
        s.WindowMode = d.WindowMode; s.DarkTheme = d.DarkTheme;
        s.LookSensitivity = d.LookSensitivity; s.MoveSpeed = d.MoveSpeed;
        s.LookSmoothing = d.LookSmoothing; s.MoveSmoothing = d.MoveSmoothing;
        s.Fov = d.Fov; s.FogDensity = d.FogDensity;
        s.VSync = d.VSync; s.Msaa = d.Msaa; s.Shadows = d.Shadows;
        s.WaterReflections = d.WaterReflections;
        s.ShadowSize = d.ShadowSize; s.ShadowDistance = d.ShadowDistance;
        s.GravityStrength = d.GravityStrength; s.Friction = d.Friction;
        s.PlayerWalkSpeed = d.PlayerWalkSpeed; s.PlayerJumpPower = d.PlayerJumpPower;
        s.PlayerZoom = d.PlayerZoom; s.PlayerSpawnHeight = d.PlayerSpawnHeight;
        s.PlayerCoyote = d.PlayerCoyote;
        s.AvatarR = d.AvatarR; s.AvatarG = d.AvatarG; s.AvatarB = d.AvatarB;
        s.BlockyAvatar = d.BlockyAvatar;
        s.TextMode = d.TextMode; s.SnapEnabled = d.SnapEnabled;
        s.DragOnSurfaces = d.DragOnSurfaces; s.DragCollision = d.DragCollision;
        s.ZoomSpeed = d.ZoomSpeed; s.InvertLookY = d.InvertLookY;
        s.SelectionGlow = d.SelectionGlow; s.Contrast = d.Contrast;
        s.SoundEnabled = d.SoundEnabled; s.SoundVolume = d.SoundVolume;
        s.CheckForUpdates = d.CheckForUpdates; s.UpdateManifestUrl = d.UpdateManifestUrl;
        s.Save();
        _main.ApplyAllSettings();
        _main.SetTheme(s.DarkTheme);
        _main.ApplyTextMode(s.TextMode);
        _main.ApplyWaterReflections(s.WaterReflections);
        _main.ApplyAvatarColor();
        _main.ApplyBlockyAvatar(s.BlockyAvatar);
        Audio.AudioEngine.ApplyVolume();
        RefreshControls();
    }

    /// <summary>TEMPORARY: force-show the GL fallback prompt to test the dialog.</summary>
    private void TestFallbackButton_Click(object sender, RoutedEventArgs e) => _main.TestFallbackPrompt();

    private void SoundCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.SoundEnabled = SoundCheck.IsChecked == true;
        AppSettings.Current.Save();
    }

    private void SoundVolumeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        SoundVolumeValue.Text = $"{SoundVolumeSlider.Value:0}";
        AppSettings.Current.SoundVolume = (float)(SoundVolumeSlider.Value / 100.0);
        AppSettings.Current.Save();
        Audio.AudioEngine.ApplyVolume();
    }

    private void TestSound_Click(object sender, RoutedEventArgs e) =>
        Audio.AudioEngine.Play("confirm", 0.9f);

    private void SkyStudio_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not Button { Tag: string name }) return;
        _main.ApplySkyPreset(name);
        HighlightStudioSky();
    }

    private void HighlightStudioSky()
    {
        string active = _main.SkyPresetName;
        string? hint = null;
        foreach (var child in SkyStudioPanel.Children)
        {
            if (child is not Button b || b.Tag is not string name) continue;
            bool on = name == active;
            b.BorderBrush = new SolidColorBrush(on
                ? Color.FromRgb(0xE6, 0x7E, 0x22) : Color.FromRgb(0x55, 0x55, 0x55));
            b.BorderThickness = new Thickness(on ? 2 : 1);
            if (on) hint = Models.SkyPreset.ByName(name)?.Hint;
        }
        SkyStudioHint.Text = hint ?? (active == "Custom" ? "Hand-tuned sky." : "");
    }

    private void UpdatesCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Current.CheckForUpdates = CheckUpdatesCheck.IsChecked == true;
        AppSettings.Current.Save();
    }

    private void ManifestBox_LostFocus(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.UpdateManifestUrl = ManifestBox.Text.Trim();
        AppSettings.Current.Save();
    }

    private void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.UpdateManifestUrl = ManifestBox.Text.Trim();
        AppSettings.Current.Save();
        _main.CheckForUpdatesNow();
    }
}
