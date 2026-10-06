using System;
using System.IO;
using System.Text.Json;

namespace builder.Settings;

/// <summary>Persisted studio settings (%AppData%/BuilderStudio/settings.json).</summary>
public sealed class AppSettings
{
    public static AppSettings Current { get; set; } = new();

    public string WindowMode { get; set; } = "Windowed"; // Windowed | Borderless
    public bool DarkTheme { get; set; } = true;
    public float LookSensitivity { get; set; } = 0.25f;
    public float MoveSpeed { get; set; } = 8f;
    public float LookSmoothing { get; set; } = 18f;
    public float MoveSmoothing { get; set; } = 10f;
    public float Fov { get; set; } = 45f;
    public float FogDensity { get; set; } = 0.008f;
    public bool VSync { get; set; } = true;
    public bool Msaa { get; set; } = true;
    public bool Shadows { get; set; } = true;
    public bool WaterReflections { get; set; } = true;    public int ShadowSize { get; set; } = 1024;
    public float ShadowDistance { get; set; } = 20f;
    public float GravityStrength { get; set; } = 21f;
    public float PlayerWalkSpeed { get; set; } = 5f;
    public float PlayerJumpPower { get; set; } = 9f;
    public float PlayerCoyote { get; set; } = 0.12f;
    public float PlayerZoom { get; set; } = 12f;
    public float PlayerSpawnHeight { get; set; } = 6f;
    public float ZoomSpeed { get; set; } = 1f;
    public bool InvertLookY { get; set; } = false;
    public float SelectionGlow { get; set; } = 1f;
    public float Contrast { get; set; } = 1.15f;
    public float AvatarR { get; set; } = 0.2f;
    public float AvatarG { get; set; } = 0.5f;
    public float AvatarB { get; set; } = 1f;
    /// <summary>Beta blocky animated avatar in Play (off = classic capsule).</summary>
    public bool BlockyAvatar { get; set; } = true;
    public bool TextSharp { get; set; } = true; // legacy: migrated into TextMode
    /// <summary>UI text rendering: Sharp (Display+ClearType), Smooth (Ideal),
    /// Gray (Display+grayscale, like browsers — no color fringing).</summary>
    public string TextMode { get; set; } = "Sharp";
    public bool SnapEnabled { get; set; } = true;
    public float SnapIncrement { get; set; } = 0.5f;
    public bool DragOnSurfaces { get; set; } = true; // body-drags ride part faces
    public bool DragCollision { get; set; } = true; // drags slide against parts, Roblox-style
    public float Friction { get; set; } = 1f;
    public float TimeOfDay { get; set; } = 12f;
    public int GlMajor { get; set; } = 4;
    public int GlMinor { get; set; } = 0;
    public int SchemaVersion { get; set; } = 2;
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Recently used part colors ("r, g, b"), most-recent-first, max 10.</summary>
    public List<string> RecentColors { get; set; } = new();
    /// <summary>Recently opened places (full paths), most-recent-first, max 8.</summary>
    public List<string> RecentFiles { get; set; } = new();
    public bool SoundEnabled { get; set; } = true;
    public float SoundVolume { get; set; } = 0.8f;    public string UpdateManifestUrl { get; set; } = "https://drive.google.com/uc?export=download&id=1HT1Yd54o1FtvOFF7rM-janmi0EFJXWlw";
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;

    public bool IsBorderless => WindowMode == "Borderless";

    /// <summary>Shadow render-distance presets (studs): Lowest..Maximum.</summary>
    public static readonly float[] ShadowDistancePresets = { 10f, 15f, 20f, 60f, 90f, 120f };

    public static float NearestShadowDistance(float v)
    {
        float best = ShadowDistancePresets[2];
        foreach (float p in ShadowDistancePresets)
            if (Math.Abs(p - v) < Math.Abs(best - v)) best = p;
        return best;
    }

    private static string Path
    {
        get
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuilderStudio");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path));
                if (loaded != null) return Sanitize(loaded);
            }
        }
        catch { /* corrupt file -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* settings are best-effort */ }
    }

    private static AppSettings Sanitize(AppSettings s)
    {
        if (s.SchemaVersion < 2)
        {
            // v1 defaults were free-plane drags with pass-through: migrate existing
            // installs to the Roblox-style behavior (still toggleable in Settings).
            s.DragOnSurfaces = true;
            s.DragCollision = true;
            s.SchemaVersion = 2;
        }
        if (s.WindowMode != "Borderless") s.WindowMode = "Windowed";
        if (string.IsNullOrEmpty(s.TextMode)) s.TextMode = s.TextSharp ? "Sharp" : "Smooth";
        if (s.TextMode is not ("Sharp" or "Smooth" or "Gray")) s.TextMode = "Sharp";
        s.TextSharp = s.TextMode != "Smooth";
        s.LookSensitivity = Math.Clamp(s.LookSensitivity, 0.05f, 0.6f);
        s.MoveSpeed = Math.Clamp(s.MoveSpeed, 1f, 30f);
        s.LookSmoothing = Math.Clamp(s.LookSmoothing, 4f, 30f);
        s.MoveSmoothing = Math.Clamp(s.MoveSmoothing, 2f, 20f);
        s.Fov = Math.Clamp(s.Fov, 30f, 90f);
        s.FogDensity = Math.Clamp(s.FogDensity, 0f, 0.05f);
        if (s.ShadowSize is not (256 or 512 or 1024 or 2048 or 4096)) s.ShadowSize = 1024;
        s.ShadowDistance = NearestShadowDistance(s.ShadowDistance);
        s.GravityStrength = Math.Clamp(s.GravityStrength, 1f, 50f);
        s.PlayerWalkSpeed = Math.Clamp(s.PlayerWalkSpeed, 1f, 16f);
        s.PlayerJumpPower = Math.Clamp(s.PlayerJumpPower, 1f, 20f);
        s.PlayerCoyote = Math.Clamp(s.PlayerCoyote, 0f, 0.3f);
        s.PlayerZoom = Math.Clamp(s.PlayerZoom, 2f, 120f);
        s.PlayerSpawnHeight = Math.Clamp(s.PlayerSpawnHeight, 2f, 15f);
        s.ZoomSpeed = Math.Clamp(s.ZoomSpeed, 0.25f, 3f);
        s.SelectionGlow = Math.Clamp(s.SelectionGlow, 0f, 1f);
        s.AvatarR = Math.Clamp(s.AvatarR, 0f, 1f);
        s.AvatarG = Math.Clamp(s.AvatarG, 0f, 1f);
        s.AvatarB = Math.Clamp(s.AvatarB, 0f, 1f);
        s.SoundVolume = Math.Clamp(s.SoundVolume, 0f, 1f);        s.Contrast = Math.Clamp(s.Contrast, 0.2f, 2f);
        if (!(s.SnapIncrement > 0)) s.SnapIncrement = 0.5f;
        s.SnapIncrement = Math.Min(s.SnapIncrement, 8f); // any positive step goes; only 0 is rejected above
        s.Friction = Math.Clamp(s.Friction, 0f, 2f);
        s.TimeOfDay = Math.Clamp(s.TimeOfDay, 0f, 24f);
        if (string.IsNullOrWhiteSpace(s.UpdateManifestUrl))
            s.UpdateManifestUrl = "https://drive.google.com/uc?export=download&id=1HT1Yd54o1FtvOFF7rM-janmi0EFJXWlw";
        return s;
    }
}
