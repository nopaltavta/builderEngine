using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using OpenTK.Mathematics;
using builder.Viewport;

namespace builder.Scripting;

/// <summary>Phase-1 Lua surface: a <c>part</c> proxy (the host, or any found/
/// spawned part), a read-only <c>player</c> proxy, and a <c>game</c> table.
/// Everything crossing the boundary is validated; destructive ops route
/// through the scheduler (deferred removal, body re-creation).</summary>
[MoonSharpUserData]
public sealed class PartProxy
{
    internal SceneObject Target { get; }
    internal ScriptScheduler Owner { get; }
    internal Script Script { get; }

    internal PartProxy(SceneObject target, ScriptScheduler owner, Script script)
    {
        Target = target;
        Owner = owner;
        Script = script;
    }

    public string Name
    {
        get => Target.Name;
        set { if (!string.IsNullOrWhiteSpace(value)) Target.Name = value.Trim(); }
    }

    public DynValue Position
    {
        get => ScriptApi.VecTable(Script, Target.Position);
        set
        {
            if (ScriptApi.TryVec(value, out var v))
            {
                Target.Position = new Vector3(
                    Math.Clamp(v.X, -10000f, 10000f),
                    Math.Clamp(v.Y, -10000f, 10000f),
                    Math.Clamp(v.Z, -10000f, 10000f));
                Owner.SyncBody(Target);
            }
        }
    }

    public DynValue Color
    {
        get => ScriptApi.VecTable(Script, new Vector3(Target.Color.R, Target.Color.G, Target.Color.B));
        set
        {
            if (ScriptApi.TryColor(value, out var c))
                Target.Color = new Color4(c.X, c.Y, c.Z, 1f);
        }
    }

    public DynValue Size
    {
        get => ScriptApi.VecTable(Script, Target.Size);
        set
        {
            if (ScriptApi.TryVec(value, out var v))
            {
                Target.Size = new Vector3(
                    Math.Clamp(v.X, 0.1f, 100f),
                    Math.Clamp(v.Y, 0.1f, 100f),
                    Math.Clamp(v.Z, 0.1f, 100f));
                Owner.ResizeBody(Target);
            }
        }
    }

    public double Transparency
    {
        get => Target.Transparency;
        set => Target.Transparency = (float)Math.Clamp(value, 0.0, 1.0);
    }

    public bool Anchored
    {
        get => Target.Anchored;
        set => Owner.SetAnchored(Target, value);
    }

    public bool CanCollide
    {
        get => Target.CanCollide;
        set => Owner.SetCollidable(Target, value);
    }

    public bool Visible
    {
        get => !Target.Hidden;
        set => Target.Hidden = !value;
    }

    /// <summary>True while the avatar overlaps this part (touch query for onTick).</summary>
    public bool isTouched() => Owner.IsTouching(Target);

    public void destroy() => Owner.QueueDestroy(Target);
}

/// <summary>Read-only avatar state (writes go through game:teleportPlayer etc.).</summary>
[MoonSharpUserData]
public sealed class PlayerProxy
{
    internal SceneObject? Target { get; set; }
}

/// <summary>Shared Lua helpers: registration (once), sandbox stripping, tables.</summary>
internal static class ScriptApi
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;
        UserData.RegisterType<PartProxy>();
        UserData.RegisterType<PlayerProxy>();
    }

    /// <summary>Remove everything that can touch the machine: IO, OS, debug,
    /// packages, loaders, coroutines (engine-owned), GC control.</summary>
    public static void Strip(Script script)
    {
        string[] banned =
        {
            "io", "os", "debug", "package", "require", "module",
            "dofile", "loadfile", "load", "loadstring", "coroutine",
            "newproxy", "collectgarbage",
        };
        foreach (string g in banned)
            script.Globals[g] = DynValue.Nil;
    }

    public static DynValue VecTable(Script script, Vector3 v)
    {
        var t = new Table(script);
        t["x"] = (double)v.X;
        t["y"] = (double)v.Y;
        t["z"] = (double)v.Z;
        return DynValue.NewTable(t);
    }

    public static bool TryVec(DynValue v, out Vector3 vec)
    {
        vec = Vector3.Zero;
        try
        {
            if (v.Type != DataType.Table) return false;
            double? xd = v.Table.Get("x").CastToNumber();
            double? yd = v.Table.Get("y").CastToNumber();
            double? zd = v.Table.Get("z").CastToNumber();
            if (!xd.HasValue || !yd.HasValue || !zd.HasValue) return false;
            double x = xd.Value, y = yd.Value, z = zd.Value;
            if (!double.IsFinite(x + y + z)) return false;
            vec = new Vector3((float)x, (float)y, (float)z);
            return true;
        }
        catch { return false; }
    }

    public static bool TryColor(DynValue v, out Vector3 rgb)
    {
        rgb = Vector3.One;
        if (!TryVec(v, out var c)) return false;
        rgb = new Vector3(
            Math.Clamp(c.X, 0f, 1f),
            Math.Clamp(c.Y, 0f, 1f),
            Math.Clamp(c.Z, 0f, 1f));
        return true;
    }

    public static double Num(DynValue v, double fallback = 0)
    {
        try
        {
            double? d = v.CastToNumber();
            return d.HasValue && double.IsFinite(d.Value) ? d.Value : fallback;
        }
        catch { return fallback; }
    }

    /// <summary>Callback arg or Nil when missing (the indexer can throw).</summary>
    public static DynValue Arg(CallbackArguments args, int i)
    {
        try
        {
            // Colon calls (game:playSound) pass self first: skip it.
            int skip = 0;
            try { if (args.IsMethodCall) skip = 1; } catch { }
            int idx = skip + i;
            return idx >= 0 && idx < args.Count ? args[idx] : DynValue.Nil;
        }
        catch { return DynValue.Nil; }
    }

    public static string Str(CallbackArguments args, int i)
    {
        try { return Arg(args, i).CastToString() ?? ""; }
        catch { return ""; }
    }

    public static bool IsFunc(DynValue v)
    {
        try { return v.Type == DataType.Function; }
        catch { return false; }
    }
}
