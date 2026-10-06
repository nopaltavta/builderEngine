using System.Collections.Generic;
using builder.Viewport;

namespace builder.Models;

/// <summary>
/// On-disk place format (.bp, JSON). Versioned: loaders accept 1+ but reject
/// unknown <see cref="Format"/>. Format 2 adds velocity + timed parts, format 3
/// adds checkpoints + bounce pads, format 4 adds part sounds, format 5 adds the
/// sky preset name, format 6 adds sky theme values (tint, stars, clouds, sun),
/// format 7 adds imported meshes (fbx/obj), format 8 adds mesh fidelity options,
/// format 9 adds water volumes, format 10 adds sound play-on-touch,
/// format 11 adds lock + magnet parts, format 12 adds the cartoon sky style,
/// format 13 adds part texture tiling, format 14 adds particle emitters,
/// format 15 adds particle effect presets, format 16 adds new shapes
/// (HalfBall, HollowCylinder, Bowl, Arch, HexPrism, Truss), format 17 adds
/// the editor Hidden flag, format 18 adds mesh color brightness, format 19
/// adds tiling texture overlays, format 20 adds Lua part scripts; older
/// files load with defaults. Only scene content lives here; render
/// content lives here; render preferences
/// stay in AppSettings.
/// </summary>
public sealed class PlaceFile
{
    public int Format { get; set; } = 20;
    public string App { get; set; } = "builder";
    public List<PartDto> Parts { get; set; } = new();
    public List<LightDto> Lights { get; set; } = new();
    public SunDto Sun { get; set; } = new();
    public AtmosphereDto Atmosphere { get; set; } = new();
    public float Ambient { get; set; } = 1f;
    public float TimeOfDay { get; set; } = 12f;
    public string SkyPreset { get; set; } = "Clear Blue";
    public CameraDto Camera { get; set; } = new();
}

public sealed class PartDto
{
    public string Name { get; set; } = "Part";
    public ShapeKind Shape { get; set; } = ShapeKind.Block;
    public float[] Position { get; set; } = { 0, 0, 0 };
    public float[] Size { get; set; } = { 2, 2, 2 };
    public float[] Rotation { get; set; } = { 0, 0, 0 };
    public float[] Color { get; set; } = { 0.25f, 0.55f, 0.95f };
    public MaterialKind Material { get; set; } = MaterialKind.Plastic;
    public bool Anchored { get; set; }
    public bool IsSpawn { get; set; }
    public bool IsNpc { get; set; }
    public NpcKind NpcType { get; set; } = NpcKind.Enemy;
    public float NpcDamage { get; set; } = 15f;
    public float NpcSpeed { get; set; } = 3f;
    public string NpcDialogue { get; set; } = "Hello!";
    public float NpcChatRange { get; set; } = 10f;
    public float NpcChatInterval { get; set; } = 5f;
    public string? NpcFaceImage { get; set; }
    public float Damage { get; set; }
    public float Tiling { get; set; } = 1f;
    public string TeleportLink { get; set; } = "";
    public bool IsCheckpoint { get; set; }
    public bool IsBouncePad { get; set; }
    public float BouncePower { get; set; } = 20f;
    public bool IsWater { get; set; }
    public bool IsEmitter { get; set; }
    public string ParticlePreset { get; set; } = "None";
    public float EmissionRate { get; set; } = 20f;
    public float ParticleLifetime { get; set; } = 1.5f;
    public float ParticleSpeed { get; set; } = 5f;
    public float ParticleSize { get; set; } = 0.5f;
    public float ParticleSpread { get; set; } = 0.3f;
    public float ParticleGravity { get; set; }
    public bool Hidden { get; set; }
    public bool Locked { get; set; }
    public bool IsMagnet { get; set; }
    public float MagnetPower { get; set; } = 15f;
    public float MagnetRange { get; set; } = 10f;
    public bool IsConveyor { get; set; }
    public float[] ConveyorDirection { get; set; } = { 1, 0, 0 };
    public float ConveyorSpeed { get; set; } = 8f;
    public bool IsMovingPlatform { get; set; }
    public float MoveDistance { get; set; } = 8f;
    public float MoveSpeed { get; set; } = 2f;
    public List<float[]> Waypoints { get; set; } = new();
    public bool IsSpinner { get; set; }
    public float SpinSpeed { get; set; } = 45f;
    public bool IsBreakable { get; set; }
    public int BreakHits { get; set; } = 2;
    public bool IsTimedPart { get; set; }
    public float TimedHidden { get; set; } = 1f;
    public float TimedOffset { get; set; }
    public float TimedVisible { get; set; } = 1f;
    public float[] VelocityDirection { get; set; } = { 0, 0, 0 };
    public float VelocitySpeed { get; set; }
    public int VelocityMode { get; set; }
    public float Mass { get; set; } = 1f;
    public bool CanCollide { get; set; } = true;
    public float Transparency { get; set; }
    public float Reflectance { get; set; }
    public float ColorBrightness { get; set; } = 1f;
    public bool CastShadow { get; set; } = true;
    public string? DecalImage { get; set; }
    public string DecalFace { get; set; } = "Front";
    public float DecalTransparency { get; set; }
    public float DecalBlur { get; set; }
    public List<DecalDto> Decals { get; set; } = new();
    public List<TextDto> Texts { get; set; } = new();
    public List<TextureDto> Textures { get; set; } = new();
    public string? SoundPath { get; set; }
    public float SoundVolume { get; set; } = 0.5f;
    public bool SoundLooped { get; set; }
    public bool SoundPlaying { get; set; }
    public bool SoundPlayOnTouch { get; set; }
    public string Script { get; set; } = "";
    public string? MeshPath { get; set; }
    public CollisionFidelityKind CollisionFidelity { get; set; } = CollisionFidelityKind.Box;
    public RenderFidelityKind RenderFidelity { get; set; } = RenderFidelityKind.Normal;
}

