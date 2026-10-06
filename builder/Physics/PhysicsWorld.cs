using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities.Memory;
using builder.Viewport;
using OTK = OpenTK.Mathematics;

namespace builder.Physics;

/// <summary>
/// Owns the Bepu simulation for Play mode. Anchored parts become statics,
/// everything else dynamic. Edit mode never touches this class.
/// Shapes are added per body and released on Stop (simulation dispose + pool clear),
/// so insert/delete churn during play can't leak.
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    public const float FixedStep = 1f / 60f;
    public const int MaxSubsteps = 4;

    private BufferPool _pool = new();
    private Simulation? _sim;
    private readonly Dictionary<SceneObject, BodyEntry> _bodies = new();
    private readonly GhostSet _ghosts = new();
    private readonly PlayerSet _players = new();
    private readonly Dictionary<SceneObject, (OTK.Vector3 Position, OTK.Quaternion Orientation, OTK.Vector3 Rotation)> _snapshots = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private float _accumulator;

    /// <summary>Swim-up speed (Space) and idle sink rate inside water volumes.</summary>
    private const float SwimSpeed = 4.5f;
    private const float SinkSpeed = -1.0f;
    /// <summary>Walk-speed multiplier while swimming (water drags).</summary>
    private const float WaterWalkFactor = 0.5f;

    /// <summary>Water volumes this step (rebuilt per Step; volumes are few).</summary>
    private readonly List<Viewport.SceneObject> _waters = new();
    /// <summary>Magnet / conveyor / platform parts this step (rebuilt per Step).</summary>
    private readonly List<Viewport.SceneObject> _magnets = new();
    private readonly List<Viewport.SceneObject> _conveyors = new();
    private readonly List<Viewport.SceneObject> _movers = new();
    private readonly List<Viewport.SceneObject> _spinners = new();
    /// <summary>Breakable shattered this step (MainWindow removes it; one per frame).</summary>
    public Viewport.SceneObject? BrokeObject { get; set; }
    private bool _disposed;

    private struct BodyEntry
    {
        public bool IsStatic;
        public BodyHandle Body;
        public StaticHandle Static;
        public TypedIndex Shape;
        public Quaternion LocalRot; // shape frame in part frame (identity except the stairs ramp)
        public Vector3 LocalPos;
    }

    /// <summary>Dynamic body handles whose contacts are filtered (ghost parts).</summary>
    public sealed class GhostSet
    {
        public readonly HashSet<int> Bodies = new();
    }

    /// <summary>Dynamic-body handles belonging to the avatar, shared with contact callbacks.</summary>
    public sealed class PlayerSet
    {
        public readonly HashSet<int> Bodies = new();
    }

    public bool Simulating => _sim != null;

    // ----- player avatar (Play mode) -----
    // Velocity-driven capsule: WASD sets ground-plane velocity in camera space,
    // gravity owns Y, Space jumps when the downward ray finds support.

    /// <summary>Live player object (in the scene). Null when not playing.</summary>
    public SceneObject? Player { get; private set; }

    /// <summary>Local move input: X = strafe (+right), Z = forward. Set every frame by the UI.</summary>
    public float PlayerMoveX { get; set; }

    public float PlayerMoveZ { get; set; }

    /// <summary>Camera yaw in degrees. Movement is relative to this (Roblox-style).</summary>
    public float PlayerCamYaw { get; set; } = -90f;

    public bool JumpHeld { get; set; }

    /// <summary>Ground speed in studs/s.</summary>
    public float WalkSpeed { get; set; } = 5f;

    /// <summary>Contact friction (0 = ice, 2 = sticky). Read when Play starts.</summary>
    public float Friction { get; set; } = 1f;

    /// <summary>Gravity used by the current simulation (set at Play start).</summary>
    public float Gravity { get; private set; } = 21f;

    /// <summary>How fast the avatar turns to face movement (higher = snappier).</summary>
    public float TurnResponsiveness { get; set; } = 12f;

    /// <summary>Jump takeoff velocity. ~2 studs of air at the default gravity of 21.</summary>
    public float JumpSpeed { get; set; } = 9f;

    /// <summary>Coyote time: jump still works this long after leaving an edge.</summary>
    public float CoyoteTime { get; set; } = 0.12f;

    /// <summary>Avatar health (Roblox-style, 100 = full). Damage comes from killbricks.</summary>
    public float Health { get; private set; } = 100f;

    /// <summary>Health after a (re)spawn. Killbricks with Damage >= this one-shot.</summary>
    public float MaxHealth { get; set; } = 100f;

    /// <summary>Min seconds between two damage ticks (one touch can't drain instantly).</summary>
    public float DamageCooldown { get; set; } = 0.5f;

    /// <summary>True for exactly one Step after the avatar died (UI logs "Oof").</summary>
    public bool DiedThisStep { get; private set; }

    /// <summary>Seconds the avatar stays vanished after a void fall before reviving.</summary>
    public const float VoidRespawnDelay = 3f;

    /// <summary>True while the avatar is vanished, waiting out a void fall.</summary>
    public bool IsVoidDead { get; private set; }

    /// <summary>True for exactly one Step when a void fall starts (UI logs it).</summary>
    public bool VoidDiedThisStep { get; private set; }

    /// <summary>True for exactly one Step when the avatar revives after a void fall.</summary>
    public bool VoidRespawnedThisStep { get; private set; }

    /// <summary>Sim-time when the current void wait ends.</summary>
    public double VoidRespawnAt { get; private set; }

    /// <summary>Seconds left in the void wait (UI countdown). 0 when not waiting.</summary>
    public double VoidRespawnRemaining => IsVoidDead ? Math.Max(0, VoidRespawnAt - SimTime) : 0;

    private double _lastDamageSimTime = -100;

    /// <summary>Min seconds between two pad teleports (stops arrival ping-pong).</summary>
    public float TeleportCooldown { get; set; } = 1f;

    /// <summary>True for exactly one Step after a pad teleport (UI logs it).</summary>
    public bool TeleportedThisStep { get; private set; }

    /// <summary>Destination pad name for <see cref="TeleportedThisStep"/>.</summary>
    public string LastTeleportName { get; private set; } = "";

    private double _lastTeleportSimTime = -100;

    /// <summary>True for exactly one Step after touching a new checkpoint (UI logs it).</summary>
    public bool CheckpointThisStep { get; private set; }

    /// <summary>Checkpoint name for <see cref="CheckpointThisStep"/>.</summary>
    public string LastCheckpointName { get; private set; } = "";

    /// <summary>True for exactly one Step after a bounce launch (UI logs it).</summary>
    public bool BouncedThisStep { get; private set; }

    /// <summary>Min seconds between two bounce launches (lets each launch leave).</summary>
    public float BounceCooldown { get; set; } = 0.3f;

    private double _lastBounceSimTime = -100;

    /// <summary>Simulated seconds (fixed steps only — pauses/hitches don't count).</summary>
    public double SimTime { get; private set; }

    private double _lastGroundedSimTime = -100;
    private bool _shedLogged; // load-shed warning: once per Play session
    private Action<string>? _log;
    private readonly Random _random = new();

    /// <summary>How long a chat bubble stays overhead, in simulated seconds.</summary>
    public const double NpcBubbleDuration = 4.0;

    /// <summary>Live speech bubbles, one per chatting NPC at most. The UI projects
    /// each bubble's NPC overhead every frame and hides expired ones.</summary>
    public List<NpcBubble> ActiveBubbles { get; } = new();

    /// <summary>Per-NPC chat memory (own timer, no-repeat, enter-range greeting).</summary>
    private readonly Dictionary<SceneObject, NpcChatState> _chatStates = new();

    /// <summary>Last applied visible phase per timed part (SimTime clock).</summary>
    private readonly Dictionary<SceneObject, bool> _timedStates = new();

    /// <summary>Timed parts: flip Hidden + swap bodies on phase edges only.</summary>
    private void DriveTimed(StudioScene scene)
    {
        if (_sim == null) return;
        foreach (var o in scene.Objects)
        {
            if (!o.IsTimedPart) continue;
            bool visible = o.TimedVisibleAt(SimTime);
            if (_timedStates.TryGetValue(o, out bool last) && last == visible) continue;
            _timedStates[o] = visible;
            o.Hidden = !visible;
            if (visible) RecreateBody(o); // re-add (no-op for ghost-anchored)
            else RemoveBody(o);
        }
    }

    public sealed class NpcBubble
    {
        public SceneObject Npc { get; set; } = null!;
        public string Line { get; set; } = "";
        public double ExpiresAt { get; set; }
    }

    private sealed class NpcChatState
    {
        public bool WasInRange;
        public double NextSpeakAt = -100;
        public int LastLine = -1;
        public string LinesSource = "";
        public string[] Lines = Array.Empty<string>();
    }

    /// <summary>Dialogue lines, split once per text change (never per frame).</summary>
    private static string[] ChatLines(SceneObject npc, NpcChatState state)
    {
        string src = npc.NpcDialogue ?? "";
        if (!ReferenceEquals(state.LinesSource, src) && state.LinesSource != src)
        {
            state.LinesSource = src;
            state.Lines = src.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        return state.Lines;
    }

    /// <summary>Scratch live set for prune passes (reused: zero per-frame allocs).</summary>
    private readonly HashSet<SceneObject> _liveNpcs = new();

    /// <summary>Scratch deletion list for prune passes (reused: zero per-frame allocs).</summary>
    private readonly List<SceneObject> _pruneScratch = new();

    /// <summary>Where the avatar (re)spawns. Set by the UI on Play.</summary>
    public OTK.Vector3 SpawnPoint { get; set; } = new(0, 6, 0);

    /// <summary>Falling below this Y respawns the avatar at <see cref="SpawnPoint"/>.</summary>
    public float KillY { get; set; } = -30f;

    /// <summary>Register an already-added scene object as the driven avatar.</summary>
    public void SpawnPlayer(SceneObject p)
    {
        if (_sim == null || Player != null || _bodies.ContainsKey(p)) return;
        p.Orientation = ToOtk(Quaternion.Identity);
        Player = p;
        AddBody(p);
        if (_bodies.TryGetValue(p, out var entry) && !entry.IsStatic)
            _players.Bodies.Add(entry.Body.Value);
    }

    /// <summary>Forget the avatar (its body dies with the simulation anyway).</summary>
    public void ClearPlayer()
    {
        Player = null;
        FaceObject = null;
        _players.Bodies.Clear();
        PlayerMoveX = 0;
        PlayerMoveZ = 0;
        JumpHeld = false;
    }

    /// <summary>Face panel tracking the avatar (a ghost part, never simulated).</summary>
    public SceneObject? FaceObject { get; set; }

    /// <summary>Face offset in avatar-local space.</summary>
    public OTK.Vector3 FaceOffset { get; set; } = new(0, 0.2f, 0.38f);

    // ----- OpenTK <-> System.Numerics -----
    private static Vector3 ToNum(OTK.Vector3 v) => new(v.X, v.Y, v.Z);
    private static OTK.Vector3 ToOtk(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Quaternion ToNum(OTK.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    private static OTK.Quaternion ToOtk(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    // ----- play control -----

    public bool StartPlay(StudioScene scene, Action<string> log, float gravityStrength)
    {
        if (_sim != null) return true;
        if (scene.Objects.Count == 0)
        {
            log("Play: nothing to simulate.");
            return false;
        }
        _snapshots.Clear();
        _log = log;
        _chatStates.Clear();
        ActiveBubbles.Clear();
        _timedStates.Clear();
        foreach (var o in scene.Objects)
        {
            o.ComposeOrientation();
            _snapshots[o] = o.Snapshot();
        }
        var gravity = new Vector3(0, -gravityStrength, 0);
        Gravity = gravityStrength;
        _ghosts.Bodies.Clear(); // handle values recycle per simulation
        _players.Bodies.Clear(); // handle values recycle per simulation
        _sim = Simulation.Create(_pool, new StudioNarrowPhaseCallbacks
            { Friction = Math.Clamp(Friction, 0f, 2f), Ghosts = _ghosts, Players = _players },
            new StudioPoseIntegratorCallbacks(gravity), new SolveDescription(8, 1));
        foreach (var o in scene.Objects) AddBody(o);
        _lastTime = _clock.Elapsed.TotalSeconds;
        _accumulator = 0;
        SimTime = 0;
        _shedLogged = false;
        _lastGroundedSimTime = -100;
        IsVoidDead = false;
        VoidDiedThisStep = false;
        VoidRespawnedThisStep = false;
        VoidRespawnAt = 0;
        scene.FreezeFollow = false;
        ResetHealth();
        log($"Playing: {_bodies.Count} bodies (boxes, spheres for balls), gravity {-gravity.Y}.");
        int driving = 0, stuck = 0;
        foreach (var o in scene.Objects)
        {
            if (!o.HasVelocity) continue;
            if (o.Anchored) stuck++;
            else driving++;
        }
        if (driving + stuck > 0)
            log($"Velocity: {driving} driving, {stuck} anchored (staying put — unanchor to move).");
        int timed = 0;
        foreach (var o in scene.Objects) if (o.IsTimedPart) timed++;
        if (timed > 0) log($"Timed: {timed} timed part(s) cycling.");
        return true;
    }

    public void StopPlay(StudioScene scene)
    {
        if (_sim == null) return;
        foreach (var (o, snap) in _snapshots)
            if (scene.Objects.Contains(o)) o.Restore(snap);
        ClearPlayer();
        _bodies.Clear();
        _ghosts.Bodies.Clear();
        _players.Bodies.Clear();
        _snapshots.Clear();
        _chatStates.Clear();
        ActiveBubbles.Clear();
        _timedStates.Clear();
        foreach (var o in scene.Objects) if (o.IsTimedPart) o.Hidden = false; // show everything again
        IsVoidDead = false;
        VoidDiedThisStep = false;
        VoidRespawnedThisStep = false;
        scene.FreezeFollow = false;
        _sim.Dispose();
        _sim = null;
        _pool.Clear();
    }

    /// <summary>Fixed-step the simulation and push poses back to the scene. Call every frame.</summary>
    public void Step(StudioScene scene)
    {
        if (_sim == null || _disposed) return;
        scene.FreezeFollow = IsVoidDead; // vanish: hold the view, don't snap to spawn
        double now = _clock.Elapsed.TotalSeconds;
        _accumulator += (float)Math.Min(now - _lastTime, 0.1);
        _lastTime = now;
        _waters.Clear();
        _magnets.Clear();
        _conveyors.Clear();
        _movers.Clear();
        _spinners.Clear();
        // One scan feeds every list (was two full passes per frame).
        foreach (var o in scene.Objects)
        {
            if (o.Hidden || o.IsAvatarFace) continue;
            if (o.IsWater) _waters.Add(o);
            if (o.IsMagnet) _magnets.Add(o);
            if (o.IsConveyor) _conveyors.Add(o);
            if (o.IsMovingPlatform) _movers.Add(o);
            if (o.IsSpinner) _spinners.Add(o);
        }
        DrivePlatforms(scene); // movers/spinners teleport first so bodies collide with fresh poses
        if (IsVoidDead) HoldDead(); // vanished at spawn: no input, no drift
        else DrivePlayer(scene); // avatar velocity before the substeps consume it
        DriveNpcs(scene);
        int n = 0;
        float minFeature = MinFeatureWidth(scene); // once per Step, not per substep
        while (_accumulator >= FixedStep && n < MaxSubsteps)
        {
            // Slice fast frames thinner: a body must never travel more than half
            // the thinnest feature per slice, or it tunnels between samples.
            int slices = ComputeSlices(minFeature);
            float h = FixedStep / slices;
            for (int s = 0; s < slices; s++)
            {
                DriveTimed(scene); // phase flips swap bodies before integrate
                DriveVelocities(h); // per-slice thrust (gravity comp scales too)
                ApplyAirDrag(h); // per-slice still air
                ApplyWater(h); // buoyancy + swim (volumes are few, bodies looped once)
                DriveMagnets(h); // radial pull toward magnet centers
                DriveConveyors(); // belt riders snap to belt velocity (absolute set)
                TryStepUp(h); // walk into slabs/stairs and glide up
                _sim.Timestep(h);
                SimTime += h;
            }
            _accumulator -= FixedStep;
            n++;
        }
        if (n == MaxSubsteps)
        {
            // Spiral-of-death guard: shed load to protect FPS, sim dilates.
            _accumulator = 0;
            if (!_shedLogged)
            {
                _shedLogged = true;
                _log?.Invoke("Physics shedding load: sim running slower than wall clock (too many parts?).");
            }
        }
        // No step-up assist: the avatar only walks and jumps. Low ledges and
        // stair steps must be jumped onto; nothing ever lifts the body for you.
        foreach (var (o, e) in _bodies)
        {
            if (e.IsStatic) continue;
            BodyReference br = _sim.Bodies[e.Body];
            // Fast path: identity shape frame (everything but stairs) copies
            // the pose straight over — no inverse, no extra transforms.
            if (e.LocalRot.X == 0f && e.LocalRot.Y == 0f && e.LocalRot.Z == 0f && e.LocalRot.W == 1f
                && e.LocalPos.X == 0f && e.LocalPos.Y == 0f && e.LocalPos.Z == 0f)
            {
                o.Position = ToOtk(br.Pose.Position);
                o.Orientation = ToOtk(br.Pose.Orientation);
                continue;
            }
            // Part frame = body frame composed with the inverse shape-local frame
            // (identity for everything but stairs, so this is a no-op elsewhere).
            var invLocal = Quaternion.Inverse(e.LocalRot);
            var partRot = Quaternion.Multiply(br.Pose.Orientation, invLocal);
            o.Position = ToOtk(br.Pose.Position - Vector3.Transform(Vector3.Transform(e.LocalPos, invLocal), br.Pose.Orientation));
            o.Orientation = ToOtk(partRot);
        }
        // Contacts during the substeps can still tilt the avatar: re-lock X/Z
        // after the push so the rendered pose is always upright.
        if (Player != null && _bodies.TryGetValue(Player, out var pe) && !pe.IsStatic)
        {
            BodyReference pbr = _sim.Bodies[pe.Body];
            SnapUpright(pbr, Player);
            pbr.Velocity.Angular = Vector3.Zero;
            Player.Orientation = ToOtk(pbr.Pose.Orientation);
        }
        // Killbrick touches damage instantly, then re-tick while held;
        // slow regen (+3 / 3s) runs alongside. Pads teleport on touch with a
        // cooldown so arrivals don't ping-pong. Void falls vanish first: the avatar
        // hides for VoidRespawnDelay, then revives at spawn with full health.
        DiedThisStep = false;
        VoidDiedThisStep = false;
        VoidRespawnedThisStep = false;
            TeleportedThisStep = false;
            LastTeleportName = "";
            CheckpointThisStep = false;
            LastCheckpointName = "";
            BouncedThisStep = false;
        CheckHazards(scene);
        CheckNpcInteractions(scene);
        RegenTick();
        CheckTeleportPads(scene);
        CheckCheckpoints(scene);
        CheckBouncePads(scene);
        CheckSoundTouch(scene);
        CheckBreakable(scene);
        if (IsVoidDead)
        {
            if (SimTime >= VoidRespawnAt)
                ReviveFromVoid();
        }
        else if (Player != null && Player.Position.Y < KillY)
        {
            StartVoidDeath();
        }
        // Face panel rides the avatar (ghost: transform only, no body).
        if (Player != null && FaceObject != null)
        {
            FaceObject.Position = Player.Position +
                ToOtk(Vector3.Transform(ToNum(FaceOffset), ToNum(Player.Orientation)));
            FaceObject.Orientation = Player.Orientation;
            FaceObject.Hidden = IsVoidDead; // vanish with the avatar
        }
        if (Player != null) Player.Hidden = IsVoidDead; // vanish until the revive
    }

    /// <summary>LinearVelocity drive: unanchored parts with Speed > 0 cruise the
    /// flat plane along their part-local Direction (rotating the part steers the
    /// thrust, mount-style) while gravity fully owns vertical, so drivers fall
    /// off edges into the void. Accelerating ramps up to Speed (~25/s²);
    /// Stable holds the exact Speed with no ramp and no braking. Mass never
    /// slows the drive (momentum still applies in collisions). NPCs and the
    /// avatar are owned by their own drivers and skipped.</summary>
    private void DriveVelocities(float dt)
    {
        if (_sim == null) return;
        foreach (var (o, e) in _bodies)
        {
            if (e.IsStatic || o.IsNpc || ReferenceEquals(o, Player)) continue;
            if (!o.HasVelocity) continue;
            BodyReference br = _sim.Bodies[e.Body];
            // Part-local direction through the body's live orientation: turning
            // the part (even mid-flight, via collisions) redirects the thrust.
            var local = ToNum(o.VelocityDirection);
            float localLen = local.Length();
            if (localLen < 1e-6f || !float.IsFinite(localLen)) continue;
            var dir = Vector3.Transform(local / localLen, br.Pose.Orientation);
            // Cruise target across the ground; Y is never touched (gravity rules).
            Vector3 flat = new Vector3(dir.X, 0f, dir.Z);
            float flatLen = flat.Length();
            Vector3 hTarget = flatLen > 1e-6f
                ? flat / flatLen * Math.Min(o.VelocitySpeed, 100f)
                : Vector3.Zero;
            if (!float.IsFinite(hTarget.X + hTarget.Z)) continue;
            Vector3 cur = br.Velocity.Linear;
            if (o.VelocityMode == SpeedMode.Stable)
                br.Velocity.Linear = new Vector3(hTarget.X, cur.Y, hTarget.Z);
            else
            {
                Vector3 dv = new Vector3(hTarget.X - cur.X, 0f, hTarget.Z - cur.Z);
                float maxDv = 25f * dt;
                // Squared compare: the sqrt runs only while actually clamping.
                float dv2 = dv.X * dv.X + dv.Z * dv.Z;
                if (dv2 > maxDv * maxDv) { float s = maxDv / MathF.Sqrt(dv2); dv.X *= s; dv.Z *= s; }
                br.Velocity.Linear = new Vector3(cur.X + dv.X, cur.Y, cur.Z + dv.Z);
            }
            // Driven bodies don't tip: hold the aimed orientation and kill spin.
            // Otherwise contacts would redirect the local-frame thrust and feed
            // a tumble loop (graze → spin → steered thrust → chaos). Steering
            // stays an edit-time act (rotate the part, thrust follows).
            br.Velocity.Angular = Vector3.Zero;
            if (_snapshots.TryGetValue(o, out var snap))
                br.Pose.Orientation = ToNum(snap.Orientation);
            _sim.Awakener.AwakenBody(e.Body);
        }
    }

    /// <summary>Slices for this frame (1..8): fastest body must travel at most
    /// half the thinnest feature per slice. Counts live velocities plus what
    /// drivers are about to reach, so ramping bodies are covered too.</summary>
    private int ComputeSlices(float minFeature)
    {
        if (_sim == null || minFeature < 0f) return 1;
        float maxDisp = 0f;
        foreach (var (o, e) in _bodies)
        {
            if (e.IsStatic) continue;
            float want = _sim.Bodies[e.Body].Velocity.Linear.Length();
            if (o.HasVelocity) want = Math.Max(want, Math.Min(o.VelocitySpeed, 100f));
            if (o.IsNpc) want = Math.Max(want, o.NpcSpeed);
            if (ReferenceEquals(o, Player)) want = Math.Max(want, WalkSpeed + JumpSpeed);
            float d = want * FixedStep;
            if (d > maxDisp) maxDisp = d;
        }
        if (maxDisp <= 0f) return 1;
        return Math.Clamp((int)MathF.Ceiling(maxDisp / (minFeature * 0.5f)), 1, 8);
    }

    /// <summary>Thinnest part dimension this step (tunneling guard input).
    /// Hoisted out of ComputeSlices: one scene scan per Step, not per substep.</summary>
    private static float MinFeatureWidth(StudioScene scene)
    {
        float minFeature = float.MaxValue;
        foreach (var o in scene.Objects)
        {
            if (o.Shape == ShapeKind.None || o.IsAvatarFace) continue;
            var s = o.Size;
            float m = Math.Min(s.X, Math.Min(s.Y, s.Z));
            if (m < minFeature) minFeature = m;
        }
        if (minFeature == float.MaxValue) return -1f;
        return Math.Clamp(minFeature, 0.1f, 2f);
    }

    /// <summary>Still air for physical bodies: quadratic + linear drag toward
    /// terminal velocity (~65 studs/s down) and angular damping so spins settle.
    /// Gentle enough that cruise drives and normal falls feel unchanged; long
    /// void falls and coasting parts visibly obey it. The avatar and NPCs are
    /// owned by their own drivers and skipped.</summary>
    private void ApplyAirDrag(float dt)
    {
        if (_sim == null) return;
        const float linear = 0.02f;
        const float quadratic = 0.0043f;
        const float angular = 0.8f;
        foreach (var (o, e) in _bodies)
        {
            if (e.IsStatic || o.IsNpc || ReferenceEquals(o, Player)) continue;
            BodyReference br = _sim.Bodies[e.Body];
            if (!br.Awake) continue; // asleep: leave it (don't defeat the sleep system)
            Vector3 v = br.Velocity.Linear;
            float speed = v.Length();
            if (!float.IsFinite(speed)) { _sim.Awakener.AwakenBody(e.Body); continue; } // broken state: stay awake
            Vector3 w = br.Velocity.Angular;
            bool spinning = w.LengthSquared() > 1e-10f;
            if (speed <= 1e-4f && !spinning) continue; // at rest: no poke, so Bepu can sleep it
            if (speed > 1e-4f)
                br.Velocity.Linear = v / (1f + (linear + quadratic * speed) * dt);
            if (spinning)
                br.Velocity.Angular = w / (1f + angular * dt);
            _sim.Awakener.AwakenBody(e.Body);
        }
    }

    /// <summary>Enemy capsules pursue the player; friendlies rotate to look at them.</summary>
    private void DriveNpcs(StudioScene scene)
    {
        if (_sim == null || Player == null) return;
        Vector3 playerPos = ToNum(Player.Position);
        foreach (var npc in scene.Objects)
        {
            if (!npc.IsNpc || !_bodies.TryGetValue(npc, out var e) || e.IsStatic) continue;
            BodyReference body = _sim.Bodies[e.Body];
            Vector3 delta = playerPos - body.Pose.Position;
            delta.Y = 0;
            if (delta.LengthSquared() < 1e-5f) continue;
            Vector3 direction = Vector3.Normalize(delta);
            float yaw = MathF.Atan2(direction.X, direction.Z);
            body.Pose.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            body.Velocity.Angular = Vector3.Zero;
            if (npc.NpcType == NpcKind.Enemy)
            {
                var v = body.Velocity.Linear;
                float speed = Math.Clamp(npc.NpcSpeed, 0.5f, 12f);
                v.X = direction.X * speed;
                v.Z = direction.Z * speed;
                body.Velocity.Linear = v;
                _sim.Awakener.AwakenBody(e.Body);
            }
            else
            {
                var v = body.Velocity.Linear;
                v.X = v.Z = 0;
                body.Velocity.Linear = v;
            }
        }
    }

    /// <summary>Every NPC chats on its own timer: a greeting the moment the player
    /// walks into its range, then a fresh (never twice in a row) line every
    /// ChatInterval seconds with natural variation. Friendly chatter and enemy
    /// taunts share the system; each bubble also lands in the Output log.</summary>
    private void CheckNpcInteractions(StudioScene scene)
    {
        if (_sim == null || Player == null) return;
        // Enemy contact damage shares the player damage cooldown with killbricks.
        float worstDamage = 0f;
        foreach (var npc in scene.Objects)
        {
            if (!npc.IsNpc || npc.NpcType != NpcKind.Enemy) continue;
            Vector3 d = ToNum(npc.Position) - ToNum(Player.Position);
            float reach = (Math.Max(npc.Size.X, npc.Size.Z) + Math.Max(Player.Size.X, Player.Size.Z)) * 0.25f;
            if (d.X * d.X + d.Z * d.Z <= reach * reach && MathF.Abs(d.Y) <= (npc.Size.Y + Player.Size.Y) * 0.5f)
                worstDamage = Math.Max(worstDamage, npc.NpcDamage);
        }
        if (worstDamage > 0 && SimTime - _lastDamageSimTime >= DamageCooldown)
        {
            _lastDamageSimTime = SimTime;
            Damage(worstDamage);
        }
        // Drop bubbles whose NPC left the scene (deleted mid-play), expired ones,
        // and chat memory for deleted NPCs. Single live-set pass, no allocs;
        // skipped entirely when nothing is chatting.
        if (ActiveBubbles.Count > 0 || _chatStates.Count > 0)
        {
            _liveNpcs.Clear();
            foreach (var o in scene.Objects)
                if (o.IsNpc) _liveNpcs.Add(o);
            for (int i = ActiveBubbles.Count - 1; i >= 0; i--)
            {
                var b = ActiveBubbles[i];
                if (!_liveNpcs.Contains(b.Npc) || SimTime > b.ExpiresAt)
                    ActiveBubbles.RemoveAt(i);
            }
            if (_chatStates.Count > 0)
            {
                _pruneScratch.Clear();
                foreach (var key in _chatStates.Keys)
                    if (!_liveNpcs.Contains(key)) _pruneScratch.Add(key);
                foreach (var key in _pruneScratch) _chatStates.Remove(key);
            }
        }
        Vector3 playerPos = ToNum(Player.Position);
        foreach (var npc in scene.Objects)
        {
            if (!npc.IsNpc) continue;
            float range = Math.Clamp(npc.NpcChatRange <= 0 ? 10f : npc.NpcChatRange, 4f, 40f);
            Vector3 delta = ToNum(npc.Position) - playerPos;
            bool inRange = delta.LengthSquared() <= range * range;
            if (!_chatStates.TryGetValue(npc, out var state))
            {
                state = new NpcChatState();
                _chatStates[npc] = state;
            }
            if (!inRange)
            {
                state.WasInRange = false;
                // Walked away mid-bubble: the words go with you, no lingering chat.
                for (int i = ActiveBubbles.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(ActiveBubbles[i].Npc, npc))
                        ActiveBubbles.RemoveAt(i);
                continue;
            }
            var lines = ChatLines(npc, state);
            if (lines.Length == 0) { state.WasInRange = true; continue; }
            bool justArrived = !state.WasInRange;
            state.WasInRange = true;
            if (!justArrived && SimTime < state.NextSpeakAt) continue;
            int pick = _random.Next(lines.Length);
            if (lines.Length > 1)
                while (pick == state.LastLine) pick = _random.Next(lines.Length);
            state.LastLine = pick;
            float interval = Math.Clamp(npc.NpcChatInterval <= 0 ? 5f : npc.NpcChatInterval, 2f, 20f);
            state.NextSpeakAt = SimTime + interval * (0.85 + _random.NextDouble() * 0.3);
            string line = lines[pick];
            NpcBubble? existing = null;
            foreach (var b in ActiveBubbles)
            {
                if (ReferenceEquals(b.Npc, npc)) { existing = b; break; }
            }
            if (existing != null) { existing.Line = line; existing.ExpiresAt = SimTime + NpcBubbleDuration; }
            else ActiveBubbles.Add(new NpcBubble { Npc = npc, Line = line, ExpiresAt = SimTime + NpcBubbleDuration });
            _log?.Invoke($"{npc.Name}: {line}");
        }
    }

    /// <summary>Kill pitch/roll on an avatar body, preserving yaw.</summary>
    private static void SnapUpright(BodyReference br, SceneObject o)
    {
        var fwd = Vector3.Transform(new Vector3(0, 0, 1), br.Pose.Orientation);
        float yaw = MathF.Atan2(fwd.X, fwd.Z);
        var upright = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        br.Pose.Orientation = upright;
        o.Orientation = ToOtk(upright);
    }

    /// <summary>Freeze the vanished avatar at spawn: no input, no drift, gravity settles it.</summary>
    private void HoldDead()
    {
        if (_sim == null || Player == null || !_bodies.TryGetValue(Player, out var e) || e.IsStatic) return;
        BodyReference br = _sim.Bodies[e.Body];
        var v = br.Velocity.Linear;
        v.X = 0;
        v.Z = 0;
        br.Velocity.Linear = v;
        br.Velocity.Angular = Vector3.Zero;
    }

    /// <summary>Void fall: park at spawn at 0 health, vanish, and revive after
    /// <see cref="VoidRespawnDelay"/> seconds.</summary>
    private void StartVoidDeath()
    {
        if (Player == null) return;
        RespawnPlayer();
        Health = 0;
        _lastDamageSimTime = SimTime;
        _wasTouchingHazard = false;
        DiedThisStep = true; // red flash via the usual path
        VoidDiedThisStep = true;
        IsVoidDead = true;
        Player.Hidden = true;
        if (FaceObject != null) FaceObject.Hidden = true;
        VoidRespawnAt = SimTime + VoidRespawnDelay;
    }

    /// <summary>Revive: reappear at spawn at full health, controls live again.</summary>
    private void ReviveFromVoid()
    {
        IsVoidDead = false;
        ResetHealth(); // also clears DiedThisStep
        VoidRespawnedThisStep = true;
        if (Player != null)
        {
            Player.Hidden = false;
            if (!_bodies.TryGetValue(Player, out var e) || e.IsStatic || _sim == null) return;
            BodyReference br = _sim.Bodies[e.Body];
            SnapUpright(br, Player);
            br.Velocity.Linear = Vector3.Zero;
            br.Velocity.Angular = Vector3.Zero;
            Player.Orientation = ToOtk(br.Pose.Orientation);
            _sim.Awakener.AwakenBody(e.Body);
        }
        if (FaceObject != null) FaceObject.Hidden = false;
    }

    private void RespawnPlayer()
    {
        if (_sim == null || Player == null || !_bodies.TryGetValue(Player, out var e) || e.IsStatic) return;
        BodyReference br = _sim.Bodies[e.Body];
        br.Pose.Position = ToNum(SpawnPoint);
        br.Pose.Orientation = Quaternion.Identity;
        br.Velocity.Linear = Vector3.Zero;
        br.Velocity.Angular = Vector3.Zero;
        Player.Position = SpawnPoint;
        Player.Orientation = ToOtk(Quaternion.Identity);
        _sim.Awakener.AwakenBody(e.Body);
    }

    /// <summary>Full heal (Play start, instant void respawns, revives, deaths).</summary>
    public void ResetHealth()
    {
        Health = Math.Max(MaxHealth, 1f);
        _lastDamageSimTime = -100;
        _lastRegenSimTime = SimTime;
        _wasTouchingHazard = false;
        // Fresh touch state too: without this, a teleport late in one session
        // silences all pads until that SimTime passes in the NEXT session.
        _lastTeleportSimTime = -100;
        // Same for bounce pads: a stale timestamp freezes them on session start.
        _lastBounceSimTime = -100;
        DiedThisStep = false;
    }

    /// <summary>Script heal (clamped, no respawn side effects).</summary>
    public void Heal(float amount)
    {
        if (_sim == null || Player == null || amount <= 0) return;
        Health = Math.Min(MaxHealth, Health + amount);
    }

    /// <summary>Script teleport: move the avatar body + pose, zero velocity.</summary>
    public void TeleportPlayer(OpenTK.Mathematics.Vector3 dest)
    {
        if (_sim == null || Player == null) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        pbr.Pose.Position = ToNum(dest);
        pbr.Velocity.Linear = Vector3.Zero;
        pbr.Velocity.Angular = Vector3.Zero;
        Player.Position = dest;
        _sim.Awakener.AwakenBody(pe.Body);
    }

    /// <summary>Apply damage; a lethal hit respawns at full health (Roblox "Oof").
    /// Ignored while vanished from a void fall (the revive owns that heal).</summary>
    public void Damage(float amount)
    {
        if (_sim == null || Player == null || IsVoidDead || amount <= 0) return;
        Health -= amount;
        if (Health <= 0)
        {
            RespawnPlayer();
            ResetHealth();
            DiedThisStep = true;
        }
    }

    private bool _wasTouchingHazard;
    private double _lastRegenSimTime;

    /// <summary>Cheap world-sphere reject before the oriented-box overlap math:
    /// skips parts the avatar cannot possibly touch. Conservative (half-diagonal
    /// plus the fattest avatar extent always contains the box test), so results
    /// are identical — distant parts just skip the inverse + transform.</summary>
    private static bool MightTouch(Vector3 pPos, Vector3 oPos, Vector3 oSize, float reach)
    {
        float dx = pPos.X - oPos.X;
        float dy = pPos.Y - oPos.Y;
        float dz = pPos.Z - oPos.Z;
        float r = oSize.Length() * 0.5f + reach;
        return dx * dx + dy * dy + dz * dz <= r * r;
    }

    /// <summary>Touch test: Killbrick parts (Damage > 0) overlapping the avatar
    /// hurt the very first frame of contact, then re-tick on a cooldown while
    /// contact lasts. Uses brick-local box vs the upright avatar extents,
    /// so rotated bricks still hit exactly on their visible faces.</summary>
    private void CheckHazards(StudioScene scene)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        var pPos = pbr.Pose.Position;
        // Exact avatar extents: the body is a flat-sided cylinder (see BuildShape),
        // radius from X/Z, half-height from Y. Using the full part half-width here
        // made the hitbox ~0.4 studs fatter than the visible avatar on every side.
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(Player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.03f; // graze tolerance so edge touches never flicker-miss
        float reach = Math.Max(pr, ph) + skin;
        float worst = 0f;
        foreach (var o in scene.Objects)
        {
            if (o.Damage <= 0.01f || o.IsAvatarFace) continue;
            if (ReferenceEquals(o, Player)) continue;
            // Killbrick type, plus legacy Block parts carrying Damage.
            if (o.Shape != Viewport.ShapeKind.Block && o.Shape != Viewport.ShapeKind.Killbrick) continue;
            if (!MightTouch(pPos, ToNum(o.Position), ToNum(o.Size), reach)) continue;
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var inv = Quaternion.Inverse(q);
            var local = Vector3.Transform(pPos - ToNum(o.Position), inv);
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) <= half.X + pr + skin &&
                MathF.Abs(local.Y) <= half.Y + ph + skin &&
                MathF.Abs(local.Z) <= half.Z + pr + skin)
                worst = Math.Max(worst, o.Damage);
        }
        if (worst > 0.01f)
        {
            // Fresh touch always hurts instantly; staying inside re-ticks slowly.
            if (!_wasTouchingHazard || SimTime - _lastDamageSimTime >= DamageCooldown)
            {
                _lastDamageSimTime = SimTime;
                Damage(worst);
            }
            _wasTouchingHazard = !DiedThisStep; // death respawns away: next touch is fresh
        }
        else _wasTouchingHazard = false;
    }

    /// <summary>Regen: +3 health every 3 simulated seconds, up to max.
    /// Paused while vanished (the bar stays at 0 until the revive heals).</summary>
    private void RegenTick()
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (SimTime - _lastRegenSimTime < 3.0) return;
        _lastRegenSimTime = SimTime;
        if (Health < MaxHealth) Health = Math.Min(MaxHealth, Health + 3f);
    }

    /// <summary>Teleport pads: touching a linked pad moves the avatar above its
    /// partner (same TeleportLink, first other match). Cooldown stops the
    /// arrival from instantly bouncing back.</summary>
    private void CheckTeleportPads(StudioScene scene)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        if (SimTime - _lastTeleportSimTime < TeleportCooldown) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        var pPos = pbr.Pose.Position;
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(Player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.03f;
        float reach = Math.Max(pr, ph) + skin;
        foreach (var o in scene.Objects)
        {
            if (string.IsNullOrWhiteSpace(o.TeleportLink) || o.IsAvatarFace) continue;
            if (ReferenceEquals(o, Player)) continue;
            if (!MightTouch(pPos, ToNum(o.Position), ToNum(o.Size), reach)) continue;
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var local = Vector3.Transform(pPos - ToNum(o.Position), Quaternion.Inverse(q));
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) > half.X + pr + skin ||
                MathF.Abs(local.Y) > half.Y + ph + skin ||
                MathF.Abs(local.Z) > half.Z + pr + skin)
                continue; // not touching this pad
            string link = o.TeleportLink.Trim();
            SceneObject? partner = null;
            foreach (var c in scene.Objects)
            {
                if (ReferenceEquals(c, o) || c.IsAvatarFace) continue;
                if (link.Equals(c.TeleportLink?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    partner = c;
                    break;
                }
            }
            if (partner == null) continue; // lone pad: nothing to jump to
            var dest = ToNum(partner.Position) + new Vector3(
                0, partner.Size.Y * 0.5f + Player.Size.Y * 0.5f + 0.5f, 0);
            pbr.Pose.Position = dest;
            pbr.Velocity.Linear = Vector3.Zero;
            pbr.Velocity.Angular = Vector3.Zero;
            Player.Position = ToOtk(dest);
            _sim.Awakener.AwakenBody(pe.Body);
            _lastTeleportSimTime = SimTime;
            TeleportedThisStep = true;
            LastTeleportName = partner.Name;
            return; // one hop per step
        }
    }

    /// <summary>Checkpoints: touching one moves the respawn point above it
    /// (void falls and deaths revive there). Idempotent: re-touching the
    /// current checkpoint is a no-op, so no cooldown is needed.</summary>
    private void CheckCheckpoints(StudioScene scene)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        var pPos = pbr.Pose.Position;
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(Player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.03f;
        float reach = Math.Max(pr, ph) + skin;
        foreach (var o in scene.Objects)
        {
            if (!o.IsCheckpoint || o.IsAvatarFace) continue;
            if (ReferenceEquals(o, Player)) continue;
            if (!MightTouch(pPos, ToNum(o.Position), ToNum(o.Size), reach)) continue;
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var local = Vector3.Transform(pPos - ToNum(o.Position), Quaternion.Inverse(q));
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) > half.X + pr + skin ||
                MathF.Abs(local.Y) > half.Y + ph + skin ||
                MathF.Abs(local.Z) > half.Z + pr + skin)
                continue; // not touching this checkpoint
            var at = ToNum(o.Position) + new Vector3(
                0, ToNum(o.Size).Y * 0.5f + ToNum(Player.Size).Y * 0.5f + 0.3f, 0);
            if ((ToNum(SpawnPoint) - at).LengthSquared() < 1e-6f) return; // already set
            SpawnPoint = ToOtk(at);
            CheckpointThisStep = true;
            LastCheckpointName = o.Name;
            return; // one claim per step
        }
    }

    /// <summary>Bounce pads: touching one launches the avatar up, keeping
    /// horizontal momentum (never steals upward speed). Short cooldown so
    /// continuous contact re-launches instead of sticking.</summary>
    private void CheckBouncePads(StudioScene scene)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        if (SimTime - _lastBounceSimTime < BounceCooldown) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        var pPos = pbr.Pose.Position;
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(Player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.12f; // resting contact jitters: trigger just above the surface
        float reach = Math.Max(pr, ph) + skin;
        foreach (var o in scene.Objects)
        {
            if (!o.IsBouncePad || o.IsAvatarFace) continue;
            if (ReferenceEquals(o, Player)) continue;
            if (!MightTouch(pPos, ToNum(o.Position), ToNum(o.Size), reach)) continue;
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var local = Vector3.Transform(pPos - ToNum(o.Position), Quaternion.Inverse(q));
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) > half.X + pr + skin ||
                MathF.Abs(local.Y) > half.Y + ph + skin ||
                MathF.Abs(local.Z) > half.Z + pr + skin)
                continue; // not touching this pad
            var v = pbr.Velocity.Linear;
            v.Y = Math.Max(v.Y, Math.Max(o.BouncePower, 0f));
            pbr.Velocity.Linear = v;
            _sim.Awakener.AwakenBody(pe.Body);
            _lastBounceSimTime = SimTime;
            BouncedThisStep = true;
            return; // one launch per step
        }
    }

    /// <summary>Touch sounds: avatar overlapping a Play-on-touch part restarts
    /// its sound from the top (1s cooldown per part).</summary>
    private void CheckSoundTouch(StudioScene scene)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        var pPos = pbr.Pose.Position;
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(Player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.03f;
        const double cooldown = 1.0;
        float reach = Math.Max(pr, ph) + skin;
        foreach (var o in scene.Objects)
        {
            if (!o.SoundPlayOnTouch || string.IsNullOrWhiteSpace(o.SoundPath) || o.IsAvatarFace) continue;
            if (ReferenceEquals(o, Player)) continue;
            if (SimTime - o.SoundTouchCooldown < cooldown) continue;
            if (!MightTouch(pPos, ToNum(o.Position), ToNum(o.Size), reach)) continue;
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var local = Vector3.Transform(pPos - ToNum(o.Position), Quaternion.Inverse(q));
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) > half.X + pr + skin ||
                MathF.Abs(local.Y) > half.Y + ph + skin ||
                MathF.Abs(local.Z) > half.Z + pr + skin)
                continue; // not touching this part
            o.SoundTouchCooldown = SimTime;
            Audio.AudioEngine.TouchPartSound(o, ToOtk(pPos));
        }
    }

    /// <summary>Magnets: every dynamic non-NPC body (avatar included) accelerates
    /// toward each magnet center inside its range. Uncapped; power is modest.</summary>
    private void DriveMagnets(float h)
    {
        if (_sim == null || _magnets.Count == 0) return;
        foreach (var m in _magnets)
        {
            float range = Math.Max(m.MagnetRange, 0.5f);
            float power = Math.Max(m.MagnetPower, 0f);
            if (power <= 0f) continue;
            float range2 = range * range;
            var mp = ToNum(m.Position);
            foreach (var (o, e) in _bodies)
            {
                if (e.IsStatic || o.IsNpc) continue;
                BodyReference br = _sim.Bodies[e.Body];
                var d = mp - br.Pose.Position;
                float dist2 = d.LengthSquared();
                if (dist2 < 1e-6f || dist2 > range2) continue;
                float dist = MathF.Sqrt(dist2); // one sqrt, only on the pulling path
                var v = br.Velocity.Linear;
                v += d / dist * power * h;
                br.Velocity.Linear = v;
                _sim.Awakener.AwakenBody(e.Body);
            }
        }
    }

    /// <summary>Conveyors: bodies resting on top snap their horizontal velocity
    /// to the belt (part-local direction, Y flattened). Avatar rides too.</summary>
    private void DriveConveyors()
    {
        if (_sim == null || _conveyors.Count == 0) return;
        foreach (var c in _conveyors)
        {
            var q = ToNum(c.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var inv = Quaternion.Inverse(q);
            var dir = Vector3.Transform(ToNum(c.ConveyorDirection), q);
            dir.Y = 0f;
            if (dir.LengthSquared() < 1e-6f) continue;
            dir = Vector3.Normalize(dir);
            var cp = ToNum(c.Position);
            var chalf = ToNum(c.Size) * 0.5f;
            var want = dir * Math.Max(c.ConveyorSpeed, 0f);
            foreach (var (o, e) in _bodies)
            {
                if (e.IsStatic || o.IsNpc) continue;
                BodyReference br = _sim.Bodies[e.Body];
                var local = Vector3.Transform(br.Pose.Position - cp, inv);
                if (MathF.Abs(local.X) > chalf.X + 0.4f || MathF.Abs(local.Z) > chalf.Z + 0.4f) continue;
                float bottom = local.Y - ToNum(o.Size).Y * 0.5f;
                if (bottom < chalf.Y - 0.4f || bottom > chalf.Y + 0.6f) continue;
                var v = br.Velocity.Linear;
                if (ReferenceEquals(o, Player))
                {
                    // Avatar: walk velocity rides ON TOP of the belt (WASD keeps working).
                    float yaw = PlayerCamYaw * MathF.PI / 180f;
                    var wish = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * PlayerMoveZ +
                               new Vector3(-MathF.Sin(yaw), 0, MathF.Cos(yaw)) * PlayerMoveX;
                    if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);
                    float walk = WalkSpeed * (WaterAt(br.Pose.Position) != null ? WaterWalkFactor : 1f);
                    v.X = wish.X * walk + want.X;
                    v.Z = wish.Z * walk + want.Z;
                }
                else
                {
                    v.X = want.X;
                    v.Z = want.Z;
                }
                br.Velocity.Linear = v;
                _sim.Awakener.AwakenBody(e.Body);
            }
        }
    }

    /// <summary>Avatar standing on a platform top (within a vertical band)?</summary>
    private bool StandingOn(Vector3 avatarPos, Viewport.SceneObject platform)
    {
        var q = ToNum(platform.Orientation);
        if (q.LengthSquared() < 1e-8f) return false;
        var local = Vector3.Transform(avatarPos - ToNum(platform.Position), Quaternion.Inverse(q));
        var half = ToNum(platform.Size) * 0.5f;
        if (MathF.Abs(local.X) > half.X + 0.4f || MathF.Abs(local.Z) > half.Z + 0.4f) return false;
        float bottom = local.Y - ToNum(Player!.Size).Y * 0.5f;
        return bottom >= half.Y - 0.3f && bottom <= half.Y + 0.3f;
    }

    /// <summary>Scratch segment lengths for mover tracks (MaxWaypoints-sized:
    /// no per-frame array alloc). Guarded; authoring caps the count anyway.</summary>
    private float[] _moverLens = new float[32];

    /// <summary>Kinematic platforms, once per Step: movers slide along part-local
    /// X on a sine track (anchored statics teleported), spinners turn around Y.
    /// A standing avatar is carried along (position only, no momentum steal).</summary>
    private void DrivePlatforms(StudioScene scene)
    {
        if (_sim == null) return;
        bool moved = false;
        foreach (var o in _movers)
        {
            if (!_bodies.TryGetValue(o, out var e) || !e.IsStatic) continue; // anchored only
            Vector3 target;
            if (o.Waypoints.Count >= 2 && o.MoveSpeed > 0f)
            {
                // Loop track 1-2-…-N-1 at constant speed (segment-length timing).
                // Scratch buffer (no per-frame alloc); waypoint loops are tiny.
                float total = 0f;
                int n = Math.Min(o.Waypoints.Count, _moverLens.Length);
                for (int i = 0; i < n; i++)
                {
                    var a = ToNum(o.Waypoints[i]);
                    var b = ToNum(o.Waypoints[(i + 1) % o.Waypoints.Count]);
                    _moverLens[i] = (b - a).Length();
                    total += _moverLens[i];
                }
                if (total < 1e-6f) continue;
                float d = (float)(SimTime * o.MoveSpeed) % total;
                int seg = 0;
                while (seg < n - 1 && d > _moverLens[seg]) { d -= _moverLens[seg]; seg++; }
                var sa = ToNum(o.Waypoints[seg]);
                var sb = ToNum(o.Waypoints[(seg + 1) % o.Waypoints.Count]);
                float f = _moverLens[seg] < 1e-6f ? 0f : d / _moverLens[seg];
                target = sa + (sb - sa) * f;
            }
            else if (o.Waypoints.Count == 1)
            {
                target = ToNum(o.Waypoints[0]); // single stop: hold it
            }
            else
            {
                // No waypoints: classic ping-pong along part-local X.
                if (!_snapshots.TryGetValue(o, out var snap)) continue;
                var q = ToNum(o.Orientation);
                if (q.LengthSquared() < 1e-8f) continue;
                var dir = Vector3.Transform(Vector3.UnitX, q);
                float dist = Math.Max(o.MoveDistance, 0f);
                Vector3 basePos = ToNum(snap.Position);
                target = dist <= 0f || o.MoveSpeed <= 0f ? basePos
                    : basePos + dir * (MathF.Sin((float)SimTime * MathF.PI * o.MoveSpeed / dist) * dist * 0.5f);
            }
            Vector3 delta = target - ToNum(o.Position);
            if (delta.LengthSquared() < 1e-10f) continue;
            if (Player != null && _bodies.TryGetValue(Player, out var pe) && !pe.IsStatic &&
                StandingOn(ToNum(Player.Position), o))
            {
                BodyReference pbr = _sim.Bodies[pe.Body];
                pbr.Pose.Position += delta;
                Player.Position = ToOtk(pbr.Pose.Position);
                _sim.Awakener.AwakenBody(pe.Body);
            }
            o.Position = ToOtk(target);
            Teleport(o);
            moved = true;
        }
        foreach (var o in _spinners)
        {
            if (!_bodies.TryGetValue(o, out var e) || !e.IsStatic) continue; // anchored only
            if (!_snapshots.TryGetValue(o, out var snap)) continue;
            float prev = o.Rotation.Y;
            float next = snap.Rotation.Y + o.SpinSpeed * (float)SimTime;
            float dRad = (next - prev) * MathF.PI / 180f;
            if (MathF.Abs(dRad) < 1e-6f) continue;
            var center = ToNum(o.Position);
            if (Player != null && _bodies.TryGetValue(Player, out var pe) && !pe.IsStatic &&
                StandingOn(ToNum(Player.Position), o))
            {
                BodyReference pbr = _sim.Bodies[pe.Body];
                var off = pbr.Pose.Position - center;
                float c = MathF.Cos(dRad), s = MathF.Sin(dRad);
                pbr.Pose.Position = center + new Vector3(off.X * c + off.Z * s, off.Y, -off.X * s + off.Z * c);
                Player.Position = ToOtk(pbr.Pose.Position);
                _sim.Awakener.AwakenBody(pe.Body);
            }
            var rot = o.Rotation;
            o.Rotation = new OTK.Vector3(rot.X, next, rot.Z);
            o.ComposeOrientation();
            Teleport(o);
            moved = true;
        }
        if (moved) scene.InvalidateShadows();
    }

    /// <summary>Breakables: a STOMP (avatar falling onto the top) counts a hit.
    /// First stomp thuds, final stomp shatters (MainWindow deletes + cracks).</summary>
    private void CheckBreakable(StudioScene scene)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var pe) || pe.IsStatic) return;
        BodyReference pbr = _sim.Bodies[pe.Body];
        var pPos = pbr.Pose.Position;
        float vy = pbr.Velocity.Linear.Y;
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(Player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.03f;
        const float stompSpeed = -7f; // must be falling onto it, not brushing past
        float reach = Math.Max(pr, ph) + skin;
        foreach (var o in scene.Objects)
        {
            if (!o.IsBreakable || o.IsAvatarFace) continue;
            if (ReferenceEquals(o, Player)) continue;
            if (SimTime - o.BreakCooldown < 0.5) continue;
            if (!MightTouch(pPos, ToNum(o.Position), ToNum(o.Size), reach)) continue;
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var local = Vector3.Transform(pPos - ToNum(o.Position), Quaternion.Inverse(q));
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) > half.X + pr + skin ||
                MathF.Abs(local.Z) > half.Z + pr + skin)
                continue; // not over this part
            float bottom = local.Y - ph;
            if (bottom < half.Y - 0.6f || bottom > half.Y + 1.2f) continue; // landing zone only
            if (vy > stompSpeed) continue; // gentle touch: no count
            o.BreakCount++;
            o.BreakCooldown = SimTime;
            if (o.BreakCount >= Math.Max(o.BreakHits, 1))
            {
                if (BrokeObject == null) BrokeObject = o;
            }
            else Audio.AudioEngine.PlayEffect("stomp", 0.9f, o.Position);
        }
    }

    // ----- player driving -----

    private void DrivePlayer(StudioScene scene)
    {
        if (_sim == null || Player == null || !_bodies.TryGetValue(Player, out var e) || e.IsStatic) return;
        BodyReference br = _sim.Bodies[e.Body];
        // Camera-relative wish direction on the ground plane.
        float yaw = PlayerCamYaw * MathF.PI / 180f;
        var fwd = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
        var rgt = new Vector3(-MathF.Sin(yaw), 0, MathF.Cos(yaw));
        var wish = fwd * PlayerMoveZ + rgt * PlayerMoveX;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);
        if (wish.LengthSquared() > 0.0001f)
        {
            // Face the walk direction (X/Z stay locked, yaw eases; face panel follows).
            var fwd0 = Vector3.Transform(new Vector3(0, 0, 1), br.Pose.Orientation);
            float cur = MathF.Atan2(fwd0.X, fwd0.Z);
            float d = MathF.Atan2(wish.X, wish.Z) - cur;
            while (d > MathF.PI) d -= 2f * MathF.PI;
            while (d < -MathF.PI) d += 2f * MathF.PI;
            float t = 1f - MathF.Exp(-TurnResponsiveness * FixedStep);
            var faceYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, cur + d * t);
            br.Pose.Orientation = faceYaw;
            Player.Orientation = ToOtk(faceYaw);
        }
        else SnapUpright(br, Player); // idle: hold facing, kill any pitch/roll
        var vel = br.Velocity.Linear;
        bool swimming = WaterAt(br.Pose.Position) != null;
        float walk = WalkSpeed * (swimming ? WaterWalkFactor : 1f);
        vel.X = wish.X * walk;
        vel.Z = wish.Z * walk;
        bool grounded = IsGrounded(Player);
        if (grounded) _lastGroundedSimTime = SimTime;
        if (JumpHeld && swimming) vel.Y = SwimSpeed; // Space swims up, no ground needed
        else if (JumpHeld && (grounded || SimTime - _lastGroundedSimTime <= CoyoteTime)) vel.Y = JumpSpeed;
        br.Velocity.Linear = vel;
        br.Velocity.Angular = Vector3.Zero;
        if (wish.LengthSquared() > 0.0001f || JumpHeld) _sim.Awakener.AwakenBody(e.Body);
    }

    /// <summary>Water this point sits inside (part-local box test), or null.</summary>
    private Viewport.SceneObject? WaterAt(Vector3 p)
    {
        foreach (var o in _waters)
        {
            var q = ToNum(o.Orientation);
            if (q.LengthSquared() < 1e-8f) continue;
            var local = Vector3.Transform(p - ToNum(o.Position), Quaternion.Inverse(q));
            var half = ToNum(o.Size) * 0.5f;
            if (MathF.Abs(local.X) <= half.X &&
                MathF.Abs(local.Y) <= half.Y &&
                MathF.Abs(local.Z) <= half.Z)
                return o;
        }
        return null;
    }

    /// <summary>Per-slice buoyancy: vertical velocity is SET (not eased — an ease
    /// this slow can't hold against gravity), horizontal motion gets heavy drag.</summary>
    private void ApplyWater(float h)
    {
        if (_sim == null || _waters.Count == 0) return;
        if (Player != null && _bodies.TryGetValue(Player, out var pe) && !pe.IsStatic)
        {
            BodyReference pbr = _sim.Bodies[pe.Body];
            if (WaterAt(pbr.Pose.Position) != null)
            {
                var v = pbr.Velocity.Linear;
                v.Y = JumpHeld ? SwimSpeed : SinkSpeed;
                pbr.Velocity.Linear = v;
                _sim.Awakener.AwakenBody(pe.Body);
            }
        }
        foreach (var (o, e) in _bodies)
        {
            if (e.IsStatic || o.IsNpc || ReferenceEquals(o, Player)) continue;
            BodyReference br = _sim.Bodies[e.Body];
            if (WaterAt(br.Pose.Position) == null) continue;
            var v = br.Velocity.Linear;
            v.Y = -1f; // slow sink; entry momentum stops dead like hitting water
            float drag = 1f / (1f + 2f * h);
            v.X *= drag;
            v.Z *= drag;
            br.Velocity.Linear = v;
            var w = br.Velocity.Angular;
            br.Velocity.Angular = w / (1f + 2f * h);
        }
    }

    /// <summary>Auto step-up: a grounded, walking avatar glides up slabs and
    /// stair steps instead of face-planting (Roblox-style). Lifts only (never
    /// snaps down, so walking off edges still falls), needs headroom, and
    /// ignores tall walls. NPCs keep their old climb-by-jump.</summary>
    private void TryStepUp(float dt)
    {
        if (_sim == null || Player == null || IsVoidDead) return;
        if (!_bodies.TryGetValue(Player, out var e) || e.IsStatic) return;
        // Wish-driven (not velocity): pushing into the step is what climbs,
        // even while the brake holds us at its face.
        float yaw = PlayerCamYaw * MathF.PI / 180f;
        var wish = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * PlayerMoveZ +
                   new Vector3(-MathF.Sin(yaw), 0, MathF.Cos(yaw)) * PlayerMoveX;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);
        float speed = wish.Length() * WalkSpeed;
        if (speed < 1f || !IsGrounded(Player)) return;
        const float stepH = 0.75f;
        const float skin = 0.05f;
        BodyReference br = _sim.Bodies[e.Body];
        var pos = br.Pose.Position;
        float pr = Math.Max(Math.Max(Player.Size.X, Player.Size.Z) * 0.25f, 0.05f);
        float hh = Math.Max(Player.Size.Y * 0.5f, 0.05f);
        Vector3 dir = Vector3.Normalize(new Vector3(wish.X, 0f, wish.Z));
        float moveDist = speed * dt;
        var probe = new GroundHit { Ignore = e.Body, Ghosts = _ghosts, NearestT = float.MaxValue };
        var shinOrigin = pos + new Vector3(0, -hh + 0.15f, 0);
        _sim.RayCast(in shinOrigin, in dir, pr + moveDist + skin, ref probe, 0);
        if (!probe.Hit) return; // nothing shin-high ahead: just walk
        // Headroom for the step?
        var headProbe = new GroundHit { Ignore = e.Body, Ghosts = _ghosts, NearestT = float.MaxValue };
        var headOrigin = pos + new Vector3(0, -hh + stepH, 0);
        _sim.RayCast(in headOrigin, in dir, pr + moveDist + skin, ref headProbe, 0);
        if (headProbe.Hit) return;
        // Ledge top ahead?
        var downProbe = new GroundHit { Ignore = e.Body, Ghosts = _ghosts, NearestT = float.MaxValue };
        var downOrigin = pos + dir * (pr + moveDist + skin) + new Vector3(0, -hh + stepH + 0.05f, 0);
        var down = new Vector3(0, -1, 0);
        _sim.RayCast(in downOrigin, in down, stepH + 0.5f, ref downProbe, 0);
        if (!downProbe.Hit) return;
        float groundY = downOrigin.Y - downProbe.NearestT;
        float newY = groundY + hh;
        if (newY <= pos.Y + 0.02f) return; // lift only: falling off edges still falls
        br.Pose.Position = new Vector3(pos.X, newY, pos.Z);
        _sim.Awakener.AwakenBody(e.Body);
    }
    /// <summary>Avatar locomotion snapshot for the blocky rig (beta):
    /// body-frame velocity, support, and medium.</summary>
    public void PlayerMotion(out float vx, out float vy, out float vz, out bool grounded, out bool swimming)
    {
        vx = 0; vy = 0; vz = 0; grounded = false; swimming = false;
        if (_sim == null || Player == null || !_bodies.TryGetValue(Player, out var e) || e.IsStatic) return;
        var v = _sim.Bodies[e.Body].Velocity.Linear;
        vx = v.X; vy = v.Y; vz = v.Z;
        grounded = IsGrounded(Player);
        swimming = WaterAt(ToNum(Player.Position)) != null;
    }

    /// <summary>True while any dynamic body is in real motion (linear or
    /// spin). Sleeping bodies read ~zero, so settled stacks cost nothing.
    /// Used to gate shadow refreshes: motion, not category, drives it.</summary>
    public bool AnyBodyMoving(float minSpeed = 0.1f)
    {
        if (_sim == null) return false;
        float lin2 = minSpeed * minSpeed;
        float ang2 = 0.04f; // ~0.2 rad/s: slower spins don't visibly move shadows
        foreach (var (o, e) in _bodies)
        {
            if (e.IsStatic) continue;
            BodyReference br = _sim.Bodies[e.Body];
            if (br.Velocity.Linear.LengthSquared() > lin2) return true;
            if (br.Velocity.Angular.LengthSquared() > ang2) return true;
        }
        return false;
    }

    public bool IsGrounded(SceneObject o)
    {
        if (_sim == null || !_bodies.TryGetValue(o, out var e) || e.IsStatic) return false;
        var origin = ToNum(o.Position);
        var dir = new Vector3(0, -1, 0);
        float maxT = o.Size.Y * 0.5f + 0.15f;
        var handler = new GroundHit { Ignore = e.Body, Ghosts = _ghosts };
        _sim.RayCast(in origin, in dir, maxT, ref handler, 0);
        return handler.Hit;
    }

    private struct GroundHit : IRayHitHandler
    {
        public BodyHandle Ignore;
        public GhostSet? Ghosts;
        public bool Hit;
        public float NearestT;

        public bool AllowTest(CollidableReference c)
        {
            if (c.Mobility != CollidableMobility.Dynamic) return true;
            if (c.BodyHandle.Value == Ignore.Value) return false;
            return Ghosts == null || !Ghosts.Bodies.Contains(c.BodyHandle.Value); // no standing on ghosts
        }

        public bool AllowTest(CollidableReference c, int childIndex) => AllowTest(c);

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            Hit = true;
            if (t < NearestT) NearestT = t;
            maximumT = t;
        }
    }

    // ----- body management -----

    public void AddBody(SceneObject o)
    {
        if (_sim == null || _bodies.ContainsKey(o)) return;
        if (o.Shape == Viewport.ShapeKind.None) return; // lights never collide
        // Anchored ghosts need nothing (frozen is correct); unanchored ghosts get
        // a real dynamic body whose contacts are filtered (gravity, no collision).
        if (!o.CanCollide && o.Anchored) return;
        var (index, inertia, localRot, localPos) = BuildShape(o);
        var pose = BodyPoseFor(o, localRot, localPos);
        if (o.Anchored)
        {
            var sh = _sim.Statics.Add(new StaticDescription(pose, index));
            _bodies[o] = new BodyEntry { IsStatic = true, Static = sh, Shape = index, LocalRot = localRot, LocalPos = localPos };
        }
        else
        {
            var bh = _sim.Bodies.Add(BodyDescription.CreateDynamic(pose.Position, inertia, index, 0.01f));
            BodyReference br = _sim.Bodies[bh];
            br.Pose.Orientation = pose.Orientation;
            _bodies[o] = new BodyEntry { IsStatic = false, Body = bh, Shape = index, LocalRot = localRot, LocalPos = localPos };
            if (!o.CanCollide) _ghosts.Bodies.Add(bh.Value);
        }
    }

    public void RemoveBody(SceneObject o)
    {
        if (_sim == null || !_bodies.TryGetValue(o, out var e)) return;
        if (e.IsStatic) _sim.Statics.Remove(e.Static);
        else
        {
            _ghosts.Bodies.Remove(e.Body.Value);
            _sim.Bodies.Remove(e.Body);
        }
        _bodies.Remove(o);
    }

    /// <summary>Rebuild a body in place (size/shape/anchor/mass changed), keeping pose + velocity.</summary>
    public void RecreateBody(SceneObject o)
    {
        if (_sim == null) return;
        if (!_bodies.TryGetValue(o, out var e))
        {
            // No body yet (e.g. CanCollide flipped back on): create if collidable.
            if (o.CanCollide) AddBody(o);
            return;
        }
        BodyVelocity vel = default;
        bool dyn = !e.IsStatic;
        if (dyn) vel = _sim.Bodies[e.Body].Velocity;
        RemoveBody(o);
        AddBody(o);
        if (dyn && _bodies.TryGetValue(o, out var ne) && !ne.IsStatic)
        {
            BodyReference br = _sim.Bodies[ne.Body];
            br.Velocity.Linear = vel.Linear;
            br.Velocity.Angular = vel.Angular;
            _sim.Awakener.AwakenBody(ne.Body);
        }
    }

    /// <summary>Teleport a body to its object's transform (gizmo drags, property edits).</summary>
    public void Teleport(SceneObject o)
    {
        if (_sim == null || !_bodies.TryGetValue(o, out var e)) return;
        var pose = BodyPoseFor(o, e.LocalRot, e.LocalPos);
        if (e.IsStatic)
        {
            _sim.Statics.Remove(e.Static);
            var sh = _sim.Statics.Add(new StaticDescription(pose, e.Shape));
            e.Static = sh;
            _bodies[o] = e;
        }
        else
        {
            BodyReference br = _sim.Bodies[e.Body];
            br.Pose.Position = pose.Position;
            br.Pose.Orientation = pose.Orientation;
            br.Velocity.Linear = Vector3.Zero;
            br.Velocity.Angular = Vector3.Zero;
            _sim.Awakener.AwakenBody(e.Body);
        }
    }

    // ----- shapes -----
    //
    // Roblox-style CollisionFidelity = Box: parts collide as centered primitives
    // matching their size (boxes for everything, real spheres for balls and real
    // capsules for capsule parts). One exception: stairs are an exact compound of
    // step columns (union == mesh solid), so tilted/dropped stairs rest exactly on
    // their visible faces. Climbing works via the avatar step-up assist, not a ramp.
    // The avatar is special-cased to a flat-sided cylinder: a capsule foot rolls up
    // vertical faces (wall climbing). No hulls, no COM offsets.

    /// <summary>Body pose for a part: part frame composed with the shape-local frame.</summary>
    private static RigidPose BodyPoseFor(SceneObject o, Quaternion localRot, Vector3 localPos)
    {
        var q = ToNum(o.Orientation);
        return new RigidPose
        {
            Position = ToNum(o.Position) + Vector3.Transform(localPos, q),
            Orientation = Quaternion.Multiply(q, localRot),
        };
    }

    private (TypedIndex Index, BodyInertia Inertia, Quaternion LocalRot, Vector3 LocalPos) BuildShape(SceneObject o)
    {
        var sim = _sim!;
        var s = o.Size;
        // Explicit per-part mass (Properties > Mass, default 1). Collision size
        // still comes from Size; only the heaviness changes.
        float mass = Math.Max(o.Mass, 0.01f);
        if (ReferenceEquals(o, Player) || o.IsNpc)
        {
            // Characters: flat-sided cylinder, never a capsule. The capsule's round
            // foot rolls up vertical faces (wall climbing); a flat side just
            // slides. Matches the mesh bounds: r from X/Z, height from Y.
            float pr = Math.Max(Math.Max(s.X, s.Z) * 0.25f, 0.05f);
            float h = Math.Max(s.Y, 0.1f);
            var cyl = new Cylinder(pr, h);
            return (sim.Shapes.Add(cyl), cyl.ComputeInertia(mass), Quaternion.Identity, Vector3.Zero);
        }
        if (o.Shape == Viewport.ShapeKind.Capsule)
        {
            // Unit mesh: r=.25 ring, total height 1. Radius scales with X/Z, height with Y.
            float r = Math.Max(Math.Max(s.X, s.Z) * 0.25f, 0.05f);
            float len = Math.Max(s.Y - 2f * r, 0.05f);
            var capsule = new Capsule(r, len);
            return (sim.Shapes.Add(capsule), capsule.ComputeInertia(mass), Quaternion.Identity, Vector3.Zero);
        }
        if (o.Shape == Viewport.ShapeKind.Ball)
        {
            float r = (s.X + s.Y + s.Z) / 6f;
            var sphere = new Sphere(r);
            var inertia = sphere.ComputeInertia(mass);
            return (sim.Shapes.Add(sphere), inertia, Quaternion.Identity, Vector3.Zero);
        }
        if (o.Shape == Viewport.ShapeKind.Wedge)
        {
            // Real wedge: convex hull of the 6 mesh corners, scaled by Size.
            // Matches MeshFactory.Wedge exactly (slopes from the top-back edge
            // down to the bottom-front edge), so balls roll down the slope and
            // avatars stand on it instead of floating on a full box.
            // CreateShape recenters around the COM; the returned center keeps
            // the body frame glued to the part frame (see BodyPoseFor/Step).
            float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
            float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
            Span<Vector3> points = stackalloc Vector3[6]
            {
                new(-hx, -hy, -hz), new(hx, -hy, -hz),
                new(hx, -hy, hz), new(-hx, -hy, hz),
                new(-hx, hy, -hz), new(hx, hy, -hz),
            };
            try
            {
                ConvexHullHelper.CreateShape(points, _pool, out Vector3 center, out ConvexHull hull);
                var index = sim.Shapes.Add(hull);
                return (index, hull.ComputeInertia(mass), Quaternion.Identity, center);
            }
            catch
            {
                // Degenerate size: fall through to the box below.
            }
        }
        if (o.Shape == Viewport.ShapeKind.Pyramid)
        {
            // Real pyramid: convex hull of the base corners + apex, scaled by
            // Size. Matches MeshFactory.Pyramid exactly (full base, centered
            // apex), so things slide off the faces instead of floating on a box.
            float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
            float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
            Span<Vector3> points = stackalloc Vector3[5]
            {
                new(-hx, -hy, -hz), new(hx, -hy, -hz),
                new(hx, -hy, hz), new(-hx, -hy, hz),
                new(0, hy, 0),
            };
            try
            {
                ConvexHullHelper.CreateShape(points, _pool, out Vector3 center, out ConvexHull hull);
                var index = sim.Shapes.Add(hull);
                return (index, hull.ComputeInertia(mass), Quaternion.Identity, center);
            }
            catch
            {
                // Degenerate size: fall through to the box below.
            }
        }
        if (o.Shape == Viewport.ShapeKind.Stairs)
        {
            // Exact steps in the part frame (union == mesh solid, so feet rest
            // on the visible treads, no stilts below, no sink above, at any
            // rotation). One thin slice per step, stacked like the mesh:
            // slice i covers its own y-band only, running from the back to
            // this step's front. Non-overlapping, so no double-counted mass.
            // Children are not recentered, so body frame == part frame.
            const int n = 4; // must match the mesh tessellation step count
            float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
            float stepH = sy / n;
            float totalVol = 0f;
            for (int i = 0; i < n; i++)
                totalVol += sx * stepH * (sz * (n - i) / n);
            totalVol = Math.Max(totalVol, 1e-6f);
            var builder = new CompoundBuilder(_pool, sim.Shapes, n);
            for (int i = 0; i < n; i++)
            {
                float h = stepH;                 // this step's own y-band thickness
                float d = sz * (n - i) / n;      // runs from the back to this step's front
                float yC = -sy * 0.5f + (i + 0.5f) * stepH; // center of this step's band
                float zC = -(sz * i / n) * 0.5f; // midpoint of [-sz/2, front]
                var col = new Box(sx, h, d);
                var cpose = new RigidPose(new Vector3(0, yC, zC), Quaternion.Identity);
                float weight = mass * (sx * h * d) / totalVol;
                builder.Add(in col, in cpose, weight);
            }
            builder.BuildDynamicCompound(out var children, out var cinertia);
            builder.Dispose();
            return (sim.Shapes.Add(new Compound(children)), cinertia, Quaternion.Identity, Vector3.Zero);
        }
        if (o.Shape == Viewport.ShapeKind.HalfBall)
        {
            // Solid dome: convex hull of apex + base ring, scaled by Size.
            float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
            float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
            Span<Vector3> points = stackalloc Vector3[9];
            points[0] = new Vector3(0, hy, 0);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * MathF.PI * 2f;
                points[i + 1] = new Vector3(MathF.Cos(a) * hx, -hy, MathF.Sin(a) * hz);
            }
            try
            {
                ConvexHullHelper.CreateShape(points, _pool, out Vector3 center, out ConvexHull hull);
                var index = sim.Shapes.Add(hull);
                return (index, hull.ComputeInertia(mass), Quaternion.Identity, center);
            }
            catch
            {
                // Degenerate size: fall through to the box below.
            }
        }
        if (o.Shape == Viewport.ShapeKind.HexPrism)
        {
            // Convex hex column: hull of the 12 ring corners, scaled by Size.
            float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
            float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
            Span<Vector3> points = stackalloc Vector3[12];
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * MathF.PI * 2f;
                float x = MathF.Cos(a) * hx, z = MathF.Sin(a) * hz;
                points[i * 2] = new Vector3(x, hy, z);
                points[i * 2 + 1] = new Vector3(x, -hy, z);
            }
            try
            {
                ConvexHullHelper.CreateShape(points, _pool, out Vector3 center, out ConvexHull hull);
                var index = sim.Shapes.Add(hull);
                return (index, hull.ComputeInertia(mass), Quaternion.Identity, center);
            }
            catch
            {
                // Degenerate size: fall through to the box below.
            }
        }
        if (o.Shape == Viewport.ShapeKind.Arch)
        {
            // Exact 3-box compound matching MeshFactory.Arch (legs + lintel),
            // so the opening stays passable at any rotation. Children are not
            // recentered, so body frame == part frame.
            float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
            float legW = sx * 0.35f;   // x .15-.5 of the unit box
            float legX = sx * 0.325f;  // leg centers
            float beamH = sy * 0.4f;   // y .1-.5 of the unit box
            float beamY = sy * 0.3f;
            float legVol = legW * sy * sz, beamVol = sx * beamH * sz;
            float totalVol = Math.Max(2f * legVol + beamVol, 1e-6f);
            var builder = new CompoundBuilder(_pool, sim.Shapes, 3);
            var legL = new Box(legW, sy, sz);
            builder.Add(in legL, new RigidPose(new Vector3(-legX, 0, 0), Quaternion.Identity), mass * legVol / totalVol);
            var legR = new Box(legW, sy, sz);
            builder.Add(in legR, new RigidPose(new Vector3(legX, 0, 0), Quaternion.Identity), mass * legVol / totalVol);
            var beam = new Box(sx, beamH, sz);
            builder.Add(in beam, new RigidPose(new Vector3(0, beamY, 0), Quaternion.Identity), mass * beamVol / totalVol);
            builder.BuildDynamicCompound(out var children, out var cinertia);
            builder.Dispose();
            return (sim.Shapes.Add(new Compound(children)), cinertia, Quaternion.Identity, Vector3.Zero);
        }
        if (o.Shape == Viewport.ShapeKind.HollowCylinder ||
            o.Shape == Viewport.ShapeKind.Bowl ||
            o.Shape == Viewport.ShapeKind.Truss)
        {
            // Concave shells: anchored parts get the EXACT triangle mesh (static
            // Bepu Mesh collider, wound like imported files). Unanchored parts
            // can't use triangle meshes: hull of the outer silhouette instead.
            if (o.Anchored)
            {
                try
                {
                    var verts = Viewport.MeshFactory.Cached(o.Shape);
                    int triCount = verts.Length / 3;
                    if (triCount > 0)
                    {
                        _pool.Take<Triangle>(triCount, out var triBuffer);
                        for (int i = 0; i < triCount; i++)
                        {
                            // Bepu meshes want clockwise winding viewed from outside
                            // (factory renders CCW-front), so B and C swap sides.
                            triBuffer[i] = new Triangle(
                                ToNum(verts[i * 3].Position),
                                ToNum(verts[i * 3 + 2].Position),
                                ToNum(verts[i * 3 + 1].Position));
                        }
                        float mx = Math.Max(s.X, 0.05f), my = Math.Max(s.Y, 0.05f), mz = Math.Max(s.Z, 0.05f);
                        var mesh = new Mesh(triBuffer, new Vector3(mx, my, mz), _pool);
                        var index = sim.Shapes.Add(mesh);
                        return (index, default, Quaternion.Identity, Vector3.Zero);
                    }
                }
                catch
                {
                    // Degenerate: fall through to the hull below.
                }
            }
            try
            {
                float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
                float hx = sx * 0.5f, hy = sy * 0.5f, hz = sz * 0.5f;
                var pts = new System.Collections.Generic.List<Vector3>();
                if (o.Shape == Viewport.ShapeKind.Bowl)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * MathF.PI * 2f;
                        pts.Add(new Vector3(MathF.Cos(a) * hx, hy, MathF.Sin(a) * hz));
                    }
                    pts.Add(new Vector3(0, -hy, 0));
                }
                else if (o.Shape == Viewport.ShapeKind.HollowCylinder)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i / 8f * MathF.PI * 2f;
                        pts.Add(new Vector3(MathF.Cos(a) * hx, hy, MathF.Sin(a) * hz));
                        pts.Add(new Vector3(MathF.Cos(a) * hx, -hy, MathF.Sin(a) * hz));
                    }
                }
                else // Truss: outer box corners
                {
                    foreach (float x in new[] { -hx, hx })
                    foreach (float y in new[] { -hy, hy })
                    foreach (float z in new[] { -hz, hz })
                        pts.Add(new Vector3(x, y, z));
                }
                if (pts.Count >= 4)
                {
                    Span<Vector3> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pts);
                    ConvexHullHelper.CreateShape(span, _pool, out Vector3 center, out ConvexHull hull);
                    var index = sim.Shapes.Add(hull);
                    return (index, hull.ComputeInertia(mass), Quaternion.Identity, center);
                }
            }
            catch
            {
                // Degenerate: fall through to the box below.
            }
        }
        if (o.Shape == Viewport.ShapeKind.Mesh &&
            o.CollisionFidelity == Viewport.CollisionFidelityKind.Precise &&
            !string.IsNullOrWhiteSpace(o.MeshPath))
        {
            // Really precise: anchored parts get the EXACT triangle mesh (static
            // Bepu Mesh collider). Unanchored parts can't use triangle meshes,
            // so they fall through to the convex hull below.
            if (o.Anchored)
            {
                try
                {
                    var model = Viewport.ModelImport.GetModel(o.MeshPath!);
                    int triCount = model.Full.Length / 3;
                    if (triCount > 0 && triCount <= 250_000)
                    {
                        _pool.Take<Triangle>(triCount, out var triBuffer);
                        for (int i = 0; i < triCount; i++)
                        {
                            // Bepu meshes want clockwise winding viewed from outside
                            // (files render CCW-front), so B and C swap sides.
                            triBuffer[i] = new Triangle(
                                ToNum(model.Full[i * 3].Position),
                                ToNum(model.Full[i * 3 + 2].Position),
                                ToNum(model.Full[i * 3 + 1].Position));
                        }
                        float mx = Math.Max(s.X, 0.05f), my = Math.Max(s.Y, 0.05f), mz = Math.Max(s.Z, 0.05f);
                        var mesh = new Mesh(triBuffer, new Vector3(mx, my, mz), _pool);
                        var index = sim.Shapes.Add(mesh);
                        return (index, default, Quaternion.Identity, Vector3.Zero);
                    }
                }
                catch
                {
                    // Unreadable/huge file: fall through to the hull below.
                }
            }
            // Approximate hull hugging the file (never exact triangles): convex
            // hull of the decimated unit verts, scaled by Size. Flat files fall
            // back to the box below.
            try
            {
                var model = Viewport.ModelImport.GetModel(o.MeshPath!);
                var src = model.Decimated.Length > 0 ? model.Decimated : model.Full;
                if (src.Length > 0)
                {
                    float sx = Math.Max(s.X, 0.05f), sy = Math.Max(s.Y, 0.05f), sz = Math.Max(s.Z, 0.05f);
                    int stride = Math.Max(1, (src.Length / 3) / 4000 + 1);
                    var pts = new System.Collections.Generic.List<Vector3>();
                    for (int i = 0; i < src.Length; i += stride)
                    {
                        var p = src[i].Position;
                        pts.Add(new Vector3(p.X * sx, p.Y * sy, p.Z * sz));
                    }
                    if (pts.Count >= 4)
                    {
                        Span<Vector3> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pts);
                        ConvexHullHelper.CreateShape(span, _pool, out Vector3 center, out ConvexHull hull);
                        var index = sim.Shapes.Add(hull);
                        return (index, hull.ComputeInertia(mass), Quaternion.Identity, center);
                    }
                }
            }
            catch
            {
                // Degenerate/coplanar file: fall through to the box below.
            }
        }
        var box = new Box(s.X, s.Y, s.Z);
        var boxInertia = box.ComputeInertia(mass);
        return (sim.Shapes.Add(box), boxInertia, Quaternion.Identity, Vector3.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bodies.Clear();
        _ghosts.Bodies.Clear();
        _players.Bodies.Clear();
        _snapshots.Clear();
        _sim?.Dispose();
        _sim = null;
        _pool.Clear();
    }
}
