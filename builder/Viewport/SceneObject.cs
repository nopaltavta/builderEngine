using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenTK.Mathematics;

namespace builder.Viewport;

/// <summary>Light flavor for ShapeKind.None objects. Parts always use None.</summary>
public enum LightKind
{
    None,
    Point,
    Spot, // legacy load alias only (mapped to Point); the spot feature is removed
}

public enum NpcKind { Enemy, Friendly }

/// <summary>How a part's LinearVelocity reaches its cruise speed in Play mode.
/// Both modes cruise the flat plane only; gravity always owns vertical.</summary>
public enum SpeedMode
{
    /// <summary>Ramp horizontal velocity toward Speed (~25 studs/s²).</summary>
    Accelerating = 0,
    /// <summary>Hold the exact Speed with no ramp and no braking.</summary>
    Stable = 1,
}

/// <summary>Installed system font families for text layers (cached, alphabetical).</summary>
public static class TextFonts
{
    private static IReadOnlyList<string>? _names;

    public static IReadOnlyList<string> Names => _names ??=
        FontFamily.Families.Select(f => f.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool Exists(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        Names.Any(n => n.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string OrFallback(string? name, string fallback = "Arial") =>
        Exists(name) ? name!.Trim() : Exists(fallback) ? fallback : Names.FirstOrDefault() ?? fallback;
}

public sealed class DecalLayer
{
    public string Image { get; set; } = "";
    public string Face { get; set; } = "Front";
    public float Transparency { get; set; }
    public float Blur { get; set; }
    /// <summary>Center offset on the face, fractions (+x right, +y up). 0,0 = centered.</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    /// <summary>Fraction of the face covered. 1 = full face.</summary>
    public float Scale { get; set; } = 1f;
    public DecalLayer Clone() => new()
    {
        Image = Image, Face = Face, Transparency = Transparency, Blur = Blur,
        OffsetX = OffsetX, OffsetY = OffsetY, Scale = Scale,
    };
}

/// <summary>Tiling image overlay: a decal that repeats across the face
/// (Roblox-style texture). Tiling in tiles, offsets in UV units (wrap).</summary>
public sealed class TextureLayer
{
    public string Image { get; set; } = "";
    public string Face { get; set; } = "Front";
    public float Transparency { get; set; }
    /// <summary>Repeat count across the face. 1 = single image like a decal.</summary>
    public float TilingX { get; set; } = 1f;
    public float TilingY { get; set; } = 1f;
    /// <summary>UV scroll (wraps). 0,0 = aligned.</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public TextureLayer Clone() => new()
    {
        Image = Image, Face = Face, Transparency = Transparency,
        TilingX = TilingX, TilingY = TilingY, OffsetX = OffsetX, OffsetY = OffsetY,
    };
}

/// <summary>Text drawn on a part face (decal sibling). Font is a system family name.</summary>
public sealed class TextLayer
{
    public string Text { get; set; } = "Text";
    public string Face { get; set; } = "Front";
    public string Font { get; set; } = "Arial";
    public float Size { get; set; } = 48;
    public string Color { get; set; } = "255, 255, 255";
    public float Transparency { get; set; }
    public float Blur { get; set; }
    /// <summary>Center offset on the face, fractions (+x right, +y up). 0,0 = centered.</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    /// <summary>Fraction of the face covered. 1 = full face.</summary>
    public float Scale { get; set; } = 1f;
    /// <summary>Use a bold face when the selected system font provides one.</summary>
    public bool Bold { get; set; }
    /// <summary>Outline thickness in raster pixels. 0 = no outline.</summary>
    public float Outline { get; set; }
    public string OutlineColor { get; set; } = "0, 0, 0";
    public TextLayer Clone() => new()
    {
        Text = Text, Face = Face, Font = Font, Size = Size,
        Color = Color, Transparency = Transparency, Blur = Blur,
        OffsetX = OffsetX, OffsetY = OffsetY, Scale = Scale,
        Bold = Bold, Outline = Outline, OutlineColor = OutlineColor,
    };
}

/// <summary>A single part in the 3D scene. Mesh is shared per shape.</summary>
public sealed class SceneObject
{
    public string Name { get; set; } = "Part";
    public ShapeKind Shape { get; set; } = ShapeKind.Block;
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Size { get; set; } = new(1, 1, 1);

    /// <summary>Orientation as XYZ euler degrees. Source of truth in edit mode.</summary>
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public Color4 Color { get; set; } = new(0.25f, 0.55f, 0.95f, 1f);
    public MaterialKind Material { get; set; } = MaterialKind.Plastic;

    /// <summary>Extra transparency 0-1 on top of the material (0 = as the material says).</summary>
    public float Transparency { get; set; }

    /// <summary>Faked sky/sun reflection 0-1 (no env map; gradient + glint).</summary>
    public float Reflectance { get; set; }

    /// <summary>Mesh color multiplier 0-2 (default 1): brightens or darkens
    /// the mesh without touching its part color. Meshes only.</summary>
    public float ColorBrightness { get; set; } = 1f;

    /// <summary>Off = invisible to the shadow map (still receives).</summary>
    public bool CastShadow { get; set; } = true;

    /// <summary>Roblox-style anchor: physics treats it as immovable (static in play mode).</summary>
    public bool Anchored { get; set; }

    /// <summary>Off = ghost: renders and selects, but never collides.</summary>
    public bool CanCollide { get; set; } = true;

    /// <summary>Transient avatar face panel (managed by Play mode, never saved).</summary>
    public bool IsAvatarFace { get; set; }

    /// <summary>Transient hide (void death: avatar + face vanish until the revive).
    /// Skipped by render, shadows, decals and picking. Never saved, never cloned.</summary>
    public bool Hidden { get; set; }

    /// <summary>Persistent 1.5-stud capsule character controlled during Play mode.</summary>
    public bool IsNpc { get; set; }
    public NpcKind NpcType { get; set; } = NpcKind.Enemy;
    public float NpcDamage { get; set; } = 15f;
    public float NpcSpeed { get; set; } = 3f;
    /// <summary>Friendly lines (or enemy taunts) separated by |. Each NPC speaks
    /// its own lines on its own timer with a bubble overhead when the player is near.</summary>
    public string NpcDialogue { get; set; } = "Hello!";

    /// <summary>Custom face image path for this NPC (front of its face panel).
    /// Null/empty = the stock face. Never validated here; Play falls back silently.</summary>
    public string? NpcFaceImage { get; set; }

    /// <summary>How close the player must be (studs) for this NPC to chat. 4-40.</summary>
    public float NpcChatRange { get; set; } = 10f;

    /// <summary>Pause between chat lines in seconds. 2-20, with slight natural variation.</summary>
    public float NpcChatInterval { get; set; } = 5f;

    /// <summary>Light flavor (only meaningful when Shape is None).</summary>
    public LightKind Light { get; set; } = LightKind.None;

    /// <summary>Light brightness 0-20 (Roblox Brightness).</summary>
    public float Brightness { get; set; } = 2f;

    /// <summary>Light reach in studs 1-100 (Roblox Range).</summary>
    public float Range { get; set; } = 16f;

    /// <summary>Body mass for Play mode (heavier parts push harder; all fall the same).</summary>
    public float Mass { get; set; } = 1f;

    /// <summary>Lua source run in Play mode (empty = no script). Capped in the editor.</summary>
    public string Script { get; set; } = "";

    /// <summary>Spawn point: the avatar starts here on Play; multiples allowed (first wins).</summary>
    public bool IsSpawn { get; set; }

    /// <summary>Killbrick damage 0-1000 (Roblox-style). 0 = harmless part,
    /// 100 = one-shot kill at full health. Only the Killbrick part type uses it.</summary>
    public float Damage { get; set; }

    /// <summary>True when this part hurts the avatar on touch.</summary>
    public bool IsKillbrick => Damage > 0.01f;

    /// <summary>Texture tiles per stud for textured materials (1 = one texture
    /// per stud). Only used when the material has a texture; clamped 0.1-8.</summary>
    public float Tiling { get; set; } = 1f;

    /// <summary>Teleport-pad link id ("Pad1"). Pads sharing an id teleport the
    /// avatar to each other on touch. Empty = not a pad.</summary>
    public string TeleportLink { get; set; } = "";

    /// <summary>True when this part is a linked teleport pad.</summary>
    public bool IsTeleportPad => !string.IsNullOrWhiteSpace(TeleportLink);

    /// <summary>Checkpoint: touching it moves the respawn point here.</summary>
    public bool IsCheckpoint { get; set; }

    /// <summary>Bounce pad: touching it launches the avatar up.</summary>
    public bool IsBouncePad { get; set; }

    /// <summary>Bounce launch velocity 0-50 (default 20).</summary>
    public float BouncePower { get; set; } = 20f;

    /// <summary>Water volume: avatars swim in it, parts sink slowly through it.</summary>
    public bool IsWater { get; set; }

    /// <summary>Particle emitter: spawns soft round sprites (CPU sim, GL points).</summary>
    public bool IsEmitter { get; set; }
    /// <summary>Effect preset: None, Custom, Fire, Smoke, Sparkles. Drives the panel + presets.</summary>
    public string ParticlePreset { get; set; } = "None";
    /// <summary>Spawn rate in particles/sec, 0-200 (default 20).</summary>
    public float EmissionRate { get; set; } = 20f;
    /// <summary>Particle lifetime in seconds, 0.1-10 (default 1.5).</summary>
    public float ParticleLifetime { get; set; } = 1.5f;
    /// <summary>Launch speed in studs/s, 0-50 (default 5).</summary>
    public float ParticleSpeed { get; set; } = 5f;
    /// <summary>Sprite diameter in studs, 0.1-4 (default 0.5).</summary>
    public float ParticleSize { get; set; } = 0.5f;
    /// <summary>Cone spread 0-1: 0 = straight up, 1 = all directions (default 0.3).</summary>
    public float ParticleSpread { get; set; } = 0.3f;
    /// <summary>Downward pull in studs/s², -20-20 (default 0; negative floats up).</summary>
    public float ParticleGravity { get; set; }
    /// <summary>Locked: not selectable in the viewport, skips transform drags and delete.</summary>
    public bool Locked { get; set; }
    /// <summary>Magnet: pulls unanchored parts (and the avatar) toward its center.</summary>
    public bool IsMagnet { get; set; }
    /// <summary>Magnet pull strength in studs/s² (default 15).</summary>
    public float MagnetPower { get; set; } = 15f;
    /// <summary>Magnet reach in studs (default 10).</summary>
    public float MagnetRange { get; set; } = 10f;
    /// <summary>Conveyor: rides bodies on its top along Direction at Speed.</summary>
    public bool IsConveyor { get; set; }
    /// <summary>Conveyor travel direction, part-local (default +X). Y is flattened.</summary>
    public Vector3 ConveyorDirection { get; set; } = Vector3.UnitX;
    /// <summary>Conveyor belt speed in studs/s (default 8).</summary>
    public float ConveyorSpeed { get; set; } = 8f;
    /// <summary>Moving platform: slides along part-local X by MoveDistance, ping-pong.</summary>
    public bool IsMovingPlatform { get; set; }
    /// <summary>Waypoint loop (world positions). 2+ loops at MoveSpeed; 1 holds; 0 = ping-pong.</summary>
    public List<Vector3> Waypoints { get; } = new();
    /// <summary>Max waypoints per platform (bounds rows + marker balls).</summary>
    public const int MaxWaypoints = 16;
    /// <summary>Moving platform travel in studs each way (default 8).</summary>
    public float MoveDistance { get; set; } = 8f;
    /// <summary>Moving platform speed in studs/s (default 2).</summary>
    public float MoveSpeed { get; set; } = 2f;
    /// <summary>Spinning platform: rotates around Y at SpinSpeed.</summary>
    public bool IsSpinner { get; set; }
    /// <summary>Spinner rate in degrees/s (default 45).</summary>
    public float SpinSpeed { get; set; } = 45f;
    /// <summary>Breakable: deleted after BreakHits avatar stomps (restored on Stop).</summary>
    public bool IsBreakable { get; set; }
    /// <summary>Stomps needed to shatter (default 2: first thuds, second breaks).</summary>
    public int BreakHits { get; set; } = 2;
    /// <summary>Hits taken so far. Never saved.</summary>
    public int BreakCount;
    /// <summary>Last break-hit SimTime (cooldown). Never saved.</summary>
    public double BreakCooldown = -1.0;

    /// <summary>Timed part: loops visible/hidden phases (disappearing platforms).</summary>
    public bool IsTimedPart { get; set; }

    /// <summary>Hidden phase length in seconds (default 1).</summary>
    public float TimedHidden { get; set; } = 1f;

    /// <summary>Phase shift in seconds into the cycle (default 0 = starts visible).</summary>
    public float TimedOffset { get; set; }

    /// <summary>Visible phase length in seconds (default 1).</summary>
    public float TimedVisible { get; set; } = 1f;

    /// <summary>Visible at cycle time t? Starts visible; offset shifts the phase.</summary>
    public bool TimedVisibleAt(double t)
    {
        float vis = Math.Max(TimedVisible, 0f);
        float hid = Math.Max(TimedHidden, 0f);
        float period = vis + hid;
        if (period < 0.05f) return true; // degenerate: always on
        double pos = (t + TimedOffset) % period;
        if (pos < 0) pos += period;
        return pos < vis;
    }

    /// <summary>LinearVelocity travel direction, relative to the part: turning the
    /// part steers the thrust (mount-style). Zero = no drive.</summary>
    public Vector3 VelocityDirection { get; set; } = Vector3.Zero;

    /// <summary>LinearVelocity travel speed 0-100 studs/s. 0 = off.</summary>
    public float VelocitySpeed { get; set; }

    /// <summary>LinearVelocity ramp behavior (Accelerating vs Always/Stable).</summary>
    public SpeedMode VelocityMode { get; set; } = SpeedMode.Accelerating;

    /// <summary>True when this part is velocity-driven in Play mode.</summary>
    public bool HasVelocity => VelocitySpeed > 0.01f && VelocityDirection.LengthSquared > 1e-8f;

    /// <summary>True when any velocity setting differs from defaults (controls
    /// whether the Properties panel shows the Velocity section).</summary>
    public bool HasVelocityConfig => VelocitySpeed != 0f
        || VelocityDirection.LengthSquared > 1e-8f
        || VelocityMode != SpeedMode.Accelerating;
    public string? DecalImage { get; set; }
    public float DecalTransparency { get; set; }

    /// <summary>Imported mesh file (fbx/obj) for Shape.Mesh. Null/empty = block placeholder.</summary>
    public string? MeshPath { get; set; }
    public bool HasMesh => Shape == ShapeKind.Mesh && !string.IsNullOrWhiteSpace(MeshPath);
    /// <summary>Mesh collision detail: box, or convex hull hugging the file.</summary>
    public CollisionFidelityKind CollisionFidelity { get; set; } = CollisionFidelityKind.Box;
    /// <summary>Mesh render detail: decimated, as imported, or resmoothed.</summary>
    public RenderFidelityKind RenderFidelity { get; set; } = RenderFidelityKind.Normal;

    /// <summary>Part sound (Roblox-style): audio file path. Null/empty = no sound.</summary>
    public string? SoundPath { get; set; }
    /// <summary>Part sound volume 0-1 (default 0.5).</summary>
    public float SoundVolume { get; set; } = 0.5f;
    /// <summary>Part sound repeats instead of playing once.</summary>
    public bool SoundLooped { get; set; }
    /// <summary>Part sound is playing (edit preview + Play mode).</summary>
    public bool SoundPlaying { get; set; }
    /// <summary>Play the sound from the top when the avatar touches the part (1s cooldown).</summary>
    public bool SoundPlayOnTouch { get; set; }
    /// <summary>Last touch-trigger SimTime (cooldown). Never saved.</summary>
    public double SoundTouchCooldown = -1.0;
    public bool HasSound => !string.IsNullOrWhiteSpace(SoundPath);
    /// <summary>Runtime OpenAL source id (0 = none). Never saved.</summary>
    public int SoundSource;
    /// <summary>Path the live source was built from (detects re-picks). Never saved.</summary>
    public string? SoundLoadedPath;
    public float DecalBlur { get; set; }
    public string DecalFace { get; set; } = "Front";
    public List<DecalLayer> Decals { get; } = new();
    public List<TextLayer> Texts { get; } = new();
    public List<TextureLayer> Textures { get; } = new();

    /// <summary>Max decals / texts / textures per part (bounds overdraw + Properties rows).</summary>
    public const int MaxDecals = 6;
    public const int MaxTexts = 6;
    public const int MaxTextures = 4;

    /// <summary>Move the legacy single decal into <see cref="Decals"/> (once). No-op otherwise.</summary>
    public void MigrateSingleToList()
    {
        if (string.IsNullOrWhiteSpace(DecalImage)) return;
        if (Decals.Count >= MaxDecals) { DecalImage = null; return; }
        Decals.Add(new DecalLayer
        {
            Image = DecalImage,
            Face = string.IsNullOrWhiteSpace(DecalFace) ? "Front" : DecalFace,
            Transparency = DecalTransparency,
            Blur = DecalBlur,
        });
        DecalImage = null;
        DecalTransparency = 0;
        DecalBlur = 0;
        DecalFace = "Front";
    }

    /// <summary>Full orientation. Edit mode composes it from Rotation; play mode reads it from physics.</summary>
    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    /// <summary>Rebuild Orientation from the editor euler (edit mode only).</summary>
    public void ComposeOrientation()
    {
        Orientation = EulerToQuat(Rotation);
    }

    /// <summary>
    /// Euler degrees -&gt; quaternion, yaw-pitch-roll (Y, then X, then Z intrinsic).
    /// Closed-form Hamilton product; matches FromAxisAngle on every single axis.
    /// </summary>
    public static Quaternion EulerToQuat(Vector3 eulerDeg)
    {
        float hx = MathHelper.DegreesToRadians(eulerDeg.X) / 2f;
        float hy = MathHelper.DegreesToRadians(eulerDeg.Y) / 2f;
        float hz = MathHelper.DegreesToRadians(eulerDeg.Z) / 2f;
        float cx = MathF.Cos(hx), sx = MathF.Sin(hx);
        float cy = MathF.Cos(hy), sy = MathF.Sin(hy);
        float cz = MathF.Cos(hz), sz = MathF.Sin(hz);
        return new Quaternion(
            cy * sx * cz + sy * cx * sz,
            sy * cx * cz - cy * sx * sz,
            cy * cx * sz - sy * sx * cz,
            cy * cx * cz + sy * sx * sz);
    }

    /// <summary>
    /// Quaternion -&gt; euler degrees, exact inverse of <see cref="EulerToQuat"/>
    /// (standard YXZ extraction on the Hamilton rotation matrix).
    /// </summary>
    public static Vector3 QuatToEuler(Quaternion q)
    {
        float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
        float xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z;
        float wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;
        float r02 = 2f * (xz + wy);
        float r12 = 2f * (yz - wx);
        float r22 = 1f - 2f * (xx + yy);
        float r01 = 2f * (xy - wz);
        float r00 = 1f - 2f * (yy + zz);
        float y = MathF.Asin(Math.Clamp(r02, -1f, 1f));
        float x, z;
        if (MathF.Abs(r02) < 0.99999f)
        {
            x = MathF.Atan2(-r12, r22);
            z = MathF.Atan2(-r01, r00);
        }
        else // gimbal lock: fold everything into roll
        {
            x = 0;
            z = MathF.Atan2(2f * (xy + wz), 1f - 2f * (xx + zz));
        }
        const float toDeg = 180f / MathF.PI;
        return new Vector3(x * toDeg, y * toDeg, z * toDeg);
    }

    public SceneObject Clone(string name)
    {
        var copy = new SceneObject
        {
            Name = name,
            Shape = Shape,
            Position = Position,
            Size = Size,
            Rotation = Rotation,
            Color = Color,
            Material = Material,
            Transparency = Transparency,
            Reflectance = Reflectance,
            ColorBrightness = ColorBrightness,
            CastShadow = CastShadow,
            Anchored = Anchored,
            IsSpawn = IsSpawn,
            Script = Script,
            Damage = Damage,
            Tiling = Tiling,
            TeleportLink = TeleportLink,
            VelocityDirection = VelocityDirection,
            VelocitySpeed = VelocitySpeed,
            VelocityMode = VelocityMode,
            Mass = Mass,
            IsCheckpoint = IsCheckpoint,
            IsBouncePad = IsBouncePad,
            BouncePower = BouncePower,
            IsWater = IsWater,
            IsEmitter = IsEmitter,
            ParticlePreset = ParticlePreset,
            EmissionRate = EmissionRate,
            ParticleLifetime = ParticleLifetime,
            ParticleSpeed = ParticleSpeed,
            ParticleSize = ParticleSize,
            ParticleSpread = ParticleSpread,
            ParticleGravity = ParticleGravity,
            Locked = Locked,
            IsMagnet = IsMagnet,
            MagnetPower = MagnetPower,
            MagnetRange = MagnetRange,
            IsConveyor = IsConveyor,
            ConveyorDirection = ConveyorDirection,
            ConveyorSpeed = ConveyorSpeed,
            IsMovingPlatform = IsMovingPlatform,
            MoveDistance = MoveDistance,
            MoveSpeed = MoveSpeed,
            IsSpinner = IsSpinner,
            SpinSpeed = SpinSpeed,
            IsBreakable = IsBreakable,
            BreakHits = BreakHits,
            IsTimedPart = IsTimedPart,
            TimedHidden = TimedHidden,
            TimedOffset = TimedOffset,
            TimedVisible = TimedVisible,
            CanCollide = CanCollide,
            IsAvatarFace = IsAvatarFace,
            IsNpc = IsNpc, NpcType = NpcType, NpcDamage = NpcDamage, NpcSpeed = NpcSpeed, NpcDialogue = NpcDialogue,
            NpcChatRange = NpcChatRange, NpcChatInterval = NpcChatInterval,
            NpcFaceImage = NpcFaceImage,
            Light = Light,
            Brightness = Brightness,
            Range = Range,
            DecalImage = DecalImage,
            MeshPath = MeshPath,
            CollisionFidelity = CollisionFidelity,
            RenderFidelity = RenderFidelity,
            DecalTransparency = DecalTransparency,
            DecalBlur = DecalBlur,
            DecalFace = DecalFace,
            SoundPath = SoundPath,
            SoundVolume = SoundVolume,
            SoundLooped = SoundLooped,
            SoundPlaying = SoundPlaying,
            SoundPlayOnTouch = SoundPlayOnTouch,
            Orientation = Orientation,
        };
        foreach (var decal in Decals) copy.Decals.Add(decal.Clone());
        foreach (var text in Texts) copy.Texts.Add(text.Clone());
        foreach (var tex in Textures) copy.Textures.Add(tex.Clone());
        foreach (var w in Waypoints) copy.Waypoints.Add(w);
        return copy;
    }

    public (Vector3 Position, Quaternion Orientation, Vector3 Rotation) Snapshot() =>
        (Position, Orientation, Rotation);

    public void Restore((Vector3 Position, Quaternion Orientation, Vector3 Rotation) snap)
    {
        Position = snap.Position;
        Orientation = snap.Orientation;
        Rotation = snap.Rotation;
    }
}
