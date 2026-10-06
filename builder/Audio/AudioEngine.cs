using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using builder.Settings;
using builder.Viewport;

namespace builder.Audio;

/// <summary>
/// Tiny OpenAL sound engine: one shared device/context, procedurally
/// synthesized UI + play-mode blips (no wav assets to ship), a small
/// round-robin source pool, master volume from settings. Everything is
/// best-effort: no device (or no openal32.dll next to the exe) means
/// silence, never a crash. All calls must come from the UI thread.
/// </summary>
public static class AudioEngine
{
    public static Action<string>? Logger;
    public static bool Available => _ok;

    private const int Rate = 22050;
    private const int PoolSize = 8;

    private static readonly object _gate = new();
    private static DateTime _lastAttemptUtc = DateTime.MinValue; // device-open throttle
    private static bool _ok;
    private static bool _announced;
    private static bool _announcedOff;
    private static ALDevice _device;
    private static ALContext _context;
    private static readonly Dictionary<string, int> _buffers = new();
    private static readonly List<int> _sources = new();
    private static int _nextSource;
    // Part sounds: decoded file buffers by path + live object tracking.
    private static readonly Dictionary<string, int> _fileBuffers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _fileFailures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<SceneObject> _tracked = new();
    private static readonly HashSet<SceneObject> _oneShots = new(); // touch one-shots the pump must not stop
    private const long MaxDecodeFloats = 32 * 1024 * 1024; // ~128 MB PCM cap per file
    // Manual distance curve (OpenAL skips attenuation for stereo buffers, so the
    // engine fades every source itself; mono sources additionally get panning).
    private const float RefDistance = 8f;
    private const float Rolloff = 1.5f;
    private const float MaxDistance = 250f;

    /// <summary>Inverse-distance-clamped gain: 1 inside RefDistance, 0 past MaxDistance.</summary>
    private static float Attenuate(float dist)
    {
        if (dist <= RefDistance) return 1f;
        if (dist >= MaxDistance) return 0f;
        return RefDistance / (RefDistance + Rolloff * (dist - RefDistance));
    }

    /// <summary>Open the default device + build the blips. Retries a dead
    /// device every few seconds (one hiccup must not silence the session).</summary>
    public static bool Ensure()
    {
        lock (_gate)
        {
            if (_ok) return true;
            if ((DateTime.UtcNow - _lastAttemptUtc).TotalSeconds < 5) return false;
            _lastAttemptUtc = DateTime.UtcNow;
            try
            {
                try { Teardown(); } catch { } // drop stale handles before reopening
                _device = ALC.OpenDevice(null);
                if (_device == ALDevice.Null) throw new InvalidOperationException("no audio device");
                _context = ALC.CreateContext(_device, (int[]?)null);
                if (_context == ALContext.Null) throw new InvalidOperationException("no audio context");
                if (!ALC.MakeContextCurrent(_context)) throw new InvalidOperationException("context not current");
                if (_buffers.Count == 0)
                {
                    Add("click", Sweep(1700, 1100, 0.035, 0.5, decay: true));
                    Add("confirm", TwoTone(660, 990, 0.05, 0.07, 0.5));
                    Add("play", Sweep(420, 920, 0.14, 0.5, decay: false));
                    Add("stop", Sweep(720, 280, 0.14, 0.5, decay: true));
                    Add("checkpoint", Chord(new[] { 880.0, 1318.5 }, 0.3, 0.45));
                    Add("bounce", Sweep(480, 160, 0.2, 0.55, decay: true));
                    Add("teleport", Sweep(280, 1250, 0.22, 0.45, decay: false));
                    Add("error", Square(150, 0.15, 0.4));
                }
                if (_sources.Count == 0)
                    for (int i = 0; i < PoolSize; i++) _sources.Add(AL.GenSource());
                ApplyVolume();
                _ok = AL.GetError() == ALError.NoError;
                if (!_ok) throw new InvalidOperationException(AL.GetError().ToString());
            }
            catch (Exception ex)
            {
                _ok = false;
                try { Teardown(); } catch { }
                if (!_announcedOff)
                {
                    _announcedOff = true;
                    Announce("Sound off: OpenAL not available (" + ex.GetType().Name + ").");
                }
                return false;
            }
            if (_announcedOff) { _announcedOff = false; _announced = false; }
            Announce("Sound on (OpenAL).");
            return _ok;
        }
    }

