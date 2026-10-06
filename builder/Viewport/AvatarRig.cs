using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using builder.Settings;

namespace builder.Viewport;

/// <summary>Beta blocky avatar (Roblox-style): a cylinder head, box torso,
/// arms and legs posed procedurally (idle, walk, jump, fall, swim) around
/// the physics capsule. Parts live in <see cref="StudioScene.AvatarRigParts"/>
/// — never in the scene — so saving, picking, physics and undo never see
/// them. The capsule hides while the rig shows; the face panel rides the head.</summary>
public sealed class AvatarRig
{
    private const float LegH = 0.6f, LegW = 0.32f;
    private const float TorsoH = 0.6f, TorsoW = 0.72f, TorsoD = 0.38f;
    private const float ArmH = 0.6f, ArmW = 0.3f;
    private const float HeadH = 0.42f, HeadD = 0.42f;

    private static readonly Color4 HeadColor = new(1f, 0.84f, 0.35f, 1f);
    private static readonly Color4 LegColor = new(0.85f, 0.86f, 0.88f, 1f);

    private SceneObject _torso = null!;
    private SceneObject _head = null!;
    private SceneObject _armL = null!;
    private SceneObject _armR = null!;
    private SceneObject _legL = null!;
    private SceneObject _legR = null!;

    /// <summary>Draw/cast order is fixed: torso, head, arms, legs.</summary>
    public readonly List<SceneObject> Parts = new();

    // Pose state (lerped every frame toward per-mode targets).
    private float _yaw;
    private float _phase;
    private float _time;
    private float _legSwing;
    private float _legBias;
    private float _armSwingL;
    private float _armSwingR;
    private float _spread;
    private float _pitch;
    private float _bob;
    private bool _attached;

    // Saved capsule/face state (restored on detach for mid-play toggling).
    private float _savedTransparency;
    private bool _savedCastShadow;
    private Vector3 _savedFaceSize = new(0.7f, 0.7f, 0.05f);

    /// <summary>Readouts for the headless pose test (radians unless noted).</summary>
    public float LegSwing => _legSwing;
    public float ArmSwingL => _armSwingL;
    public float ArmSwingR => _armSwingR;
    public float ArmSpread => _spread;
    public float TorsoPitch => _pitch;
    public float Phase => _phase;
    public float Yaw => _yaw;

    private static SceneObject Limb(string name, ShapeKind shape, Vector3 size, Color4 color) => new()
    {
        Name = name, Shape = shape, Size = size, Color = color,
        Material = MaterialKind.Plastic, Anchored = true, CanCollide = false,
    };

    /// <summary>Build the six parts, hide the capsule, fit the face to the head.</summary>
    public void Attach(SceneObject player, SceneObject? face, List<SceneObject> sceneRig)
    {
        if (_attached) return;
        _attached = true;
        var s = AppSettings.Current;
        var body = new Color4(s.AvatarR, s.AvatarG, s.AvatarB, 1f);
        var limb = new Color4(
            body.R + (1f - body.R) * 0.45f,
            body.G + (1f - body.G) * 0.45f,
            body.B + (1f - body.B) * 0.45f, 1f);
        _torso = Limb("Avatar Torso", ShapeKind.Block, new Vector3(TorsoW, TorsoH, TorsoD), body);
        _head = Limb("Avatar Head", ShapeKind.RoundedBox, new Vector3(HeadD, HeadH, HeadD), HeadColor);
        _armL = Limb("Avatar Arm L", ShapeKind.Block, new Vector3(ArmW, ArmH, ArmW), limb);
        _armR = Limb("Avatar Arm R", ShapeKind.Block, new Vector3(ArmW, ArmH, ArmW), limb);
        _legL = Limb("Avatar Leg L", ShapeKind.Block, new Vector3(LegW, LegH, LegW), LegColor);
        _legR = Limb("Avatar Leg R", ShapeKind.Block, new Vector3(LegW, LegH, LegW), LegColor);
        Parts.Clear();
        Parts.Add(_torso); Parts.Add(_head);
        Parts.Add(_armL); Parts.Add(_armR);
        Parts.Add(_legL); Parts.Add(_legR);
        _yaw = SceneObject.QuatToEuler(player.Orientation).Y * MathF.PI / 180f;
        _phase = 0; _time = 0;
        _legSwing = 0; _legBias = 0; _armSwingL = 0; _armSwingR = 0;
        _spread = 0.08f; _pitch = 0; _bob = 0;
        _savedTransparency = player.Transparency;
        _savedCastShadow = player.CastShadow;
        player.Transparency = 1f; // capsule vanishes (also auto-skips the depth pass)
        player.CastShadow = false;
        if (face != null)
        {
            _savedFaceSize = face.Size;
            face.Size = new Vector3(0.38f, 0.38f, 0.05f);
        }
        sceneRig.Clear();
        sceneRig.AddRange(Parts);
    }

