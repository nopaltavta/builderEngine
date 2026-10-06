using System.Collections.Generic;
using OpenTK.Mathematics;

namespace builder.Models;

/// <summary>
/// Named sky styles for Game Settings: a clock time (drives the sun curve),
/// atmosphere overrides, and shader theme values (tint, stars, clouds, sun).
/// Everything lands in already-saved fields, so presets persist for free.
/// </summary>
public sealed class SkyPreset
{
    public string Name { get; }
    public string Hint { get; }
    public float Time { get; }
    public float Haze { get; }
    public Vector3 FogColor { get; }
    public float FogDensity { get; }
    public Vector3? SunColor { get; }
    public float? SunIntensity { get; }
    public float? Ambient { get; }
    public Vector3 Tint { get; }
    public float Stars { get; }
    public float Clouds { get; }
    public float SunSize { get; }
    public bool Stripes { get; }
    public int Style { get; }

    public SkyPreset(string name, string hint, float time, float haze,
        Vector3 fogColor, float fogDensity,
        Vector3? sunColor = null, float? sunIntensity = null, float? ambient = null,
        Vector3? tint = null, float stars = 1f, float clouds = 1f,
        float sunSize = 1f, bool stripes = false, int style = 0)
    {
        Name = name;
        Hint = hint;
        Time = time;
        Haze = haze;
        FogColor = fogColor;
        FogDensity = fogDensity;
        SunColor = sunColor;
        SunIntensity = sunIntensity;
        Ambient = ambient;
        Tint = tint ?? new Vector3(1f, 1f, 1f);
        Stars = stars;
        Clouds = clouds;
        SunSize = sunSize;
        Stripes = stripes;
        Style = style;
    }