    private static void Announce(string msg)
    {
        if (_announced) return;
        _announced = true;
        try { Logger?.Invoke(msg); } catch { }
    }

    /// <summary>Fire-and-forget blip. Unknown names, missing device, or a
    /// disabled setting are all silent no-ops.</summary>
    public static void Play(string name, float volume = 1f, float pitch = 1f)
    {
        if (!AppSettings.Current.SoundEnabled) return;
        if (!Ensure()) return;
        lock (_gate)
        {
            if (!_ok) return;
            try
            {
                if (!_buffers.TryGetValue(name, out int buf)) return;
                int src = _sources[_nextSource];
                _nextSource = (_nextSource + 1) % _sources.Count;
                AL.SourceStop(src);
                AL.Source(src, ALSourcei.Buffer, buf);
                AL.Source(src, ALSourceb.Looping, false);
                AL.Source(src, ALSourcef.RolloffFactor, 0f);
                AL.Source(src, ALSource3f.Position, ref _listenerPos); // UI blips ignore old effect positions
                AL.Source(src, ALSourcef.Gain, Math.Clamp(volume, 0f, 1f));
                AL.Source(src, ALSourcef.Pitch, Math.Clamp(pitch, 0.25f, 4f));
                AL.SourcePlay(src);
            }
            catch
            {
                _ok = false; // context lost mid-run: go quiet instead of spamming
            }
        }
    }

    private static Vector3 _listenerPos;
    private static readonly Random _pitchRnd = new();

    /// <summary>Named sound effect from SoundEffects/ (mp3, then wav, then a
    /// synth fallback). Fire-and-forget on the blip pool, slight pitch wobble.
    /// Pass a world position for distance fading (same curve as part sounds).</summary>
    public static void PlayEffect(string name, float volume = 1f, Vector3? at = null)
    {
        if (!AppSettings.Current.SoundEnabled) return;
        if (!Ensure()) return;
        lock (_gate)
        {
            if (!_ok || _sources.Count == 0) return;
            try
            {
                int buf = EffectBuffer(name);
                if (buf == 0) return;
                int src = _sources[_nextSource];
                _nextSource = (_nextSource + 1) % _sources.Count;
                AL.SourceStop(src);
                AL.Source(src, ALSourcei.Buffer, buf);
                AL.Source(src, ALSourceb.Looping, false);
                AL.Source(src, ALSourcef.RolloffFactor, 0f); // engine fades manually (stereo-safe)
                float gain = Math.Clamp(volume, 0f, 1f);
                if (at != null)
                {
                    Vector3 p = at.Value;
                    AL.Source(src, ALSource3f.Position, ref p);
                    gain *= Attenuate((at.Value - _listenerPos).Length);
                }
                AL.Source(src, ALSourcef.Gain, gain);
                AL.Source(src, ALSourcef.Pitch, 0.95f + (float)_pitchRnd.NextDouble() * 0.1f);
                AL.SourcePlay(src);
            }
            catch
            {
                _ok = false;
            }
        }
    }

