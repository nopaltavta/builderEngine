using System.Collections.Generic;
using OpenTK.Mathematics;

namespace builder.Viewport;

/// <summary>Named part colors (Roblox BrickColor-inspired) + conversions.</summary>
public sealed class PartColor
{
    public string Name { get; }
    public Color4 Color { get; }

    public PartColor(string name, byte r, byte g, byte b)
    {
        Name = name;
        Color = new Color4(r / 255f, g / 255f, b / 255f, 1f);
    }

    public System.Windows.Media.Color ToMediaColor() =>
        System.Windows.Media.Color.FromRgb(ToByte(Color.R), ToByte(Color.G), ToByte(Color.B));

    public static Color4 FromMediaColor(System.Windows.Media.Color c) =>
        new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

    public static byte ToByte(float v) =>
        (byte)System.Math.Round(System.Math.Clamp(v * 255f, 0f, 255f));

    public string Hex => $"#{ToByte(Color.R):X2}{ToByte(Color.G):X2}{ToByte(Color.B):X2}";

    public static string ToRgbText(System.Windows.Media.Color c) => $"{c.R}, {c.G}, {c.B}";

    public static bool TryParseHex(string text, out System.Windows.Media.Color color)
    {
        color = default;
        string t = text.Trim().TrimStart('#');
        if (t.Length != 6 || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out uint v))
            return false;
        color = System.Windows.Media.Color.FromRgb((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
        return true;
    }

    /// <summary>Parses "r, g, b" (commas, spaces or semicolons, 0-255 each).</summary>
    public static bool TryParseRgb(string text, out System.Windows.Media.Color color)
    {
        color = default;
        var parts = text.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        byte[] b = new byte[3];
        for (int i = 0; i < 3; i++)
            if (!byte.TryParse(parts[i].Trim(), out b[i])) return false;
        color = System.Windows.Media.Color.FromRgb(b[0], b[1], b[2]);
        return true;
    }

    public static System.Windows.Media.Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = System.Math.Clamp(s, 0, 1);
        v = System.Math.Clamp(v, 0, 1);
        double c = v * s;
        double x = c * (1 - System.Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return System.Windows.Media.Color.FromRgb(
            (byte)System.Math.Round((r + m) * 255),
            (byte)System.Math.Round((g + m) * 255),
            (byte)System.Math.Round((b + m) * 255));
    }

    public static void RgbToHsv(System.Windows.Media.Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = System.Math.Max(r, System.Math.Max(g, b));
        double min = System.Math.Min(r, System.Math.Min(g, b));
        v = max;
        double d = max - min;
        s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    public static readonly IReadOnlyList<PartColor> Palette = new List<PartColor>
    {
        new("White", 242, 243, 243),
        new("Light gray", 168, 168, 168),
        new("Medium gray", 124, 128, 131),
        new("Dark gray", 89, 93, 96),
        new("Black", 27, 42, 53),
        new("Bright red", 196, 40, 28),
        new("Dark red", 124, 18, 18),
        new("Reddish brown", 105, 64, 40),
        new("Nougat", 204, 142, 105),
        new("Bright orange", 218, 133, 65),
        new("Neon orange", 255, 130, 0),
        new("Bright yellow", 245, 205, 48),
        new("New Yeller", 255, 255, 0),
        new("Olive", 124, 156, 107),
        new("Bright green", 75, 151, 75),
        new("Dark green", 39, 70, 44),
        new("Lime green", 163, 206, 39),
        new("Bright bluish green", 75, 151, 131),
        new("Cyan", 4, 175, 236),
        new("Bright blue", 13, 105, 172),
        new("Dark blue", 20, 48, 92),
        new("Navy blue", 0, 32, 96),
        new("Bright violet", 107, 50, 124),
        new("Bright purple", 205, 84, 169),
        new("Hot pink", 255, 102, 204),
        new("Light reddish violet", 232, 186, 200),
        new("Brown", 124, 92, 70),
        new("Tan", 235, 203, 164),
        new("Sand", 220, 208, 172),
        new("Khaki", 226, 220, 188),
        new("Institutional white", 248, 248, 248),
        new("Mid gray", 160, 161, 165),
        new("Really black", 17, 17, 17),
        new("Deep orange", 255, 176, 0),
        new("Alder", 180, 128, 255),
    };
};
