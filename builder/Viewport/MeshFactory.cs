using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using static builder.Viewport.StudioScene;

namespace builder.Viewport;

/// <summary>Available part meshes. All unit-sized, centered on the origin.</summary>
public enum ShapeKind
{
    Block,
    Killbrick, // block mesh, hurts the avatar (Damage). Roblox-style killbrick.
    Ball,
    Cylinder,
    Wedge,
    Cone,
    Torus,
    Capsule,
    Pyramid,
    CornerWedge,
    Stairs,
    Mesh, // imported fbx/obj (SceneObject.MeshPath); renders the file, collides as a box
    None, // lights: no solid mesh, editor glyph instead
    HalfBall,      // solid dome (spherical cap)
    HollowCylinder, // open tube (concave: exact only when anchored)
    Bowl,          // hollow hemisphere shell (concave: exact only when anchored)
    Arch,          // blocky 3-box arch (exact compound, always)
    HexPrism,      // six-sided prism
    Truss,         // cube frame of 12 beams (concave: exact only when anchored)
    RoundedBox,    // rounded-corner block (beta avatar head; rig-only for now)
}

/// <summary>How a Mesh part collides: bounding box, or the file itself when anchored.</summary>
public enum CollisionFidelityKind
{
    Box,
    Precise, // anchored = exact triangles; unanchored = convex hull (Bepu has no dynamic meshes)
}

/// <summary>How a Mesh part renders: decimated, as imported, or resmoothed full detail.</summary>
public enum RenderFidelityKind
{
    Performance, // vertex-clustered lowpoly
    Normal,      // file as imported
    Ultra,       // full detail, recomputed smooth shading
}
/// <summary>Procedural primitive meshes (positions + flat normals). Windings are CCW-outside.</summary>
public static class MeshFactory
{
    private static void Tri(List<Vertex> dst, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        dst.Add(new Vertex(a, n));
        dst.Add(new Vertex(b, n));
        dst.Add(new Vertex(c, n));
    }

    private static void Quad(List<Vertex> dst, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        Tri(dst, a, b, c, n);
        Tri(dst, a, c, d, n);
    }

    public static Vertex[] Block()
    {
        var tris = new List<Vertex>();
        Vector3[] p =
        {
            new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
            new(-0.5f, -0.5f,  0.5f), new(0.5f, -0.5f,  0.5f), new(0.5f, 0.5f,  0.5f), new(-0.5f, 0.5f,  0.5f),
        };
        Quad(tris, p[4], p[5], p[6], p[7], Vector3.UnitZ);
        Quad(tris, p[1], p[0], p[3], p[2], -Vector3.UnitZ);
        Quad(tris, p[0], p[4], p[7], p[3], -Vector3.UnitX);
        Quad(tris, p[5], p[1], p[2], p[6], Vector3.UnitX);
        Quad(tris, p[7], p[6], p[2], p[3], Vector3.UnitY);
        Quad(tris, p[0], p[1], p[5], p[4], -Vector3.UnitY);
        return tris.ToArray();
    }