    public static readonly IReadOnlyList<SkyPreset> All = new List<SkyPreset>
    {
        new("Day", "Bright noon, blue sky.", 10f, 0.012f,
            new Vector3(0.55f, 0.62f, 0.72f), 0.012f,
            tint: new Vector3(1f, 1f, 1f), stars: 1f, clouds: 0.8f),
        new("Sunrise", "Low warm sun, pink clouds.", 6.4f, 0.02f,
            new Vector3(0.72f, 0.62f, 0.58f), 0.016f,
            tint: new Vector3(1f, 0.95f, 0.9f), stars: 0.6f, clouds: 1.1f, sunSize: 1.1f),
        new("Sunset", "Orange afterglow on the horizon.", 17.6f, 0.03f,
            new Vector3(0.78f, 0.55f, 0.42f), 0.018f,
            tint: new Vector3(1f, 0.9f, 0.85f), stars: 0.8f, clouds: 1.1f, sunSize: 1.3f),
        new("Night", "Stars, moon, dark ground.", 0f, 0.008f,
            new Vector3(0.05f, 0.08f, 0.16f), 0.02f,
            tint: new Vector3(0.9f, 0.95f, 1.1f), stars: 1.5f, clouds: 0.6f),
        new("Storm", "Dark clouds, flat gray light.", 13.5f, 0.05f,
            new Vector3(0.32f, 0.35f, 0.40f), 0.032f,
            new Vector3(0.70f, 0.72f, 0.78f), 0.4f, 0.6f,
            new Vector3(0.75f, 0.78f, 0.85f), 0.2f, 2f, 0.9f),
        new("Desert", "Hot haze, sandy horizon.", 14f, 0.04f,
            new Vector3(0.85f, 0.72f, 0.55f), 0.02f,
            sunColor: new Vector3(1f, 0.88f, 0.72f),
            tint: new Vector3(1f, 0.95f, 0.85f), stars: 0.4f, clouds: 0.3f, sunSize: 1.2f),
        new("Alien", "Green sky, giant sun.", 11f, 0.025f,
            new Vector3(0.30f, 0.55f, 0.45f), 0.016f,
            sunColor: new Vector3(0.60f, 1f, 0.75f),
            tint: new Vector3(0.55f, 1f, 0.7f), stars: 0.5f, clouds: 0.7f, sunSize: 1.8f),
        new("Fog", "Thick gray blanket, dim sun.", 9.5f, 0.05f,
            new Vector3(0.70f, 0.72f, 0.75f), 0.038f,
            sunColor: new Vector3(0.90f, 0.92f, 0.95f), sunIntensity: 0.55f,
            tint: new Vector3(0.9f, 0.92f, 0.95f), stars: 0.1f, clouds: 0.4f),
        new("Synthwave", "Striped purple sun, pink haze.", 17.9f, 0.02f,
            new Vector3(0.45f, 0.20f, 0.50f), 0.02f,
            new Vector3(1f, 0.50f, 0.80f), 0.55f, 0.55f,
            new Vector3(0.9f, 0.55f, 1.1f), 1.2f, 0.5f, 2.2f, true),
        new("Clear Blue", "Soft gradient noon, Roblox-style.", 12f, 0.005f,
            new Vector3(0.60f, 0.69f, 0.84f), 0.008f,
            tint: new Vector3(1f, 1f, 1f), stars: 0f, clouds: 0f, style: 2),
        new("Toon", "Saturated cartoon noon, puffy clouds.", 10.5f, 0.008f,
            new Vector3(0.65f, 0.75f, 0.90f), 0.01f,
            tint: new Vector3(0.7f, 0.82f, 1.2f), stars: 0f, clouds: 1.3f, sunSize: 1.1f),
        new("Summer", "Bleached heat-haze midday.", 13f, 0.03f,
            new Vector3(0.78f, 0.78f, 0.78f), 0.015f,
            sunColor: new Vector3(1f, 0.95f, 0.85f), ambient: 1.05f,
            tint: new Vector3(1.05f, 1f, 0.9f), stars: 0f, clouds: 0.5f, sunSize: 1.1f),
        new("Autumn", "Golden afternoon, warm air.", 16f, 0.025f,
            new Vector3(0.80f, 0.62f, 0.42f), 0.018f,
            sunColor: new Vector3(1f, 0.78f, 0.50f),
            tint: new Vector3(1.1f, 0.85f, 0.6f), stars: 0f, clouds: 0.9f, sunSize: 1.2f),
        new("Winter", "Pale ice-blue morning.", 8.5f, 0.015f,
            new Vector3(0.75f, 0.80f, 0.88f), 0.022f,
            sunColor: new Vector3(0.90f, 0.93f, 1f), sunIntensity: 0.6f, ambient: 0.8f,
            tint: new Vector3(0.85f, 0.92f, 1.05f), stars: 0f, clouds: 1.2f),
        new("Spring", "Fresh pink morning.", 9f, 0.012f,
            new Vector3(0.80f, 0.70f, 0.72f), 0.014f,
            sunColor: new Vector3(1f, 0.90f, 0.85f),
            tint: new Vector3(1f, 0.88f, 0.95f), stars: 0f, clouds: 1f, sunSize: 1.1f),
        new("Space", "Black starfield, tiny distant sun.", 0f, 0.003f,
            new Vector3(0.02f, 0.02f, 0.05f), 0.005f,
            tint: new Vector3(0.6f, 0.65f, 0.9f), stars: 2.5f, clouds: 0f, sunSize: 0.5f),
        new("Candy", "Bubblegum pink world.", 12.5f, 0.015f,
            new Vector3(0.90f, 0.60f, 0.80f), 0.016f,
            sunColor: new Vector3(1f, 0.60f, 0.85f),
            tint: new Vector3(1.1f, 0.7f, 1f), stars: 0f, clouds: 1.2f, sunSize: 1.4f),
        new("Tropical", "Turquoise water-sky midday.", 12f, 0.015f,
            new Vector3(0.60f, 0.80f, 0.78f), 0.014f,
            tint: new Vector3(0.7f, 1f, 0.95f), stars: 0f, clouds: 0.9f, sunSize: 1.1f),
        new("Horror", "Dead purple dark, choked moon.", 0.7f, 0.03f,
            new Vector3(0.10f, 0.08f, 0.15f), 0.03f,
            ambient: 0.3f,
            tint: new Vector3(0.5f, 0.45f, 0.7f), stars: 1f, clouds: 1.6f, sunSize: 0.8f),
        new("Cartoon", "Flat Roblox-style sky: banded blue, solid sun, hard clouds.", 10f, 0.008f,
            new Vector3(0.60f, 0.78f, 0.95f), 0.01f,
            tint: new Vector3(1f, 1f, 1f), stars: 0f, clouds: 1f, sunSize: 1f, style: 1),
    };

    public static SkyPreset? ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var p in All)
            if (p.Name.Equals(name.Trim(), System.StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }
}
