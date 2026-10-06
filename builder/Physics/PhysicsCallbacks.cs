using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace builder.Physics;

/// <summary>Contact/material policy: everything collides, uniform studio material.</summary>
public struct StudioNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    /// <summary>Contact friction, set once when the simulation is created.</summary>
    public float Friction;

    /// <summary>Shared ghost registry (reference: survives the struct copy into the sim).</summary>
    public PhysicsWorld.GhostSet? Ghosts;

    /// <summary>Dynamic avatar bodies use frictionless contacts so vertical walls cannot support them.</summary>
    public PhysicsWorld.PlayerSet? Players;

    public void Initialize(Simulation simulation) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        if (Ghosts != null && (IsGhost(a) || IsGhost(b))) return false;
        return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        if (Ghosts != null && (IsGhost(pair.A) || IsGhost(pair.B))) return false;
        return true;
    }

    private bool IsGhost(CollidableReference c) =>
        Ghosts != null && c.Mobility == CollidableMobility.Dynamic && Ghosts.Bodies.Contains(c.BodyHandle.Value);

    private bool IsPlayer(CollidableReference c) =>
        Players != null && c.Mobility == CollidableMobility.Dynamic && Players.Bodies.Contains(c.BodyHandle.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        // Friction on a vertical wall acts upward against gravity, making a capsule/cylinder
        // appear to cling to it. Player velocity is driven directly, so it needs no contact
        // friction for walking; zero friction makes wall contacts slide cleanly downward.
        pairMaterial.FrictionCoefficient = IsPlayer(pair.A) || IsPlayer(pair.B) ? 0f : Friction;
        pairMaterial.MaximumRecoveryVelocity = 2f;
        pairMaterial.SpringSettings = new SpringSettings(30, 1);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        return true;
    }

    public void Dispose() { }
}

/// <summary>Gravity + velocity integration (single-threaded friendly).</summary>
public struct StudioPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;

    public StudioPoseIntegratorCallbacks(Vector3 gravity) : this()
    {
        Gravity = gravity;
    }

    public void Initialize(Simulation simulation) { }

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    private Vector3Wide _gravityWideDt;

    public void PrepareForIntegration(float dt)
    {
        _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear += _gravityWideDt;
    }
}
