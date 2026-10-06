namespace builder.Viewport;

/// <summary>Named surface presets. Real shader params, not just tints.</summary>
public enum MaterialKind
{
    Plastic,
    SmoothPlastic,
    Wood,
    Grass,
    Marble,
    Granite,
    Brick,
    Sand,
    Metal,
    Foil,
    Glass,
    Ice,
    ForceField,
    Neon,
    Grid,
}

public readonly struct MaterialParams
{
    public readonly float Shininess;
    public readonly float Metallic;
    public readonly float Opacity;
    public readonly float Emissive;
    public readonly string Blurb;

    /// <summary>Procedural texture key (see MaterialTexture), or null for a solid finish.</summary>
    public readonly string? Texture;

    private MaterialParams(float shininess, float metallic, float opacity, float emissive, string blurb, string? texture = null)
    {
        Shininess = shininess;
        Metallic = metallic;
        Opacity = opacity;
        Emissive = emissive;
        Blurb = blurb;
        Texture = texture;
    }

    public bool Transparent => Opacity < 0.999f;

    public static MaterialParams Of(MaterialKind kind) => kind switch
    {
        MaterialKind.SmoothPlastic => new(64f, 0f, 1f, 0f, "Glossy highlights"),
        MaterialKind.Wood => new(10f, 0f, 1f, 0f, "Textured grain", "wood"),
        MaterialKind.Grass => new(6f, 0f, 1f, 0f, "Textured blades — pair with green", "grass"),
        MaterialKind.Marble => new(110f, 0f, 1f, 0f, "Textured veins, polished", "marble"),
        MaterialKind.Granite => new(40f, 0.35f, 1f, 0f, "Textured speckle", "granite"),
        MaterialKind.Brick => new(14f, 0f, 1f, 0f, "Textured courses + mortar", "brick"),
        MaterialKind.Sand => new(8f, 0f, 1f, 0f, "Fine textured grain", "sand"),
        MaterialKind.Metal => new(90f, 1f, 1f, 0f, "Tinted specular, dark diffuse"),
        MaterialKind.Foil => new(140f, 1f, 1f, 0f, "Mirror-like, sharpest glints"),
        MaterialKind.Glass => new(120f, 0f, 0.30f, 0f, "See-through, sorted back-to-front"),
        MaterialKind.Ice => new(150f, 0f, 0.55f, 0.15f, "Frosty, denser than glass"),
        MaterialKind.ForceField => new(70f, 0f, 0.5f, 0.9f, "Glowing transparency"),
        MaterialKind.Neon => new(32f, 0f, 1f, 1.4f, "Self-lit glow"),
        MaterialKind.Grid => new(30f, 0f, 1f, 0f, "Baseplate stud grid, one cell per stud", "grid"),
        _ => new(24f, 0f, 1f, 0f, "Matte everyday plastic"),
    };
}

/// <summary>Tileable procedural textures (128px RGBA, light-valued so they
/// multiply cleanly with the part color). No assets, no downloads.</summary>
internal static class MaterialTexture
{
    public const int Size = 128;

    public static readonly string[] Keys = { "grass", "wood", "marble", "granite", "brick", "sand", "grid" };

    /// <summary>Deterministic 0-1 hash on an integer lattice (tiles by construction).</summary>
    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    public static byte[] Generate(string key)
    {
        const int S = Size;
        var px = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float r = 235, g = 235, b = 235;
            switch (key)
            {
                case "grass": // short vertical blades on a pale base
                {
                    float n = Hash((x * 64 / S) % 64, (y * 16 / S) % 16);
                    if (n > 0.70f)
                    {
                        float d = (n - 0.70f) * 260f;
                        r -= d * 0.55f; g -= d * 0.85f; b -= d * 0.55f;
                    }
                    break;
                }
                case "wood": // horizontal grain stripes with wobble
                {
                    float wob = Hash(x / 16, y / 16) - 0.5f;
                    float s = 0.5f + 0.5f * MathF.Sin((y * 9f / S + 1.5f * wob) * MathF.PI * 2f);
                    float d = s * s * s * 72f;
                    r -= d * 0.55f; g -= d * 0.75f; b -= d;
                    break;
                }
                case "marble": // soft dark veins on near-white
                {
                    float v = MathF.Abs(MathF.Sin((x * 3f + y * 5f) / S * MathF.PI
                        + 3f * (Hash(x / 32, y / 32) - 0.5f) * 2f));
                    if (v < 0.10f)
                    {
                        float d = (0.10f - v) * 640f;
                        r -= d * 0.8f; g -= d * 0.8f; b -= d * 0.85f;
                    }
                    float m = (Hash(x / 32, y / 32) - 0.5f) * 14f;
                    r -= m; g -= m; b -= m;
                    break;
                }
                case "granite": // sparse dark speckle
                {
                    float n = Hash(x, y);
                    if (n > 0.93f) { r -= 95; g -= 95; b -= 100; }
                    else if (n < 0.06f) { r -= 55; g -= 55; b -= 55; }
                    break;
                }
                case "brick": // 4 courses x 2 bricks, offset rows, dark mortar
                {
                    const int courseH = 32, brickW = 64;
                    int row = y / courseH;
                    int xx = (x + (row % 2) * (brickW / 2)) % S;
                    if (y % courseH < 3 || xx % brickW < 3)
                    {
                        r = 172; g = 166; b = 160; // mortar
                    }
                    else
                    {
                        float j = Hash(xx / brickW, row) * 26f;
                        r = 243 - j; g = 232 - j; b = 224 - j;
                    }
                    break;
                }
                default: // "sand": fine pale speckle
                {
                    float n = Hash(x, y);
                    float d = n * 38f;
                    r = 243 - d; g = 239 - d; b = 230 - d;
                    break;
                }
                case "grid": // baseplate stud grid: one cell per tile, darker lines on two edges
                {
                    const int line = 5;
                    if (x < line || y < line) { r = 178; g = 178; b = 182; }
                    break;
                }
            }
            int i = (y * S + x) * 4;
            px[i] = (byte)Math.Round(Math.Clamp(r, 0f, 255f));
            px[i + 1] = (byte)Math.Round(Math.Clamp(g, 0f, 255f));
            px[i + 2] = (byte)Math.Round(Math.Clamp(b, 0f, 255f));
            px[i + 3] = 255;
        }
        return px;
    }
}