public sealed class DecalDto
{
    public string Image { get; set; } = "";
    public string Face { get; set; } = "Front";
    public float Transparency { get; set; }
    public float Blur { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1f;
}

public sealed class TextureDto
{
    public string Image { get; set; } = "";
    public string Face { get; set; } = "Front";
    public float Transparency { get; set; }
    public float TilingX { get; set; } = 1f;
    public float TilingY { get; set; } = 1f;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
}

public sealed class TextDto
{
    public string Text { get; set; } = "Text";
    public string Face { get; set; } = "Front";
    public string Font { get; set; } = "Arial";
    public float Size { get; set; } = 48;
    public string Color { get; set; } = "255, 255, 255";
    public float Transparency { get; set; }
    public float Blur { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1f;
    public bool Bold { get; set; }
    public float Outline { get; set; }
    public string OutlineColor { get; set; } = "0, 0, 0";
}

public sealed class LightDto
{
    public string Name { get; set; } = "PointLight";
    public LightKind Kind { get; set; } = LightKind.Point;
    public float[] Position { get; set; } = { 0, 5, 0 };
    public float[] Color { get; set; } = { 1f, 1f, 1f };
    public float Brightness { get; set; } = 2f;
    public float Range { get; set; } = 16f;
}

public sealed class SunDto
{
    public float[] Direction { get; set; } = { 0.5f, 0.8f, 0.6f };
    public float[] Color { get; set; } = { 1f, 0.97f, 0.92f };
    public float Intensity { get; set; } = 1f;
    public bool Shadows { get; set; } = true;
}

public sealed class AtmosphereDto
{
    public float FogDensity { get; set; } = 0.008f;
    public float[] FogColor { get; set; } = { 0.50f, 0.62f, 0.80f };
    public float Haze { get; set; } = 0.005f;
    public float[] SkyTint { get; set; } = { 0.8f, 0.9f, 1.1f };
    public float StarAmount { get; set; } = 0f;
    public float CloudAmount { get; set; } = 0f;
    public float SunSize { get; set; } = 1f;
    public bool SunStripes { get; set; }
    public int SkyStyle { get; set; } = 0;
}

public sealed class CameraDto
{
    public float[] Position { get; set; } = { 0, 1.5f, 8f };
    public float Yaw { get; set; } = -90f;
    public float Pitch { get; set; } = -10f;
}
