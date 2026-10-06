using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq;
using MoonSharp.Interpreter;
using OpenTK.Mathematics;
using builder.Physics;
using builder.Viewport;

namespace builder.Scripting;

/// <summary>Phase-1 Lua runtime: compiles every non-empty part script at Play
/// start, then drives onTick / onTouch / timers each frame. Every handler runs
/// inside a budgeted coroutine (auto-yield + wall clock): infinite loops and
/// errors disable that one script with an Output message, never the game.</summary>
public sealed class ScriptScheduler
{
    private const int AutoYieldInstructions = 20000;
    private const int MaxResumesPerCall = 20;
    private const double MaxMsPerCall = 4.0;

    private sealed class ScriptState
    {
        public SceneObject Host = null!;
        public Script Script = null!;
        public DynValue OnTick = DynValue.Nil;
        public DynValue OnTouch = DynValue.Nil;
        public Table GameTable = null!;
        public readonly Dictionary<SceneObject, PartProxy> Proxies = new();
        public bool WasTouching;
        public bool Disabled;
    }

    private sealed class Timer
    {
        public double Due;
        public DynValue Fn = DynValue.Nil;
        public ScriptState Owner = null!;
    }

    private readonly StudioScene _scene;
    private readonly PhysicsWorld _physics;
    private readonly Action<string> _log;
    private readonly Action _frameKick;
    private readonly Action _explorerRefresh;
    private readonly Func<SceneObject, SceneObject> _addPart;
    private readonly Action<SceneObject> _removePart;

    private readonly List<ScriptState> _scripts = new();
    private readonly List<Timer> _timers = new();
    private readonly List<SceneObject> _destroyQueue = new();
    private readonly PlayerProxy _player = new();
    private double _time;
    private double _lastSimTime;
    private static readonly Stopwatch _watch = Stopwatch.StartNew();

    public ScriptScheduler(
        StudioScene scene, PhysicsWorld physics,
        Action<string> log, Action frameKick, Action explorerRefresh,
        Func<SceneObject, SceneObject> addPart, Action<SceneObject> removePart)
    {
        _scene = scene;
        _physics = physics;
        _log = log;
        _frameKick = frameKick;
        _explorerRefresh = explorerRefresh;
        _addPart = addPart;
        _removePart = removePart;
        ScriptApi.EnsureRegistered();
    }

    public int Running => _scripts.Count(s => !s.Disabled);

    public void Start()
    {
        Stop();
        foreach (var o in _scene.Objects)
        {
            if (string.IsNullOrWhiteSpace(o.Script)) continue;
            Compile(o);
        }
        _lastSimTime = _physics.SimTime;
        _log($"Lua: {Running} script(s) running.");
    }

    public void Stop()
    {
        _scripts.Clear();
        _timers.Clear();
        _destroyQueue.Clear();
        _waits.Clear();
    }

    private readonly List<WaitEntry> _waits = new();