    /// <summary>Restore the capsule + face, drop the parts.</summary>
    public void Detach(SceneObject? player, SceneObject? face, List<SceneObject> sceneRig)
    {
        if (!_attached) return;
        _attached = false;
        if (player != null)
        {
            player.Transparency = _savedTransparency;
            player.CastShadow = _savedCastShadow;
        }
        if (face != null) face.Size = _savedFaceSize;
        sceneRig.Clear();
        Parts.Clear();
    }

    private static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= MathF.PI * 2f;
        while (a < -MathF.PI) a += MathF.PI * 2f;
        return a;
    }

    private static float LerpAngle(float a, float b, float t) => a + WrapPi(b - a) * t;
    private static Vector3 Xform(Vector3 v, Quaternion q) => Vector3.Transform(v, q);

    /// <summary>Pose the rig around the capsule. Pure scene-object math
    /// (no physics calls) so the headless probe can drive it directly.</summary>
    public void Update(SceneObject player, SceneObject? face,
        float vx, float vy, float vz, bool grounded, bool swimming, double dt)
    {
        float dts = (float)Math.Clamp(dt, 0.0, 0.05);
        bool hidden = player.Hidden;
        foreach (var p in Parts) p.Hidden = hidden;
        if (hidden) return;
        float speed = MathF.Sqrt(vx * vx + vz * vz);
        _time += dts;

        bool air = !grounded && !swimming;
        float runAmp = Math.Clamp(speed / 5f, 0f, 1f);

        float legSwingT = 0f, legBiasT = 0f, armLT = 0f, armRT = 0f;
        float spreadT = 0.08f, pitchT = 0f, bobT = 0f;
        if (swimming)
        {
            _phase += dts * 7.5f; // paddle windmill (continuous angles, wrapped lerp)
            armLT = _phase;
            armRT = _phase + MathF.PI;
            legSwingT = MathF.Sin(_phase * 2f) * 0.3f; // flutter kick
            spreadT = 0.25f;
            pitchT = 1.05f; // near-horizontal in the water
            bobT = MathF.Sin(_phase * 2f) * 0.03f;
        }
        else if (air)
        {
            if (vy > 1f) // rising: arms up, legs trail
            {
                legBiasT = -0.3f;
                armLT = -2.5f; armRT = -2.5f;
                spreadT = 0.55f;
                pitchT = -0.12f;
            }
            else if (vy < -1f) // falling: starfish, dangling kick
            {
                legSwingT = MathF.Sin(_time * 7f) * 0.12f;
                armLT = -0.5f; armRT = -0.5f;
                spreadT = 1.15f;
                pitchT = 0.15f;
            }
            // apex (|vy| small): hold — the lerp coasts on previous values.
        }
        else if (speed > 0.5f)
        {
            _phase += dts * (3f + speed * 1.35f);
            legSwingT = MathF.Sin(_phase) * 0.62f * runAmp;
            armLT = -legSwingT * 0.9f; // arms oppose the same-side leg
            armRT = legSwingT * 0.9f;
            pitchT = 0.10f * runAmp; // slight forward lean
            bobT = Math.Abs(MathF.Cos(_phase)) * 0.07f * runAmp;
        }
        else
        {
            bobT = MathF.Sin(_time * 2.2f) * 0.015f; // breathe
        }

        float k = Math.Min(1f, dts * 10f);
        float ka = swimming ? Math.Min(1f, dts * 6f) : k;
        _legSwing += (legSwingT - _legSwing) * k;
        _legBias += (legBiasT - _legBias) * k;
        _armSwingL = LerpAngle(_armSwingL, armLT, ka);
        _armSwingR = LerpAngle(_armSwingR, armRT, ka);
        _spread += (spreadT - _spread) * k;
        _pitch += (pitchT - _pitch) * k;
        _bob += (bobT - _bob) * k;

        // Live avatar color on torso + arms.
        var s = AppSettings.Current;
        var body = new Color4(s.AvatarR, s.AvatarG, s.AvatarB, 1f);
        _torso.Color = body;
        _armL.Color = _armR.Color = new Color4(
            body.R + (1f - body.R) * 0.45f,
            body.G + (1f - body.G) * 0.45f,
            body.B + (1f - body.B) * 0.45f, 1f);

        // Facing follows the physics yaw (it eases toward the walk direction).
        _yaw = SceneObject.QuatToEuler(player.Orientation).Y * MathF.PI / 180f;
        var yawQ = Quaternion.FromAxisAngle(Vector3.UnitY, _yaw);
        var torsoQ = yawQ * Quaternion.FromAxisAngle(Vector3.UnitX, _pitch);
        var legFrameQ = yawQ * Quaternion.FromAxisAngle(Vector3.UnitX, _pitch * 0.5f);

        float ch = player.Size.Y * 0.5f; // capsule half height: feet plant here
        Vector3 hip = player.Position + new Vector3(0, -ch + LegH + _bob, 0);

        _torso.Position = hip + Xform(new Vector3(0, TorsoH / 2f, 0), torsoQ);
        _torso.Orientation = torsoQ;
        Vector3 headC = hip + Xform(new Vector3(0, TorsoH + HeadH / 2f + 0.02f, 0), torsoQ);
        _head.Position = headC;
        var headQ = yawQ * Quaternion.FromAxisAngle(Vector3.UnitX, _pitch * 0.35f);
        _head.Orientation = headQ;

        var qLegL = legFrameQ * Quaternion.FromAxisAngle(Vector3.UnitX, _legSwing + _legBias);
        var qLegR = legFrameQ * Quaternion.FromAxisAngle(Vector3.UnitX, -_legSwing + _legBias);
        Vector3 hipL = hip + Xform(new Vector3(-0.17f, 0, 0), yawQ);
        Vector3 hipR = hip + Xform(new Vector3(0.17f, 0, 0), yawQ);
        _legL.Position = hipL + Xform(new Vector3(0, -LegH / 2f, 0), qLegL);
        _legL.Orientation = qLegL;
        _legR.Position = hipR + Xform(new Vector3(0, -LegH / 2f, 0), qLegR);
        _legR.Orientation = qLegR;

        var qArmL = torsoQ * Quaternion.FromAxisAngle(Vector3.UnitX, _armSwingL)
            * Quaternion.FromAxisAngle(Vector3.UnitZ, -_spread);
        var qArmR = torsoQ * Quaternion.FromAxisAngle(Vector3.UnitX, _armSwingR)
            * Quaternion.FromAxisAngle(Vector3.UnitZ, _spread);
        float shX = TorsoW / 2f + 0.05f, shY = TorsoH - 0.12f;
        Vector3 shL = hip + Xform(new Vector3(-shX, shY, 0), torsoQ);
        Vector3 shR = hip + Xform(new Vector3(shX, shY, 0), torsoQ);
        _armL.Position = shL + Xform(new Vector3(0, -ArmH / 2f, 0), qArmL);
        _armL.Orientation = qArmL;
        _armR.Position = shR + Xform(new Vector3(0, -ArmH / 2f, 0), qArmR);
        _armR.Orientation = qArmR;

        // The face panel rides the head front (physics keeps it on the
        // capsule otherwise; the rig runs after Step so it wins).
        if (face != null)
        {
            Vector3 fwd = Xform(Vector3.UnitZ, headQ);
            face.Position = headC + fwd * (HeadD / 2f + 0.04f);
            face.Orientation = headQ;
        }
    }
}