    public static Vertex[] Sphere(int lat = 32, int lon = 48)
    {
        var tris = new List<Vertex>();
        Vector3 P(int i, int j)
        {
            float theta = i / (float)lat * MathF.PI;
            float phi = j / (float)lon * MathF.PI * 2f;
            float st = MathF.Sin(theta);
            // Unit primitives span ±0.5 (direction matters, the shader normalizes).
            return new Vector3(st * MathF.Cos(phi), MathF.Cos(theta), st * MathF.Sin(phi)) * 0.5f;
        }
        static Vector3 Nrm(Vector3 p) => Vector3.Normalize(p); // radial: smooth per-vertex
        for (int i = 0; i < lat; i++)
        for (int j = 0; j < lon; j++)
        {
            var p00 = P(i, j); var p10 = P(i + 1, j);
            var p01 = P(i, j + 1); var p11 = P(i + 1, j + 1);
            // Same winding as before — only the normals change (one shared face
            // normal made it look faceted; per-vertex radial normals shade smooth).
            tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p11, Nrm(p11))); tris.Add(new Vertex(p10, Nrm(p10)));
            tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p01, Nrm(p01))); tris.Add(new Vertex(p11, Nrm(p11)));
        }
        return tris.ToArray();
    }

    public static Vertex[] Cylinder(int seg = 32)
    {
        const float r = 0.5f;
        var tris = new List<Vertex>();
        Vector3 Ring(float a, float y) => new(r * MathF.Cos(a), y, r * MathF.Sin(a));
        Vector3 Rad(float a) => new(MathF.Cos(a), 0, MathF.Sin(a));
        for (int k = 0; k < seg; k++)
        {
            float a0 = k / (float)seg * MathF.PI * 2f;
            float a1 = (k + 1) / (float)seg * MathF.PI * 2f;
            var b0 = Ring(a0, -0.5f); var b1 = Ring(a1, -0.5f);
            var t0 = Ring(a0, 0.5f); var t1 = Ring(a1, 0.5f);
            var n0 = Rad(a0); var n1 = Rad(a1);
            // Smooth sides: per-vertex radial normals so highlights don't facet.
            tris.Add(new Vertex(b0, n0)); tris.Add(new Vertex(t1, n1)); tris.Add(new Vertex(b1, n1));
            tris.Add(new Vertex(b0, n0)); tris.Add(new Vertex(t0, n0)); tris.Add(new Vertex(t1, n1));
            Tri(tris, new Vector3(0, 0.5f, 0), t1, t0, Vector3.UnitY);   // top cap
            Tri(tris, new Vector3(0, -0.5f, 0), b0, b1, -Vector3.UnitY); // bottom cap
        }
        return tris.ToArray();
    }

    public static Vertex[] Wedge()
    {
        // Right triangular prism: full square base, slopes from the top-back
        // edge (y=+.5, z=-.5) down to the bottom-front edge (y=-.5, z=+.5).
        var tris = new List<Vertex>();
        var b0 = new Vector3(-0.5f, -0.5f, -0.5f); var b1 = new Vector3(0.5f, -0.5f, -0.5f);
        var b2 = new Vector3(0.5f, -0.5f, 0.5f); var b3 = new Vector3(-0.5f, -0.5f, 0.5f);
        var t0 = new Vector3(-0.5f, 0.5f, -0.5f); var t1 = new Vector3(0.5f, 0.5f, -0.5f);
        var slopeN = Vector3.Normalize(new Vector3(0, 1, 1));
        Quad(tris, b0, b1, b2, b3, -Vector3.UnitY); // bottom
        Tri(tris, b0, t1, b1, -Vector3.UnitZ);      // back
        Tri(tris, b0, t0, t1, -Vector3.UnitZ);
        Tri(tris, b0, b3, t0, -Vector3.UnitX);      // left triangle
        Tri(tris, b1, t1, b2, Vector3.UnitX);       // right triangle
        Tri(tris, t0, b2, t1, slopeN);              // slope
        Tri(tris, t0, b3, b2, slopeN);
        return tris.ToArray();
    }

    public static Vertex[] Cone(int seg = 32)
    {
        const float r = 0.5f;
        var tris = new List<Vertex>();
        var tip = new Vector3(0, 0.5f, 0);
        var bc = new Vector3(0, -0.5f, 0);
        for (int k = 0; k < seg; k++)
        {
            float a0 = k / (float)seg * MathF.PI * 2f;
            float a1 = (k + 1) / (float)seg * MathF.PI * 2f;
            float am = (a0 + a1) / 2f;
            var p0 = new Vector3(r * MathF.Cos(a0), -0.5f, r * MathF.Sin(a0));
            var p1 = new Vector3(r * MathF.Cos(a1), -0.5f, r * MathF.Sin(a1));
            var n = Vector3.Normalize(new Vector3(MathF.Cos(am), 0.5f, MathF.Sin(am)));
            Tri(tris, tip, p1, p0, n);
            Tri(tris, bc, p0, p1, -Vector3.UnitY);
        }
        return tris.ToArray();
    }

    public static Vertex[] Torus(float R = 0.32f, float r = 0.18f, int rings = 36, int sides = 24)
    {
        var tris = new List<Vertex>();
        Vector3 P(int i, int j)
        {
            float u = i / (float)rings * MathF.PI * 2f;
            float v = j / (float)sides * MathF.PI * 2f;
            float cu = MathF.Cos(u), su = MathF.Sin(u);
            float cv = MathF.Cos(v), sv = MathF.Sin(v);
            return new Vector3((R + r * cv) * cu, r * sv, (R + r * cv) * su);
        }
        Vector3 Nrm(int i, int j)
        {
            float u = i / (float)rings * MathF.PI * 2f;
            float v = j / (float)sides * MathF.PI * 2f;
            return new Vector3(MathF.Cos(v) * MathF.Cos(u), MathF.Sin(v), MathF.Cos(v) * MathF.Sin(u));
        }
        for (int i = 0; i < rings; i++)
        for (int j = 0; j < sides; j++)
        {
            var a = P(i, j); var b = P(i + 1, j); var c = P(i + 1, j + 1); var d = P(i, j + 1);
            var na = Nrm(i, j); var nb = Nrm(i + 1, j); var nc = Nrm(i + 1, j + 1); var nd = Nrm(i, j + 1);
            tris.Add(new Vertex(a, na)); tris.Add(new Vertex(c, nc)); tris.Add(new Vertex(b, nb));
            tris.Add(new Vertex(a, na)); tris.Add(new Vertex(d, nd)); tris.Add(new Vertex(c, nc));
        }
        return tris.ToArray();
    }

    public static Vertex[] Capsule(int seg = 24, int capRings = 12)
    {
        // Pill fitting the unit box: cylinder midsection (|y| <= .25, r = .25)
        // with hemispherical caps. Seam normals match, so shading stays smooth.
        const float r = 0.25f, hy = 0.25f;
        var tris = new List<Vertex>();
        for (int k = 0; k < seg; k++)
        {
            float a0 = k / (float)seg * MathF.PI * 2f;
            float a1 = (k + 1) / (float)seg * MathF.PI * 2f;
            var b0 = new Vector3(r * MathF.Cos(a0), -hy, r * MathF.Sin(a0));
            var b1 = new Vector3(r * MathF.Cos(a1), -hy, r * MathF.Sin(a1));
            var t0 = new Vector3(r * MathF.Cos(a0), hy, r * MathF.Sin(a0));
            var t1 = new Vector3(r * MathF.Cos(a1), hy, r * MathF.Sin(a1));
            var n0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0));
            var n1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1));
            tris.Add(new Vertex(b0, n0)); tris.Add(new Vertex(t1, n1)); tris.Add(new Vertex(b1, n1));
            tris.Add(new Vertex(b0, n0)); tris.Add(new Vertex(t0, n0)); tris.Add(new Vertex(t1, n1));
        }
        Vector3 TopCap(int i, int j, out Vector3 n)
        {
            float p = i / (float)capRings * MathF.PI / 2f;
            float a = j / (float)seg * MathF.PI * 2f;
            float sp = MathF.Sin(p), cp = MathF.Cos(p);
            n = new Vector3(sp * MathF.Cos(a), cp, sp * MathF.Sin(a));
            return new Vector3(r * n.X, hy + r * n.Y, r * n.Z);
        }
        Vector3 BotCap(int i, int j, out Vector3 n)
        {
            float q = i / (float)capRings * MathF.PI / 2f;
            float a = j / (float)seg * MathF.PI * 2f;
            float sq = MathF.Sin(q), cq = MathF.Cos(q);
            n = new Vector3(sq * MathF.Cos(a), -cq, sq * MathF.Sin(a));
            return new Vector3(r * n.X, -hy + r * n.Y, r * n.Z);
        }
        for (int i = 0; i < capRings; i++)
        for (int j = 0; j < seg; j++)
        {
            var p00 = TopCap(i, j, out var n00); var p10 = TopCap(i + 1, j, out var n10);
            var p01 = TopCap(i, j + 1, out var n01); var p11 = TopCap(i + 1, j + 1, out var n11);
            tris.Add(new Vertex(p00, n00)); tris.Add(new Vertex(p11, n11)); tris.Add(new Vertex(p10, n10));
            tris.Add(new Vertex(p00, n00)); tris.Add(new Vertex(p01, n01)); tris.Add(new Vertex(p11, n11));

            var q00 = BotCap(i, j, out var m00); var q10 = BotCap(i + 1, j, out var m10);
            var q01 = BotCap(i, j + 1, out var m01); var q11 = BotCap(i + 1, j + 1, out var m11);
            tris.Add(new Vertex(q00, m00)); tris.Add(new Vertex(q10, m10)); tris.Add(new Vertex(q11, m11));
            tris.Add(new Vertex(q00, m00)); tris.Add(new Vertex(q11, m11)); tris.Add(new Vertex(q01, m01));
        }
        return tris.ToArray();
    }

    /// <summary>Square pyramid: full base, centered apex. Side normals are exact 45°.</summary>
    public static Vertex[] Pyramid()
    {
        var tris = new List<Vertex>();
        var b0 = new Vector3(-0.5f, -0.5f, -0.5f); var b1 = new Vector3(0.5f, -0.5f, -0.5f);
        var b2 = new Vector3(0.5f, -0.5f, 0.5f); var b3 = new Vector3(-0.5f, -0.5f, 0.5f);
        var p = new Vector3(0, 0.5f, 0);
        var back = Vector3.Normalize(new Vector3(0, 1, -1));
        var front = Vector3.Normalize(new Vector3(0, 1, 1));
        var left = Vector3.Normalize(new Vector3(-1, 1, 0));
        var right = Vector3.Normalize(new Vector3(1, 1, 0));
        Quad(tris, b0, b1, b2, b3, -Vector3.UnitY); // base
        Tri(tris, p, b1, b0, back);
        Tri(tris, p, b3, b2, front);
        Tri(tris, p, b0, b3, left);
        Tri(tris, p, b2, b1, right);
        return tris.ToArray();
    }

    /// <summary>Corner wedge: unit cube with the top-front-right corner cut off.
    /// Bottom/back/left stay full quads; the cut is one triangular hypotenuse.</summary>
    public static Vertex[] CornerWedge()
    {
        var tris = new List<Vertex>();
        var a = new Vector3(-0.5f, -0.5f, -0.5f); var b = new Vector3(0.5f, -0.5f, -0.5f);
        var c = new Vector3(0.5f, -0.5f, 0.5f); var d = new Vector3(-0.5f, -0.5f, 0.5f);
        var e = new Vector3(-0.5f, 0.5f, -0.5f); var f = new Vector3(0.5f, 0.5f, -0.5f);
        var g = new Vector3(-0.5f, 0.5f, 0.5f);
        var hyp = Vector3.Normalize(new Vector3(1, 1, 1));
        Quad(tris, a, b, c, d, -Vector3.UnitY); // bottom
        Tri(tris, a, f, b, -Vector3.UnitZ);     // back
        Tri(tris, a, e, f, -Vector3.UnitZ);
        Quad(tris, a, d, g, e, -Vector3.UnitX); // left
        Tri(tris, b, f, c, Vector3.UnitX);      // right triangle
        Tri(tris, d, c, g, Vector3.UnitZ);      // front triangle
        Tri(tris, e, g, f, Vector3.UnitY);      // top triangle
        Tri(tris, g, c, f, hyp);                // hypotenuse
        return tris.ToArray();
    }

    /// <summary>Staircase: non-overlapping slab rects tile the profile, so no
    /// coplanar faces z-fight. Ascends from the front (z=+.5) to the back.</summary>
    public static Vertex[] Stairs(int steps = 4)
    {
        var tris = new List<Vertex>();
        float n = Math.Max(steps, 1);
        static Vector3 V(float x, float y, float z) => new(x, y, z);
        for (int i = 0; i < n; i++)
        {
            float y0 = -0.5f + i / n, y1 = y0 + 1f / n;
            float zf = 0.5f - i / n; // front of this slab; slab runs back to z=-.5
            // Slab sides (tile the side profile exactly, no overlap).
            Quad(tris, V(-0.5f, y0, -0.5f), V(-0.5f, y0, zf), V(-0.5f, y1, zf), V(-0.5f, y1, -0.5f), -Vector3.UnitX);
            Quad(tris, V(0.5f, y0, zf), V(0.5f, y0, -0.5f), V(0.5f, y1, -0.5f), V(0.5f, y1, zf), Vector3.UnitX);
            // Riser (front face) and exposed tread (top) of this step.
            float zt = zf - 1f / n; // tread runs from the next step's front back to this front
            Quad(tris, V(-0.5f, y0, zf), V(0.5f, y0, zf), V(0.5f, y1, zf), V(-0.5f, y1, zf), Vector3.UnitZ);
            Quad(tris, V(-0.5f, y1, zf), V(0.5f, y1, zf), V(0.5f, y1, zt), V(-0.5f, y1, zt), Vector3.UnitY);
        }
        // Full back and bottom.
        Quad(tris, V(0.5f, -0.5f, -0.5f), V(-0.5f, -0.5f, -0.5f), V(-0.5f, 0.5f, -0.5f), V(0.5f, 0.5f, -0.5f), -Vector3.UnitZ);
        Quad(tris, V(-0.5f, -0.5f, -0.5f), V(0.5f, -0.5f, -0.5f), V(0.5f, -0.5f, 0.5f), V(-0.5f, -0.5f, 0.5f), -Vector3.UnitY);
        return tris.ToArray();
    }

    /// <summary>Axis-aligned box from min to max (same winding as Block()).</summary>
    private static void BoxAt(List<Vertex> dst, Vector3 min, Vector3 max)
    {
        var p0 = new Vector3(min.X, min.Y, min.Z); var p1 = new Vector3(max.X, min.Y, min.Z);
        var p2 = new Vector3(max.X, max.Y, min.Z); var p3 = new Vector3(min.X, max.Y, min.Z);
        var p4 = new Vector3(min.X, min.Y, max.Z); var p5 = new Vector3(max.X, min.Y, max.Z);
        var p6 = new Vector3(max.X, max.Y, max.Z); var p7 = new Vector3(min.X, max.Y, max.Z);
        Quad(dst, p4, p5, p6, p7, Vector3.UnitZ);
        Quad(dst, p1, p0, p3, p2, -Vector3.UnitZ);
        Quad(dst, p0, p4, p7, p3, -Vector3.UnitX);
        Quad(dst, p5, p1, p2, p6, Vector3.UnitX);
        Quad(dst, p7, p6, p2, p3, Vector3.UnitY);
        Quad(dst, p0, p1, p5, p4, -Vector3.UnitY);
    }

    /// <summary>Solid dome: spherical cap (apex +.5, base ring r=.5 at y=-.5).</summary>
    public static Vertex[] HalfBall(int rows = 12, int lon = 28)
    {
        // Sphere center (0,-.125,0), R=.625: rim cos phi = -.6 exactly.
        var tris = new List<Vertex>();
        var C = new Vector3(0, -0.125f, 0);
        const float R = 0.625f;
        float phiMax = MathF.Acos(-0.6f);
        Vector3 P(int i, int j)
        {
            float phi = i / (float)rows * phiMax;
            float a = j / (float)lon * MathF.PI * 2f;
            float sp = MathF.Sin(phi);
            return C + new Vector3(R * sp * MathF.Cos(a), R * MathF.Cos(phi), R * sp * MathF.Sin(a));
        }
        Vector3 Nrm(Vector3 p) => Vector3.Normalize(p - C); // exact sphere radial
        for (int i = 0; i < rows; i++)
        for (int j = 0; j < lon; j++)
        {
            var p00 = P(i, j); var p10 = P(i + 1, j);
            var p01 = P(i, j + 1); var p11 = P(i + 1, j + 1);
            tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p11, Nrm(p11))); tris.Add(new Vertex(p10, Nrm(p10)));
            tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p01, Nrm(p01))); tris.Add(new Vertex(p11, Nrm(p11)));
        }
        var bc = new Vector3(0, -0.5f, 0); // base disc
        for (int k = 0; k < lon; k++)
        {
            float a0 = k / (float)lon * MathF.PI * 2f;
            float a1 = (k + 1) / (float)lon * MathF.PI * 2f;
            var b0 = new Vector3(0.5f * MathF.Cos(a0), -0.5f, 0.5f * MathF.Sin(a0));
            var b1 = new Vector3(0.5f * MathF.Cos(a1), -0.5f, 0.5f * MathF.Sin(a1));
            Tri(tris, bc, b0, b1, -Vector3.UnitY);
        }
        return tris.ToArray();
    }

    /// <summary>Open tube: outer + inner walls with flat rim annuli.</summary>
    public static Vertex[] HollowCylinder(int seg = 28)
    {
        const float ro = 0.5f, ri = 0.3f;
        var tris = new List<Vertex>();
        Vector3 Out(float a, float y) => new(ro * MathF.Cos(a), y, ro * MathF.Sin(a));
        Vector3 In(float a, float y) => new(ri * MathF.Cos(a), y, ri * MathF.Sin(a));
        Vector3 Rad(float a) => new(MathF.Cos(a), 0, MathF.Sin(a));
        for (int k = 0; k < seg; k++)
        {
            float a0 = k / (float)seg * MathF.PI * 2f;
            float a1 = (k + 1) / (float)seg * MathF.PI * 2f;
            var b0 = Out(a0, -0.5f); var b1 = Out(a1, -0.5f);
            var t0 = Out(a0, 0.5f); var t1 = Out(a1, 0.5f);
            var n0 = Rad(a0); var n1 = Rad(a1);
            tris.Add(new Vertex(b0, n0)); tris.Add(new Vertex(t1, n1)); tris.Add(new Vertex(b1, n1));
            tris.Add(new Vertex(b0, n0)); tris.Add(new Vertex(t0, n0)); tris.Add(new Vertex(t1, n1));
            var c0 = In(a0, -0.5f); var c1 = In(a1, -0.5f);
            var d0 = In(a0, 0.5f); var d1 = In(a1, 0.5f);
            var m0 = -n0; var m1 = -n1;
            tris.Add(new Vertex(c0, m0)); tris.Add(new Vertex(c1, m1)); tris.Add(new Vertex(d1, m1));
            tris.Add(new Vertex(c0, m0)); tris.Add(new Vertex(d1, m1)); tris.Add(new Vertex(d0, m0));
            Quad(tris, t1, t0, d0, d1, Vector3.UnitY);   // top rim
            Quad(tris, b0, b1, c1, c0, -Vector3.UnitY); // bottom rim
        }
        return tris.ToArray();
    }

    /// <summary>True spherical bowl: outer dome + inner shell + flat rim ring.</summary>
    public static Vertex[] Bowl(int rows = 12, int lon = 28)
    {
        // Sphere center (0,+.125,0): outer R=.625 (rim r=.5 at y=+.5),
        // inner R=.535 (rim r=.3816 at y=+.5, pole at y=-.41).
        var tris = new List<Vertex>();
        var C = new Vector3(0, 0.125f, 0);
        const float R = 0.625f, Ri = 0.535f;
        void Shell(float radius, float phiStart, bool flip)
        {
            Vector3 P(int i, int j)
            {
                float phi = phiStart + i / (float)rows * (MathF.PI - phiStart);
                float a = j / (float)lon * MathF.PI * 2f;
                float sp = MathF.Sin(phi);
                return C + new Vector3(radius * sp * MathF.Cos(a), radius * MathF.Cos(phi), radius * sp * MathF.Sin(a));
            }
            Vector3 Nrm(Vector3 p) => Vector3.Normalize((p - C) * (flip ? -1f : 1f));
            for (int i = 0; i < rows; i++)
            for (int j = 0; j < lon; j++)
            {
                var p00 = P(i, j); var p10 = P(i + 1, j);
                var p01 = P(i, j + 1); var p11 = P(i + 1, j + 1);
                if (!flip)
                {
                    tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p11, Nrm(p11))); tris.Add(new Vertex(p10, Nrm(p10)));
                    tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p01, Nrm(p01))); tris.Add(new Vertex(p11, Nrm(p11)));
                }
                else
                {
                    tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p10, Nrm(p10))); tris.Add(new Vertex(p11, Nrm(p11)));
                    tris.Add(new Vertex(p00, Nrm(p00))); tris.Add(new Vertex(p11, Nrm(p11))); tris.Add(new Vertex(p01, Nrm(p01)));
                }
            }
        }
        Shell(R, MathF.Acos(0.6f), false);
        Shell(Ri, MathF.Acos(0.375f / Ri), true);
        float ri = MathF.Sqrt(Ri * Ri - 0.375f * 0.375f);
        for (int k = 0; k < lon; k++)
        {
            float a0 = k / (float)lon * MathF.PI * 2f;
            float a1 = (k + 1) / (float)lon * MathF.PI * 2f;
            var o0 = new Vector3(0.5f * MathF.Cos(a0), 0.5f, 0.5f * MathF.Sin(a0));
            var o1 = new Vector3(0.5f * MathF.Cos(a1), 0.5f, 0.5f * MathF.Sin(a1));
            var i0 = new Vector3(ri * MathF.Cos(a0), 0.5f, ri * MathF.Sin(a0));
            var i1 = new Vector3(ri * MathF.Cos(a1), 0.5f, ri * MathF.Sin(a1));
            Quad(tris, o1, o0, i0, i1, Vector3.UnitY);
        }
        return tris.ToArray();
    }

    /// <summary>Blocky arch: two legs + lintel (matches the physics compound exactly).</summary>
    public static Vertex[] Arch()
    {
        var tris = new List<Vertex>();
        BoxAt(tris, new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.15f, 0.5f, 0.5f)); // left leg
        BoxAt(tris, new Vector3(0.15f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f));   // right leg
        BoxAt(tris, new Vector3(-0.5f, 0.1f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f));    // lintel
        return tris.ToArray();
    }

    /// <summary>Six-sided prism with flat faces and caps.</summary>
    public static Vertex[] HexPrism()
    {
        const float r = 0.5f;
        var tris = new List<Vertex>();
        Vector3 Ring(float a, float y) => new(r * MathF.Cos(a), y, r * MathF.Sin(a));
        for (int k = 0; k < 6; k++)
        {
            float a0 = k / 6f * MathF.PI * 2f;
            float a1 = (k + 1) / 6f * MathF.PI * 2f;
            float am = (a0 + a1) / 2f;
            var n = new Vector3(MathF.Cos(am), 0, MathF.Sin(am));
            var b0 = Ring(a0, -0.5f); var b1 = Ring(a1, -0.5f);
            var t0 = Ring(a0, 0.5f); var t1 = Ring(a1, 0.5f);
            // Same triangle order as Cylinder() sides (a Quad() split here
            // bowties on the curved patch); flat face normal, not smooth.
            tris.Add(new Vertex(b0, n)); tris.Add(new Vertex(t1, n)); tris.Add(new Vertex(b1, n));
            tris.Add(new Vertex(b0, n)); tris.Add(new Vertex(t0, n)); tris.Add(new Vertex(t1, n));
            Tri(tris, new Vector3(0, 0.5f, 0), t1, t0, Vector3.UnitY);
            Tri(tris, new Vector3(0, -0.5f, 0), b0, b1, -Vector3.UnitY);
        }
        return tris.ToArray();
    }

    /// <summary>Cube frame: 12 edge beams (classic truss look).</summary>
    public static Vertex[] Truss(float half = 0.07f)
    {
        var tris = new List<Vertex>();
        const float e = 0.43f; // beam centerlines stay inside the unit box
        foreach (float y in new[] { -e, e })
        foreach (float z in new[] { -e, e })
            BoxAt(tris, new Vector3(-0.5f, y - half, z - half), new Vector3(0.5f, y + half, z + half));
        foreach (float x in new[] { -e, e })
        foreach (float z in new[] { -e, e })
            BoxAt(tris, new Vector3(x - half, -0.5f, z - half), new Vector3(x + half, 0.5f, z + half));
        foreach (float x in new[] { -e, e })
        foreach (float y in new[] { -e, e })
            BoxAt(tris, new Vector3(x - half, y - half, -0.5f), new Vector3(x + half, y + half, 0.5f));
        return tris.ToArray();
    }

    public static Vertex[] For(ShapeKind shape) => shape switch
    {
        ShapeKind.Killbrick => Block(),
        ShapeKind.Ball => Sphere(),
        ShapeKind.Cylinder => Cylinder(),
        ShapeKind.Wedge => Wedge(),
        ShapeKind.Cone => Cone(),
        ShapeKind.Torus => Torus(),
        ShapeKind.Capsule => Capsule(),
        ShapeKind.Pyramid => Pyramid(),
        ShapeKind.CornerWedge => CornerWedge(),
        ShapeKind.Stairs => Stairs(4),
        ShapeKind.HalfBall => HalfBall(),
        ShapeKind.HollowCylinder => HollowCylinder(),
        ShapeKind.Bowl => Bowl(),
        ShapeKind.Arch => Arch(),
        ShapeKind.HexPrism => HexPrism(),
        ShapeKind.Truss => Truss(),
        ShapeKind.RoundedBox => RoundedBox(),
        ShapeKind.None => Octahedron(),
        _ => Block(),
    };

    private static readonly Dictionary<ShapeKind, Vertex[]> _cache = new();

    /// <summary>Cached factory verts (picking/physics reuse; never mutate).</summary>
    public static Vertex[] Cached(ShapeKind shape)
    {
        if (!_cache.TryGetValue(shape, out var v))
        {
            v = For(shape);
            _cache[shape] = v;
        }
        return v;
    }

    /// <summary>Rounded-corner block (superellipsoid: e=1 is a sphere,
    /// smaller e is boxier; 0.4 reads as a soft blockhead). Implicit
    /// gradient normals; same CCW-outside winding as <see cref="Sphere"/>.</summary>
    public static Vertex[] RoundedBox(float e = 0.4f, int lat = 12, int lon = 16)
    {
        var tris = new List<Vertex>();
        static float Spow(float a, float e) => MathF.Sign(a) * MathF.Pow(MathF.Abs(a), e);
        Vector3 P(int i, int j)
        {
            float theta = i / (float)lat * MathF.PI;
            float phi = j / (float)lon * MathF.PI * 2f;
            float st = Spow(MathF.Sin(theta), e), ct = Spow(MathF.Cos(theta), e);
            return new Vector3(st * Spow(MathF.Cos(phi), e), ct, st * Spow(MathF.Sin(phi), e)) * 0.5f;
        }
        // Implicit f = |x|^k + |y|^k + |z|^k - 1, k = 2/e: gradient normals.
        static Vector3 Nrm(Vector3 p, float k)
        {
            var n = new Vector3(
                MathF.Sign(p.X) * MathF.Pow(MathF.Abs(p.X), k - 1f),
                MathF.Sign(p.Y) * MathF.Pow(MathF.Abs(p.Y), k - 1f),
                MathF.Sign(p.Z) * MathF.Pow(MathF.Abs(p.Z), k - 1f));
            return n.LengthSquared > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
        }
        float k = 2f / e;
        for (int i = 0; i < lat; i++)
        for (int j = 0; j < lon; j++)
        {
            var p00 = P(i, j); var p10 = P(i + 1, j);
            var p01 = P(i, j + 1); var p11 = P(i + 1, j + 1);
            tris.Add(new Vertex(p00, Nrm(p00, k))); tris.Add(new Vertex(p11, Nrm(p11, k))); tris.Add(new Vertex(p10, Nrm(p10, k)));
            tris.Add(new Vertex(p00, Nrm(p00, k))); tris.Add(new Vertex(p01, Nrm(p01, k))); tris.Add(new Vertex(p11, Nrm(p11, k)));
        }
        return tris.ToArray();
    }

    /// <summary>Small octahedron: editor glyph for light objects.</summary>
    public static Vertex[] Octahedron(float r = 0.35f)
    {
        var tris = new List<Vertex>();
        var px = new Vector3(r, 0, 0); var nx = new Vector3(-r, 0, 0);
        var py = new Vector3(0, r, 0); var ny = new Vector3(0, -r, 0);
        var pz = new Vector3(0, 0, r); var nz = new Vector3(0, 0, -r);
        void Face(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Tri(tris, a, b, c, n);
        }
        Face(py, pz, px); Face(py, nx, pz); Face(py, nz, nx); Face(py, px, nz);
        Face(ny, px, pz); Face(ny, pz, nx); Face(ny, nx, nz); Face(ny, nz, px);
        return tris.ToArray();
    }
}