    public void Tick()
    {
        if (_scripts.Count == 0) return;
        double now = _physics.SimTime;
        double dt = Math.Clamp(now - _lastSimTime, 0.0, 0.25);
        _lastSimTime = now;
        _time += dt;
        _player.Target = _physics.Player;

        // Parked waits due now (sequential code across frames).
        for (int i = _waits.Count - 1; i >= 0; i--)
        {
            var w = _waits[i];
            if (w.Owner.Disabled || !_scripts.Contains(w.Owner)) { _waits.RemoveAt(i); continue; }
            if (_time < w.Due) continue;
            _waits.RemoveAt(i);
            var result = RunBudgetedCo(w.Coroutine, Array.Empty<object>(), out string error,
                (co, sec) => _waits.Add(new WaitEntry { Coroutine = co, Owner = w.Owner, Due = _time + sec, Label = w.Label }));
            if (result == BudgetResult.Error)
            {
                w.Owner.Disabled = true;
                _log($"Lua error on {w.Owner.Host.Name} ({w.Label}): {error}");
            }
            else if (result == BudgetResult.Timeout)
            {
                w.Owner.Disabled = true;
                _log($"Lua timeout on {w.Owner.Host.Name} ({w.Label}): killed, script disabled.");
            }
        }

        foreach (var s in _scripts)
        {
            if (s.Disabled) continue;
            s.GameTable["time"] = _time;
            // Single-flight: a handler still parked in wait() is not re-entered
            // (a waiting onTick would otherwise pile up a coroutine per tick).
            if (s.OnTick.Type == DataType.Function && !HasParked(s, "onTick"))
                RunBudgeted(s, s.OnTick, new object[] { dt }, "onTick");

            // One-shot timers due now (fired even if their owner errored later).
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                var t = _timers[i];
                if (t.Owner.Disabled || t.Owner != s) continue;
                if (_time < t.Due) continue;
                _timers.RemoveAt(i);
                RunBudgeted(s, t.Fn, Array.Empty<object>(), "after");
            }

            // Touch transitions (avatar only, enter events).
            bool touching = s.OnTouch.Type == DataType.Function && OverlapsAvatar(s.Host);
            if (touching && !s.WasTouching && !HasParked(s, "onTouch"))
                RunBudgeted(s, s.OnTouch, new object[] { _player }, "onTouch");
            s.WasTouching = touching;
        }

