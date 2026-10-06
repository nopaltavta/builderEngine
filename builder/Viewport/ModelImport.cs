using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using OpenTK.Mathematics;

namespace builder.Viewport;

/// <summary>
/// FBX + OBJ import through Assimp: merges every mesh in the file into one
/// triangle soup of unit-space Verts (longest side spans ±0.5, like the
/// primitives, so Size scales it). Normals are smoothed; missing normals fall
/// back to flat face normals. Pure CPU (no GL): safe to call headless.
/// </summary>
public static class ModelImport
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".fbx", ".obj" };

    // No vertex cap: anything Assimp parses is importable. Downstream stages
    // budget independently (decimate 6k, physics stride-sampling, hull points),
    // so giant files cost RAM/VRAM but stay functional.

    /// <summary>Cached import (full + decimated + smooth-shaded variants).</summary>
    public sealed class ImportedModel
    {
        public StudioScene.Vertex[] Full = Array.Empty<StudioScene.Vertex>();
        public StudioScene.Vertex[] Decimated = Array.Empty<StudioScene.Vertex>();
        public StudioScene.Vertex[] Smooth = Array.Empty<StudioScene.Vertex>();
    }

    private static readonly Dictionary<string, ImportedModel> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> _useTick = new(StringComparer.OrdinalIgnoreCase);
    private static long _tick;
    private const int CacheCap = 24;

    /// <summary>Import once per path (shared by render LODs, picking and hulls).</summary>
    public static ImportedModel GetModel(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("no mesh file", nameof(path));
        lock (_cache)
        {
            if (_cache.TryGetValue(path, out var hit))
            {
                _useTick[path] = ++_tick;
                return hit;
            }
        }
        var full = Import(path);
        var model = new ImportedModel
        {
            Full = full,
            Decimated = Decimate(full),
            Smooth = Resmooth(full),
        };
        lock (_cache)
        {
            _cache[path] = model;
            _useTick[path] = ++_tick;
            if (_cache.Count > CacheCap) SweepLocked();
        }
        return model;
    }

    /// <summary>Evict the stalest quarter past the cap (caller holds the lock).</summary>
    private static void SweepLocked()
    {
        var order = new List<(string Key, long Tick)>(_cache.Count);
        foreach (var kv in _cache)
            order.Add((kv.Key, _useTick.TryGetValue(kv.Key, out long t) ? t : 0));
        order.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        int drop = Math.Max(1, _cache.Count / 4);
        for (int i = 0; i < drop && i < order.Count; i++)
        {
            _cache.Remove(order[i].Key);
            _useTick.Remove(order[i].Key);
        }
    }

    /// <summary>Forget a cached import (file replaced/removed).</summary>
    public static void DropModel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_cache) { _cache.Remove(path!); _useTick.Remove(path!); }
    }

    public static StudioScene.Vertex[] Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("no mesh file", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("mesh file not found", path);
        var pos = new List<OpenTK.Mathematics.Vector3>();
        var nrm = new List<OpenTK.Mathematics.Vector3>();
        var col = new List<OpenTK.Mathematics.Vector3>();
        using (var ctx = new AssimpContext())
        {
            var scene = ctx.ImportFile(path,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateSmoothNormals |
                PostProcessSteps.JoinIdenticalVertices);
            foreach (var m in scene.Meshes)
            {
                if (!m.HasVertices) continue;
                // Base color: per-vertex colors win, else the mesh material's
                // diffuse (multi-material files arrive pre-split, one mesh per
                // material), else white. Textures are not sampled.
                var baseCol = new OpenTK.Mathematics.Vector3(1f, 1f, 1f);
                System.Collections.Generic.List<Assimp.Color4D>? vc = null;
                try
                {
                    if (m.HasVertexColors(0)) vc = m.VertexColorChannels[0];
                }
                catch { vc = null; }
                if (vc == null && m.MaterialIndex >= 0 && m.MaterialIndex < scene.MaterialCount)
                {
                    try
                    {
                        var dc = scene.Materials[m.MaterialIndex].ColorDiffuse;
                        baseCol = new OpenTK.Mathematics.Vector3(dc.R, dc.G, dc.B);
                    }
                    catch { }
                }
                foreach (var f in m.Faces)
                {
                    if (f.IndexCount != 3) continue;
                    var p = new OpenTK.Mathematics.Vector3[3];
                    var n = new OpenTK.Mathematics.Vector3[3];
                    var c = new OpenTK.Mathematics.Vector3[3];
                    bool ok = true;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = f.Indices[k];
                        if (i < 0 || i >= m.VertexCount) { ok = false; break; }
                        var v = m.Vertices[i];
                        p[k] = new OpenTK.Mathematics.Vector3(v.X, v.Y, v.Z);
                        if (m.HasNormals)
                        {
                            var nv = m.Normals[i];
                            n[k] = new OpenTK.Mathematics.Vector3(nv.X, nv.Y, nv.Z);
                        }
                        if (vc != null)
                        {
                            try
                            {
                                var vcol = vc[i];
                                c[k] = new OpenTK.Mathematics.Vector3(vcol.R, vcol.G, vcol.B);
                            }
                            catch { c[k] = baseCol; }
                        }
                        else c[k] = baseCol;
                    }
                    if (!ok) continue;
                    var flat = OpenTK.Mathematics.Vector3.Cross(p[1] - p[0], p[2] - p[0]);
                    if (flat.LengthSquared < 1e-12f) continue; // degenerate
                    flat = OpenTK.Mathematics.Vector3.Normalize(flat);
                    for (int k = 0; k < 3; k++)
                    {
                        pos.Add(p[k]);
                        nrm.Add(n[k].LengthSquared > 1e-8f
                            ? OpenTK.Mathematics.Vector3.Normalize(n[k])
                            : flat);
                        col.Add(c[k]);
                    }
                }
            }
        }
        if (pos.Count == 0) throw new InvalidDataException("no triangles found");
        // Normalize to the unit box: uniform scale (keeps proportions) + recenter.
        var min = pos[0];
        var max = pos[0];
        foreach (var p in pos)
        {
            min = OpenTK.Mathematics.Vector3.ComponentMin(min, p);
            max = OpenTK.Mathematics.Vector3.ComponentMax(max, p);
        }
        var size = max - min;
        float extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (extent < 1e-6f || !float.IsFinite(extent)) throw new InvalidDataException("degenerate mesh");
        var center = (min + max) * 0.5f;
        float s = 1f / extent;
        var verts = new StudioScene.Vertex[pos.Count];
        for (int i = 0; i < pos.Count; i++)
            verts[i] = new StudioScene.Vertex((pos[i] - center) * s, nrm[i], col[i]);
        return verts;
    }

    /// <summary>Vertex-clustered decimation (Performance LOD + hull points):
    /// welds soup verts into grid cells, drops degenerate tris, flat normals.
    /// Retries on coarser grids until the tri count fits the budget.</summary>
    public static StudioScene.Vertex[] Decimate(StudioScene.Vertex[] full, int budget = 6000)
    {
        if (full.Length == 0) return Array.Empty<StudioScene.Vertex>();
        float cell = 1f / 48f;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var rep = new Dictionary<(int, int, int), OpenTK.Mathematics.Vector3>();
            var csum = new Dictionary<(int, int, int), OpenTK.Mathematics.Vector3>();
            var ccnt = new Dictionary<(int, int, int), int>();
            for (int t = 0; t + 2 < full.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    var p = full[t + k].Position;
                    var key = ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
                    if (!rep.ContainsKey(key))
                    {
                        rep[key] = new OpenTK.Mathematics.Vector3(
                            (key.Item1 + 0.5f) * cell, (key.Item2 + 0.5f) * cell, (key.Item3 + 0.5f) * cell);
                        csum[key] = OpenTK.Mathematics.Vector3.Zero;
                        ccnt[key] = 0;
                    }
                    csum[key] += full[t + k].Color;
                    ccnt[key]++;
                }
            }
            var cavg = new Dictionary<(int, int, int), OpenTK.Mathematics.Vector3>();
            foreach (var kv in csum) cavg[kv.Key] = kv.Value / Math.Max(ccnt[kv.Key], 1);
            var tris = new List<StudioScene.Vertex>();
            for (int t = 0; t + 2 < full.Length; t += 3)
            {
                var c = new OpenTK.Mathematics.Vector3[3];
                var cc = new OpenTK.Mathematics.Vector3[3];
                bool bad = false;
                for (int k = 0; k < 3; k++)
                {
                    var p = full[t + k].Position;
                    var key = ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
                    c[k] = rep[key];
                    cc[k] = cavg[key];
                    if (!float.IsFinite(c[k].X + c[k].Y + c[k].Z)) { bad = true; break; }
                }
                if (bad) continue;
                var e1 = c[1] - c[0];
                var e2 = c[2] - c[0];
                var n = OpenTK.Mathematics.Vector3.Cross(e1, e2);
                if (n.LengthSquared < 1e-12f) continue; // welded degenerate
                n = OpenTK.Mathematics.Vector3.Normalize(n);
                tris.Add(new StudioScene.Vertex(c[0], n, cc[0]));
                tris.Add(new StudioScene.Vertex(c[1], n, cc[1]));
                tris.Add(new StudioScene.Vertex(c[2], n, cc[2]));
            }
            if (tris.Count / 3 <= budget || attempt == 3) return tris.ToArray();
            cell *= 2f;
        }
        return Array.Empty<StudioScene.Vertex>();
    }

    /// <summary>Recomputed smooth shading (Ultra LOD): averages face normals by
    /// position so CAD-flat files shade organic. Already-smooth files barely change.</summary>
    public static StudioScene.Vertex[] Resmooth(StudioScene.Vertex[] full)
    {
        if (full.Length == 0) return Array.Empty<StudioScene.Vertex>();
        const float cell = 1f / 512f;
        var acc = new Dictionary<(int, int, int), OpenTK.Mathematics.Vector3>();
        for (int t = 0; t + 2 < full.Length; t += 3)
        {
            var e1 = full[t + 1].Position - full[t].Position;
            var e2 = full[t + 2].Position - full[t].Position;
            var fn = OpenTK.Mathematics.Vector3.Cross(e1, e2);
            if (fn.LengthSquared < 1e-12f) continue;
            fn = OpenTK.Mathematics.Vector3.Normalize(fn);
            for (int k = 0; k < 3; k++)
            {
                var p = full[t + k].Position;
                var key = ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
                acc[key] = acc.TryGetValue(key, out var a) ? a + fn : fn;
            }
        }
        var out_ = new StudioScene.Vertex[full.Length];
        for (int i = 0; i < full.Length; i++)
        {
            var p = full[i].Position;
            var key = ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
            var n = acc.TryGetValue(key, out var a) && a.LengthSquared > 1e-8f
                ? OpenTK.Mathematics.Vector3.Normalize(a)
                : full[i].Normal;
            out_[i] = new StudioScene.Vertex(p, n, full[i].Color);
        }
        return out_;
    }
}