    /// <summary>Effect buffer: SoundEffects/name.mp3, then .wav, then synth. 0 = none.</summary>
    private static int EffectBuffer(string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "SoundEffects");
        foreach (string ext in new[] { ".mp3", ".wav" })
        {
            string path = Path.Combine(dir, name + ext);
            if (!File.Exists(path)) continue;
            int buf = LoadFileBuffer(path);
            if (buf != 0) return buf;
        }
        string key = "fx_" + name;
        if (_buffers.TryGetValue(key, out int synth)) return synth;
        byte[] pcm = name == "wood-breaking"
            ? Crack(0.28, 0.8)
            : Sweep(150, 55, 0.18, 0.8, decay: true); // stomp-ish thud default
        int buf2 = AL.GenBuffer();
        var pin = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        try { AL.BufferData(buf2, ALFormat.Mono16, pin.AddrOfPinnedObject(), pcm.Length, Rate); }
        finally { pin.Free(); }
        if (AL.GetError() != ALError.NoError) throw new InvalidOperationException("effect upload failed");
        _buffers[key] = buf2;
        return buf2;
    }

    /// <summary>Noise crackle with decaying low thumps (breaking wood).</summary>
    private static byte[] Crack(double secs, double vol)
    {
        int n = Math.Max(1, (int)(Rate * secs));
        var s = new short[n];
        var rnd = new Random(99);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            double v = (rnd.NextDouble() * 2 - 1) * Math.Exp(-11 * t);
            v += Math.Sin(2.0 * Math.PI * 140 * i / Rate) * Math.Exp(-14 * t) * 0.5;
            v += Math.Sin(2.0 * Math.PI * 90 * i / Rate) * Math.Exp(-8 * t) * 0.4;
            s[i] = (short)(Math.Clamp(v * 0.9, -1.0, 1.0) * vol * 32767);
        }
        return ToBytes(s);
    }

    /// <summary>Push the settings volume to the listener (live, even mid-play).</summary>
    public static void ApplyVolume()
    {
        lock (_gate)
        {
            if (!_ok) return;
            try { AL.Listener(ALListenerf.Gain, Math.Clamp(AppSettings.Current.SoundVolume, 0f, 1f)); }
            catch { _ok = false; }
        }
    }

    public static void Shutdown()
    {
        lock (_gate)
        {
            try { Teardown(); } catch { }
            _ok = false;
            _lastAttemptUtc = DateTime.MinValue;
            _announced = false;
            _announcedOff = false;
        }
    }

    // ---------- part sounds (Roblox-style, positional) ----------

    /// <summary>Listener follows the camera (call each rendered frame).</summary>
    public static void SetListener(Vector3 pos, Vector3 forward)
    {
        lock (_gate)
        {
            _listenerPos = pos;
            if (!_ok) return;
            try
            {
                Vector3 fwd = forward.LengthSquared > 1e-8f ? Vector3.Normalize(forward) : -Vector3.UnitZ;
                Vector3 right = Vector3.Cross(fwd, Vector3.UnitY);
                if (right.LengthSquared < 1e-6f) right = Vector3.UnitX;
                right = Vector3.Normalize(right);
                Vector3 up = Vector3.Cross(right, fwd);
                AL.Listener(ALListener3f.Position, ref pos);
                AL.Listener(ALListenerfv.Orientation, new[] { fwd.X, fwd.Y, fwd.Z, up.X, up.Y, up.Z });
            }
            catch { _ok = false; }
        }
    }

    /// <summary>Decode an audio file to an OpenAL buffer (cached by path). 0 on failure.
    /// Decode failures blacklist the file; device failures don't (auto-retry).</summary>
    public static int LoadFileBuffer(string path, string? baseDir = null)
    {
        string candidate = path;
        if (!Path.IsPathRooted(candidate))
        {
            if (!string.IsNullOrEmpty(baseDir))
            {
                string joined = Path.Combine(baseDir, candidate);
                if (File.Exists(joined)) candidate = joined;
            }
            if (!File.Exists(candidate))
            {
                string appRel = Path.Combine(AppContext.BaseDirectory, candidate);
                if (File.Exists(appRel)) candidate = appRel;
            }
        }
        lock (_gate)
        {
            if (_fileBuffers.TryGetValue(candidate, out int cached)) return cached;
            if (_fileFailures.Contains(candidate)) return 0;
        }
        float[] data;
        int channels, rate;
        try
        {
            (data, channels, rate) = DecodeFile(candidate);
        }
        catch (Exception ex)
        {
            lock (_gate) { _fileFailures.Add(candidate); }
            try { Logger?.Invoke($"Sound: can't play {Path.GetFileName(candidate)} ({ex.GetType().Name})."); } catch { }
            return 0;
        }
        int frames = data.Length / Math.Max(channels, 1);
        bool stereo = channels >= 2;
        var pcm = new short[frames * (stereo ? 2 : 1)];
        for (int f = 0; f < frames; f++)
        {
            if (!stereo)
            {
                pcm[f] = ToShort(data[f * channels]);
            }
            else
            {
                double l = 0, r = 0;
                int ln = 0, rn = 0;
                for (int c = 0; c < channels; c++)
                {
                    float v = data[f * channels + c];
                    if (c % 2 == 0) { l += v; ln++; } else { r += v; rn++; }
                }
                pcm[f * 2] = ToShort((float)(l / Math.Max(ln, 1)));
                pcm[f * 2 + 1] = ToShort((float)(r / Math.Max(rn, 1)));
            }
        }
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        lock (_gate)
        {
            if (!Ensure()) return 0; // transient: retry later, file NOT blacklisted
            int buf = 0;
            try
            {
                buf = AL.GenBuffer();
                var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try { AL.BufferData(buf, stereo ? ALFormat.Stereo16 : ALFormat.Mono16, pin.AddrOfPinnedObject(), bytes.Length, rate); }
                finally { pin.Free(); }
                if (AL.GetError() != ALError.NoError) throw new InvalidOperationException("buffer upload failed");
                _fileBuffers[candidate] = buf;
                return buf;
            }
            catch
            {
                if (buf != 0) { try { AL.DeleteBuffer(buf); } catch { } }
                return 0;
            }
        }
    }

    /// <summary>Decode to float PCM without trusting stream Length (mp3 lies).</summary>
    public static (float[] Data, int Channels, int Rate) DecodeFile(string path)
    {
        using var reader = new AudioFileReader(path);
        var sampler = (ISampleProvider)reader;
        int channels = Math.Max(reader.WaveFormat.Channels, 1);
        int rate = Math.Max(reader.WaveFormat.SampleRate, 1);
        var tmp = new float[Rate * 10];
        var parts = new List<float[]>();
        long total = 0;
        int r;
        while ((r = sampler.Read(tmp.AsSpan())) > 0)
        {
            total += r;
            if (total > MaxDecodeFloats) throw new InvalidDataException("audio too long");
            var cp = new float[r];
            Array.Copy(tmp, cp, r);
            parts.Add(cp);
        }
        if (total == 0) throw new InvalidDataException("empty audio");
        var data = new float[total];
        int off = 0;
        foreach (var p in parts) { Array.Copy(p, 0, data, off, p.Length); off += p.Length; }
        return (data, channels, rate);
    }

    private static short ToShort(float v) =>
        (short)(Math.Clamp(v, -1f, 1f) * 32767);

    /// <summary>Reconcile one part's live source with its flags (call per frame).</summary>
    public static void PartSoundUpdate(SceneObject o, Vector3 listenerPos, string? baseDir = null)
    {
        if (!AppSettings.Current.SoundEnabled || !o.SoundPlaying || !o.HasSound)
        {
            PartSoundStop(o);
            return;
        }
        if (!Ensure()) return;
        lock (_gate)
        {
            if (!_ok || _oneShots.Contains(o)) return; // engine-managed one-shot: hands off
            try
            {
                if (!EnsurePartSourceLocked(o, baseDir)) return;
                Vector3 pos = o.Position;
                AL.Source(o.SoundSource, ALSource3f.Position, ref pos);
                float dist = (o.Position - listenerPos).Length;
                AL.Source(o.SoundSource, ALSourcef.Gain,
                    Math.Clamp(o.SoundVolume, 0f, 1f) * Attenuate(dist));
                AL.Source(o.SoundSource, ALSourceb.Looping, o.SoundLooped);
                AL.Source(o.SoundSource, ALSourcef.RolloffFactor, 0f);
                AL.GetSource(o.SoundSource, ALGetSourcei.SourceState, out int st);
                if ((ALSourceState)st != ALSourceState.Playing)
                    AL.SourcePlay(o.SoundSource);
            }
            catch { _ok = false; }
        }
    }

    /// <summary>Touch trigger: restart the part's sound from the top. Looped
    /// sounds latch Playing on (the pump maintains them); one-shots are
    /// engine-managed until they finish (the pump leaves them alone).</summary>
    public static void TouchPartSound(SceneObject o, Vector3 listenerPos, string? baseDir = null)
    {
        if (!AppSettings.Current.SoundEnabled || !o.HasSound) return;
        if (!Ensure()) return;
        lock (_gate)
        {
            if (!_ok) return;
            try
            {
                if (!EnsurePartSourceLocked(o, baseDir)) return;
                Vector3 pos = o.Position;
                AL.Source(o.SoundSource, ALSource3f.Position, ref pos);
                float dist = (o.Position - listenerPos).Length;
                AL.Source(o.SoundSource, ALSourcef.Gain,
                    Math.Clamp(o.SoundVolume, 0f, 1f) * Attenuate(dist));
                AL.Source(o.SoundSource, ALSourceb.Looping, o.SoundLooped);
                AL.Source(o.SoundSource, ALSourcef.RolloffFactor, 0f);
                AL.SourceStop(o.SoundSource);
                AL.SourcePlay(o.SoundSource);
                if (o.SoundLooped) o.SoundPlaying = true; // pump maintains looped touches
                else _oneShots.Add(o);
            }
            catch { _ok = false; }
        }
    }

    /// <summary>Free finished one-shots (call per frame while playing).</summary>
    public static void PollOneShots()
    {
        List<SceneObject>? done = null;
        lock (_gate)
        {
            if (!_ok || _oneShots.Count == 0) return;
            foreach (var o in _oneShots)
            {
                try
                {
                    if (o.SoundSource == 0) { (done ??= new List<SceneObject>()).Add(o); continue; }
                    AL.GetSource(o.SoundSource, ALGetSourcei.SourceState, out int st);
                    if ((ALSourceState)st == ALSourceState.Stopped)
                    {
                        FreeSourceLocked(o);
                        (done ??= new List<SceneObject>()).Add(o);
                    }
                }
                catch { _ok = false; return; }
            }
            if (done != null)
                foreach (var o in done) _oneShots.Remove(o);
        }
    }

    /// <summary>Build + attach the live source when missing/stale. False = failed.</summary>
    private static bool EnsurePartSourceLocked(SceneObject o, string? baseDir)
    {
        if (o.SoundSource != 0 && o.SoundLoadedPath == o.SoundPath) return true;
        FreeSourceLocked(o);
        int buf = LoadFileBuffer(o.SoundPath!, baseDir);
        if (buf == 0) return false; // failure already logged + cached
        int src = AL.GenSource();
        AL.Source(src, ALSourcei.Buffer, buf);
        AL.Source(src, ALSourceb.Looping, o.SoundLooped);
        AL.Source(src, ALSourcef.RolloffFactor, 0f); // engine fades manually (stereo-safe)
        AL.Source(src, ALSourcef.MaxDistance, MaxDistance);
        o.SoundSource = src;
        o.SoundLoadedPath = o.SoundPath;
        _tracked.Add(o);
        return true;
    }

    /// <summary>Stop + free one part's source (keeps its flags).</summary>
    public static void PartSoundStop(SceneObject o)
    {
        lock (_gate)
        {
            try { FreeSourceLocked(o); } catch { }
            _tracked.Remove(o);
        }
    }

    /// <summary>Stop sources whose object left the scene (undo/delete/load).</summary>
    public static void ReapStale(ICollection<SceneObject> live)
    {
        SceneObject[] dead;
        lock (_gate)
        {
            if (_tracked.Count == 0) return;
            var alive = new HashSet<SceneObject>(live);
            dead = new SceneObject[_tracked.Count];
            int n = 0;
            foreach (var o in _tracked)
                if (!alive.Contains(o)) dead[n++] = o;
            Array.Resize(ref dead, n);
            foreach (var o in dead)
            {
                try { FreeSourceLocked(o); } catch { }
                _tracked.Remove(o);
            }
        }
    }

    /// <summary>Silence every part sound (Play-mode exit). Flags are kept.</summary>
    public static void StopAllPartSounds()
    {
        SceneObject[] all;
        lock (_gate)
        {
            all = new SceneObject[_tracked.Count];
            _tracked.CopyTo(all);
        }
        foreach (var o in all) PartSoundStop(o);
    }

    private static void FreeSourceLocked(SceneObject o)
    {
        _oneShots.Remove(o);
        if (o.SoundSource != 0)
        {
            try { AL.SourceStop(o.SoundSource); AL.DeleteSource(o.SoundSource); } catch { }
            o.SoundSource = 0;
            o.SoundLoadedPath = null;
        }
    }

    private static void Add(string name, byte[] pcm)
    {
        int buf = AL.GenBuffer();
        var pin = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        try { AL.BufferData(buf, ALFormat.Mono16, pin.AddrOfPinnedObject(), pcm.Length, Rate); }
        finally { pin.Free(); }
        _buffers[name] = buf;
    }

    private static void Teardown()
    {
        foreach (int src in _sources) { try { AL.SourceStop(src); AL.DeleteSource(src); } catch { } }
        _sources.Clear();
        foreach (var o in _tracked) { o.SoundSource = 0; o.SoundLoadedPath = null; }
        _tracked.Clear();
        foreach (var buf in _fileBuffers.Values) { try { AL.DeleteBuffer(buf); } catch { } }
        _fileBuffers.Clear();
        // NOTE: _fileFailures survives: undecodable stays undecodable across device retries.
        foreach (var buf in _buffers.Values) { try { AL.DeleteBuffer(buf); } catch { } }
        _buffers.Clear();
        try { ALC.MakeContextCurrent(ALContext.Null); } catch { }
        try { if (_context != ALContext.Null) ALC.DestroyContext(_context); } catch { }
        _context = ALContext.Null;
        try { if (_device != ALDevice.Null) ALC.CloseDevice(_device); } catch { }
        _device = ALDevice.Null;
    }

    // ---------- procedural blips (16-bit mono PCM) ----------

    private static byte[] ToBytes(short[] s)
    {
        var b = new byte[s.Length * 2];
        Buffer.BlockCopy(s, 0, b, 0, b.Length);
        return b;
    }

    private static byte[] Sweep(double f0, double f1, double secs, double vol, bool decay)
    {
        int n = Math.Max(1, (int)(Rate * secs));
        var s = new short[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            phase += 2.0 * Math.PI * (f0 + (f1 - f0) * t) / Rate;
            double env = decay ? Math.Exp(-3.5 * t) : Math.Min(1.0, t / 0.15) * Math.Exp(-1.2 * t);
            s[i] = (short)(Math.Sin(phase) * env * vol * 32767);
        }
        return ToBytes(s);
    }

    private static byte[] TwoTone(double f0, double f1, double secs0, double secs1, double vol)
    {
        int n0 = Math.Max(1, (int)(Rate * secs0)), n1 = Math.Max(1, (int)(Rate * secs1));
        var s = new short[n0 + n1];
        double phase = 0;
        for (int i = 0; i < n0; i++)
        {
            double t = i / (double)n0;
            phase += 2.0 * Math.PI * f0 / Rate;
            s[i] = (short)(Math.Sin(phase) * Math.Exp(-2.5 * t) * vol * 32767);
        }
        for (int i = 0; i < n1; i++)
        {
            double t = i / (double)n1;
            phase += 2.0 * Math.PI * f1 / Rate;
            s[n0 + i] = (short)(Math.Sin(phase) * Math.Exp(-3.0 * t) * vol * 32767);
        }
        return ToBytes(s);
    }

    private static byte[] Chord(double[] freqs, double secs, double vol)
    {
        int n = Math.Max(1, (int)(Rate * secs));
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            double v = 0;
            foreach (double f in freqs) v += Math.Sin(2.0 * Math.PI * f * i / Rate);
            s[i] = (short)(v / freqs.Length * Math.Exp(-3.0 * t) * vol * 32767);
        }
        return ToBytes(s);
    }

    private static byte[] Square(double freq, double secs, double vol)
    {
        int n = Math.Max(1, (int)(Rate * secs));
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            double v = Math.Sign(Math.Sin(2.0 * Math.PI * freq * i / Rate)) * 0.6;
            s[i] = (short)(v * Math.Exp(-2.5 * t) * vol * 32767);
        }
        return ToBytes(s);
    }
}