        if (_destroyQueue.Count > 0)
        {
            var dead = new HashSet<SceneObject>();
            foreach (var o in _destroyQueue)
                if (_scene.Objects.Contains(o))
                {
                    _removePart(o);
                    dead.Add(o);
                }
            _destroyQueue.Clear();
            if (dead.Count > 0)
            {
                // Ghost scripts die with their parts (timers + parked waits too).
                _scripts.RemoveAll(s => dead.Contains(s.Host));
                _timers.RemoveAll(t => dead.Contains(t.Owner.Host));
                _waits.RemoveAll(w => dead.Contains(w.Owner.Host));
            }
            _explorerRefresh();
            _frameKick();
        }
    }

    // ---------- engine callbacks used by proxies ----------

    public bool IsTouching(SceneObject o) => OverlapsAvatar(o);

    public void QueueDestroy(SceneObject o)
    {
        if (_scene.Objects.Contains(o) && !_destroyQueue.Contains(o))
            _destroyQueue.Add(o);
    }

    public void SyncBody(SceneObject o)
    {
        try { _physics.Teleport(o); } catch { /* static or missing body */ }
        _frameKick();
    }

    public void SetAnchored(SceneObject o, bool anchored)
    {
        if (o.Anchored == anchored) return;
        try
        {
            _physics.RemoveBody(o);
            o.Anchored = anchored;
            _physics.AddBody(o);
        }
        catch { o.Anchored = anchored; }
        _frameKick();
    }

    public void SetCollidable(SceneObject o, bool collidable)
    {
        if (o.CanCollide == collidable) return;
        try
        {
            _physics.RemoveBody(o);
            o.CanCollide = collidable;
            _physics.AddBody(o);
        }
        catch { o.CanCollide = collidable; }
        _frameKick();
    }

    public void ResizeBody(SceneObject o)
    {
        try
        {
            _physics.RemoveBody(o);
            _physics.AddBody(o);
        }
        catch { }
        _frameKick();
    }

    // ---------- internals ----------

    private void Compile(SceneObject host)
    {
        var script = new Script();
        ScriptApi.Strip(script);
        script.Options.DebugPrint = s => _log($"[lua {host.Name}] {s}");
        script.Globals["print"] = DynValue.NewCallback((ctx, args) =>
        {
            var parts = new List<string>();
            for (int i = 0; i < args.Count; i++) parts.Add(ScriptApi.Arg(args, i).ToPrintString());
            _log($"[lua {host.Name}] {string.Join("\t", parts)}");
            return DynValue.Nil;
        });

        var state = new ScriptState { Host = host, Script = script };
        script.Globals["part"] = Wrap(host, state);
        script.Globals["player"] = _player;
        state.GameTable = BuildGameTable(script, state);
        script.Globals["game"] = state.GameTable;

        // Top level runs budgeted too (wrap in a function: a bare infinite
        // loop at file scope must not hang Play).
        DynValue boot;
        try
        {
            boot = script.DoString("local __boot__ = (function()\n" + (host.Script ?? "") + "\nend)\nreturn __boot__");
        }
        catch (Exception ex)
        {
            _log($"Lua error on {host.Name} (compile): {OneLine(ex)}");
            return;
        }
        if (boot.Type != DataType.Function)
        {
            _log($"Lua error on {host.Name} (compile): top level did not load.");
            return;
        }
        if (!RunBudgeted(state, boot, Array.Empty<object>(), "top level"))
            return;
        state.OnTick = script.Globals.Get("onTick");
        if (!ScriptApi.IsFunc(state.OnTick)) state.OnTick = DynValue.Nil;
        state.OnTouch = script.Globals.Get("onTouch");
        if (!ScriptApi.IsFunc(state.OnTouch)) state.OnTouch = DynValue.Nil;
        _scripts.Add(state);
    }

    private Table BuildGameTable(Script script, ScriptState state)
    {
        var game = new Table(script);
        game["time"] = 0.0;
        game["log"] = DynValue.NewCallback((ctx, args) =>
        {
            _log($"[lua {state.Host.Name}] {ScriptApi.Str(args, 0)}");
            return DynValue.Nil;
        });
        game["playSound"] = DynValue.NewCallback((ctx, args) =>
        {
            string name = ScriptApi.Str(args, 0);
            double vol = ScriptApi.Num(ScriptApi.Arg(args, 1), 0.8);
            if (string.IsNullOrWhiteSpace(name)) return DynValue.Nil;
            try
            {
                var at = state.Host.Position;
                Audio.AudioEngine.PlayEffect(name, (float)Math.Clamp(vol, 0.0, 1.0), at);
            }
            catch { }
            return DynValue.Nil;
        });
        game["damagePlayer"] = DynValue.NewCallback((ctx, args) =>
        {
            try { _physics.Damage((float)Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 0)), 0.0, 1000.0)); } catch { }
            return DynValue.Nil;
        });
        game["healPlayer"] = DynValue.NewCallback((ctx, args) =>
        {
            try { _physics.Heal((float)Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 0)), 0.0, 1000.0)); } catch { }
            return DynValue.Nil;
        });
        game["teleportPlayer"] = DynValue.NewCallback((ctx, args) =>
        {
            try
            {
                var dest = new Vector3(
                    (float)Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 0)), -10000.0, 10000.0),
                    (float)Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 1)), -10000.0, 10000.0),
                    (float)Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 2)), -10000.0, 10000.0));
                _physics.TeleportPlayer(dest);
            }
            catch { }
            return DynValue.Nil;
        });
        game["findPart"] = DynValue.NewCallback((ctx, args) =>
        {
            string name = ScriptApi.Str(args, 0);
            var found = _scene.Objects.FirstOrDefault(o =>
                o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return found != null ? DynValue.FromObject(script, Wrap(found, state)) : DynValue.Nil;
        });
        game["createPart"] = DynValue.NewCallback((ctx, args) =>
        {
            try
            {
                var o = ParsePartTable(ScriptApi.Arg(args, 0));
                if (o == null) return DynValue.Nil;
                _addPart(o);
                _explorerRefresh();
                _frameKick();
                return DynValue.FromObject(script, Wrap(o, state));
            }
            catch { return DynValue.Nil; }
        });
        game["after"] = DynValue.NewCallback((ctx, args) =>
        {
            try
            {
                double sec = Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 0)), 0.0, 60.0);
                var fn = ScriptApi.Arg(args, 1);
                if (!ScriptApi.IsFunc(fn)) return DynValue.Nil;
                _timers.Add(new Timer { Due = _time + sec, Fn = fn, Owner = state });
            }
            catch { }
            return DynValue.Nil;
        });
        game["wait"] = DynValue.NewCallback((ctx, args) =>
        {
            // Yield-park the calling coroutine; the scheduler resumes it when due.
            // A yield (unlike a throw) leaves the coroutine alive, so execution
            // continues on the next line after the wait. wait() in a loop runs
            // forever without tripping the timeout.
            double sec = Math.Clamp(ScriptApi.Num(ScriptApi.Arg(args, 0), 0.5), 0.0, 60.0);
            return DynValue.NewYieldReq(new DynValue[]
                { DynValue.NewString("!wait"), DynValue.NewNumber(sec) });
        });
        return game;
    }

    private PartProxy Wrap(SceneObject o, ScriptState state)
    {
        if (!state.Proxies.TryGetValue(o, out var p))
        {
            p = new PartProxy(o, this, state.Script);
            state.Proxies[o] = p;
        }
        return p;
    }

    private static SceneObject? ParsePartTable(DynValue v)
    {
        if (v.Type != DataType.Table) return null;
        var t = v.Table;
        string shapeName = t.Get("shape").CastToString() ?? "Block";
        if (!Enum.TryParse<ShapeKind>(shapeName, true, out var shape) || shape == ShapeKind.None)
            shape = ShapeKind.Block;
        Vector3 pos = new(0, 5, 0), size = new(1, 1, 1), col = Vector3.One;
        if (ScriptApi.TryVec(t.Get("position"), out var pv))
            pos = new Vector3(
                Math.Clamp(pv.X, -10000f, 10000f),
                Math.Clamp(pv.Y, -10000f, 10000f),
                Math.Clamp(pv.Z, -10000f, 10000f));
        if (ScriptApi.TryVec(t.Get("size"), out var sv))
            size = new Vector3(
                Math.Clamp(sv.X, 0.1f, 100f),
                Math.Clamp(sv.Y, 0.1f, 100f),
                Math.Clamp(sv.Z, 0.1f, 100f));
        if (ScriptApi.TryColor(t.Get("color"), out var cv)) col = cv;
        string name = t.Get("name").CastToString() ?? "";
        bool anchored = t.Get("anchored").IsNil() || t.Get("anchored").CastToBool();
        return new SceneObject
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Script Part" : name.Trim(),
            Shape = shape,
            Position = pos,
            Size = size,
            Color = new Color4(col.X, col.Y, col.Z, 1f),
            Material = MaterialKind.Plastic,
            Anchored = anchored,
        };
    }

    private bool OverlapsAvatar(SceneObject o)
    {
        var player = _physics.Player;
        if (player == null || ReferenceEquals(o, player)) return false;
        float pr = Math.Max(Math.Max(player.Size.X, player.Size.Z) * 0.25f, 0.05f);
        float ph = Math.Max(player.Size.Y, 0.1f) * 0.5f;
        const float skin = 0.12f;
        var pp = player.Position;
        var op = o.Position;
        var hs = o.Size * 0.5f;
        return Math.Abs(pp.X - op.X) <= hs.X + pr + skin &&
               Math.Abs(pp.Y - op.Y) <= hs.Y + ph + skin &&
               Math.Abs(pp.Z - op.Z) <= hs.Z + pr + skin;
    }

    /// <summary>True while this script has a coroutine parked in wait()
    /// under this label (single-flight for re-entrant handlers).</summary>
    private bool HasParked(ScriptState state, string label)
    {
        for (int i = 0; i < _waits.Count; i++)
            if (ReferenceEquals(_waits[i].Owner, state) && _waits[i].Label == label)
                return true;
        return false;
    }

    /// <summary>Run a handler to completion inside coroutine budgets. False =
    /// script disabled (timeout or error, already logged). Parked waits resume later.</summary>
    private bool RunBudgeted(ScriptState state, DynValue fn, object[] args, string label)
    {
        if (state.Disabled || !ScriptApi.IsFunc(fn)) return false;
        var result = RunBudgetedFn(state.Script, fn, args, out string error,
            (co, sec) => _waits.Add(new WaitEntry { Coroutine = co, Owner = state, Due = _time + sec, Label = label }));
        if (result == BudgetResult.Done || result == BudgetResult.Parked) return true;
        state.Disabled = true;
        _log(result == BudgetResult.Timeout
            ? $"Lua timeout on {state.Host.Name} ({label}): killed, script disabled."
            : $"Lua error on {state.Host.Name} ({label}): {error}");
        return false;
    }

    /// <summary>A coroutine parked by game:wait, resumed when Due.</summary>
    private sealed class WaitEntry
    {
        public Coroutine Coroutine = null!;
        public ScriptState Owner = null!;
        public double Due;
        public string Label = "";
    }

    internal enum BudgetResult { Done, Timeout, Error, Parked }

    /// <summary>Shared budgeted-call core (scheduler + headless smoke test).</summary>
    internal static BudgetResult RunBudgetedFn(Script script, DynValue fn, object[] args, out string error,
        Action<Coroutine, double>? onPark = null)
    {
        Coroutine co;
        try { co = script.CreateCoroutine(fn).Coroutine; }
        catch (Exception ex) { error = OneLine(ex); return BudgetResult.Error; }
        return RunBudgetedCo(co, args, out error, onPark);
    }

    internal static BudgetResult RunBudgetedCo(Coroutine co, object[] args, out string error,
        Action<Coroutine, double>? onPark = null)
    {
        error = "";
        co.AutoYieldCounter = AutoYieldInstructions;
        _watch.Restart();
        try
        {
            bool first = true;
            for (int i = 0; i < MaxResumesPerCall; i++)
            {
                if (co.State == CoroutineState.Dead) return BudgetResult.Done;
                if (_watch.Elapsed.TotalMilliseconds > MaxMsPerCall) break;
                DynValue ret = first ? co.Resume(args) : co.Resume(Array.Empty<object>());
                first = false;
                if (co.State == CoroutineState.Dead) return BudgetResult.Done;
                // Suspended mid-handler: either a wait-yield (park it) or an
                // auto-yield timeslice (keep looping inside the budget).
                if (IsWaitYield(ret, out double sec))
                {
                    if (onPark != null)
                    {
                        onPark(co, sec);
                        return BudgetResult.Parked;
                    }
                    error = "wait() outside a handler";
                    return BudgetResult.Error;
                }
            }
            if (co.State != CoroutineState.Dead) return BudgetResult.Timeout;
            return BudgetResult.Done;
        }
        catch (ScriptRuntimeException ex) { error = ex.DecoratedMessage; return BudgetResult.Error; }
        catch (Exception ex) { error = OneLine(ex); return BudgetResult.Error; }
    }

    /// <summary>A wait-yield is the Tuple ["!wait", seconds] returned by
    /// game:wait; any other suspension is an auto-yield timeslice, not a park.</summary>
    private static bool IsWaitYield(DynValue ret, out double seconds)
    {
        seconds = 0;
        try
        {
            if (ret.Type == DataType.Tuple && ret.Tuple.Length >= 2
                && ret.Tuple[0].Type == DataType.String && ret.Tuple[0].String == "!wait")
            {
                double? d = ret.Tuple[1].CastToNumber();
                seconds = d.HasValue && double.IsFinite(d.Value)
                    ? Math.Clamp(d.Value, 0.0, 60.0) : 0;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string OneLine(Exception? ex)
    {
        if (ex == null) return "unknown";
        string m = ex.Message ?? ex.GetType().Name;
        int nl = m.IndexOfAny(new[] { '\r', '\n' });
        return (nl >= 0 ? m[..nl] : m).Trim();
    }
}
