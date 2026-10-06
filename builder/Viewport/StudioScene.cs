using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.IO;
using Gdi = System.Drawing;
using GdiImaging = System.Drawing.Imaging;
using GdiText = System.Drawing.Text;
using GdiDrawing = System.Drawing.Drawing2D;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using StbImageSharp;

namespace builder.Viewport;

/// <summary>Which manipulator the viewport draws for the selection.</summary>
public enum GizmoKind
{
    None,
    Move,
    Scale,
    Rotate,
}

/// <summary>Lit multi-object scene (Blinn-Phong + rim + fog), one shared cube mesh.</summary>
public sealed class StudioScene : IDisposable
{
    private const string DecalVertexSrc = @"#version 460 core
layout(location=0) in vec3 vPosition;
layout(location=1) in vec2 vUv;
layout(location=2) in vec4 vA;
layout(location=3) in vec2 vB;
uniform mat4 uVP;
out vec2 fUv;
out vec4 fA;
out vec2 fB;
void main(){ fUv=vUv; fA=vA; fB=vB; gl_Position=uVP*vec4(vPosition,1.0); }";
    private const string DecalFragmentSrc = @"#version 460 core
in vec2 fUv;
in vec4 fA;
in vec2 fB;
uniform sampler2D uImage;
uniform float uContrast;
out vec4 oColor;
void main(){
 vec2 px=1.0/vec2(textureSize(uImage,0));
 vec2 tuv=fUv*vec2(fA.z,fA.w)+fB;
 vec4 c=texture(uImage,tuv);
 if(fA.y>0.001){
  // 9-tap Gaussian (center + cross + diagonals): soft without box artifacts.
  // Mipmapped sampler keeps it stable at distance. Blur taps stay on the
  // unclamped UV like before (only the center honors tiling).
  vec2 d=px*fA.y*4.0;
  vec4 acc=c*0.20;
  acc+=texture(uImage,fUv+vec2(d.x,0))*0.12;
  acc+=texture(uImage,fUv-vec2(d.x,0))*0.12;
  acc+=texture(uImage,fUv+vec2(0,d.y))*0.12;
  acc+=texture(uImage,fUv-vec2(0,d.y))*0.12;
  acc+=texture(uImage,fUv+d)*0.08;
  acc+=texture(uImage,fUv-d)*0.08;
  acc+=texture(uImage,fUv+vec2(d.x,-d.y))*0.08;
  acc+=texture(uImage,fUv+vec2(-d.x,d.y))*0.08;
  c=acc;
 }
 c.a*=1.0-fA.x;
 if(c.a<0.004) discard;
 c.rgb=(c.rgb-vec3(0.5))*uContrast+vec3(0.5);
 oColor=c;
}";
    private const string VertexSrc = @"#version 460 core
in vec3 vPosition;
in vec3 vNormal;
in vec3 vColor;
uniform mat4 uMVP;
uniform mat4 uModel;
uniform mat3 uNormalMat;
uniform vec4 uClipPlane; // reflection clip (disabled in the main pass: write ignored)
out vec3 fWorldPos;
out vec3 fNormal;
out vec3 fLocalPos;
out vec3 fLocalNrm;
out vec3 fColor;
void main()
{
    vec4 wp = uModel * vec4(vPosition, 1.0);
    fWorldPos = wp.xyz;
    fNormal = uNormalMat * vNormal;
    fColor = vColor;
    // Unit-space position scaled to studs (column lengths of the model matrix),
    // so material textures tile per stud like Roblox instead of stretching.
    vec3 msz = vec3(length(uModel[0].xyz), length(uModel[1].xyz), length(uModel[2].xyz));
    fLocalPos = vPosition * msz;
    fLocalNrm = vNormal;
    gl_ClipDistance[0] = dot(wp.xyz, uClipPlane.xyz) + uClipPlane.w;
    gl_Position = uMVP * vec4(vPosition, 1.0);
}";

    private const string FragmentSrc = @"#version 460 core
in vec3 fWorldPos;
in vec3 fNormal;
in vec3 fLocalPos;
in vec3 fLocalNrm;
in vec3 fColor;
uniform vec3 uColor;
uniform sampler2D uTex;
uniform float uUseTex;
uniform float uTiling;
uniform vec3 uCamPos;
uniform float uPulse;
uniform vec3 uFogColor;
uniform float uFogDensity;
uniform float uContrast;uniform float uMetallic;
uniform float uShininess;
uniform float uOpacity;
uniform float uEmissive;
uniform sampler2DShadow uShadowMap;
uniform mat4 uLightVP;
uniform float uShadowsOn;
uniform float uShadowSize;
uniform float uShadowDistance;
uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform float uSunI;
uniform float uAmbient;
uniform float uReflectance;
uniform float uGloss; // mesh gloss gate: 1 everywhere, driven by Reflection on meshes
uniform float uTime; // seconds (water ripple)
uniform float uWater; // 1 on water volumes: animated ripple normals
uniform float uReflOn; // 1 when a planar reflection is bound for this water
uniform sampler2D uReflTex; // mirrored scene (half res), sampled projectively
uniform mat4 uReflVP; // mirrored view-projection of this water's plane
// Dynamic lights (Roblox-style points), sun-independent.
uniform int uPtCount;
uniform vec3 uPtPos[16];
uniform vec3 uPtColor[16];
uniform vec3 uPtParams[16]; // x = range
out vec4 oColor;

// How far from the camera shadows render: full strength nearby, fading to
// fully lit at uShadowDistance (Settings > Shadow distance).
float ShadowRange(vec3 wp)
{
    float camDist = length(uCamPos - wp);
    return 1.0 - smoothstep(uShadowDistance * 0.75, uShadowDistance, camDist);
}

// Tight core stays crisp; a wide ring blends in only where the core is
// fractional (i.e. on shadow edges), so interiors stay deep and lit stays lit.
float ShadowFactor(vec3 wp, vec3 n)
{
    vec4 lp = uLightVP * vec4(wp + n * 0.02, 1.0);
    vec3 c = lp.xyz / lp.w * 0.5 + 0.5;
    if (c.x < 0.0 || c.x > 1.0 || c.y < 0.0 || c.y > 1.0 || c.z > 1.0) return 1.0;
    // Receiver bias in world units, slope-scaled: flat contacts (a part sunk
    // a bit into another) keep their shadows, while grazing angles ramp
    // the bias up so they don't acne. Ortho depth is linear over the light
    // range [near, far] = [1, 80 + 4*D], so a fixed NDC bias used to eat
    // ~0.4 studs of contact (over a stud at long shadow distances).
    float zRange = 79.0 + 4.0 * uShadowDistance;
    float slope = max(abs(dFdx(c.z)), abs(dFdy(c.z))) * zRange;
    float depth = c.z - (0.03 + slope * 2.0) / zRange;
    // Small maps (Lowest/Low): one center tap. Cheap, and soft enough already.
    float core = texture(uShadowMap, vec3(c.xy, depth));
    if (uShadowSize > 512.5)
    {
        vec2 texel = vec2(1.0 / uShadowSize);
        core = 0.0;
        core += texture(uShadowMap, vec3(c.xy + vec2(-0.5, -0.5) * texel, depth));
        core += texture(uShadowMap, vec3(c.xy + vec2( 0.5, -0.5) * texel, depth));
        core += texture(uShadowMap, vec3(c.xy + vec2(-0.5,  0.5) * texel, depth));
        core += texture(uShadowMap, vec3(c.xy + vec2( 0.5,  0.5) * texel, depth));
        core /= 4.0;
        float edge = core * (1.0 - core) * 4.0; // 0 in flat areas, 1 mid-transition
        if (edge > 0.001)
        {
            float wide = 0.0;
            wide += texture(uShadowMap, vec3(c.xy + vec2(-2.0, -2.0) * texel, depth));
            wide += texture(uShadowMap, vec3(c.xy + vec2( 2.0, -2.0) * texel, depth));
            wide += texture(uShadowMap, vec3(c.xy + vec2(-2.0,  2.0) * texel, depth));
            wide += texture(uShadowMap, vec3(c.xy + vec2( 2.0,  2.0) * texel, depth));
            core = mix(core, wide / 4.0, clamp(edge * 1.5, 0.0, 1.0));
        }
    }
    return mix(1.0, core, uShadowsOn * ShadowRange(wp));
}
void main()
{
    vec3 N = normalize(fNormal);
    vec3 V = normalize(uCamPos - fWorldPos);
    vec3 L = normalize(uSunDir); // key sun (Lighting > Sun > Direction)

    // Water ripple: two scrolling sine octaves tilt the normal (drives the
    // sun specular, rim and reflections below). rippleUv distorts the
    // planar-reflection sample so reflections shimmer.
    vec2 rippleUv = vec2(0.0);
    if (uWater > 0.5) {
        vec2 rp = fWorldPos.xz * 1.35;
        float t = uTime * 1.7;
        float w1 = sin(rp.x * 2.1 + t) * sin(rp.y * 1.7 - t * 1.3);
        float w2 = sin((rp.x + rp.y) * 3.1 - t * 2.2) + 0.5 * sin(rp.y * 5.3 + t * 1.1);
        rippleUv = vec2(w1, w2) * 0.035;
        N = normalize(N + vec3(w1 * 0.16, 0.0, w2 * 0.16));
    }

    // Material texture: project local stud coords on the dominant face axis
    // (one tile per stud, repeat-wrapped) and multiply with the part color.
    vec3 albedo = uColor * fColor;
    if (uUseTex > 0.5) {
        vec3 lan = abs(normalize(fLocalNrm));
        vec2 luv = lan.x > 0.5 ? fLocalPos.zy : (lan.y > 0.5 ? fLocalPos.xz : fLocalPos.xy);
        albedo *= texture(uTex, luv * uTiling).rgb;
    }

    float sun = ShadowFactor(fWorldPos, N);
    float diff = max(dot(N, L), 0.0);
    vec3 H = normalize(L + V);
    // Metals: tinted specular, suppressed diffuse. Plastics: white specular.
    vec3 specTint = mix(vec3(1.0), albedo, uMetallic);
    vec3 diffTint = albedo * (1.0 - uMetallic * 0.75);
    float spec = pow(max(dot(N, H), 0.0), uShininess) * mix(0.35, 1.2, uMetallic) * uGloss;

    float hemi = 0.5 + 0.5 * N.y; // sky/ground ambient tint
    vec3 ambient = mix(vec3(0.22, 0.22, 0.25), vec3(0.42, 0.44, 0.48), hemi);

    float rim = pow(1.0 - max(dot(N, V), 0.0), 3.0) * (0.30 + 0.40 * uMetallic) * uGloss;

    vec3 lit = diffTint * (ambient * uAmbient + diff * uSunColor * uSunI * sun)
             + specTint * spec * uSunI * sun
             + vec3(0.45, 0.65, 1.0) * rim;

    // Faked reflection (no env map): sky gradient + sun glint in the mirror dir.
    vec3 R = reflect(-V, N);
    vec3 env = mix(uFogColor * 0.55, vec3(0.55, 0.70, 0.92), clamp(R.y * 0.5 + 0.5, 0.0, 1.0));
    env += vec3(1.0, 0.96, 0.90) * pow(max(dot(R, L), 0.0), 24.0) * sun;
    lit += uReflectance * env;

    // Planar reflection on water: project into the mirrored scene texture
    // (ripple-distorted), fresnel-weighted so steep views stay watery.
    if (uWater > 0.5 && uReflOn > 0.5) {
        vec4 rproj = uReflVP * vec4(fWorldPos, 1.0);
        vec2 ruv = rproj.xy / max(rproj.w, 1e-4) * 0.5 + 0.5 + rippleUv;
        vec3 refl = texture(uReflTex, clamp(ruv, vec2(0.001), vec2(0.999))).rgb;
        float fres = 0.25 + 0.75 * pow(1.0 - max(dot(N, V), 0.0), 3.0);
        lit = mix(lit, refl * (0.75 + 0.25 * uSunI), clamp(fres, 0.0, 1.0));
    }
    lit += albedo * (uEmissive + uPulse); // neon glow + selection glow

    // Point lights: omnidirectional, distance-faded. The count guard lets
    // lightless scenes skip the unrolled loop entirely on weak GPUs.
    if (uPtCount > 0) {
    for (int i = 0; i < 16; i++)
    {
        if (i >= uPtCount) break;
        vec3 Ld = uPtPos[i] - fWorldPos;
        float dist = length(Ld);
        float range = uPtParams[i].x;
        if (dist > range) continue;
        Ld /= max(dist, 1e-4);
        float att = pow(clamp(1.0 - dist / range, 0.0, 1.0), 2.0);
        float pdiff = max(dot(N, Ld), 0.0);
        vec3 pH = normalize(Ld + V);
        float pspec = pow(max(dot(N, pH), 0.0), uShininess);
        lit += (diffTint * pdiff + specTint * pspec * 0.6 * uGloss) * uPtColor[i] * att;
    }
    }

    float dist = length(uCamPos - fWorldPos);
    float fog = 1.0 - exp(-dist * dist * uFogDensity * uFogDensity);
    lit = mix(lit, uFogColor, clamp(fog, 0.0, 1.0));

    // Display contrast around mid gray (Settings > Viewport > Contrast).
    lit = (lit - vec3(0.5)) * uContrast + vec3(0.5);

    oColor = vec4(lit, uOpacity);
}";
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Color;
        public Vertex(Vector3 p, Vector3 n) { Position = p; Normal = n; Color = Vector3.One; }
        public Vertex(Vector3 p, Vector3 n, Vector3 c) { Position = p; Normal = n; Color = c; }
    }

    private sealed class Mesh
    {
        public int Vao;
        public int Vbo;
        public int Count;
    }

    /// <summary>One live particle: CPU-integrated, uploaded as a point sprite.</summary>
    private struct Particle
    {
        public Vector3 Pos;
        public Vector3 Vel;
        public float Age;
        public float Life;
        public float Size;
        public float Gravity;
        public Vector3 Color;
        public float Seed;    // turbulence phase
        public float Ramp;    // 0 = tint fade, 1 = fire color ramp
        public float Growth;  // size multiplier gained over life
        public bool Additive; // glow blending (fire, sparkles)
    }

    /// <summary>Pale horizon haze, also the clear color (Lighting &gt; Atmosphere &gt; Fog Color).</summary>
    public Vector3 FogColor { get; set; } = new(0.50f, 0.62f, 0.80f);

    /// <summary>Exponential fog density. Tiny values; 0 disables.</summary>
    public float FogDensity { get; set; } = 0.008f;

    /// <summary>Display contrast around mid gray (Settings &gt; Viewport &gt; Contrast). 1 = off.</summary>
    public float Contrast { get; set; } = 1.15f;

    private const string SkyVertexSrc = @"#version 460 core
in vec3 vPosition;
uniform mat4 uMVP;
out vec3 fDir;
void main()
{
    fDir = vPosition;
    gl_Position = uMVP * vec4(vPosition, 1.0);
}";

    private const string SkyFragmentSrc = @"#version 460 core
in vec3 fDir;
uniform vec3 uSunDir;
uniform vec3 uSunTint;
uniform float uSunI;
uniform float uHaze;
uniform vec3 uMoonDir;
uniform float uTime;
uniform float uContrast;
uniform vec3 uTint;
uniform float uStarAmt;
uniform float uCloudAmt;
uniform float uSunSize;
uniform float uStripes;
uniform float uStyle;
out vec4 oColor;

const float PI = 3.14159265;

// Compact single-scattering analytic sky: wavelength-weighted Rayleigh for
// blue, forward-lobed Mie (Henyey-Greenstein) for haze and sun glow, ozone
// absorption so horizons go pale instead of green. Layered on top: a drifting
// procedural cloud deck, night stars, a moon, and a dusk afterglow band.
// All direction-based, so it needs no textures and works at any scene scale.
float hash21(vec2 p)
{
    p = fract(p * vec2(234.34, 435.345));
    p += dot(p, p + 34.23);
    return fract(p.x * p.y);
}

float vnoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), u.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int k = 0; k < 5; k++)
    {
        v += a * vnoise(p);
        p = p * 2.03 + vec2(17.3, 9.1);
        a *= 0.5;
    }
    return v;
}

// Flat Roblox-style gradient for the Clear Blue preset (uStyle 2): pale
// periwinkle zenith melting to a near-white horizon collar, muted blue-gray
// below. No sun disk, clouds or stars; dims to navy at night via nightF.
vec3 SoftSky(vec3 d, float nightF)
{
    vec3 zen = vec3(0.46, 0.60, 0.89) * uTint;
    vec3 hor = vec3(0.93, 0.955, 0.98) * uTint;
    vec3 gnd = vec3(0.58, 0.67, 0.78) * uTint;
    // One continuous ramp: both sides take off from the horizon value with
    // zero slope (smoothstep), so the sky melts through the line instead of
    // creasing. The glow collar is symmetric, identical just above/below.
    vec3 day;
    if (d.y >= 0.0)
        day = mix(hor, zen, smoothstep(0.0, 1.0, pow(d.y, 0.75)));
    else
        day = mix(hor, gnd, smoothstep(0.0, 0.75, -d.y));
    day += vec3(1.0) * exp(-abs(d.y) * 9.0) * 0.08;
    // Soft stylized sun: warm disk with a gentle halo, day only. Follows
    // the scene sun (uSunDir) and honors the preset sun-size slider.
    float cosTheta = dot(d, normalize(uSunDir));
    float sunAng = acos(clamp(cosTheta, -1.0, 1.0));
    float diskR = 0.030 * uSunSize;
    float disk = smoothstep(diskR, diskR * 0.6, sunAng);
    float halo = pow(max(cosTheta, 0.0), 300.0) * 0.5 + pow(max(cosTheta, 0.0), 24.0) * 0.10;
    day += vec3(1.0, 0.97, 0.90) * (disk * 1.1 + halo) * (1.0 - nightF);
    vec3 night = mix(vec3(0.030, 0.050, 0.105), vec3(0.020, 0.032, 0.070), clamp(-d.y, 0.0, 1.0));
    return mix(day, night, nightF * 0.94);
}

void main()
{
    vec3 d = normalize(fDir);
    vec3 sunDir = normalize(uSunDir); // matches key light
    vec3 moonDir = normalize(uMoonDir);

    // 0 by day, 1 deep at night (mirrors the C# day curve driving uSunI).
    float nightF = 1.0 - clamp((uSunI - 0.06) / 0.94, 0.0, 1.0);

    const vec3 betaR = vec3(0.046, 0.108, 0.265);  // Rayleigh 1/lambda^4, zenith depths
    const vec3 betaO3 = vec3(0.006, 0.014, 0.003); // ozone Chappuis band
    vec3 beta = betaR + vec3(uHaze) + betaO3;

    float sunI = 22.0 * uSunI;
    const float g = 0.76; // Mie asymmetry (forward scattering)

    // Plane-parallel optical depth: ~1 overhead, deepening toward horizon.
    float h = max(d.y, 0.02);
    float viewDepth = min(1.0 / h, 30.0);
    float cosTheta = dot(d, sunDir);

    // Sunlight reddened on its way down (longer path at low sun).
    float sunAirMass = 1.0 / max(sunDir.y, 0.05);
    vec3 sunAtten = exp(-beta * sunAirMass);

    // Closed-form single-scatter integral along the view ray.
    vec3 transmittance = exp(-beta * viewDepth);
    vec3 scatterIntegral = (vec3(1.0) - transmittance) / beta;

    float phaseR = 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
    float denom = max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4);
    float phaseM = (1.0 - g * g) / (4.0 * PI * pow(denom, 1.5));

    vec3 sky = sunI * uSunTint * uTint * (phaseR * betaR + phaseM * uHaze) * scatterIntegral * sunAtten;

    // Sun disk with soft limb + tight corona. uSunSize scales the disk;
    // uStripes cuts synthwave bands across it.
    float sunAng = acos(clamp(cosTheta, -1.0, 1.0));
    float diskR = 0.012 * uSunSize;
    float disk = smoothstep(diskR, diskR * 0.55, sunAng);
    float bands = mix(1.0, smoothstep(0.0, 0.45, fract(d.y * 160.0)), uStripes * step(sunAng, diskR * 1.5));
    sky += uSunTint * disk * bands * 4.0;
    sky += uSunTint * pow(max(cosTheta, 0.0), 800.0 / max(uSunSize * uSunSize, 0.25)) * 0.6;

    // Stars: one sparkle per hash cell, twinkling, out only at night.
    vec2 sc = d.xz / (d.y + 0.35) * 90.0;
    vec2 cell = floor(sc);
    vec2 cpos = fract(sc) - 0.5;
    float sh = hash21(cell);
    vec2 soff = vec2(hash21(cell + 7.1), hash21(cell + 3.7)) - 0.5;
    float star = (1.0 - smoothstep(0.0, 0.10, length(cpos - soff * 0.7))) * step(0.80, sh);
    float twinkle = 0.6 + 0.4 * sin(uTime * (2.0 + sh * 4.0) + sh * 40.0);
    sky += vec3(0.85, 0.90, 1.0) * star * twinkle * nightF * uStarAmt
         * smoothstep(0.0, 0.15, d.y) * (0.35 + 0.65 * sh);

    // Moon disk + halo, riding opposite the sun (full and high at midnight).
    float cosM = dot(d, moonDir);
    float mdisk = smoothstep(0.99988, 0.99996, cosM);
    float mhalo = pow(max(cosM, 0.0), 600.0) * 0.35 + pow(max(cosM, 0.0), 60.0) * 0.08;
    sky += vec3(0.92, 0.95, 1.0) * (mdisk * 1.6 + mhalo) * nightF;

    // Warm afterglow hugging the horizon around the sun while it is low.
    float lowSun = 1.0 - smoothstep(0.02, 0.45, sunDir.y);
    vec2 dxz = d.xz + vec2(1e-5, 0.0);
    vec2 sxz = sunDir.xz + vec2(1e-5, 0.0);
    float around = pow(max(dot(dxz / length(dxz), sxz / length(sxz)), 0.0), 3.0);
    float band = exp(-max(d.y, 0.0) * 7.0);
    sky += uSunTint * around * band * lowSun * 0.35 * (1.0 - nightF * 0.5);

    // Cloud deck: fbm density on an imaginary plane, drifting with time.
    // Bellies stay gray, tops catch the sun (pink at dusk), nights go dark.
    vec2 cuv = d.xz / (abs(d.y) + 0.12) * 1.6 + vec2(uTime * 0.008, uTime * 0.003);
    float dens = fbm(cuv) * 0.65 + fbm(cuv * 2.7 + vec2(4.7, 1.3)) * 0.35;
    float cl = smoothstep(0.80 - uCloudAmt * 0.28, 0.80, dens);
    float cloudA = cl * smoothstep(0.015, 0.16, d.y);
    float sunAmt = clamp(dot(d, sunDir) * 0.5 + 0.5, 0.0, 1.0);
    vec3 cloudDay = mix(vec3(0.60, 0.63, 0.68), vec3(1.05, 1.0, 0.95), pow(sunAmt, 2.5));
    cloudDay = mix(cloudDay, uSunTint * 1.1, pow(max(cosTheta, 0.0), 5.0) * 0.55 * lowSun);
    cloudDay *= 0.12 + 0.88 * clamp(uSunI * 1.6, 0.0, 1.0);
    vec3 cloudNight = vec3(0.055, 0.075, 0.13)
        * (0.4 + 0.6 * pow(max(dot(d, moonDir) * 0.5 + 0.5, 0.0), 2.0));
    sky = mix(sky, mix(cloudDay, cloudNight, nightF), cloudA);

    // Below the horizon: melt into ground haze, darker at night.
    vec3 ground = vec3(0.11, 0.17, 0.30) * uTint * (1.0 - nightF * 0.75);
    sky = mix(sky, ground, smoothstep(0.0, 0.2, -d.y));

    // Soft-gradient look (Clear Blue, uStyle 2): replace the whole analytic
    // result with the flat reference ramp; contrast + dither still apply.
    if (uStyle > 1.5)
        sky = SoftSky(d, nightF);

    // Display contrast around mid gray (Settings > Viewport > Contrast).
    sky = (sky - vec3(0.5)) * uContrast + vec3(0.5);

    // Dither: one LSB of triangular noise kills fullscreen color banding.
    float dg = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
    oColor = vec4(sky + (dg - 0.5) * (1.5 / 255.0), 1.0);
}";

    /// <summary>Skybox half-size. Corners (~95) stay well inside the far plane (500).</summary>
    private const float SkyHalf = 55f;

    private static readonly Vector3 DefaultSun = Vector3.Normalize(new Vector3(0.5f, 0.8f, 0.6f));

    private Vector3 _sunDirection = DefaultSun;

    /// <summary>Key sun direction (Lighting &gt; Sun &gt; Direction). Retargets light + sky + shadows.</summary>
    public Vector3 SunDirection
    {
        get => _sunDirection;
        set
        {
            if (value.LengthSquared > 1e-8f) _sunDirection = Vector3.Normalize(value);
            InvalidateShadows(); // light space moved
        }
    }

    /// <summary>Sunlight tint for parts and sky (Lighting &gt; Sun &gt; Color).</summary>
    public Vector3 SunColor { get; set; } = new(1f, 0.97f, 0.92f);

    /// <summary>Sunlight multiplier 0-2 (Lighting &gt; Sun &gt; Intensity).</summary>
    public float SunIntensity { get; set; } = 1f;

    /// <summary>Clock time 0-24h driving sun, sky and ambient. Set via <see cref="SetTimeOfDay"/>.</summary>
    public float TimeOfDay { get; private set; } = 12f;

    /// <summary>Moon direction for the night sky (opposite the sun). Set via <see cref="SetTimeOfDay"/>.</summary>
    public Vector3 MoonDirection { get; private set; } = Vector3.Normalize(new Vector3(-0.5f, 0.8f, -0.6f));

    /// <summary>
    /// Drive sun direction/color/intensity and ambient from a 24h clock: rise ~6h,
    /// noon peak, set ~18h, dim blue night. Pure math (no GL): safe headless.
    /// </summary>
    public void SetTimeOfDay(float t)
    {
        t = Math.Clamp(t, 0f, 24f);
        TimeOfDay = t;
        float sine = MathF.Sin((t - 6f) / 12f * MathF.PI); // 1 noon, 0 at 6h/18h, -1 midnight
        float day = Math.Clamp(sine, 0f, 1f);
        float el = Math.Max(sine, 0.06f); // never below the horizon (no under-lighting)
        float ce = MathF.Sqrt(Math.Max(1f - el * el, 0.01f));
        float az = t / 24f * MathF.PI * 2f;
        SunDirection = new Vector3(MathF.Cos(az) * ce, el, MathF.Sin(az) * ce);
        float duskW = Math.Clamp(1f - day * 2.5f, 0f, 1f) * (sine > -0.08f ? 1f : 0f);
        var dayCol = new Vector3(1f, 0.97f, 0.92f);
        var duskCol = new Vector3(1f, 0.55f, 0.30f);
        var nightCol = new Vector3(0.45f, 0.60f, 1f);
        SunColor = Vector3.Lerp(Vector3.Lerp(nightCol, dayCol, day), duskCol, duskW);
        SunIntensity = 0.06f + day * 0.75f; // moonlight floor at night, softer noon
        AmbientBoost = 0.22f + day * 0.78f;
        // Moon rides opposite the sun: full and high at midnight, gone by day.
        float mEl = Math.Clamp(-sine, 0.08f, 1f);
        float mAz = az + MathF.PI;
        float mce = MathF.Sqrt(Math.Max(1f - mEl * mEl, 0.01f));
        MoonDirection = new Vector3(MathF.Cos(mAz) * mce, mEl, MathF.Sin(mAz) * mce);
        InvalidateShadows(); // sun moved
    }

    /// <summary>Sky/ground ambient multiplier 0-2 (Lighting &gt; Ambient).</summary>
    public float AmbientBoost { get; set; } = 1f;

    /// <summary>Sky haze (Mie amount) 0-0.05 (Lighting &gt; Atmosphere &gt; Haze).</summary>
    public float SkyHaze { get; set; } = 0.005f;

    /// <summary>Sky scattering tint multiplier, white = natural (sky preset themes).</summary>
    public Vector3 SkyTint { get; set; } = new(0.8f, 0.9f, 1.1f);
    /// <summary>Star visibility 0-2.5, 0 = none (sky preset themes).</summary>
    public float StarAmount { get; set; } = 0f;
    /// <summary>Cloud coverage 0-2: 0 = clear, 1 = natural, 2 = overcast (sky preset themes).</summary>
    public float CloudAmount { get; set; } = 0f;
    /// <summary>Sun disk size 0.5-3, 1 = natural (sky preset themes).</summary>
    public float SunSize { get; set; } = 1f;
    /// <summary>Synthwave striped sun (sky preset themes).</summary>
    public bool SunStripes { get; set; }
    /// <summary>Sky renderer: 0 = realistic analytic, 1 = flat cartoon (dormant),
    /// 2 = soft Roblox-style gradient (sky presets). Default matches the
    /// default Clear Blue preset.</summary>
    public int SkyStyle { get; set; } = 2;

    private const string DepthVertexSrc = @"#version 460 core
layout(location = 0) in vec3 vPosition;
uniform mat4 uLightMVP;
void main()
{
    gl_Position = uLightMVP * vec4(vPosition, 1.0);
}";

    private const string DepthFragmentSrc = @"#version 460 core
void main()
{
}";

    private const int ShadowSize = 1024;

    /// <summary>Shadow frustum half-extent in studs (Settings &gt; Shadow distance).</summary>
    public float ShadowDistance { get; set; } = 20f;

    /// <summary>Active shadow map resolution. Change via <see cref="RequestShadowSize"/>.</summary>
    public int ShadowMapSize { get; private set; } = ShadowSize;
    private int _pendingShadowSize = ShadowSize;

    /// <summary>Queue a shadow map resize (256/512/1024/2048/4096); applied on the next frame.</summary>
    public void RequestShadowSize(int size)
    {
        _pendingShadowSize = size is 256 or 512 or 1024 or 2048 or 4096 ? size : ShadowSize;
    }

    /// <summary>
    /// Effective map size: exactly what the quality setting asks (snapped up to
    /// a power of two, clamped 256..4096). Density degrades with distance, like
    /// every game: picking Lowest always means a cheap 256px map, no matter
    /// the shadow distance.
    /// </summary>
    private int WantedShadowSize()
    {
        long want = Math.Clamp((long)_pendingShadowSize, 256, 4096);
        int p = 256;
        while (p < want) p <<= 1;
        return Math.Min(p, 4096);
    }

    private const float FovDefault = 45f;

    /// <summary>Vertical field of view in degrees (lives in render, pick, gizmo math).</summary>
    public float FovDeg { get; set; } = FovDefault;

    // ---------- move gizmo ----------
    private int _gizmoProgram;
    private int _gizmoMvpLoc = -1;
    private int _gizmoColorLoc = -1;
    private readonly int[] _gizmoVaos = new int[3];
    private readonly int[] _gizmoVbos = new int[3];
    private readonly uint[] _gizmoCounts = new uint[3];
    private readonly int[] _scaleVaos = new int[3];
    private readonly int[] _scaleVbos = new int[3];
    private readonly uint[] _scaleCounts = new uint[3];
    private readonly int[] _ringVaos = new int[3];
    private readonly int[] _ringVbos = new int[3];
    private readonly uint[] _ringCounts = new uint[3];
    // Velocity direction arrow: single +Y mesh, rotated to the part's direction at draw.
    private int _velVao;
    private int _velVbo;
    private uint _velCount;

    // Selection/hover outline: one unit-cube wire box, scaled per part at draw.
    private int _boxVao;
    private int _boxVbo;
    private uint _boxCount;
    // Mesh silhouette contours: dynamic line VBO reused by every outlined mesh.
    private int _silVao;
    private int _silVbo;
    /// <summary>Part under the cursor (yellow wire box). Set by the UI; null clears.</summary>
    public SceneObject? HoverObject { get; set; }

    /// <summary>Selected waypoint ball (mover + index) for dragging. -1 = none.</summary>
    public SceneObject? SelWaypointObj { get; set; }
    public int SelWaypointIndex { get; set; } = -1;
    /// <summary>Waypoint ball under the free cursor (hover outline). -1 = none.</summary>
    public SceneObject? HoverWaypointObj { get; set; }
    public int HoverWaypointIndex { get; set; } = -1;

    /// <summary>Waypoint ball under the cursor (mover, index) or (null, -1).
    /// Grab radius is ~12px at ball depth (never smaller than the ball itself).</summary>
    public (SceneObject? Obj, int Index) PickWaypoint(float mx, float my, float w, float h)
    {
        if (w <= 0 || h <= 0 || PhysicsDriven) return (null, -1);
        GetPickRay(mx, my, w, h, out var ro, out var rd);
        float tanHalf = MathF.Tan(MathHelper.DegreesToRadians(FovDeg / 2f));
        SceneObject? best = null;
        int bestIdx = -1;
        float bestT = float.MaxValue;
        foreach (var o in Objects)
        {
            if (!o.IsMovingPlatform || o.Hidden || o.Waypoints.Count == 0) continue;
            for (int i = 0; i < o.Waypoints.Count; i++)
            {
                var c = o.Waypoints[i];
                float bd = Math.Max((c - Position).Length, 0.001f);
                float r = Math.Max(0.18f, 12f * bd * 2f * tanHalf / h);
                var oc = ro - c;
                float b = Vector3.Dot(oc, rd);
                float cc = oc.LengthSquared - r * r;
                float disc = b * b - cc;
                if (disc < 0f) continue;
                float t = -b - MathF.Sqrt(disc);
                if (t < 0f)
                {
                    if (cc > 0f) continue; // behind the camera
                    t = 0f; // camera inside the ball: grab it
                }
                if (t < bestT) { bestT = t; best = o; bestIdx = i; }
            }
        }
        return (best, bestIdx);
    }

    /// <summary>Hovered arrow (0=X, 1=Y, 2=Z, -1=none). Set by the UI for highlight.</summary>
    public int HoverAxis { get; set; } = -1;
    /// <summary>Arrow currently being dragged. Set by the UI for highlight.</summary>
    public int ActiveAxis { get; set; } = -1;
    /// <summary>Which gizmo the active tool shows. None hides it.</summary>
    public GizmoKind ActiveGizmo { get; set; } = GizmoKind.Move;

    public static Vector3 GizmoAxisVec(int a) =>
        a == 0 ? Vector3.UnitX : a == 2 ? Vector3.UnitZ : Vector3.UnitY;

    /// <summary>Half extent of the active selection along a gizmo axis (for
    /// face-centered handles).</summary>
    private float HalfExtent(int a)
    {
        if (Selected == null) return 0.5f;
        var s = Selected.Size;
        return (a == 0 ? s.X : a == 1 ? s.Y : s.Z) * 0.5f;
    }

    /// <summary>Gizmo axis in world space: follows the selection's orientation
    /// (world axes when nothing is selected).</summary>
    public Vector3 GizmoAxis(int a)
    {
        return Vector3.Transform(GizmoAxisVec(a), GizmoOrientation);
    }

    private int _program;
    private int _mvpLoc = -1;
    private int _modelLoc = -1;
    private int _normalLoc = -1;
    private int _colorLoc = -1;
    private int _texLoc = -1;
    private int _useTexLoc = -1;
    private int _tilingLoc = -1;
    private int _camLoc = -1;
    private int _pulseLoc = -1;
    private int _fogColorLoc = -1;
    private int _fogDensityLoc = -1;
    private int _contrastLoc = -1;
    private int _metallicLoc = -1;
    private int _shininessLoc = -1;
    private int _opacityLoc = -1;
    private int _emissiveLoc = -1;
    private int _shadowMapLoc = -1;
    private int _lightVpLoc = -1;
    private int _shadowsOnLoc = -1;
    private int _shadowSizeLoc = -1;
    private int _shadowDistanceLoc = -1;
    private int _sunDirLoc = -1;
    private int _sunColorLoc = -1;
    private int _sunILoc = -1;
    private int _ambientLoc = -1;
    private int _reflectanceLoc = -1;
    private int _glossLoc = -1;
    private int _timeLoc = -1;
    private int _waterLoc = -1;
    private int _reflOnLoc = -1;
    private int _reflTexLoc = -1;
    private int _reflVpLoc = -1;
    private int _clipLoc = -1;
    private int _ptCountLoc = -1;
    private int _ptPosLoc = -1;
    private int _ptColorLoc = -1;
    private int _ptParamsLoc = -1;
    private int _skyProgram;
    private int _skyMvpLoc = -1;
    private int _skySunDirLoc = -1;
    private int _skySunTintLoc = -1;
    private int _skySunILoc = -1;
    private int _skyHazeLoc = -1;
    private int _skyMoonDirLoc = -1;
    private int _skyTimeLoc = -1;
    private int _skyContrastLoc = -1;
    private int _skyTintLoc = -1;
    private int _skyStarLoc = -1;
    private int _skyCloudLoc = -1;
    private int _skySunSizeLoc = -1;
    private int _skyStripesLoc = -1;
    private int _skyStyleLoc = -1;
    private int _skyVao;
    private int _decalProgram;
    private int _decalVpLoc = -1, _decalImageLoc = -1;
    private int _decalContrastLoc = -1;
    private int _decalVao, _decalVbo;
    private readonly Dictionary<string, int> _decalTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tiling overlay textures by path (repeat-wrapped; decals clamp).</summary>
    private readonly Dictionary<string, int> _partTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Procedural material textures by pattern key (see MaterialTexture).</summary>
    private readonly Dictionary<string, int> _materialTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Real material photos live here (grass.png, wood.png, ...). Created on
    /// first run beside the exe; published games carry it along automatically.</summary>
    public static string TextureFolder => Path.Combine(AppContext.BaseDirectory, "Textures");

    private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg" };

    private int _particleProgram;
    private int _particleProjLoc = -1, _particleViewLoc = -1, _particlePixelScaleLoc = -1;
    private int _particleVao, _particleVbo;
    private readonly List<Particle> _particles = new();
    private readonly Dictionary<SceneObject, float> _emitAcc = new();
    private readonly Random _rand = new();
    private float[] _particleVerts = Array.Empty<float>();
    private float[] _particleVertsAdd = Array.Empty<float>();
    private bool _particlesActive;
    private const int MaxParticles = 4000;

    /// <summary>Imported fbx/obj meshes by file path (see ModelImport). GL context required.</summary>
    private readonly Dictionary<string, Mesh> _importedMeshes = new(StringComparer.OrdinalIgnoreCase);
    // Last-used frame per GPU mesh (same key space as above).
    private readonly Dictionary<string, long> _meshTick = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _meshFailures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Import failure sink (the UI shows these in Output; InitLog only covers startup).</summary>
    public static Action<string>? MeshLog;

    /// <summary>Pending screenshot path (captured at the end of the next frame).</summary>
    public string? ScreenshotPath;
    /// <summary>Screenshot result sink (the UI shows these in Output).</summary>
    public Action<string>? ScreenshotLog;

    private ShapeKind? _ghostShape;
    private Vector3 _ghostPos = Vector3.Zero, _ghostSize = Vector3.One, _ghostColor = Vector3.One;

    /// <summary>Toolbox drag ghost: translucent placement preview (cleared on drop).</summary>
    public void SetGhost(ShapeKind shape, Vector3 pos, Vector3 size, Vector3 color)
    {
        _ghostShape = shape;
        _ghostPos = pos;
        _ghostSize = size;
        _ghostColor = color;
    }

    /// <summary>Hide the toolbox drag ghost.</summary>
    public void ClearGhost() => _ghostShape = null;

    /// <summary>Render mesh for an object: imported file LOD for Mesh shapes
    /// (block placeholder when missing/unloadable), primitive table otherwise.</summary>
    private Mesh MeshFor(SceneObject o)
    {
        if (o.Shape != ShapeKind.Mesh || string.IsNullOrWhiteSpace(o.MeshPath) ||
            _meshFailures.Contains(o.MeshPath!))
            return _meshes[o.Shape];
        string key = o.MeshPath! + "\0" + (int)o.RenderFidelity;
        if (!_importedMeshes.TryGetValue(key, out var m))
        {
            try
            {
                var model = ModelImport.GetModel(o.MeshPath!);
                var verts = o.RenderFidelity switch
                {
                    RenderFidelityKind.Performance => model.Decimated.Length > 0 ? model.Decimated : model.Full,
                    RenderFidelityKind.Ultra => model.Smooth.Length > 0 ? model.Smooth : model.Full,
                    _ => model.Full,
                };
                if (verts.Length == 0) throw new InvalidDataException("empty mesh");
                m = UploadMesh(verts);
                _importedMeshes[key] = m;
                _meshTick[key] = _texFrame;
                SweepMeshCache();
            }
            catch (Exception ex)
            {
                InitLog += $"Mesh import failed ({o.MeshPath}): {ex.Message}\n";
                try { MeshLog?.Invoke($"Mesh import failed ({Path.GetFileName(o.MeshPath!)}): {ex.Message}"); } catch { }
                _meshFailures.Add(o.MeshPath!);
                return _meshes[o.Shape];
            }
        }
        else _meshTick[key] = _texFrame;
        return m;
    }

    /// <summary>Evict stalest GPU meshes past the cap (recreated on demand).</summary>
    private void SweepMeshCache()
    {
        const int cap = 48;
        if (_importedMeshes.Count <= cap) return;
        var order = new List<(string Key, long Tick)>(_importedMeshes.Count);
        foreach (var kv in _importedMeshes)
            order.Add((kv.Key, _meshTick.TryGetValue(kv.Key, out long t) ? t : 0));
        order.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        int drop = Math.Max(1, _importedMeshes.Count / 4);
        for (int i = 0; i < drop && i < order.Count; i++)
        {
            if (_importedMeshes.Remove(order[i].Key, out var m))
            {
                if (m.Vbo != 0) try { GL.DeleteBuffer(m.Vbo); } catch { }
                if (m.Vao != 0) try { GL.DeleteVertexArray(m.Vao); } catch { }
            }
            _meshTick.Remove(order[i].Key);
        }
    }

    /// <summary>Drop cached imports (file replaced/removed). Best-effort GL delete.</summary>
    public void ClearMeshCache(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        foreach (RenderFidelityKind f in Enum.GetValues<RenderFidelityKind>())
        {
            string k = path! + "\0" + (int)f;
            if (_importedMeshes.Remove(k, out var m))
            {
                try { GL.DeleteBuffer(m.Vbo); GL.DeleteVertexArray(m.Vao); } catch { }
            }
            _meshTick.Remove(k);
        }
        ModelImport.DropModel(path);
        _meshFailures.Remove(path!);
    }

    /// <summary>Exact ray-vs-triangles over factory verts (new primitives pick
    /// through holes/gaps like meshes). Bbox reject first, like RayMesh.</summary>
    private static bool RayTris(Vertex[] verts, Vector3 origin, Vector3 dir, out float t)
    {
        t = 0;
        if (verts.Length == 0) return false;
        if (!RaySlab(origin, dir, new Vector3(0.5f, 0.5f, 0.5f), out _)) return false;
        float best = float.MaxValue;
        bool hit = false;
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            if (RayTri(origin, dir, verts[i].Position, verts[i + 1].Position, verts[i + 2].Position,
                out float th) && th > 0f && th < best)
            {
                best = th;
                hit = true;
            }
        }
        t = best;
        return hit;
    }

    /// <summary>Silhouette data for one mesh file: local-space tri normals,
    /// edge endpoints (2 per edge) and the adjacent tri pair per edge
    /// (second = -1 for boundary edges, always drawn).</summary>
    private sealed class SilhouetteCache
    {
        public Vector3[] Normals = Array.Empty<Vector3>();
        public Vector3[] Points = Array.Empty<Vector3>();
        public int[] EdgeTris = Array.Empty<int>();
        public DateTime WriteTime;
    }
    private readonly Dictionary<string, SilhouetteCache> _silCaches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _silTick = new(StringComparer.OrdinalIgnoreCase);
    private float[] _silVerts = Array.Empty<float>();

    /// <summary>Edge adjacency for a mesh file, built once (quantized endpoints).</summary>
    private SilhouetteCache? SilhouetteFor(string path)
    {
        DateTime wt = DateTime.MinValue;
        try { wt = File.GetLastWriteTimeUtc(path); } catch { }
        if (_silCaches.TryGetValue(path, out var hit))
        {
            if (wt == hit.WriteTime)
            {
                _silTick[path] = _texFrame;
                return hit;
            }
            _silCaches.Remove(path);
            _silTick.Remove(path);
        }
        ModelImport.ImportedModel model;
        try { model = ModelImport.GetModel(path); }
        catch { return null; }
        var tris = model.Full;
        // Contour precompute is one-time per file, but the per-frame walk is
        // per tri: cap here (box outline beyond) so giant meshes stay fluid.
        if (tris.Length == 0 || tris.Length > 900_000) return null;
        SweepSilCache();
        int triCount = tris.Length / 3;
        var normals = new Vector3[triCount];
        var valid = new bool[triCount];
        for (int t = 0; t < triCount; t++)
        {
            var a = tris[t * 3].Position;
            var b = tris[t * 3 + 1].Position;
            var c = tris[t * 3 + 2].Position;
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared > 1e-12f) { normals[t] = Vector3.Normalize(n); valid[t] = true; }
        }
        static long Q(float f) => (long)MathF.Round(f * 1e4f);
        var map = new Dictionary<((long, long, long), (long, long, long)), List<int>>();
        void Edge(int i, int j, int t)
        {
            if (!valid[t]) return;
            var pi = tris[i].Position; var pj = tris[j].Position;
            var k1 = (Q(pi.X), Q(pi.Y), Q(pi.Z));
            var k2 = (Q(pj.X), Q(pj.Y), Q(pj.Z));
            var key = k1.CompareTo(k2) <= 0 ? (k1, k2) : (k2, k1);
            if (!map.TryGetValue(key, out var list)) { list = new List<int>(2); map[key] = list; }
            list.Add(t);
        }
        for (int t = 0; t < triCount; t++)
        {
            Edge(t * 3, t * 3 + 1, t);
            Edge(t * 3 + 1, t * 3 + 2, t);
            Edge(t * 3 + 2, t * 3, t);
        }
        var pts = new List<Vector3>();
        var pairs = new List<int>();
        foreach (var kv in map)
        {
            var list = kv.Value;
            if (list.Count == 1)
            {
                // Boundary edge: always drawn.
                pts.Add(tris[EdgeVert(kv, list, tris, 0)].Position);
                pts.Add(tris[EdgeVert(kv, list, tris, 1)].Position);
                pairs.Add(list[0]); pairs.Add(-1);
            }
            else
            {
                // Non-manifold fans: first two decide (rest ignored).
                pts.Add(tris[EdgeVert(kv, list, tris, 0)].Position);
                pts.Add(tris[EdgeVert(kv, list, tris, 1)].Position);
                pairs.Add(list[0]); pairs.Add(list[1]);
            }
        }
        var sil = new SilhouetteCache
        {
            Normals = normals,
            Points = pts.ToArray(),
            EdgeTris = pairs.ToArray(),
            WriteTime = wt,
        };
        _silCaches[path] = sil;
        _silTick[path] = _texFrame;
        return sil;

        static int EdgeVert(
            KeyValuePair<((long, long, long), (long, long, long)), List<int>> kv,
            List<int> list, Vertex[] tris, int which)
        {
            // Recover one endpoint vert index: scan the first tri for a match.
            var want = which == 0 ? kv.Key.Item1 : kv.Key.Item2;
            int t = list[0];
            for (int k = 0; k < 3; k++)
            {
                var p = tris[t * 3 + k].Position;
                if (Q(p.X) == want.Item1 && Q(p.Y) == want.Item2 && Q(p.Z) == want.Item3)
                    return t * 3 + k;
            }
            return t * 3; // degenerate fallback (zero-length line, harmless)
        }
    }

    /// <summary>Evict stalest silhouette precomputes past the cap (rebuilt on demand).</summary>
    private void SweepSilCache()
    {
        const int cap = 32;
        if (_silCaches.Count <= cap) return;
        var order = new List<(string Key, long Tick)>(_silCaches.Count);
        foreach (var kv in _silCaches)
            order.Add((kv.Key, _silTick.TryGetValue(kv.Key, out long t) ? t : 0));
        order.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        int drop = Math.Max(1, _silCaches.Count / 4);
        for (int i = 0; i < drop && i < order.Count; i++)
        {
            _silCaches.Remove(order[i].Key);
            _silTick.Remove(order[i].Key);
        }
    }

    /// <summary>Exact ray-vs-triangles in unit space (mesh picking follows the
    /// file, not the box). Bbox reject + tri cap keep hover cheap.</summary>
    private bool RayMesh(SceneObject o, Vector3 origin, Vector3 dir, out float t)
    {
        t = 0;
        if (string.IsNullOrWhiteSpace(o.MeshPath) || _meshFailures.Contains(o.MeshPath!)) return false;
        ModelImport.ImportedModel model;
        try { model = ModelImport.GetModel(o.MeshPath!); }
        catch { _meshFailures.Add(o.MeshPath!); return false; }
        var tris = model.Full;
        if (tris.Length == 0) return false;
        if (!RaySlab(origin, dir, new Vector3(0.5f, 0.5f, 0.5f), out _)) return false;
        float best = float.MaxValue;
        bool hit = false;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            if (RayTri(origin, dir, tris[i].Position, tris[i + 1].Position, tris[i + 2].Position,
                out float th) && th > 0f && th < best)
            {
                best = th;
                hit = true;
            }
        }
        t = best;
        return hit;
    }

    /// <summary>Möller–Trumbore, front faces only (matches the renderer).</summary>
    private static bool RayTri(Vector3 ro, Vector3 rd, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(rd, e2);
        float det = Vector3.Dot(e1, p);
        if (det < 1e-8f) return false;
        float inv = 1f / det;
        var tv = ro - a;
        float u = Vector3.Dot(tv, p) * inv;
        if (u < 0f || u > 1f) return false;
        var q = Vector3.Cross(tv, e1);
        float v = Vector3.Dot(rd, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 0f;
    }

    /// <summary>Upload every material pattern once (repeat-wrapped,
    /// mipmapped, one tile per stud in-shader). Real photos win: Textures/&lt;key&gt;.png
    /// (or .jpg) beside the exe replaces the procedural fallback for that key.</summary>
    private void BuildMaterialTextures() => BuildMaterialTextures(null);

    private void BuildMaterialTextures(List<string>? report)
    {
        Directory.CreateDirectory(TextureFolder); // make sure users can find it
        foreach (string key in MaterialTexture.Keys)
        {
            try
            {
                int w = 0, h = 0;
                string src = "procedural";
                byte[]? px = null;
                foreach (string ext in TextureExtensions)
                {
                    string file = Path.Combine(TextureFolder, key + ext);
                    if (!File.Exists(file)) continue;
                    var image = ImageResult.FromMemory(File.ReadAllBytes(file), ColorComponents.RedGreenBlueAlpha);
                    px = image.Data;
                    w = image.Width;
                    h = image.Height;
                    src = $"file ({w}x{h})";
                    break;
                }
                px ??= MaterialTexture.Generate(key);
                w = w == 0 ? MaterialTexture.Size : w;
                h = h == 0 ? MaterialTexture.Size : h;
                int tex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tex);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                    w, h, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, px);
                TrilinearSampling();
                // TrilinearSampling clamps (right for decals); materials tile.
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                _materialTextures[key] = tex;
                report?.Add($"{key}: {src}");
            }
            catch (Exception ex)
            {
                InitLog += $"Material texture failed ({key}): {ex.Message}\n";
                _materialTextures[key] = 0;
                report?.Add($"{key}: failed");
            }
        }
    }

    /// <summary>Re-read Textures/ from disk without restarting (drop PNGs in, click,
    /// done). Best-effort GL delete like ClearMeshCache. Returns a summary for Output.</summary>
    public string ReloadMaterialTextures()
    {
        foreach (int tex in _materialTextures.Values)
            try { if (tex != 0) GL.DeleteTexture(tex); } catch { }
        _materialTextures.Clear();
        var report = new List<string>();
        BuildMaterialTextures(report);
        int files = 0;
        foreach (string r in report) if (r.Contains(": file")) files++;
        return $"Textures reloaded: {files} from files, {report.Count - files} procedural. [{string.Join(", ", report)}]";
    }
    private readonly Dictionary<string, int> _textTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _textAspects = new(StringComparer.Ordinal);

    // Last-used frame per cached texture, across all three caches (text keys
    // contain \0 so they can never collide with file paths; one dict serves).
    private readonly Dictionary<string, long> _texTick = new(StringComparer.OrdinalIgnoreCase);
    private long _texFrame;

    // ---------- directional shadow map ----------
    // ---------- planar water reflections (half-res mirror pass per pool) ----------
    private int _reflFbo;
    private int _reflTex;
    private int _reflDepth;
    private int _reflW;
    private int _reflH;

    /// <summary>Mirror the pools (extra scene pass at half res). Off = ripple + fake env only.</summary>
    public bool WaterReflections { get; set; } = true;

    /// <summary>World sphere inside the main view frustum? Cheap cull for
    /// mirror passes (behind/near/offscreen pools skip their render).</summary>
    public bool InView(Vector3 c, float r, int pixelWidth, int pixelHeight)
    {
        Vector3 fwd = Forward;
        if (fwd.LengthSquared < 1e-8f) return true; // degenerate camera: render
        fwd = Vector3.Normalize(fwd);
        Vector3 right = Vector3.Cross(fwd, Vector3.UnitY);
        if (right.LengthSquared < 1e-8f) right = Vector3.UnitX; // looking straight up/down
        else right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(right, fwd);
        Vector3 rel = c - Position;
        float z = Vector3.Dot(rel, fwd);
        if (z < 0.1f - r || z > 500f + r) return false; // behind near / past far plane
        float tanH = MathF.Tan(MathHelper.DegreesToRadians(FovDeg) * 0.5f);
        float tanW = tanH * pixelWidth / (float)Math.Max(pixelHeight, 1);
        float x = Vector3.Dot(rel, right);
        float y = Vector3.Dot(rel, up);
        return Math.Abs(x) <= z * tanW + r && Math.Abs(y) <= z * tanH + r;
    }

    /// <summary>Mirrored VP per water part this frame (main pass samples it).</summary>
    private readonly Dictionary<SceneObject, Matrix4> _reflVpByWater = new();

    /// <summary>Reflect a point across the plane (n normalized, p0 on the plane).</summary>
    private static Vector3 ReflectPoint(Vector3 v, Vector3 n, Vector3 p0) =>
        v - 2f * Vector3.Dot(v - p0, n) * n;

    /// <summary>Reflect a direction across the plane normal.</summary>
    private static Vector3 ReflectDir(Vector3 v, Vector3 n) =>
        v - 2f * Vector3.Dot(v, n) * n;

    /// <summary>(Re)create the half-res reflection target. GL context required.</summary>
    private void EnsureReflectionTarget(int w, int h)
    {
        w = Math.Max(w, 8); h = Math.Max(h, 8);
        if (_reflFbo != 0 && _reflW == w && _reflH == h) return;
        if (_reflTex != 0) GL.DeleteTexture(_reflTex);
        if (_reflDepth != 0) GL.DeleteRenderbuffer(_reflDepth);
        if (_reflFbo != 0) GL.DeleteFramebuffer(_reflFbo);
        _reflW = w; _reflH = h;
        _reflTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _reflTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        _reflDepth = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _reflDepth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, w, h);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        _reflFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _reflFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _reflTex, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _reflDepth);
        var fbStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fbStatus != FramebufferErrorCode.FramebufferComplete)
            InitLog += $"Reflection FBO incomplete: {fbStatus}\n";
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private int _depthProgram;
    private int _depthMvpLoc = -1;
    private int _shadowFbo;
    private int _shadowTex;

    /// <summary>Master shadow toggle (View tab). Off skips the depth pass.</summary>
    public bool ShadowsEnabled { get; set; } = true;

    private bool _shadowDirty = true; // light space is static: re-render depth only when set

    /// <summary>Mark the cached shadow map stale (call when parts move/change).</summary>
    public void InvalidateShadows() => _shadowDirty = true;

    /// <summary>Last snapped shadow focus (texel grid). A cell change re-renders the map.</summary>
    private Vector3 _lastShadowFocus = new(float.MaxValue, 0f, 0f);

    /// <summary>Shader compiler/linker messages from the last Initialize (empty = clean).</summary>
    public string InitLog { get; private set; } = "";

    private readonly Dictionary<ShapeKind, Mesh> _meshes = new();

    // Reused scratch for the transparent pass (zero per-frame allocation).
    private readonly List<SceneObject> _transparent = new();

    /// <summary>Beta blocky-avatar limbs (posed by AvatarRig, never in Objects:
    /// no save/pick/physics/undo footprint). Drawn + shadow-cast with the parts.</summary>
    public List<SceneObject> AvatarRigParts { get; } = new();

    // Sort scratch past the insertion-sort cutoff (grown on demand only).
    private float[] _sortKeys = Array.Empty<float>();
    private SceneObject[] _sortItems = Array.Empty<SceneObject>();

    /// <summary>Far-to-near order for the transparent pass: insertion while small
    /// (fastest, no garbage), keyed introsort past 64 (insertion goes quadratic).</summary>
    private void SortTransparentFarToNear()
    {
        int n = _transparent.Count;
        if (n <= 1) return;
        if (n <= 64)
        {
            for (int i = 1; i < n; i++)
            {
                var item = _transparent[i];
                float key = (item.Position - Position).LengthSquared;
                int j = i - 1;
                while (j >= 0 && ((_transparent[j].Position - Position).LengthSquared < key))
                {
                    _transparent[j + 1] = _transparent[j];
                    j--;
                }
                _transparent[j + 1] = item;
            }
            return;
        }
        if (_sortKeys.Length < n)
        {
            _sortKeys = new float[n];
            _sortItems = new SceneObject[n];
        }
        for (int i = 0; i < n; i++)
        {
            _sortItems[i] = _transparent[i];
            _sortKeys[i] = -(_transparent[i].Position - Position).LengthSquared; // negate: ascending sort, no comparer alloc
        }
        Array.Sort(_sortKeys, _sortItems, 0, n);
        for (int i = 0; i < n; i++) _transparent[i] = _sortItems[i];
        Array.Clear(_sortItems, 0, n); // don't retain scene refs in the scratch
    }

    // Reused scratch for the dynamic-light gather (nearest 16, no allocs).
    private readonly List<SceneObject> _litPoints = new();

    /// <summary>Sort a light bucket nearest-first and cap at 16 (insertion sort, no allocs).</summary>
    private void CapLights(List<SceneObject> bucket)
    {
        if (bucket.Count <= 16) return; // nothing to cull: order irrelevant
        for (int i = 1; i < bucket.Count; i++)
        {
            var item = bucket[i];
            float key = (item.Position - Position).LengthSquared;
            int j = i - 1;
            while (j >= 0 && (bucket[j].Position - Position).LengthSquared > key)
            {
                bucket[j + 1] = bucket[j];
                j--;
            }
            bucket[j + 1] = item;
        }
        while (bucket.Count > 16) bucket.RemoveAt(bucket.Count - 1);
    }

    private Mesh UploadMesh(Vertex[] verts)
    {
        var mesh = new Mesh
        {
            Vao = GL.GenVertexArray(),
            Vbo = GL.GenBuffer(),
            Count = verts.Length,
        };
        GL.BindVertexArray(mesh.Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.Vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * Unsafe.SizeOf<Vertex>(), verts, BufferUsageHint.StaticDraw);
        int pos = GL.GetAttribLocation(_program, "vPosition");
        int nrm = GL.GetAttribLocation(_program, "vNormal");
        GL.EnableVertexAttribArray(pos);
        GL.VertexAttribPointer(pos, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 0);
        GL.EnableVertexAttribArray(nrm);
        GL.VertexAttribPointer(nrm, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), Unsafe.SizeOf<Vector3>());
        int col = GL.GetAttribLocation(_program, "vColor");
        GL.EnableVertexAttribArray(col);
        GL.VertexAttribPointer(col, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), Unsafe.SizeOf<Vector3>() * 2);
        GL.BindVertexArray(0);
        return mesh;
    }

    private bool _initialized;
    private bool _disposed;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;

    public double Fps { get; private set; }

    /// <summary>EMA of CPU milliseconds spent submitting a frame (not GPU time).</summary>
    public double CpuMs { get; private set; }

    public List<SceneObject> Objects { get; } = new();
    public SceneObject? Selected { get; set; }
    /// <summary>All selected objects. <see cref="Selected"/> remains the active member.</summary>
    public HashSet<SceneObject> SelectedObjects { get; } = new();

    /// <summary>Gizmo/pick pivot: centroid of the selection set (== active when single).</summary>
    public Vector3 GizmoPivot { get; set; } = Vector3.Zero;

    /// <summary>Gizmo orientation: follows the active object.</summary>
    public Quaternion GizmoOrientation { get; set; } = Quaternion.Identity;

    /// <summary>When true, a physics simulation owns object transforms (Play mode).</summary>
    public bool PhysicsDriven { get; set; }

    // ---------- Roblox-Studio-style free camera ----------
    // Right-drag looks, WASD+QE flies (while right button held), wheel zooms,
    // middle-drag pans, F resets. Yaw -90 / pitch -10 faces the origin from +Z.
    //
    // All motion is smoothed: look eases toward its target, fly velocity ramps
    // up/down exponentially, and wheel input becomes a decaying zoom impulse.
    // Lambdas are frame-rate independent (higher = snappier).
    public Vector3 Position { get; set; } = new(0, 1.5f, 8f);

    private float _yaw = -90f;
    private float _pitch = -10f;
    private float _targetYaw = -90f;
    private float _targetPitch = -10f;
    private Vector3 _flyVel = Vector3.Zero;
    private float _zoomVel;

    public float Yaw
    {
        get => _yaw;
        set { _yaw = value; _targetYaw = value; }
    }

    public float Pitch
    {
        get => _pitch;
        set { _pitch = value; _targetPitch = value; }
    }

    public float LookResponsiveness { get; set; } = 18f;
    public float MoveResponsiveness { get; set; } = 10f;
    public float ZoomDecay { get; set; } = 10f;
    public float ScaleResponsiveness { get; set; } = 45f; // tight: tracks the mouse (~3 frames), keeps a touch of smoothing
    public float LookSensitivity { get; set; } = 0.25f;
    public float MoveSpeed { get; set; } = 8f;
    public float SlowMultiplier { get; set; } = 0.25f;

    /// <summary>Flip vertical look (mouse up looks down).</summary>
    public bool InvertLookY { get; set; }

    /// <summary>Selection pulse strength 0-1 (0 = no glow).</summary>
    public float PulseStrength { get; set; } = 1f;

    /// <summary>Play-mode follow target (the avatar). Null = free camera.</summary>
    public SceneObject? FollowTarget { get; set; }

    /// <summary>Live camera boom length: slams in on occlusion, eases back out,
    /// so debris whipping through the boom doesn't strobe the view.</summary>
    private float _camDist = 12f;

    /// <summary>Snap the boom to the configured distance (play start / zoom).</summary>
    public void ResetFollowDistance() => _camDist = FollowDistance;

    /// <summary>When true, the follow camera holds its position (void death:
    /// the view stays where the fall happened instead of snapping to spawn).
    /// Look/orbit still work; follow resumes when cleared.</summary>
    public bool FreezeFollow { get; set; }

    /// <summary>Third-person orbit distance, driven by the wheel in play mode.</summary>
    public float FollowDistance { get; set; } = 12f;

    /// <summary>Focus height above the target center (0 = dead-center on the avatar).</summary>
    public float FollowHeight { get; set; } = 0f;

    /// <summary>Fly input for the current frame. X = strafe (+right), Y = vertical (+up, world), Z = forward.</summary>
    public Vector3 FlyInput { get; set; } = Vector3.Zero;
    public bool FlySlow { get; set; }

    public Vector3 Forward
    {
        get
        {
            float yaw = MathHelper.DegreesToRadians(Yaw);
            float pitch = MathHelper.DegreesToRadians(Pitch);
            return new Vector3(
                MathF.Cos(pitch) * MathF.Cos(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Sin(yaw));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
    public Vector3 CameraUp => Vector3.Normalize(Vector3.Cross(Right, Forward));

    /// <summary>Mouse-look: drag right to turn right, drag up to look up (unless inverted).</summary>
    public void Look(float dx, float dy)
    {
        _targetYaw += dx * LookSensitivity;
        float pitchDelta = dy * LookSensitivity;
        _targetPitch = Math.Clamp(_targetPitch + (InvertLookY ? pitchDelta : -pitchDelta), -89f, 89f);
    }

    /// <summary>Pan: drag moves the camera across its own plane.</summary>
    public void Pan(float dx, float dy)
    {
        float k = Math.Clamp(Position.Length * 0.0016f, 0.002f, 0.05f);
        Position += (-Right * dx + CameraUp * dy) * k;
    }

    /// <summary>Wheel zoom: feeds a decaying velocity impulse (smooth glide).</summary>
    public void Dolly(float amount)
    {
        _zoomVel += amount * ZoomDecay;
    }

    public void ResetCamera()
    {
        Position = new Vector3(0, 1.5f, 8f);
        Yaw = -90f;   // setters sync target + actual, stopping all drift
        Pitch = -10f;
        _flyVel = Vector3.Zero;
        _zoomVel = 0f;
    }

    /// <summary>Frame a point: keep the view direction, move so the target
    /// fits the viewport given its bounding radius.</summary>
    public void FocusOn(Vector3 target, float radius)
    {
        float dist = Math.Clamp(
            Math.Max(radius, 0.5f) / MathF.Tan(MathHelper.DegreesToRadians(FovDeg / 2f)) * 1.4f,
            3f, 100f);
        Position = target - Forward * dist;
        _flyVel = Vector3.Zero;
        _zoomVel = 0f;
    }

    /// <summary>Point-sprite particles: world-size attenuated, age + ramp in fragment.</summary>
    private const string ParticleVertexSrc = @"#version 460 core
in vec3 vPos;
in vec3 vColor;
in vec3 vMisc;
uniform mat4 uProj;
uniform mat4 uView;
uniform float uPixelScale;
out vec3 fColor;
out vec3 fMisc;
void main()
{
    vec4 vp = uView * vec4(vPos, 1.0);
    gl_Position = uProj * vp;
    float dist = max(-vp.z, 0.1);
    gl_PointSize = clamp(vMisc.x * uPixelScale / dist, 1.0, 256.0);
    fColor = vColor;
    fMisc = vMisc;
}";

    private const string ParticleFragmentSrc = @"#version 460 core
in vec3 fColor;
in vec3 fMisc;
out vec4 oColor;
void main()
{
    float d = length(gl_PointCoord - vec2(0.5)) * 2.0;
    float m = smoothstep(1.0, 0.35, d);
    float t = fMisc.y;
    vec3 col;
    float a;
    if (fMisc.z > 0.5) {
        // Fire: white-hot birth -> orange -> deep red -> gray smoke wisp,
        // with a bright core that cools as it rises.
        vec3 c = mix(vec3(1.0, 0.93, 0.55), vec3(1.0, 0.5, 0.08), smoothstep(0.0, 0.35, t));
        c = mix(c, vec3(0.65, 0.12, 0.02), smoothstep(0.35, 0.7, t));
        c = mix(c, vec3(0.22, 0.2, 0.18), smoothstep(0.7, 1.0, t));
        float core = pow(max(0.0, 1.0 - d), 3.0) * (1.0 - t);
        col = c + vec3(1.0, 0.85, 0.6) * core * 0.9;
        a = smoothstep(0.0, 0.06, t) * (1.0 - smoothstep(0.55, 1.0, t));
    } else {
        col = fColor;
        a = smoothstep(0.0, 0.1, t) * (1.0 - smoothstep(0.5, 1.0, t));
    }
    float alpha = a * m;
    if (alpha < 0.01) discard;
    oColor = vec4(col, alpha);
}";

    private const string GizmoVertexSrc = @"#version 460 core
in vec3 vPosition;
uniform mat4 uMVP;
void main()
{
    gl_Position = uMVP * vec4(vPosition, 1.0);
}";

    private const string GizmoFragmentSrc = @"#version 460 core
uniform vec3 uColor;
out vec4 oColor;
void main()
{
    oColor = vec4(uColor, 1.0);
}";

    /// <summary>Unit-cube wire box (12 edges, 24 verts) for selection/hover outlines.</summary>
    private static Vector3[] BuildBoxLines()
    {
        Vector3[] c =
        {
            new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
            new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f),
            new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f),
            new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
        };
        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        var arr = new Vector3[edges.Length];
        for (int k = 0; k < edges.Length; k++) arr[k] = c[edges[k]];
        return arr;
    }

    /// <summary>Arrow mesh along +Y (shaft box + cone head), as a triangle soup.</summary>
    private static List<Vector3> BuildArrowTris()
    {
        var tris = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { tris.Add(a); tris.Add(b); tris.Add(c); }

        float r = 0.03f, y0 = 0.02f, y1 = 0.68f;
        Vector3[] q =
        {
            new(-r, y0, -r), new(r, y0, -r), new(r, y1, -r), new(-r, y1, -r),
            new(-r, y0,  r), new(r, y0,  r), new(r, y1,  r), new(-r, y1,  r),
        };
        int[][] faces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 },
            new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 },
        };
        foreach (var f in faces)
        {
            Tri(q[f[0]], q[f[1]], q[f[2]]);
            Tri(q[f[0]], q[f[2]], q[f[3]]);
        }

        const int N = 12;
        float br = 0.09f, by = 0.68f;
        var tip = new Vector3(0, 1, 0);
        var bc = new Vector3(0, by, 0);
        for (int k = 0; k < N; k++)
        {
            float a0 = k / (float)N * MathF.PI * 2f;
            float a1 = (k + 1) / (float)N * MathF.PI * 2f;
            var p0 = new Vector3(br * MathF.Cos(a0), by, br * MathF.Sin(a0));
            var p1 = new Vector3(br * MathF.Cos(a1), by, br * MathF.Sin(a1));
            Tri(tip, p0, p1);
            Tri(bc, p1, p0);
        }
        return tris;
    }

    /// <summary>Scale-handle mesh along +Y: a grab sphere centered on the face
    /// plane (half embedded, Roblox-style), triangle soup. No stem: the grab
    /// point is the face itself, so sizing tracks the mouse exactly.</summary>
    private static List<Vector3> BuildScaleTris()
    {
        var tris = new List<Vector3>();
        // Unit sphere scaled to grab size, centered at the mesh origin (= face).
        const float sr = 0.19f;
        foreach (var v in MeshFactory.Sphere(12, 24))
            tris.Add(new Vector3(v.Position.X * sr * 2f, v.Position.Y * sr * 2f, v.Position.Z * sr * 2f));
        return tris;
    }

    /// <summary>Upload one +Y mesh baked into all 3 axis orientations (no runtime rotation risk).</summary>
    private void UploadAxisMesh(List<Vector3> baseTris, int[] vaos, int[] vbos, uint[] counts)
    {
        for (int a = 0; a < 3; a++)
        {
            var arr = new Vector3[baseTris.Count];
            for (int k = 0; k < baseTris.Count; k++)
            {
                var v = baseTris[k];
                arr[k] = a == 0 ? new Vector3(v.Y, v.X, v.Z)  // +Y -> +X
                       : a == 2 ? new Vector3(v.X, v.Z, v.Y)  // +Y -> +Z
                       : v;
            }
            vaos[a] = GL.GenVertexArray();
            vbos[a] = GL.GenBuffer();
            GL.BindVertexArray(vaos[a]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbos[a]);
            GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * Unsafe.SizeOf<Vector3>(), arr, BufferUsageHint.StaticDraw);
            int gp = GL.GetAttribLocation(_gizmoProgram, "vPosition");
            GL.EnableVertexAttribArray(gp);
            GL.VertexAttribPointer(gp, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vector3>(), 0);
            GL.BindVertexArray(0);
            counts[a] = (uint)arr.Length;
        }
    }

    /// <summary>(Re)create the shadow depth target at the pending size. GL context required.</summary>
    private void RecreateShadowTarget()
    {
        if (_shadowTex != 0) GL.DeleteTexture(_shadowTex);
        if (_shadowFbo != 0) GL.DeleteFramebuffer(_shadowFbo);
        ShadowMapSize = WantedShadowSize();
        _shadowTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _shadowTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24,
            ShadowMapSize, ShadowMapSize, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, new float[] { 1f, 1f, 1f, 1f });
        // Hardware PCF: single-tap filtered depth compare in the shader.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)All.Lequal);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        _shadowFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _shadowTex, 0);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        var fbStatus = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (fbStatus != FramebufferErrorCode.FramebufferComplete)
            InitLog += $"Shadow FBO incomplete: {fbStatus}\n";
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        InvalidateShadows(); // fresh target holds garbage until re-rendered
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _program = CreateProgram(VertexSrc, FragmentSrc);
        _mvpLoc = GL.GetUniformLocation(_program, "uMVP");
        _modelLoc = GL.GetUniformLocation(_program, "uModel");
        _normalLoc = GL.GetUniformLocation(_program, "uNormalMat");
        _colorLoc = GL.GetUniformLocation(_program, "uColor");
        _texLoc = GL.GetUniformLocation(_program, "uTex");
        _useTexLoc = GL.GetUniformLocation(_program, "uUseTex");
        _tilingLoc = GL.GetUniformLocation(_program, "uTiling");
        _camLoc = GL.GetUniformLocation(_program, "uCamPos");
        _pulseLoc = GL.GetUniformLocation(_program, "uPulse");
        _fogColorLoc = GL.GetUniformLocation(_program, "uFogColor");
        _fogDensityLoc = GL.GetUniformLocation(_program, "uFogDensity");
        _contrastLoc = GL.GetUniformLocation(_program, "uContrast");
        _metallicLoc = GL.GetUniformLocation(_program, "uMetallic");
        _shininessLoc = GL.GetUniformLocation(_program, "uShininess");
        _opacityLoc = GL.GetUniformLocation(_program, "uOpacity");
        _emissiveLoc = GL.GetUniformLocation(_program, "uEmissive");
        _shadowMapLoc = GL.GetUniformLocation(_program, "uShadowMap");
        _lightVpLoc = GL.GetUniformLocation(_program, "uLightVP");
        _shadowsOnLoc = GL.GetUniformLocation(_program, "uShadowsOn");
        _shadowSizeLoc = GL.GetUniformLocation(_program, "uShadowSize");
        _shadowDistanceLoc = GL.GetUniformLocation(_program, "uShadowDistance");
        _sunDirLoc = GL.GetUniformLocation(_program, "uSunDir");
        _sunColorLoc = GL.GetUniformLocation(_program, "uSunColor");
        _sunILoc = GL.GetUniformLocation(_program, "uSunI");
        _ambientLoc = GL.GetUniformLocation(_program, "uAmbient");
        _reflectanceLoc = GL.GetUniformLocation(_program, "uReflectance");
        _glossLoc = GL.GetUniformLocation(_program, "uGloss");
        _timeLoc = GL.GetUniformLocation(_program, "uTime");
        _waterLoc = GL.GetUniformLocation(_program, "uWater");
        _reflOnLoc = GL.GetUniformLocation(_program, "uReflOn");
        _reflTexLoc = GL.GetUniformLocation(_program, "uReflTex");
        _reflVpLoc = GL.GetUniformLocation(_program, "uReflVP");
        _clipLoc = GL.GetUniformLocation(_program, "uClipPlane");
        _ptCountLoc = GL.GetUniformLocation(_program, "uPtCount");
        _ptPosLoc = GL.GetUniformLocation(_program, "uPtPos[0]");
        _ptColorLoc = GL.GetUniformLocation(_program, "uPtColor[0]");
        _ptParamsLoc = GL.GetUniformLocation(_program, "uPtParams[0]");

        // One mesh per shape; the skybox reuses the Block VBO (position attribute only).
        foreach (ShapeKind shape in Enum.GetValues<ShapeKind>())
            _meshes[shape] = UploadMesh(MeshFactory.For(shape));

        BuildMaterialTextures();

        _skyProgram = CreateProgram(SkyVertexSrc, SkyFragmentSrc);
        _skyMvpLoc = GL.GetUniformLocation(_skyProgram, "uMVP");
        _skySunDirLoc = GL.GetUniformLocation(_skyProgram, "uSunDir");
        _skySunTintLoc = GL.GetUniformLocation(_skyProgram, "uSunTint");
        _skySunILoc = GL.GetUniformLocation(_skyProgram, "uSunI");
        _skyHazeLoc = GL.GetUniformLocation(_skyProgram, "uHaze");
        _skyMoonDirLoc = GL.GetUniformLocation(_skyProgram, "uMoonDir");
        _skyTimeLoc = GL.GetUniformLocation(_skyProgram, "uTime");
        _skyContrastLoc = GL.GetUniformLocation(_skyProgram, "uContrast");
        _skyTintLoc = GL.GetUniformLocation(_skyProgram, "uTint");
        _skyStarLoc = GL.GetUniformLocation(_skyProgram, "uStarAmt");
        _skyCloudLoc = GL.GetUniformLocation(_skyProgram, "uCloudAmt");
        _skySunSizeLoc = GL.GetUniformLocation(_skyProgram, "uSunSize");
        _skyStripesLoc = GL.GetUniformLocation(_skyProgram, "uStripes");
        _skyStyleLoc = GL.GetUniformLocation(_skyProgram, "uStyle");
        _skyVao = GL.GenVertexArray();
        GL.BindVertexArray(_skyVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _meshes[ShapeKind.Block].Vbo);
        int skyPos = GL.GetAttribLocation(_skyProgram, "vPosition");
        GL.EnableVertexAttribArray(skyPos);
        GL.VertexAttribPointer(skyPos, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vertex>(), 0);
        GL.BindVertexArray(0);

        _decalProgram = CreateProgram(DecalVertexSrc, DecalFragmentSrc);
        _decalVpLoc = GL.GetUniformLocation(_decalProgram, "uVP");
        _decalImageLoc = GL.GetUniformLocation(_decalProgram, "uImage");
        _decalContrastLoc = GL.GetUniformLocation(_decalProgram, "uContrast");
        _decalVao = GL.GenVertexArray();
        _decalVbo = GL.GenBuffer();
        // World-space batched quads, interleaved 11 floats:
        // pos(3) uv(2) a(trans,blur,tileX,tileY)(4) b(offX,offY)(2).
        GL.BindVertexArray(_decalVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _decalVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _decalBufFloats * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 11 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 11 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 11 * sizeof(float), 5 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, 11 * sizeof(float), 9 * sizeof(float));
        GL.BindVertexArray(0);

        // Move-gizmo arrows: small unlit shader + one arrow mesh per axis
        // (axis baked in at build time, so no runtime rotation convention risk).
        _gizmoProgram = CreateProgram(GizmoVertexSrc, GizmoFragmentSrc);
        _gizmoMvpLoc = GL.GetUniformLocation(_gizmoProgram, "uMVP");
        _gizmoColorLoc = GL.GetUniformLocation(_gizmoProgram, "uColor");
        var arrowTris = BuildArrowTris();
        UploadAxisMesh(arrowTris, _gizmoVaos, _gizmoVbos, _gizmoCounts);
        UploadAxisMesh(BuildScaleTris(), _scaleVaos, _scaleVbos, _scaleCounts);
        // Rotate rings: flat torus in the XZ plane permuted per axis (same trick).
        var ringTris = new List<Vector3>();
        foreach (var v in MeshFactory.Torus(0.65f, 0.03f, 48, 8)) ringTris.Add(v.Position);
        UploadAxisMesh(ringTris, _ringVaos, _ringVbos, _ringCounts);
        // Velocity arrow: one +Y mesh, oriented per-part at draw time.
        {
            var arr = arrowTris.ToArray();
            _velVao = GL.GenVertexArray();
            _velVbo = GL.GenBuffer();
            GL.BindVertexArray(_velVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _velVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * Unsafe.SizeOf<Vector3>(), arr, BufferUsageHint.StaticDraw);
            int gp = GL.GetAttribLocation(_gizmoProgram, "vPosition");
            GL.EnableVertexAttribArray(gp);
            GL.VertexAttribPointer(gp, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vector3>(), 0);
            GL.BindVertexArray(0);
            _velCount = (uint)arr.Length;
        }

        // Selection/hover outline box: one shared wire cube.
        {
            var arr = BuildBoxLines();
            _boxVao = GL.GenVertexArray();
            _boxVbo = GL.GenBuffer();
            GL.BindVertexArray(_boxVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _boxVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * Unsafe.SizeOf<Vector3>(), arr, BufferUsageHint.StaticDraw);
            int gp = GL.GetAttribLocation(_gizmoProgram, "vPosition");
            GL.EnableVertexAttribArray(gp);
            GL.VertexAttribPointer(gp, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vector3>(), 0);
            GL.BindVertexArray(0);
            _boxCount = (uint)arr.Length;
        }

        // Silhouette contour lines: dynamic VBO, same gizmo shader as the boxes.
        _silVao = GL.GenVertexArray();
        _silVbo = GL.GenBuffer();
        GL.BindVertexArray(_silVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _silVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float), new float[1], BufferUsageHint.DynamicDraw);
        int sp = GL.GetAttribLocation(_gizmoProgram, "vPosition");
        GL.EnableVertexAttribArray(sp);
        GL.VertexAttribPointer(sp, 3, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vector3>(), 0);
        GL.BindVertexArray(0);

        // Particle points: dynamic VBO (pos3 + color3 + size/alpha), own program.
        _particleProgram = CreateProgram(ParticleVertexSrc, ParticleFragmentSrc);
        _particleProjLoc = GL.GetUniformLocation(_particleProgram, "uProj");
        _particleViewLoc = GL.GetUniformLocation(_particleProgram, "uView");
        _particlePixelScaleLoc = GL.GetUniformLocation(_particleProgram, "uPixelScale");
        _particleVao = GL.GenVertexArray();
        _particleVbo = GL.GenBuffer();
        GL.BindVertexArray(_particleVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float), new float[1], BufferUsageHint.DynamicDraw);
        int ppStride = 9 * sizeof(float);
        int ppPos = GL.GetAttribLocation(_particleProgram, "vPos");
        GL.EnableVertexAttribArray(ppPos);
        GL.VertexAttribPointer(ppPos, 3, VertexAttribPointerType.Float, false, ppStride, 0);
        int ppCol = GL.GetAttribLocation(_particleProgram, "vColor");
        GL.EnableVertexAttribArray(ppCol);
        GL.VertexAttribPointer(ppCol, 3, VertexAttribPointerType.Float, false, ppStride, 3 * sizeof(float));
        int ppMisc = GL.GetAttribLocation(_particleProgram, "vMisc");
        GL.EnableVertexAttribArray(ppMisc);
        GL.VertexAttribPointer(ppMisc, 3, VertexAttribPointerType.Float, false, ppStride, 6 * sizeof(float));
        GL.BindVertexArray(0);

        // Shadow map: depth texture + FBO, no color buffer.
        _depthProgram = CreateProgram(DepthVertexSrc, DepthFragmentSrc);
        _depthMvpLoc = GL.GetUniformLocation(_depthProgram, "uLightMVP");
        RecreateShadowTarget();

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Multisample); // MSAA resolve when the control allocates samples
        // All cube faces wind CCW from outside, so back faces can be culled.
        // (The skybox is viewed from inside and disables this during its pass.)
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Ccw);

        if (Objects.Count == 0)
        {
            // Same set as File > New Place (single source of truth).
            Objects.AddRange(Models.Templates.Baseplate().Objects);
            Selected = Objects.FirstOrDefault(o => o.IsSpawn);
        }
    }

    /// <summary>GLSL version baked into shaders at compile (460, or 400 on fallback hardware).</summary>
    public static int GlslVersion { get; set; } = 460;

    private int CreateProgram(string vs, string fs)
    {
        void Note(string msg)
        {
            InitLog += msg + "\n";
            Debug.WriteLine(msg);
        }

        // All sources are authored for 460 core but only use <= 4.0 constructs,
        // so fallback hardware just needs the version line retargeted.
        if (GlslVersion != 460)
        {
            string tag = "#version " + GlslVersion + " core";
            vs = vs.Replace("#version 460 core", tag);
            fs = fs.Replace("#version 460 core", tag);
        }

        int program = GL.CreateProgram();
        int v = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(v, vs);
        GL.CompileShader(v);
        GL.GetShader(v, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) Note("VS error: " + GL.GetShaderInfoLog(v));

        int f = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(f, fs);
        GL.CompileShader(f);
        GL.GetShader(f, ShaderParameter.CompileStatus, out ok);
        if (ok == 0) Note("FS error: " + GL.GetShaderInfoLog(f));

        GL.AttachShader(program, v);
        GL.AttachShader(program, f);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out ok);
        if (ok == 0) Note("Link error: " + GL.GetProgramInfoLog(program));

        GL.DetachShader(program, v);
        GL.DetachShader(program, f);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
        return program;
    }

    /// <summary>Draw one part with its material. Caller sets program + shared uniforms.</summary>
    private void DrawObject(SceneObject o, Matrix4 view, Matrix4 proj, double now)
    {
        if (o.Shape == ShapeKind.None)
        {
            if (PhysicsDriven) return; // editor-only light glyphs
            DrawLightGlyph(o, view, proj);
            return;
        }
        // Row-major: scale, then rotate in place, then translate.
        var model = ModelMatrix(o);
        var mvp = model * view * proj;
        GL.UniformMatrix4(_mvpLoc, false, ref mvp);
        GL.UniformMatrix4(_modelLoc, false, ref model);

        // Normal matrix for rotation + non-uniform scale: S^-1 * R
        // (inverse-transpose of the model's upper 3x3, row-major order).
        var normalMat =
            new Matrix3(
                1f / o.Size.X, 0, 0,
                0, 1f / o.Size.Y, 0,
                0, 0, 1f / o.Size.Z) *
            RotationPart(o);
        GL.UniformMatrix3(_normalLoc, false, ref normalMat);

        var mat = MaterialParams.Of(o.Material);
        // Meshes scale albedo by Color Brightness (0-50, default 1).
        float cb = o.Shape == ShapeKind.Mesh ? Math.Clamp(o.ColorBrightness, 0f, 50f) : 1f;
        GL.Uniform3(_colorLoc, new Vector3(o.Color.R * cb, o.Color.G * cb, o.Color.B * cb));
        // Albedo lives on unit 2: unit 0 is the shadow map, unit 1 is decals/text.
        GL.Uniform1(_texLoc, 2);
        if (mat.Texture != null && _materialTextures.TryGetValue(mat.Texture, out int mtex) && mtex != 0)
        {
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, mtex);
            GL.Uniform1(_useTexLoc, 1f);
        }
        else GL.Uniform1(_useTexLoc, 0f);
        GL.Uniform1(_tilingLoc, o.Tiling);
        GL.Uniform1(_metallicLoc, mat.Metallic);
        GL.Uniform1(_shininessLoc, mat.Shininess);
        GL.Uniform1(_opacityLoc, mat.Opacity * (1f - o.Transparency));
        GL.Uniform1(_emissiveLoc, mat.Emissive);
        GL.Uniform1(_reflectanceLoc, Math.Clamp(o.Reflectance, 0f, 1f));
        // Water volumes: animated ripple + planar reflection (when bound).
        GL.Uniform1(_timeLoc, (float)(now % 7200.0));
        bool water = o.IsWater;
        GL.Uniform1(_waterLoc, water ? 1f : 0f);
        if (water && _reflVpByWater.TryGetValue(o, out var rvp))
        {
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, _reflTex);
            GL.Uniform1(_reflTexLoc, 3);
            GL.UniformMatrix4(_reflVpLoc, false, ref rvp);
        }
        // Meshes honor Reflection as a full gloss gate (pure matte at 0:
        // sun/rim/point specular all scale to nothing); everything else
        // renders exactly as before.
        GL.Uniform1(_glossLoc, o.Shape == ShapeKind.Mesh
            ? Math.Clamp(o.Reflectance, 0f, 1f)
            : 1f);

        // Selected object glows softly so the Explorer selection is visible.
        float pulse = SelectedObjects.Contains(o)
            ? (0.22f + 0.10f * MathF.Sin((float)now * 6f)) * PulseStrength
            : 0f;
        GL.Uniform1(_pulseLoc, pulse);

        var mesh = MeshFor(o);
        GL.BindVertexArray(mesh.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mesh.Count);
    }

    /// <summary>Unlit editor glyph for light objects, in the light's own color.</summary>
    private void DrawLightGlyph(SceneObject o, Matrix4 view, Matrix4 proj)
    {
        var model = ModelMatrix(o);
        var mvp = model * view * proj;
        GL.UniformMatrix4(_mvpLoc, false, ref mvp);
        GL.UniformMatrix4(_modelLoc, false, ref model);
        var glyphNormal = Matrix3.Identity;
        GL.UniformMatrix3(_normalLoc, false, ref glyphNormal);
        GL.Uniform3(_colorLoc, new Vector3(o.Color.R, o.Color.G, o.Color.B));
        GL.Uniform1(_metallicLoc, 0f);
        GL.Uniform1(_shininessLoc, 8f);
        GL.Uniform1(_opacityLoc, 1f);
        GL.Uniform1(_emissiveLoc, 2.2f);
        GL.Uniform1(_reflectanceLoc, 0f);
        GL.Uniform1(_glossLoc, 1f);
        GL.Uniform1(_waterLoc, 0f); // glyphs never ripple/sample (shared program)
        GL.Uniform1(_pulseLoc, 0f);
        var mesh = _meshes[ShapeKind.None];
        GL.BindVertexArray(mesh.Vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, mesh.Count);
    }

    /// <summary>Tiling overlay texture by path (repeat wrap + mipmaps).</summary>
    private int GetPartTexture(string path)
    {
        if (_partTextures.TryGetValue(path, out int texture))
        {
            _texTick[path] = _texFrame;
            return texture;
        }
        try
        {
            var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, image.Width, image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            TrilinearSampling();
            // TrilinearSampling clamps (right for decals); tiling overlays repeat.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _partTextures[path] = texture;
            _texTick[path] = _texFrame;
            SweepTexCache(_partTextures, null, 256);
            return texture;
        }
        catch (Exception ex)
        {
            InitLog += $"Texture image failed ({path}): {ex.Message}\n";
            _partTextures[path] = 0;
            return 0;
        }
    }

    private int GetDecalTexture(string path)
    {
        if (_decalTextures.TryGetValue(path, out int texture))
        {
            _texTick[path] = _texFrame;
            return texture;
        }
        try
        {
            var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, image.Width, image.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            TrilinearSampling();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _decalTextures[path] = texture;
            _texTick[path] = _texFrame;
            SweepTexCache(_decalTextures, null, 256);
            return texture;
        }
        catch (Exception ex)
        {
            InitLog += $"Decal image failed ({path}): {ex.Message}\n";
            _decalTextures[path] = 0;
            return 0;
        }
    }

    /// <summary>Trilinear + mipmaps + aniso on the currently bound 2D texture.</summary>
    private static void TrilinearSampling()
    {
        // Decals/texts stay crisp at glancing angles / distance instead of shimmering.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        try
        {
            GL.GetFloat((GetPName)0x84FF, out float maxAniso); // MAX_TEXTURE_MAX_ANISOTROPY_EXT
            if (maxAniso >= 2f)
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, Math.Min(8f, maxAniso));
        }
        catch { /* anisotropy is best-effort */ }
    }

    private int GetTextTexture(TextLayer t, out float aspect)
    {
        string key = $"{t.Text}\0{TextFonts.OrFallback(t.Font)}\0{Math.Clamp(t.Size, 8f, 256f):0.#}\0{t.Color}\0{t.Bold}\0{t.Outline:0.#}\0{t.OutlineColor}";
        if (_textTextures.TryGetValue(key, out int texture))
        {
            aspect = _textAspects[key];
            _texTick[key] = _texFrame;
            return texture;
        }
        texture = RenderTextTexture(t, out aspect);
        _textTextures[key] = texture;
        _textAspects[key] = aspect;
        _texTick[key] = _texFrame;
        SweepTexCache(_textTextures, _textAspects, 128); // LRU instead of nuke-all
        return texture;
    }

    /// <summary>Evict the stalest quarter of a texture cache past its cap
    /// (LRU by rendered frame). GL context required; sweeps are rare.</summary>
    private void SweepTexCache(Dictionary<string, int> cache, Dictionary<string, float>? aspects, int cap)
    {
        if (cache.Count <= cap) return;
        var order = new List<(string Key, long Tick)>(cache.Count);
        foreach (var kv in cache)
            order.Add((kv.Key, _texTick.TryGetValue(kv.Key, out long t) ? t : 0));
        order.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        int drop = Math.Max(1, cache.Count / 4);
        for (int i = 0; i < drop && i < order.Count; i++)
        {
            if (cache.Remove(order[i].Key, out int tex) && tex != 0)
                try { GL.DeleteTexture(tex); } catch { }
            aspects?.Remove(order[i].Key);
            _texTick.Remove(order[i].Key);
        }
    }

    private void ClearTextTextures()
    {
        foreach (var texture in _textTextures.Values)
            if (texture != 0) GL.DeleteTexture(texture);
        _textTextures.Clear();
        _textAspects.Clear();
    }

    /// <summary>Rasterize a text layer with GDI+ (system fonts) into a GL texture. 0 on failure.</summary>
    private int RenderTextTexture(TextLayer t, out float aspect)
    {
        aspect = 1f;
        try
        {
            string text = string.IsNullOrEmpty(t.Text) ? " " : t.Text.Length > 200 ? t.Text[..200] : t.Text;
            if (!PartColor.TryParseRgb(t.Color, out var mc) && !PartColor.TryParseHex(t.Color, out mc))
                mc = System.Windows.Media.Color.FromRgb(255, 255, 255);
            float px = Math.Clamp(t.Size, 8f, 256f);
            var style = t.Bold ? Gdi.FontStyle.Bold : Gdi.FontStyle.Regular;
            using var font = new Gdi.Font(TextFonts.OrFallback(t.Font), px, style, Gdi.GraphicsUnit.Pixel);
            int w, h;
            using (var measure = new Gdi.Bitmap(2, 2, GdiImaging.PixelFormat.Format32bppArgb))
            using (var mg = Gdi.Graphics.FromImage(measure))
            {
                var ms = mg.MeasureString(text, font, 1024);
                int padding = (int)Math.Ceiling(Math.Clamp(t.Outline, 0f, 16f)) * 2 + 4;
                w = Math.Clamp((int)Math.Ceiling(ms.Width) + padding, 2, 1024);
                h = Math.Clamp((int)Math.Ceiling(ms.Height) + padding, 2, 1024);
            }
            aspect = w / (float)h;
            using var bmp = new Gdi.Bitmap(w, h, GdiImaging.PixelFormat.Format32bppArgb);
            using (var g = Gdi.Graphics.FromImage(bmp))
            {
                g.Clear(Gdi.Color.Transparent);
                g.TextRenderingHint = GdiText.TextRenderingHint.AntiAlias;
                using var brush = new Gdi.SolidBrush(Gdi.Color.FromArgb(mc.R, mc.G, mc.B));
                using var format = new Gdi.StringFormat { Alignment = Gdi.StringAlignment.Center, LineAlignment = Gdi.StringAlignment.Center };
                if (t.Outline > 0.01f)
                {
                    if (!PartColor.TryParseRgb(t.OutlineColor, out var outline) && !PartColor.TryParseHex(t.OutlineColor, out outline))
                        outline = System.Windows.Media.Color.FromRgb(0, 0, 0);
                    using var path = new GdiDrawing.GraphicsPath();
                    path.AddString(text, font.FontFamily, (int)font.Style, font.Size,
                        new Gdi.RectangleF(0, 0, w, h), format);
                    using var pen = new Gdi.Pen(Gdi.Color.FromArgb(outline.R, outline.G, outline.B), Math.Clamp(t.Outline, 0f, 16f) * 2f);
                    g.DrawPath(pen, path);
                    g.FillPath(brush, path);
                }
                else g.DrawString(text, font, brush, new Gdi.RectangleF(0, 0, w, h), format);
            }
            var data = bmp.LockBits(new Gdi.Rectangle(0, 0, w, h), GdiImaging.ImageLockMode.ReadOnly, GdiImaging.PixelFormat.Format32bppArgb);
            try
            {
                int tex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tex);
                // GDI memory is top-row-first BGRA: matches the decal UV convention as-is.
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0,
                    PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                TrilinearSampling();
                GL.BindTexture(TextureTarget.Texture2D, 0);
                return tex;
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception ex)
        {
            InitLog += $"Text render failed ({t.Text}): {ex.Message}\n";
            return 0;
        }
    }

    private void DrawDecal(SceneObject o, Matrix4 view, Matrix4 proj)
    {
        // Legacy single decal (pre-multi) + every list layer, then texts.
        // Tiling textures draw first (surface finish under decals).
        // Each layer lifts slightly further off the face so same-face stacks don't z-fight.
        // Layers append world-space quads into per-texture buckets (one draw
        // each at flush); the model matrix is composed once per part here.
        var model = ModelMatrix(o);
        int lift = 0;
        foreach (var tex in o.Textures)
            DrawTextureLayer(o, model, tex, lift++);
        if (!string.IsNullOrWhiteSpace(o.DecalImage))
            DrawDecalLayer(o, model, o.DecalImage, o.DecalFace, o.DecalTransparency, o.DecalBlur, lift++, 0, 0, 1);
        foreach (var decal in o.Decals)
            DrawDecalLayer(o, model, decal.Image, decal.Face, decal.Transparency, decal.Blur, lift++,
                decal.OffsetX, decal.OffsetY, decal.Scale);
        foreach (var text in o.Texts)
            DrawTextLayer(o, model, text, lift++);
    }

    /// <summary>Draw every bucketed quad: one upload + one draw per texture
    /// instead of per-layer programs, uniforms and binds.</summary>
    private void FlushDecals(Matrix4 view, Matrix4 proj)
    {
        if (_decalQuads == 0) return;
        _decalQuads = 0;
        GL.UseProgram(_decalProgram);
        var vp = view * proj;
        GL.UniformMatrix4(_decalVpLoc, false, ref vp);
        GL.Uniform1(_decalContrastLoc, Contrast);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.Uniform1(_decalImageLoc, 1);
        GL.BindVertexArray(_decalVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _decalVbo);
        foreach (var kv in _decalBuckets)
        {
            var quad = kv.Value;
            if (quad.Count == 0) continue;
            if (_decalUpload.Length < quad.Count)
            {
                int n = Math.Max(quad.Count, 65536);
                n--; n |= n >> 1; n |= n >> 2; n |= n >> 4; n |= n >> 8; n |= n >> 16; n++;
                _decalUpload = new float[n];
                _decalBufFloats = n;
                GL.BufferData(BufferTarget.ArrayBuffer, n * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }
            quad.CopyTo(_decalUpload);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, quad.Count * sizeof(float), _decalUpload);
            GL.BindTexture(TextureTarget.Texture2D, kv.Key);
            GL.DrawArrays(PrimitiveType.Triangles, 0, quad.Count / 11);
            quad.Clear();
        }
        GL.BindVertexArray(0);
    }

    /// <summary>Tiling image overlay: full-face quad, shader-side repeat + scroll.</summary>
    private void DrawTextureLayer(SceneObject o, Matrix4 model, TextureLayer t, int stackIndex)
    {
        if (string.IsNullOrWhiteSpace(t.Image)) return;
        int texture = GetPartTexture(t.Image);
        if (texture == 0) return;
        DrawFaceQuad(o, model, texture, t.Face, t.Transparency, 0f, 0f, 0f, 1f, 1f, stackIndex,
            Math.Clamp(t.TilingX, 0.1f, 32f), Math.Clamp(t.TilingY, 0.1f, 32f),
            Math.Clamp(t.OffsetX, -10f, 10f), Math.Clamp(t.OffsetY, -10f, 10f), true);
    }

    private void DrawDecalLayer(SceneObject o, Matrix4 model, string? imagePath, string face, float transparency, float blur, int stackIndex, float offsetX, float offsetY, float scale)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;
        int texture = GetDecalTexture(imagePath);
        if (texture == 0) return;
        DrawFaceQuad(o, model, texture, face, transparency, blur, offsetX, offsetY, scale, scale, stackIndex);
    }

    private void DrawTextLayer(SceneObject o, Matrix4 model, TextLayer t, int stackIndex)
    {
        int texture = GetTextTexture(t, out _);
        if (texture == 0) return;
        // Text intentionally stretches to the selected face, like a sign/decal.
        // This keeps a scale of 1 as a full-face label and avoids clipping long text.
        DrawFaceQuad(o, model, texture, t.Face, t.Transparency, t.Blur, t.OffsetX, t.OffsetY, t.Scale, t.Scale, stackIndex);
    }

    // Batched decal upload: quads grouped by texture (one draw per texture).
    // Buckets persist across frames (capacity retained, counts reset).
    private readonly Dictionary<int, List<float>> _decalBuckets = new();
    private float[] _decalUpload = Array.Empty<float>();
    private int _decalBufFloats = 65536; // 992 quads; grows geometrically on demand
    private int _decalQuads; // quads appended this frame (early-out when none)

    private void DrawFaceQuad(SceneObject o, Matrix4 model, int texture, string face, float transparency, float blur, float offsetX, float offsetY, float scaleX, float scaleY, int stackIndex, float tileX = 1f, float tileY = 1f, float tileOX = 0f, float tileOY = 0f, bool perStud = false)
    {
        if (texture == 0) return;
        // Backface culling for overlays: skip faces pointing away from the
        // camera (depth alone can't hide them on transparent/thin parts).
        Vector3 ln = face switch
        {
            "Back" => -Vector3.UnitZ,
            "Left" => -Vector3.UnitX,
            "Right" => Vector3.UnitX,
            "Top" => Vector3.UnitY,
            "Bottom" => -Vector3.UnitY,
            _ => Vector3.UnitZ,
        };
        var rot = RotationPart(o);
        // Local -> world with the render rotation (column-vector order:
        // identical to Vector3.Transform(v, Orientation) and to the
        // ModelMatrix the shader draws the quad with — verified numerically
        // for yaw/pitch/roll. The old row-major order was the transpose, so
        // on any rotated part the test normal pointed elsewhere (exactly
        // backwards at 90°), culling the overlay when looking straight at it.
        Vector3 wn = new(
            ln.X * rot.M11 + ln.Y * rot.M21 + ln.Z * rot.M31,
            ln.X * rot.M12 + ln.Y * rot.M22 + ln.Z * rot.M32,
            ln.X * rot.M13 + ln.Y * rot.M23 + ln.Z * rot.M33);
        Vector3 lo = new(
            ln.X * Math.Abs(o.Size.X) * 0.5f,
            ln.Y * Math.Abs(o.Size.Y) * 0.5f,
            ln.Z * Math.Abs(o.Size.Z) * 0.5f);
        Vector3 wc = o.Position + new Vector3(
            lo.X * rot.M11 + lo.Y * rot.M21 + lo.Z * rot.M31,
            lo.X * rot.M12 + lo.Y * rot.M22 + lo.Z * rot.M32,
            lo.X * rot.M13 + lo.Y * rot.M23 + lo.Z * rot.M33);
        if (Vector3.Dot(wn, Position - wc) <= 1e-4f) return;
        // Stack lift: each layer floats a hair further off the surface so
        // same-face stacks never z-fight. Index 0 keeps the legacy 0.501.
        float p = 0.501f + Math.Max(stackIndex, 0) * 0.0015f;
        // Face-local frame (origin at UV 0,0; U right, V up). Matches the legacy
        // full-face winding exactly at offset 0 / scale 1.
        Vector3 O, U, V;
        switch (face)
        {
            case "Back":   O = new(.5f, -.5f, -p); U = new(-1f, 0, 0); V = new(0, 1f, 0); break;
            // Left/Right U runs toward screen-right (same coverage as before,
            // mirrored): texture-right must appear screen-right, like Front.
            case "Left":   O = new(-p, -.5f, -.5f); U = new(0, 0, 1f);  V = new(0, 1f, 0); break;
            case "Right":  O = new(p, -.5f, .5f); U = new(0, 0, -1f);   V = new(0, 1f, 0); break;
            case "Top":    O = new(-.5f, p, .5f); U = new(1f, 0, 0);   V = new(0, 0, -1f); break;
            case "Bottom": O = new(-.5f, -p, -.5f); U = new(1f, 0, 0);  V = new(0, 0, 1f); break;
            default:       O = new(-.5f, -.5f, p); U = new(1f, 0, 0);   V = new(0, 1f, 0); break; // Front
        }
        float sx = Math.Clamp(scaleX <= 0 ? 1f : scaleX, 0.05f, 1f);
        float sy = Math.Clamp(scaleY <= 0 ? 1f : scaleY, 0.05f, 1f);
        float cu = 0.5f + Math.Clamp(offsetX, -1f, 1f);
        float cv = 0.5f + Math.Clamp(offsetY, -1f, 1f);
        float u0 = cu - sx / 2f, u1 = cu + sx / 2f;
        float v0 = cv - sy / 2f, v1 = cv + sy / 2f;
        // UVs track the unclamped rect (edge sampler clamps); V flipped per convention.
        // Corners, inlined (was a capturing local function: one closure alloc per layer per frame).
        float qx0 = Math.Clamp(u0, 0f, 1f), qx1 = Math.Clamp(u1, 0f, 1f);
        float qy0 = Math.Clamp(v0, 0f, 1f), qy1 = Math.Clamp(v1, 0f, 1f);
        // Corners to world (one model transform per layer instead of per-vertex
        // in the shader). No array garbage: straight-line adds per vertex.
        Vector3 wa = Vector3.TransformPosition(O + U * qx0 + V * qy0, model);
        Vector3 wb = Vector3.TransformPosition(O + U * qx1 + V * qy0, model);
        Vector3 wc2 = Vector3.TransformPosition(O + U * qx1 + V * qy1, model);
        Vector3 wd = Vector3.TransformPosition(O + U * qx0 + V * qy1, model);
        // Material-style tiling: tile counts multiply by face size in studs
        // (1 = one tile per stud, like materials). Decals/text keep per-face UV.
        float su = 1f, sv = 1f;
        if (perStud)
        {
            if (face is "Left" or "Right") { su = Math.Abs(o.Size.Z); sv = Math.Abs(o.Size.Y); }
            else if (face is "Top" or "Bottom") { su = Math.Abs(o.Size.X); sv = Math.Abs(o.Size.Z); }
            else { su = Math.Abs(o.Size.X); sv = Math.Abs(o.Size.Y); }
        }
        float tu = Math.Max(tileX * su, 0.05f), tv = Math.Max(tileY * sv, 0.05f);
        float tr = Math.Clamp(transparency, 0f, 1f), bl = Math.Clamp(blur, 0f, 1f);
        if (!_decalBuckets.TryGetValue(texture, out var bucket))
        {
            bucket = new List<float>(66);
            _decalBuckets[texture] = bucket;
        }
        // Triangles a/b/c + a/c/d; UVs track the unclamped rect (V flipped).
        QuadVert(bucket, wa, u0, 1f - v0, tr, bl, tu, tv, tileOX, tileOY);
        QuadVert(bucket, wb, u1, 1f - v0, tr, bl, tu, tv, tileOX, tileOY);
        QuadVert(bucket, wc2, u1, 1f - v1, tr, bl, tu, tv, tileOX, tileOY);
        QuadVert(bucket, wa, u0, 1f - v0, tr, bl, tu, tv, tileOX, tileOY);
        QuadVert(bucket, wc2, u1, 1f - v1, tr, bl, tu, tv, tileOX, tileOY);
        QuadVert(bucket, wd, u0, 1f - v1, tr, bl, tu, tv, tileOX, tileOY);
        _decalQuads++;
    }

    /// <summary>One interleaved decal vertex: pos(3) uv(2) a(trans,blur,tile)(4) b(offset)(2).</summary>
    private static void QuadVert(List<float> b, Vector3 p, float u, float v,
        float tr, float bl, float tu, float tv, float ox, float oy)
    {
        b.Add(p.X); b.Add(p.Y); b.Add(p.Z);
        b.Add(u); b.Add(v);
        b.Add(tr); b.Add(bl); b.Add(tu); b.Add(tv);
        b.Add(ox); b.Add(oy);
    }

    /// <summary>Skybox centered on the eye (main camera or a mirror).
    /// No depth writes, no culling: cubes always draw over it.</summary>
    private void DrawSky(Vector3 eye, Matrix4 view, Matrix4 proj, double now)
    {
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);
        GL.UseProgram(_skyProgram);
        var skyModel = Matrix4.CreateScale(SkyHalf) * Matrix4.CreateTranslation(eye);
        var skyMvp = skyModel * view * proj;
        GL.UniformMatrix4(_skyMvpLoc, false, ref skyMvp);
        GL.Uniform3(_skySunDirLoc, SunDirection);
        GL.Uniform3(_skySunTintLoc, SunColor);
        GL.Uniform1(_skySunILoc, SunIntensity);
        GL.Uniform1(_skyHazeLoc, SkyHaze);
        GL.Uniform3(_skyMoonDirLoc, MoonDirection);
        GL.Uniform1(_skyTimeLoc, (float)(now % 7200.0)); // wraps every 2h: float stays precise
        GL.Uniform1(_skyContrastLoc, Contrast);
        GL.Uniform3(_skyTintLoc, SkyTint);
        GL.Uniform1(_skyStarLoc, StarAmount);
        GL.Uniform1(_skyCloudLoc, CloudAmount);
        GL.Uniform1(_skySunSizeLoc, SunSize);
        GL.Uniform1(_skyStripesLoc, SunStripes ? 1f : 0f);
        GL.Uniform1(_skyStyleLoc, (float)SkyStyle); // float uniform: the int overload would silently no-op
        GL.BindVertexArray(_skyVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        GL.Enable(EnableCap.CullFace);
        GL.DepthMask(true);
    }

    /// <summary>Scene-wide part-program uniforms (main + mirror passes share this).</summary>
    private void SetSharedUniforms(Vector3 camPos, Vector4 clip, double now, Matrix4 lightVp, bool reflOn)
    {
        GL.UseProgram(_program);
        GL.Uniform3(_fogColorLoc, FogColor);
        GL.Uniform3(_sunDirLoc, SunDirection);
        GL.Uniform3(_sunColorLoc, SunColor);
        GL.Uniform1(_sunILoc, SunIntensity);
        GL.Uniform1(_ambientLoc, AmbientBoost);
        GL.Uniform1(_fogDensityLoc, FogDensity);
        GL.Uniform1(_contrastLoc, Contrast);
        GL.Uniform3(_camLoc, camPos);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _shadowTex);
        GL.Uniform1(_shadowMapLoc, 0);
        GL.UniformMatrix4(_lightVpLoc, false, ref lightVp);
        GL.Uniform1(_shadowsOnLoc, ShadowsEnabled ? 1f : 0f);
        GL.Uniform1(_shadowSizeLoc, (float)ShadowMapSize);
        GL.Uniform1(_shadowDistanceLoc, ShadowDistance);
        GL.Uniform1(_timeLoc, (float)(now % 7200.0));
        GL.Uniform1(_waterLoc, 0f);
        GL.Uniform1(_reflOnLoc, reflOn ? 1f : 0f);
        GL.Uniform4(_clipLoc, clip);

        // Dynamic lights from the gather above (array slots are contiguous).
        GL.Uniform1(_ptCountLoc, _litPoints.Count);
        for (int i = 0; i < _litPoints.Count; i++)
        {
            var o = _litPoints[i];
            GL.Uniform3(_ptPosLoc + i, o.Position);
            var c = o.Color;
            GL.Uniform3(_ptColorLoc + i, new Vector3(c.R * o.Brightness, c.G * o.Brightness, c.B * o.Brightness));
            GL.Uniform3(_ptParamsLoc + i, new Vector3(Math.Max(o.Range, 0.5f), 0f, 0f));
        }
    }

    /// <summary>Mirror pass per visible pool (half res, sky + opaque only, water
    /// excluded). Fills each part's reflection texture; skipped when toggled off.</summary>
    private void RenderReflectionPasses(Matrix4 proj, Matrix4 lightVp, double now, int pixelWidth, int pixelHeight)
    {
        _reflVpByWater.Clear();
        if (!WaterReflections) return;
        // Frustum gate: no visible pool, no mirror work (behind-camera and
        // offscreen water costs nothing now).
        bool anyVisible = false;
        foreach (var o in Objects)
        {
            if (!o.IsWater || o.Hidden || o.Shape == ShapeKind.None) continue;
            if (InView(o.Position, o.Size.Length * 0.5f, pixelWidth, pixelHeight)) { anyVisible = true; break; }
        }
        if (!anyVisible) return;
        EnsureReflectionTarget(pixelWidth / 2, pixelHeight / 2);
        // Save the control's framebuffer (GLWpfControl renders offscreen:
        // hardcoding 0 blanks the main image, like the shadow pass avoids).
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _reflFbo);
        GL.Viewport(0, 0, _reflW, _reflH);
        GL.Enable(EnableCap.ClipDistance0); // part shader clips below each plane
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.DepthMask(true);
        foreach (var w in Objects)
        {
            if (!w.IsWater || w.Hidden || w.Shape == ShapeKind.None) continue;
            if (!InView(w.Position, w.Size.Length * 0.5f, pixelWidth, pixelHeight)) continue;
            // Mirror across this pool's top face (works at any orientation).
            Vector3 n = Vector3.Transform(Vector3.UnitY, w.Orientation);
            if (n.LengthSquared < 1e-8f) n = Vector3.UnitY;
            n = Vector3.Normalize(n);
            Vector3 c = w.Position + n * (Math.Abs(w.Size.Y) * 0.5f);
            Vector3 eyeM = ReflectPoint(Position, n, c);
            Vector3 tgtM = ReflectPoint(Position + Forward, n, c);
            Vector3 upM = ReflectDir(Vector3.UnitY, n);
            Vector3 vd = tgtM - eyeM;
            if (vd.LengthSquared > 1e-8f && Math.Abs(Vector3.Dot(
                    vd / vd.Length, upM.LengthSquared > 1e-8f ? upM / upM.Length : Vector3.UnitY)) > 0.999f)
                upM = ReflectDir(Vector3.UnitZ, n); // looking straight down the mirror: pick another up
            var viewM = Matrix4.LookAt(eyeM, tgtM, upM);
            GL.ClearColor(FogColor.X, FogColor.Y, FogColor.Z, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.FrontFace(FrontFaceDirection.Cw); // mirrored view flips winding
            SetSharedUniforms(eyeM, new Vector4(n, -Vector3.Dot(n, c)), now, lightVp, false);
            DrawSky(eyeM, viewM, proj, now);
            foreach (var o in Objects)
            {
                if (o.Hidden || o.IsWater || o.Shape == ShapeKind.None) continue;
                // Opaque only in the mirror (matches the main opaque test).
                if (MaterialParams.Of(o.Material).Transparent || o.Transparency > 0.001f) continue;
                DrawObject(o, viewM, proj, now);
            }
            foreach (var o in AvatarRigParts)
            {
                if (o.Hidden) continue;
                DrawObject(o, viewM, proj, now);
            }
            GL.FrontFace(FrontFaceDirection.Ccw);
            _reflVpByWater[w] = viewM * proj;
        }
        GL.Disable(EnableCap.ClipDistance0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
        GL.Viewport(0, 0, pixelWidth, pixelHeight);
    }

    public void Render(int pixelWidth, int pixelHeight)
    {
        if (!_initialized || pixelWidth <= 0 || pixelHeight <= 0) return;
        _texFrame++; // texture LRU clock: only rendered frames age entries
        long t0 = _clock.ElapsedTicks; // CPU submit time (GPU runs async behind this)

        double now = _clock.Elapsed.TotalSeconds;
        double dt = now - _lastTime;
        _lastTime = now;
        if (dt > 0) Fps = Fps * 0.95 + (1.0 / Math.Max(dt, 1e-4)) * 0.05;
        // True frame time (hunger-clamped): the exp easing below is stable for any
        // dt, and clamping tight (e.g. 0.05) makes the camera trail the mouse at
        // low fps instead of catching up with the frames you get.
        float h = Math.Clamp((float)dt, 0f, 0.25f);
        if (!PhysicsDriven)
            foreach (var o in Objects) o.ComposeOrientation();

        // Ease look toward its target (frame-rate independent).
        float lookT = 1f - MathF.Exp(-LookResponsiveness * h);
        _yaw += (_targetYaw - _yaw) * lookT;
        _pitch += (_targetPitch - _pitch) * lookT;
        if ((_targetYaw - _yaw) * (_targetYaw - _yaw) + (_targetPitch - _pitch) * (_targetPitch - _pitch) < 1e-10f)
        {
            _yaw = _targetYaw; // snap settled values so IsIdle goes exactly true
            _pitch = _targetPitch;
        }

        // Ramp fly velocity toward desired (smooth starts/stops), then integrate.
        var desired = (Forward * FlyInput.Z + Right * FlyInput.X + Vector3.UnitY * FlyInput.Y)
                      * MoveSpeed * (FlySlow ? SlowMultiplier : 1f);
        float moveT = 1f - MathF.Exp(-MoveResponsiveness * h);
        _flyVel += (desired - _flyVel) * moveT;
        if (_flyVel.LengthSquared < 1e-10f) _flyVel = Vector3.Zero;
        Position += _flyVel * h;

        // Decay the wheel-zoom impulse and integrate, keeping out of the cube.
        if (_zoomVel != 0f)
        {
            var next = Position + Forward * (_zoomVel * h);
            if (next.Length < 1.5f || next.Length > 200f)
            {
                next = Position;
                _zoomVel = 0f;
            }
            Position = next;
            _zoomVel *= MathF.Exp(-ZoomDecay * h);
            if (MathF.Abs(_zoomVel) < 0.01f) _zoomVel = 0f;
        }

        // Roblox-style follow: the orbit angles (yaw/pitch) + wheel distance sit
        // rigidly on the avatar (no trailing offset). Runs before the view matrix
        // so there is zero frame lag. The boom slams in on occlusion (never clips)
        // but eases back out, so crash debris crossing the boom doesn't strobe
        // the view. A cheap floor slide keeps the camera out of the ground (no
        // full camera collision yet). Frozen while the avatar is vanished
        // (void death): the view holds instead of snapping to spawn.
        if (PhysicsDriven && FollowTarget != null && !FreezeFollow)
        {
            _targetPitch = Math.Clamp(_targetPitch, -75f, 80f);
            _pitch = Math.Clamp(_pitch, -75f, 80f);
            var focus = FollowTarget.Position + new Vector3(0, FollowHeight, 0);
            float wantDist = Math.Max(FollowDistance, 0.6f);
            float target = (CollideCamera(focus, focus - Forward * wantDist) - focus).Length;
            if (target < _camDist) _camDist = target; // slam in: never clip into walls
            else _camDist += (Math.Min(target, wantDist) - _camDist) * (1f - MathF.Exp(-5f * h));
            _camDist = Math.Clamp(_camDist, 0.6f, Math.Max(wantDist, 0.6f));
            var wantPos = focus - Forward * _camDist;
            if (wantPos.Y < 0.25f && focus.Y > 0.25f)
            {
                float a = (focus.Y - 0.25f) / (focus.Y - wantPos.Y);
                wantPos = focus + (wantPos - focus) * Math.Clamp(a, 0.05f, 1f);
            }
            Position = wantPos;
        }

        GL.Viewport(0, 0, pixelWidth, pixelHeight);
        GL.ClearColor(FogColor.X, FogColor.Y, FogColor.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var view = Matrix4.LookAt(Position, Position + Forward, Vector3.UnitY);
        var proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FovDeg), pixelWidth / (float)pixelHeight, 0.1f, 500f);

        // Sun depth prepass into the shadow map (skipped when toggled off).
        // Follow frustum: the shadow box (half-extent = ShadowDistance) travels
        // with the action (avatar in play, selection — or view focus — in edit)
        // so distant objects keep shadows at full resolution. The focus snaps to
        // the texel grid so the traveling box doesn't shimmer shadow edges; a
        // cell change re-renders the cached map.
        Vector3 wantFocus = (PhysicsDriven && FollowTarget != null && !FreezeFollow) ? FollowTarget.Position
            : Selected != null ? Selected.Position
            : Position + Forward * 12f;
        {
            float texel = 2f * ShadowDistance / ShadowMapSize;
            var side = Vector3.Normalize(Vector3.Cross(SunDirection, Vector3.UnitY));
            var up2 = Vector3.Normalize(Vector3.Cross(side, SunDirection));
            wantFocus += side * (MathF.Round(Vector3.Dot(wantFocus, side) / texel) * texel - Vector3.Dot(wantFocus, side))
                       + up2 * (MathF.Round(Vector3.Dot(wantFocus, up2) / texel) * texel - Vector3.Dot(wantFocus, up2));
        }
        if ((wantFocus - _lastShadowFocus).LengthSquared > 1e-10f)
        {
            _lastShadowFocus = wantFocus;
            InvalidateShadows();
        }
        var lightDist = 40f + ShadowDistance;
        var lightView = Matrix4.LookAt(wantFocus + SunDirection * lightDist, wantFocus, Vector3.UnitY);
        var lightProj = Matrix4.CreateOrthographicOffCenter(
            -ShadowDistance, ShadowDistance, -ShadowDistance, ShadowDistance, 1f, 80f + 4f * ShadowDistance);
        var lightVp = lightView * lightProj;
        // Cached: re-render depth only when parts move/change or the follow
        // focus hops a texel cell (plain camera motion stays free otherwise).
        if (WantedShadowSize() != ShadowMapSize) RecreateShadowTarget();
        if (ShadowsEnabled && _shadowDirty)
        {
            int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
            GL.Viewport(0, 0, ShadowMapSize, ShadowMapSize);
            GL.Clear(ClearBufferMask.DepthBufferBit);
            GL.UseProgram(_depthProgram);
            // Push caster depth slightly away from the receiver to prevent
            // precision noise from shadowing the caster's own surface.
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(1.5f, 2.0f);
            // Default back-face culling: front faces land in the map, keeping
            // contacts tight (back faces + big bias detached the shadows).
            foreach (var o in Objects)
            {
                if (o.Hidden) continue; // void-dead avatar: casts nothing
                if (o.Shape == ShapeKind.None) continue; // lights never block sunlight
                if (MaterialParams.Of(o.Material).Transparent) continue; // glass casts none
                if (o.Transparency >= 0.99f) continue; // effectively invisible casts none
                if (!o.CastShadow) continue; // per-part opt-out (still receives)
                var lm = ModelMatrix(o) * lightVp;
                GL.UniformMatrix4(_depthMvpLoc, false, ref lm);
                var mesh = MeshFor(o);
                GL.BindVertexArray(mesh.Vao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, mesh.Count);
            }
            // Beta rig casts too (opaque limbs only; never transparent).
            foreach (var o in AvatarRigParts)
            {
                if (o.Hidden || !o.CastShadow) continue;
                var lm = ModelMatrix(o) * lightVp;
                GL.UniformMatrix4(_depthMvpLoc, false, ref lm);
                var mesh = MeshFor(o);
                GL.BindVertexArray(mesh.Vao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, mesh.Count);
            }
            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
            GL.Viewport(0, 0, pixelWidth, pixelHeight);
            _shadowDirty = false;
        }

        // Dynamic lights: nearest 16 point lights feed the shader loop.
        _litPoints.Clear();
        foreach (var o in Objects)
        {
            if (o.Shape != ShapeKind.None || o.Light == LightKind.None) continue;
            _litPoints.Add(o);
        }
        // Cap + sort nearest-first (insertion sort, no allocs).
        CapLights(_litPoints);

        // Skybox, centered on the eye. No depth writes, no culling
        // (seen from inside), so cubes always draw over it regardless of distance.
        DrawSky(Position, view, proj, now);

        // Planar water reflections before the main image (mirrored passes
        // fill each pool's texture; skipped entirely when toggled off).
        RenderReflectionPasses(proj, lightVp, now, pixelWidth, pixelHeight);

        GL.UseProgram(_program);
        SetSharedUniforms(Position, new Vector4(0f, 0f, 0f, 0f), now, lightVp,
            WaterReflections && _reflVpByWater.Count > 0);

        // Opaque pass, then transparents far-to-near (sorted, no per-frame garbage).
        // Anything with effective transparency joins the sorted pass.
        // The beta rig rides along (opaque limbs; Hidden follows void death).
        _transparent.Clear();
        foreach (var o in Objects)
        {
            if (o.Hidden) continue; // void-dead avatar: draws nothing
            if (MaterialParams.Of(o.Material).Transparent || o.Transparency > 0.001f)
                _transparent.Add(o);
            else DrawObject(o, view, proj, now);
        }
        foreach (var o in AvatarRigParts)
        {
            if (o.Hidden) continue;
            DrawObject(o, view, proj, now);
        }
        SortTransparentFarToNear();
        GL.DepthMask(false); // transparents test depth but don't write it
        GL.Enable(EnableCap.Blend); // without this, alpha is ignored and transparents draw opaque
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        foreach (var o in _transparent) DrawObject(o, view, proj, now);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.BindVertexArray(0);

        // Edit-mode spawn markers: bobbing neon dots above Spawn parts
        // (plain lit ball, no scene membership, casts no shadow).
        if (!PhysicsDriven)
        {
            GL.UseProgram(_program);
            float bob = 0.15f * MathF.Sin((float)now * 3f);
            var markerNormal = Matrix3.Identity;
            foreach (var o in Objects)
            {
                if (!o.IsSpawn) continue;
                var mPos = o.Position + Vector3.Transform(
                    new Vector3(0, o.Size.Y * 0.5f + 0.6f + bob, 0), o.Orientation);
                var model = Matrix4.CreateScale(0.4f) * Matrix4.CreateTranslation(mPos);
                var mvp = model * view * proj;
                GL.UniformMatrix4(_mvpLoc, false, ref mvp);
                GL.UniformMatrix4(_modelLoc, false, ref model);
                GL.UniformMatrix3(_normalLoc, false, ref markerNormal);
                GL.Uniform3(_colorLoc, new Vector3(0.3f, 1f, 0.4f));
                GL.Uniform1(_metallicLoc, 0f);
                GL.Uniform1(_shininessLoc, 32f);
                GL.Uniform1(_opacityLoc, 1f);
                GL.Uniform1(_emissiveLoc, 1.5f);
                GL.Uniform1(_waterLoc, 0f); // markers never ripple/sample (shared program)
                GL.Uniform1(_pulseLoc, 0f);
                var markerMesh = _meshes[ShapeKind.Ball];
                GL.BindVertexArray(markerMesh.Vao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, markerMesh.Count);
            }
            // Edit-mode waypoint balls: every mover's loop track (hidden in play).
            var wpMesh = _meshes[ShapeKind.Ball];
            foreach (var o in Objects)
            {
                if (!o.IsMovingPlatform || o.Waypoints.Count == 0 || o.Hidden) continue;
                for (int wi = 0; wi < o.Waypoints.Count; wi++)
                {
                    var w = o.Waypoints[wi];
                    var model = Matrix4.CreateScale(0.35f) * Matrix4.CreateTranslation(w);
                    var mvp = model * view * proj;
                    GL.UniformMatrix4(_mvpLoc, false, ref mvp);
                    GL.UniformMatrix4(_modelLoc, false, ref model);
                    GL.UniformMatrix3(_normalLoc, false, ref markerNormal);
                    GL.Uniform3(_colorLoc, new Vector3(0.3f, 0.9f, 1f));
                    GL.Uniform1(_metallicLoc, 0f);
                    GL.Uniform1(_shininessLoc, 32f);
                    GL.Uniform1(_opacityLoc, 1f);
                    GL.Uniform1(_emissiveLoc, 1.5f);
                    GL.Uniform1(_pulseLoc, 0f);
                    GL.BindVertexArray(wpMesh.Vao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, wpMesh.Count);
                }
            }
            GL.BindVertexArray(0);
        }

        // Toolbox drag ghost: translucent placement preview, depth-tested so it
        // sits correctly on surfaces. Reuses the shape table + main program.
        if (_ghostShape is { } gshape && _ghostSize.X > 0f && _ghostSize.Y > 0f && _ghostSize.Z > 0f &&
            _meshes.TryGetValue(gshape, out var gmesh))
        {
            GL.UseProgram(_program);
            var gmodel = Matrix4.CreateScale(_ghostSize) * Matrix4.CreateTranslation(_ghostPos);
            var gmvp = gmodel * view * proj;
            GL.UniformMatrix4(_mvpLoc, false, ref gmvp);
            GL.UniformMatrix4(_modelLoc, false, ref gmodel);
            var gnm = new Matrix3(
                1f / _ghostSize.X, 0, 0,
                0, 1f / _ghostSize.Y, 0,
                0, 0, 1f / _ghostSize.Z);
            GL.UniformMatrix3(_normalLoc, false, ref gnm);
            GL.Uniform3(_colorLoc, _ghostColor);
            GL.Uniform1(_texLoc, 2);
            GL.Uniform1(_useTexLoc, 0f);
            GL.Uniform1(_metallicLoc, 0f);
            GL.Uniform1(_shininessLoc, 24f);
            GL.Uniform1(_opacityLoc, 0.45f);
            GL.Uniform1(_emissiveLoc, 0.3f);
        GL.Uniform1(_reflectanceLoc, 0f);
        GL.Uniform1(_glossLoc, 1f);
        GL.Uniform1(_waterLoc, 0f); // ghost never ripples/samples (shared program)
        GL.Uniform1(_pulseLoc, 0f);
        GL.BindVertexArray(gmesh.Vao);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);
            GL.DrawArrays(PrimitiveType.Triangles, 0, gmesh.Count);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.BindVertexArray(0);
        }

        // Decals are independent textured overlays, projected onto the chosen
        // local part face just above its surface to avoid z-fighting.
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.DepthMask(false);
        // Slope-scaled depth bias for the quads: the geometric lift alone falls
        // inside depth precision noise at distance (even striping), while this
        // holds at any range. Scoped to this pass only.
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(-2f, -2f);
        foreach (var o in Objects)
        {
            if (o.Hidden) continue;
            DrawDecal(o, view, proj);
        }
        FlushDecals(view, proj);
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.CullFace);

        // Particle sprites: CPU sim + point draw, depth-tested, no depth writes.
        DrawParticles(h, view, proj, pixelHeight);

        // Selection + hover outlines: Roblox-style wire boxes, depth-tested so
        // hidden parts stay hidden. Blue = selected, yellow = hovered.
        // Exact part size (no inflation): LEQUAL depth lets the lines win the
        // tie against their own faces deterministically, so no flicker.
        // Waypoint balls use the same language: blue box = selected ball,
        // yellow box = hovered ball.
        bool wpSel = SelWaypointObj != null && SelWaypointIndex >= 0 &&
            SelWaypointObj.Waypoints.Count > SelWaypointIndex &&
            Objects.Contains(SelWaypointObj);
        bool wpHov = !wpSel && HoverWaypointObj != null && HoverWaypointIndex >= 0 &&
            HoverWaypointObj.Waypoints.Count > HoverWaypointIndex &&
            Objects.Contains(HoverWaypointObj);
        if (_boxCount > 0 && (SelectedObjects.Count > 0 || Selected != null || HoverObject != null || wpSel || wpHov))
        {
            bool boxBound = false;
            void DrawBoxAt(Vector3 center, Quaternion orientation, Vector3 size, Vector3 col)
            {
                var m = Matrix4.CreateScale(size) *
                        Matrix4.CreateFromQuaternion(orientation) *
                        Matrix4.CreateTranslation(center);
                var bm = m * view * proj;
                if (!boxBound)
                {
                    GL.UseProgram(_gizmoProgram);
                    GL.BindVertexArray(_boxVao);
                    GL.LineWidth(2f);
                    GL.DepthFunc(DepthFunction.Lequal);
                    boxBound = true;
                }
                GL.UniformMatrix4(_gizmoMvpLoc, false, ref bm);
                GL.Uniform3(_gizmoColorLoc, col);
                GL.DrawArrays(PrimitiveType.Lines, 0, (int)_boxCount);
            }
            void DrawMeshOutline(SceneObject o, Vector3 col)
            {
                // Contour lines only: edges where a front face meets a back
                // face this frame, plus open boundary edges. No interior
                // edges, no fill. Occluded lines die in the depth test.
                if (o.Hidden || o.MeshPath == null) return;
                var sil = SilhouetteFor(o.MeshPath);
                if (sil == null || sil.Points.Length == 0)
                {
                    // Unreadable/huge: box fallback (direct: DrawBox recurses here).
                    DrawBoxAt(o.Position, o.Orientation, new Vector3(
                            Math.Abs(o.Size.X), Math.Abs(o.Size.Y), Math.Abs(o.Size.Z)), col);
                    return;
                }
                // Camera direction in part-local space (rotation transpose = inverse).
                var rot = RotationPart(o);
                Vector3 toCam = Position - o.Position;
                Vector3 ld = new(
                    toCam.X * rot.M11 + toCam.Y * rot.M21 + toCam.Z * rot.M31,
                    toCam.X * rot.M12 + toCam.Y * rot.M22 + toCam.Z * rot.M32,
                    toCam.X * rot.M13 + toCam.Y * rot.M23 + toCam.Z * rot.M33);
                if (ld.LengthSquared < 1e-8f) return;
                ld = Vector3.Normalize(ld);
                var model = ModelMatrix(o);
                int edgeCount = sil.Points.Length / 2;
                int need = edgeCount * 6;
                if (_silVerts.Length < need) _silVerts = new float[need];
                int n = 0;
                for (int e = 0; e < edgeCount; e++)
                {
                    int t0 = sil.EdgeTris[e * 2], t1 = sil.EdgeTris[e * 2 + 1];
                    if (t1 >= 0)
                    {
                        bool f0 = Vector3.Dot(sil.Normals[t0], ld) > 0f;
                        bool f1 = Vector3.Dot(sil.Normals[t1], ld) > 0f;
                        if (f0 == f1) continue; // interior edge this frame
                    }
                    var w0 = Vector3.TransformPosition(sil.Points[e * 2], model);
                    var w1 = Vector3.TransformPosition(sil.Points[e * 2 + 1], model);
                    _silVerts[n++] = w0.X; _silVerts[n++] = w0.Y; _silVerts[n++] = w0.Z;
                    _silVerts[n++] = w1.X; _silVerts[n++] = w1.Y; _silVerts[n++] = w1.Z;
                }
                if (n == 0) return;
                if (!boxBound)
                {
                    GL.UseProgram(_gizmoProgram);
                    GL.LineWidth(2f);
                    GL.DepthFunc(DepthFunction.Lequal);
                    boxBound = true;
                }
                var wm = view * proj; // verts already world-space
                GL.UniformMatrix4(_gizmoMvpLoc, false, ref wm);
                GL.Uniform3(_gizmoColorLoc, col);
                GL.BindVertexArray(_silVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _silVbo);
                GL.BufferData(BufferTarget.ArrayBuffer, n * sizeof(float), _silVerts, BufferUsageHint.DynamicDraw);
                GL.DrawArrays(PrimitiveType.Lines, 0, n / 3);
                GL.BindVertexArray(_boxVao);
            }
            void DrawBox(SceneObject o, Vector3 col)
            {
                if (o.Hidden) return;
                if (o.Shape == ShapeKind.Mesh && !string.IsNullOrWhiteSpace(o.MeshPath) &&
                    !_meshFailures.Contains(o.MeshPath!)) { DrawMeshOutline(o, col); return; }
                DrawBoxAt(o.Position, o.Orientation, new Vector3(
                        Math.Abs(o.Size.X), Math.Abs(o.Size.Y), Math.Abs(o.Size.Z)), col);
            }
            var selectBlue = new Vector3(0.10f, 0.60f, 1.0f);
            foreach (var o in SelectedObjects) DrawBox(o, selectBlue);
            if (Selected != null && !SelectedObjects.Contains(Selected))
                DrawBox(Selected, selectBlue);
            if (HoverObject != null && !SelectedObjects.Contains(HoverObject) &&
                !ReferenceEquals(HoverObject, Selected))
                DrawBox(HoverObject, new Vector3(1.0f, 0.78f, 0.08f));
            if (wpSel)
                DrawBoxAt(SelWaypointObj!.Waypoints[SelWaypointIndex], Quaternion.Identity,
                    new Vector3(0.55f, 0.55f, 0.55f), selectBlue);
            else if (wpHov)
                DrawBoxAt(HoverWaypointObj!.Waypoints[HoverWaypointIndex], Quaternion.Identity,
                    new Vector3(0.55f, 0.55f, 0.55f), new Vector3(1.0f, 0.78f, 0.08f));
            if (boxBound)
            {
                GL.BindVertexArray(0);
                GL.LineWidth(1f);
                GL.DepthFunc(DepthFunction.Less);
            }
        }

        // Active tool's gizmo: constant screen size, drawn last without depth/culling (always on top).
        // Lights show no rings (a point needs no orientation).
        bool selectedIsLight = Selected is { Shape: ShapeKind.None, Light: not LightKind.None };
        if (Selected != null && ActiveGizmo != GizmoKind.None &&
            !(selectedIsLight && ActiveGizmo == GizmoKind.Rotate))
        {
            var vaos = ActiveGizmo == GizmoKind.Scale ? _scaleVaos
                     : ActiveGizmo == GizmoKind.Rotate ? _ringVaos : _gizmoVaos;
            var counts = ActiveGizmo == GizmoKind.Scale ? _scaleCounts
                       : ActiveGizmo == GizmoKind.Rotate ? _ringCounts : _gizmoCounts;
            float gs = GizmoScale(pixelHeight);
            // Scale balls size from CAMERA DISTANCE only (never part size):
            // sub-linear (sqrt) falloff, so zooming out shrinks them on screen
            // gently instead of holding a fixed pixel size (classic) or tracking
            // the world 1:1 (vanishes far away). 0.62 base keeps them small like
            // the reference; clamped both ends so close zoom never balloons and
            // far balls stay grabbable (the pick window stays full-size anyway).
            float handleGs = gs;
            if (ActiveGizmo == GizmoKind.Scale && Selected != null && gs > 0f)
            {
                float gdist = Math.Max((GizmoPivot - Position).Length, 0.001f);
                const float refDist = 15f; // zoom where balls match 0.62x classic
                float f = Math.Clamp(MathF.Sqrt(refDist / gdist), 0.35f, 1.25f);
                handleGs = gs * 0.62f * f;
            }
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.UseProgram(_gizmoProgram);
            // Move arrows + scale spheres sit ON the selection's faces (one per
            // face, Roblox-style), not at the pivot; rings stay centered.
            // Mirroring flips winding, which is fine here.
            // Handles follow the selection set's centroid and the active orientation.
            var grot = Matrix4.CreateFromQuaternion(GizmoOrientation);
            int sides = ActiveGizmo == GizmoKind.Rotate ? 1 : 2;
            for (int a = 0; a < 3; a++)
            {
                // Hovered handle goes yellow, dragged handle pale yellow; else axis color.
                Vector3 col = a == ActiveAxis ? new Vector3(1.0f, 0.95f, 0.45f)
                            : a == HoverAxis ? new Vector3(1.0f, 0.78f, 0.08f)
                            : AxisColors[a];
                GL.Uniform3(_gizmoColorLoc, col);
                GL.BindVertexArray(vaos[a]);
                for (int side = 0; side < sides; side++)
                {
                    float s = side == 0 ? 1f : -1f;
                    float g = ActiveGizmo == GizmoKind.Scale ? handleGs : gs;
                    Vector3 sc = a == 0 ? new Vector3(s * g, g, g)
                                : a == 1 ? new Vector3(g, s * g, g)
                                : new Vector3(g, g, s * g);
                    // Rotate rings center on the pivot; move/scale handles sit on
                    // the selection's faces, Roblox-style.
                    Vector3 origin = ActiveGizmo == GizmoKind.Rotate
                        ? GizmoPivot
                        : GizmoPivot + GizmoAxis(a) * (HalfExtent(a) * s);
                    var m = Matrix4.CreateScale(sc) * grot * Matrix4.CreateTranslation(origin);
                    var gm = m * view * proj;
                    GL.UniformMatrix4(_gizmoMvpLoc, false, ref gm);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, (int)counts[a]);
                }
            }
            GL.BindVertexArray(0);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
        }

        // LinearVelocity direction arrow on the active part: cyan, always on top,
        // length grows with speed. Display only (not pickable, not draggable).
        // Direction is part-local: rotate it by the part so the arrow matches
        // the Play-mode thrust.
        var velSel = Selected;
        if (velSel != null && velSel.HasVelocity && velSel.Shape != ShapeKind.None && _velCount > 0)
        {
            Vector3 vd = Vector3.Transform(velSel.VelocityDirection, velSel.Orientation);
            float vlen = vd.Length;
            if (vlen > 1e-6f && float.IsFinite(vlen))
            {
                Vector3 dir = vd / vlen;
            float gs = GizmoScale(pixelHeight);
                if (gs > 0)
                {
                    float len = gs * Math.Clamp(0.8f + velSel.VelocitySpeed * 0.08f, 0.8f, 3f);
                    Matrix4 rot = Matrix4.Identity;
                    float dot = Math.Clamp(Vector3.Dot(Vector3.UnitY, dir), -1f, 1f);
                    if (dot < 0.9999f)
                    {
                        if (dot < -0.9999f)
                            rot = Matrix4.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
                        else
                            rot = Matrix4.CreateFromAxisAngle(Vector3.Cross(Vector3.UnitY, dir), MathF.Acos(dot));
                    }
                    var m = Matrix4.CreateScale(len) * rot * Matrix4.CreateTranslation(velSel.Position);
                    GL.Disable(EnableCap.DepthTest);
                    GL.Disable(EnableCap.CullFace);
                    GL.UseProgram(_gizmoProgram);
                    GL.Uniform3(_gizmoColorLoc, new Vector3(0.15f, 0.85f, 0.9f));
                    var gm = m * view * proj;
                    GL.UniformMatrix4(_gizmoMvpLoc, false, ref gm);
                    GL.BindVertexArray(_velVao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, (int)_velCount);
                    GL.BindVertexArray(0);
                    GL.Enable(EnableCap.CullFace);
                    GL.Enable(EnableCap.DepthTest);
                }
            }
        }

        // Conveyor belt arrow on the active part: orange, always on top, length
        // grows with speed. Display only. Part-local direction, Y flattened,
        // exactly like the Play-mode belt.
        var convSel = Selected;
        if (convSel != null && convSel.IsConveyor && convSel.Shape != ShapeKind.None && _velCount > 0)
        {
            Vector3 cd = Vector3.Transform(convSel.ConveyorDirection, convSel.Orientation);
            cd.Y = 0f;
            float clen = cd.Length;
            if (clen > 1e-6f && float.IsFinite(clen))
            {
                Vector3 dir = cd / clen;
            float gs = GizmoScale(pixelHeight);
                if (gs > 0)
                {
                    float len = gs * Math.Clamp(0.8f + convSel.ConveyorSpeed * 0.08f, 0.8f, 3f);
                    Matrix4 rot = Matrix4.Identity;
                    float dot = Math.Clamp(Vector3.Dot(Vector3.UnitY, dir), -1f, 1f);
                    if (dot < 0.9999f)
                    {
                        if (dot < -0.9999f)
                            rot = Matrix4.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
                        else
                            rot = Matrix4.CreateFromAxisAngle(Vector3.Cross(Vector3.UnitY, dir), MathF.Acos(dot));
                    }
                    var m = Matrix4.CreateScale(len) * rot * Matrix4.CreateTranslation(convSel.Position);
                    GL.Disable(EnableCap.DepthTest);
                    GL.Disable(EnableCap.CullFace);
                    GL.UseProgram(_gizmoProgram);
                    GL.Uniform3(_gizmoColorLoc, new Vector3(1.0f, 0.6f, 0.1f));
                    var gm = m * view * proj;
                    GL.UniformMatrix4(_gizmoMvpLoc, false, ref gm);
                    GL.BindVertexArray(_velVao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, (int)_velCount);
                    GL.BindVertexArray(0);
                    GL.Enable(EnableCap.CullFace);
                    GL.Enable(EnableCap.DepthTest);
                }
            }
        }

        // Screenshot: capture this finished frame to PNG (armed via ScreenshotPath).
        if (ScreenshotPath != null)
        {
            string shotPath = ScreenshotPath;
            ScreenshotPath = null;
            try
            {
                byte[] rgba = new byte[pixelWidth * pixelHeight * 4];
                GL.ReadPixels(0, 0, pixelWidth, pixelHeight, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
                int stride = pixelWidth * 4;
                byte[] bgra = new byte[rgba.Length]; // GL is bottom-up RGBA, WPF wants top-down BGRA
                for (int y = 0; y < pixelHeight; y++)
                {
                    int src = (pixelHeight - 1 - y) * stride;
                    int dst = y * stride;
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        bgra[dst] = rgba[src + 2];
                        bgra[dst + 1] = rgba[src + 1];
                        bgra[dst + 2] = rgba[src];
                        bgra[dst + 3] = rgba[src + 3];
                        src += 4; dst += 4;
                    }
                }
                var frame = System.Windows.Media.Imaging.BitmapSource.Create(
                    pixelWidth, pixelHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, bgra, stride);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
                using var fs = new FileStream(shotPath, FileMode.Create, FileAccess.Write);
                enc.Save(fs);
                ScreenshotLog?.Invoke($"Screenshot saved: {shotPath}");
            }
            catch (Exception ex)
            {
                try { ScreenshotLog?.Invoke("Screenshot failed: " + ex.Message); } catch { }
            }
        }

        double cpuMs = (double)(_clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
        CpuMs = CpuMs * 0.95 + cpuMs * 0.05;
    }

    /// <summary>Integrate all emitters and draw live particles as soft points.</summary>
    private void DrawParticles(float h, Matrix4 view, Matrix4 proj, int pixelHeight)
    {
        if (h <= 0f) h = 0.016f;
        h = Math.Min(h, 0.05f); // sim stays stable on hitches
        // Drop spawn debt for deleted emitters.
        if (_emitAcc.Count > 0)
        {
            SceneObject? dead = null;
            foreach (var k in _emitAcc.Keys)
                if (!Objects.Contains(k)) { dead = k; break; }
            while (dead != null)
            {
                _emitAcc.Remove(dead);
                dead = null;
                foreach (var k in _emitAcc.Keys)
                    if (!Objects.Contains(k)) { dead = k; break; }
            }
        }
        foreach (var o in Objects)
        {
            if (!o.IsEmitter || o.Hidden) continue;
            if (!_emitAcc.TryGetValue(o, out float acc)) acc = 0f;
            acc += Math.Clamp(o.EmissionRate, 0f, 200f) * h;
            float life = Math.Clamp(o.ParticleLifetime, 0.1f, 10f);
            float speed = Math.Clamp(o.ParticleSpeed, 0f, 50f);
            float size = Math.Clamp(o.ParticleSize, 0.1f, 4f);
            float spread = Math.Clamp(o.ParticleSpread, 0f, 1f);
            float grav = Math.Clamp(o.ParticleGravity, -20f, 20f);
            Vector3 up = Vector3.Transform(Vector3.UnitY, o.Orientation);
            var tint = new Vector3(o.Color.R, o.Color.G, o.Color.B);
            string preset = o.ParticlePreset ?? "Custom";
            float ramp = preset == "Fire" ? 1f : 0f;
            bool additive = preset == "Fire" || preset == "Sparkles";
            float growth = preset == "Fire" ? 1.8f : preset == "Smoke" ? 2.6f : 0.6f;
            while (acc >= 1f)
            {
                acc -= 1f;
                if (_particles.Count >= MaxParticles) { acc = 0f; break; }
                Vector3 dir = RandomCone(up, spread);
                float lf = life * (0.7f + 0.6f * (float)_rand.NextDouble());
                _particles.Add(new Particle
                {
                    Pos = o.Position + dir * 0.1f,
                    Vel = dir * speed * (0.7f + 0.6f * (float)_rand.NextDouble()),
                    Age = 0f,
                    Life = Math.Max(lf, 0.05f),
                    Size = size,
                    Gravity = grav,
                    Color = tint,
                    Seed = (float)_rand.NextDouble(),
                    Ramp = ramp,
                    Growth = growth,
                    Additive = additive,
                });
            }
            _emitAcc[o] = acc;
        }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += h;
            if (p.Age >= p.Life) { _particles.RemoveAt(i); continue; }
            p.Vel.Y -= p.Gravity * h;
            // Turbulence wobble (organic drift) + drag (terminal velocity).
            float wob = MathF.Sin(p.Age * 7f + p.Seed * 6.28f);
            p.Vel.X += wob * 1.5f * h;
            p.Vel.Z += MathF.Cos(p.Age * 6f + p.Seed * 6.28f) * 1.5f * h;
            p.Vel *= Math.Max(0f, 1f - 0.6f * h);
            p.Pos += p.Vel * h;
            _particles[i] = p;
        }
        _particlesActive = _particles.Count > 0;
        if (_particles.Count == 0) return;
        // Partition into normal and glow (additive) runs; two draws, two blends.
        int need = _particles.Count * 9;
        if (_particleVerts.Length < need)
        {
            _particleVerts = new float[need];
            _particleVertsAdd = new float[need];
        }
        int n = 0, na = 0;
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            float t = p.Age / p.Life;
            float[] dst = p.Additive ? _particleVertsAdd : _particleVerts;
            int b = (p.Additive ? na++ : n++) * 9;
            dst[b] = p.Pos.X;
            dst[b + 1] = p.Pos.Y;
            dst[b + 2] = p.Pos.Z;
            dst[b + 3] = p.Color.X;
            dst[b + 4] = p.Color.Y;
            dst[b + 5] = p.Color.Z;
            dst[b + 6] = p.Size * (0.7f + t * p.Growth);
            dst[b + 7] = t;
            dst[b + 8] = p.Ramp;
        }
        GL.UseProgram(_particleProgram);
        GL.UniformMatrix4(_particleProjLoc, false, ref proj);
        GL.UniformMatrix4(_particleViewLoc, false, ref view);
        float pixelScale = pixelHeight / (2f * MathF.Tan(MathHelper.DegreesToRadians(FovDeg / 2f)));
        GL.Uniform1(_particlePixelScaleLoc, pixelScale);
        GL.BindVertexArray(_particleVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
        GL.Enable(EnableCap.ProgramPointSize); // core profile: allow gl_PointSize
        GL.Enable(EnableCap.Blend);
        GL.DepthMask(false);
        if (n > 0)
        {
            GL.BufferData(BufferTarget.ArrayBuffer, n * 9 * sizeof(float), _particleVerts, BufferUsageHint.DynamicDraw);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DrawArrays(PrimitiveType.Points, 0, n);
        }
        if (na > 0)
        {
            GL.BufferData(BufferTarget.ArrayBuffer, na * 9 * sizeof(float), _particleVertsAdd, BufferUsageHint.DynamicDraw);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One); // glow
            GL.DrawArrays(PrimitiveType.Points, 0, na);
        }
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.ProgramPointSize);
        GL.BindVertexArray(0);
    }

    private Vector3 RandomCone(Vector3 up, float spread)
    {
        if (spread <= 0.001f) return up;
        double z = _rand.NextDouble() * 2.0 - 1.0;
        double a = _rand.NextDouble() * Math.PI * 2.0;
        double r = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
        var s = new Vector3((float)(r * Math.Cos(a)), (float)z, (float)(r * Math.Sin(a)));
        var d = up * (1f - spread) + s * spread;
        return d.LengthSquared > 1e-8f ? Vector3.Normalize(d) : up;
    }

    /// <summary>
    /// True when a frame would look identical to the last: nothing selected
    /// (no pulse), camera fully settled, no fly input. Lets the UI skip GL work.
    /// </summary>
    public bool IsIdle =>
        Selected == null &&
        _flyVel == Vector3.Zero &&
        _zoomVel == 0f &&
        _yaw == _targetYaw && _pitch == _targetPitch &&
        FlyInput == Vector3.Zero &&
        !_particlesActive;

    // ---------- picking + gizmo math (all world units, camera-independent of DPI) ----------

    /// <summary>Constant on-screen gizmo size (~110 px tall at any distance).</summary>
    public float GizmoScale(float viewportHeightPx)
    {
        if (Selected == null || viewportHeightPx <= 0) return 0;
        float dist = Math.Max((GizmoPivot - Position).Length, 0.001f);
        return dist * 2f * MathF.Tan(MathHelper.DegreesToRadians(FovDeg / 2f)) * (110f / viewportHeightPx);
    }

    public void GetPickRay(float mx, float my, float w, float h, out Vector3 origin, out Vector3 dir)
    {
        float nx = mx / w * 2f - 1f;
        float ny = 1f - my / h * 2f;
        var view = Matrix4.LookAt(Position, Position + Forward, Vector3.UnitY);
        var proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FovDeg), w / h, 0.1f, 100f);
        var inv = (view * proj).Inverted();
        var near = new Vector4(nx, ny, -1f, 1f) * inv;
        var far = new Vector4(nx, ny, 1f, 1f) * inv;
        near /= near.W;
        far /= far.W;
        origin = Position;
        dir = Vector3.Normalize(new Vector3(far.X - Position.X, far.Y - Position.Y, far.Z - Position.Z));
    }

    /// <summary>Project a world point to viewport pixels (origin top-left).
    /// Returns false when behind the camera. Uses the current camera state
    /// (Position/Forward/FovDeg), same convention as the render pass.</summary>
    public bool WorldToScreen(Vector3 world, float viewportW, float viewportH, out float sx, out float sy)
    {
        sx = sy = 0;
        if (viewportW <= 0 || viewportH <= 0) return false;
        var view = Matrix4.LookAt(Position, Position + Forward, Vector3.UnitY);
        var proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FovDeg), viewportW / viewportH, 0.1f, 500f);
        var clip = new Vector4(world, 1f) * view * proj;
        if (clip.W <= 1e-6f) return false; // behind the camera
        var ndc = clip.Xyz / clip.W;
        sx = (ndc.X * 0.5f + 0.5f) * viewportW;
        sy = (1f - (ndc.Y * 0.5f + 0.5f)) * viewportH;
        return true;
    }

    /// <summary>Nearest object under the cursor, or null. Exact per-shape tests, world-unit t.</summary>
    /// <summary>Nearest object under the cursor, or null. Locked parts are
    /// skipped when skipLocked (viewport clicks pass through them).</summary>
    public SceneObject? Pick(float mx, float my, float w, float h, bool skipLocked = false)
    {
        if (w <= 0 || h <= 0) return null;
        GetPickRay(mx, my, w, h, out var origin, out var dir);
        SceneObject? best = null;
        float bestT = float.MaxValue;
        foreach (var o in Objects)
        {
            if (o.Hidden) continue; // invisible things can't be clicked
            if (o.IsAvatarFace) continue; // managed face panel: click through to the avatar
            if (skipLocked && o.Locked) continue; // locked: click through to whatever is behind
            // World -> object-local via the transpose of the render rotation (exact inverse).
            var rot = RotationPart(o);
            Vector3 rel = origin - o.Position;
            Vector3 lo = new(
                rel.X * rot.M11 + rel.Y * rot.M21 + rel.Z * rot.M31,
                rel.X * rot.M12 + rel.Y * rot.M22 + rel.Z * rot.M32,
                rel.X * rot.M13 + rel.Y * rot.M23 + rel.Z * rot.M33);
            Vector3 ld = new(
                dir.X * rot.M11 + dir.Y * rot.M21 + dir.Z * rot.M31,
                dir.X * rot.M12 + dir.Y * rot.M22 + dir.Z * rot.M32,
                dir.X * rot.M13 + dir.Y * rot.M23 + dir.Z * rot.M33);
            // Strip scale -> unit-shape space, solve, then measure world t from the hit.
            Vector3 lu = Div(lo, o.Size);
            Vector3 du = Div(ld, o.Size);
            float tl;
            bool hit = o.Shape switch
            {
                ShapeKind.Ball => RaySphere(lu, du, 0.5f, out tl),
                ShapeKind.Cylinder => RayCylinder(lu, du, out tl),
                ShapeKind.Wedge => RayWedge(lu, du, out tl),
                ShapeKind.Cone => RayCone(lu, du, out tl),
                ShapeKind.Capsule => RayCapsule(lu, du, out tl),
                ShapeKind.Stairs => RayStairs(lu, du, out tl),
                ShapeKind.Pyramid => RayPyramid(lu, du, out tl),
                ShapeKind.Mesh => RayMesh(o, lu, du, out tl),
                ShapeKind.HalfBall or ShapeKind.HollowCylinder or ShapeKind.Bowl
                or ShapeKind.Arch or ShapeKind.HexPrism or ShapeKind.Truss
                    => RayTris(MeshFactory.Cached(o.Shape), lu, du, out tl),
                _ => RaySlab(lu, du, new Vector3(0.5f, 0.5f, 0.5f), out tl), // Block + Torus bbox
            };
            if (!hit || tl <= 0) continue;
            var model = ModelMatrix(o);
            Vector3 worldHit = Vector3.TransformPosition(lu + du * tl, model);
            float tw = (worldHit - origin).Length;
            if (tw < bestT)
            {
                bestT = tw;
                best = o;
            }
        }
        return best;
    }

    /// <summary>World-space surface point under the cursor (parts first, y=0
    /// ground plane fallback). For toolbox drop placement.</summary>
    public bool PickPoint(float mx, float my, float w, float h, out Vector3 point)
    {
        point = Vector3.Zero;
        if (w <= 0 || h <= 0) return false;
        GetPickRay(mx, my, w, h, out var origin, out var dir);
        float bestT = float.MaxValue;
        bool found = false;
        foreach (var o in Objects)
        {
            if (o.Hidden) continue; // invisible things can't be clicked
            if (o.IsAvatarFace) continue; // managed face panel: click through to the avatar
            var rot = RotationPart(o);
            Vector3 rel = origin - o.Position;
            Vector3 lo = new(
                rel.X * rot.M11 + rel.Y * rot.M21 + rel.Z * rot.M31,
                rel.X * rot.M12 + rel.Y * rot.M22 + rel.Z * rot.M32,
                rel.X * rot.M13 + rel.Y * rot.M23 + rel.Z * rot.M33);
            Vector3 ld = new(
                dir.X * rot.M11 + dir.Y * rot.M21 + dir.Z * rot.M31,
                dir.X * rot.M12 + dir.Y * rot.M22 + dir.Z * rot.M32,
                dir.X * rot.M13 + dir.Y * rot.M23 + dir.Z * rot.M33);
            Vector3 lu = Div(lo, o.Size);
            Vector3 du = Div(ld, o.Size);
            float tl;
            bool hit = o.Shape switch
            {
                ShapeKind.Ball => RaySphere(lu, du, 0.5f, out tl),
                ShapeKind.Cylinder => RayCylinder(lu, du, out tl),
                ShapeKind.Wedge => RayWedge(lu, du, out tl),
                ShapeKind.Cone => RayCone(lu, du, out tl),
                ShapeKind.Capsule => RayCapsule(lu, du, out tl),
                ShapeKind.Stairs => RayStairs(lu, du, out tl),
                ShapeKind.Pyramid => RayPyramid(lu, du, out tl),
                ShapeKind.Mesh => RayMesh(o, lu, du, out tl),
                ShapeKind.HalfBall or ShapeKind.HollowCylinder or ShapeKind.Bowl
                or ShapeKind.Arch or ShapeKind.HexPrism or ShapeKind.Truss
                    => RayTris(MeshFactory.Cached(o.Shape), lu, du, out tl),
                _ => RaySlab(lu, du, new Vector3(0.5f, 0.5f, 0.5f), out tl), // Block + Torus bbox
            };
            if (!hit || tl <= 0) continue;
            var model = ModelMatrix(o);
            Vector3 worldHit = Vector3.TransformPosition(lu + du * tl, model);
            float tw = (worldHit - origin).Length;
            if (tw < bestT) { bestT = tw; found = true; }
        }
        if (Math.Abs(dir.Y) > 1e-6f)
        {
            float t = -origin.Y / dir.Y;
            if (t > 0f && t < bestT) { bestT = t; found = true; }
        }
        if (!found) return false;
        point = origin + dir * bestT;
        return true;
    }

    /// <summary>Exact-mesh surface under the cursor (point + face normal),
    /// ignoring dragged fellows, ghosts and lights. Move-drag placement uses
    /// this (same raycast as toolbox drops) instead of AABB boxes.</summary>
    public bool PickSurface(float mx, float my, float w, float h, HashSet<SceneObject> ignore,
        out Vector3 point, out Vector3 normal)
    {
        point = Vector3.Zero;
        normal = Vector3.UnitY;
        if (w <= 0 || h <= 0) return false;
        GetPickRay(mx, my, w, h, out var origin, out var dir);
        float bestT = float.MaxValue;
        bool found = false;
        foreach (var o in Objects)
        {
            if (o.Hidden || o.IsAvatarFace) continue;
            if (ignore.Contains(o)) continue; // moves with us
            if (!o.CanCollide || o.Shape == ShapeKind.None) continue; // ghosts/lights aren't surfaces
            var rot = RotationPart(o);
            Vector3 rel = origin - o.Position;
            Vector3 lo = new(
                rel.X * rot.M11 + rel.Y * rot.M21 + rel.Z * rot.M31,
                rel.X * rot.M12 + rel.Y * rot.M22 + rel.Z * rot.M32,
                rel.X * rot.M13 + rel.Y * rot.M23 + rel.Z * rot.M33);
            Vector3 ld = new(
                dir.X * rot.M11 + dir.Y * rot.M21 + dir.Z * rot.M31,
                dir.X * rot.M12 + dir.Y * rot.M22 + dir.Z * rot.M32,
                dir.X * rot.M13 + dir.Y * rot.M23 + dir.Z * rot.M33);
            Vector3 lu = Div(lo, o.Size);
            Vector3 du = Div(ld, o.Size);
            float tl;
            bool hit = o.Shape switch
            {
                ShapeKind.Ball => RaySphere(lu, du, 0.5f, out tl),
                ShapeKind.Cylinder => RayCylinder(lu, du, out tl),
                ShapeKind.Wedge => RayWedge(lu, du, out tl),
                ShapeKind.Cone => RayCone(lu, du, out tl),
                ShapeKind.Capsule => RayCapsule(lu, du, out tl),
                ShapeKind.Stairs => RayStairs(lu, du, out tl),
                ShapeKind.Pyramid => RayPyramid(lu, du, out tl),
                ShapeKind.Mesh => RayMesh(o, lu, du, out tl),
                ShapeKind.HalfBall or ShapeKind.HollowCylinder or ShapeKind.Bowl
                or ShapeKind.Arch or ShapeKind.HexPrism or ShapeKind.Truss
                    => RayTris(MeshFactory.Cached(o.Shape), lu, du, out tl),
                _ => RaySlab(lu, du, new Vector3(0.5f, 0.5f, 0.5f), out tl), // Block + Torus bbox
            };
            if (!hit || tl <= 0) continue;
            var model = ModelMatrix(o);
            Vector3 worldHit = Vector3.TransformPosition(lu + du * tl, model);
            float tw = (worldHit - origin).Length;
            if (tw >= bestT) continue;
            bestT = tw;
            found = true;
            point = worldHit;
            Vector3 lp = lu + du * tl; // local hit: exact normal per shape
            Vector3 ln = o.Shape switch
            {
                ShapeKind.Ball => lp,
                ShapeKind.Cylinder => Math.Abs(lp.Y) >= 0.49f
                    ? new Vector3(0, Math.Sign(lp.Y), 0)
                    : new Vector3(lp.X, 0, lp.Z),
                _ => DominantAxis(lp),
            };
            Vector3 wn = new(
                ln.X * rot.M11 + ln.Y * rot.M12 + ln.Z * rot.M13,
                ln.X * rot.M21 + ln.Y * rot.M22 + ln.Z * rot.M23,
                ln.X * rot.M31 + ln.Y * rot.M32 + ln.Z * rot.M33);
            normal = wn.LengthSquared > 1e-8f ? Vector3.Normalize(wn) : Vector3.UnitY;
        }
        if (Math.Abs(dir.Y) > 1e-6f)
        {
            float t = -origin.Y / dir.Y;
            if (t > 0f && t < bestT) { bestT = t; found = true; point = origin + dir * t; normal = Vector3.UnitY; }
        }
        return found;
    }

    private static Vector3 DominantAxis(Vector3 v)
    {
        float ax = Math.Abs(v.X), ay = Math.Abs(v.Y), az = Math.Abs(v.Z);
        if (ax >= ay && ax >= az) return new Vector3(Math.Sign(v.X), 0, 0);
        if (ay >= ax && ay >= az) return new Vector3(0, Math.Sign(v.Y), 0);
        return new Vector3(0, 0, Math.Sign(v.Z));
    }

    private static Vector3 Div(Vector3 a, Vector3 b) => new(
        a.X / Math.Max(b.X, 1e-4f), a.Y / Math.Max(b.Y, 1e-4f), a.Z / Math.Max(b.Z, 1e-4f));

    private static Matrix4 ModelMatrix(SceneObject o)
    {
        return Matrix4.CreateScale(o.Size) *
               Matrix4.CreateFromQuaternion(o.Orientation) *
               Matrix4.CreateTranslation(o.Position);
    }

    private static Matrix3 RotationPart(SceneObject o) =>
        Matrix3.CreateFromQuaternion(o.Orientation);

    /// <summary>Roblox-style play-camera collision: pull the follow camera in
    /// front of the nearest CanCollide part so it can't clip through walls.
    /// Parts containing the focus are ignored (avatar standing inside one).</summary>
    private Vector3 CollideCamera(Vector3 focus, Vector3 wantPos)
    {
        var toCam = wantPos - focus;
        float dist = toCam.Length;
        if (dist < 1e-4f) return wantPos;
        var dir = toCam / dist;
        const float radius = 0.35f; // keep the near plane out of the wall
        const float minKeep = 0.6f; // never pull into the avatar's head
        float best = dist;
        foreach (var o in Objects)
        {
            if (!o.CanCollide || o.Shape == ShapeKind.None) continue;
            if (ReferenceEquals(o, FollowTarget) || o.IsAvatarFace) continue;
            if (o.Orientation.LengthSquared < 1e-8f) continue;
            var inv = Quaternion.Invert(o.Orientation);
            var lp = Vector3.Transform(focus - o.Position, inv);
            var ld = Vector3.Transform(dir, inv);
            var half = o.Size * 0.5f;
            float tmin = 0f, tmax = best;
            if (!Slab(lp.X, ld.X, half.X, ref tmin, ref tmax)) continue;
            if (!Slab(lp.Y, ld.Y, half.Y, ref tmin, ref tmax)) continue;
            if (!Slab(lp.Z, ld.Z, half.Z, ref tmin, ref tmax)) continue;
            if (tmin > radius && tmin < best) best = tmin - radius;
        }
        if (best < minKeep) best = minKeep;
        return focus + dir * best;
    }

    private static bool Slab(float origin, float direction, float half, ref float tmin, ref float tmax)
    {
        if (MathF.Abs(direction) < 1e-8f) return origin >= -half && origin <= half;
        float t0 = (-half - origin) / direction;
        float t1 = (half - origin) / direction;
        if (t0 > t1) (t0, t1) = (t1, t0);
        if (t0 > tmin) tmin = t0;
        if (t1 < tmax) tmax = t1;
        return tmin <= tmax;
    }

    private static bool RaySphere(Vector3 o, Vector3 d, float r, out float t)
    {
        float a = Vector3.Dot(d, d);
        float b = Vector3.Dot(o, d);
        float c = Vector3.Dot(o, o) - r * r;
        t = 0;
        if (a < 1e-12f) return false;
        float disc = b * b - a * c;
        if (disc < 0) return false;
        float sq = MathF.Sqrt(disc);
        float t0 = (-b - sq) / a;
        if (t0 > 1e-4f) { t = t0; return true; }
        float t1 = (-b + sq) / a;
        if (t1 > 1e-4f) { t = t1; return true; } // origin inside
        return false;
    }

    private static bool RayCylinder(Vector3 o, Vector3 d, out float t)
    {
        const float r = 0.5f;
        float best = float.MaxValue;
        bool found = false;
        void Consider(float tc, float y)
        {
            if (tc > 1e-4f && y >= -0.5f && y <= 0.5f && tc < best) { best = tc; found = true; }
        }
        float a = d.X * d.X + d.Z * d.Z;
        if (a > 1e-12f)
        {
            float b = o.X * d.X + o.Z * d.Z;
            float c = o.X * o.X + o.Z * o.Z - r * r;
            float disc = b * b - a * c;
            if (disc >= 0)
            {
                float sq = MathF.Sqrt(disc);
                float t0 = (-b - sq) / a;
                Consider(t0, o.Y + t0 * d.Y);
                float t1 = (-b + sq) / a;
                Consider(t1, o.Y + t1 * d.Y);
            }
        }
        if (MathF.Abs(d.Y) > 1e-8f)
        {
            foreach (float yc in new[] { -0.5f, 0.5f })
            {
                float tc = (yc - o.Y) / d.Y;
                float x = o.X + tc * d.X, z = o.Z + tc * d.Z;
                if (tc > 1e-4f && x * x + z * z <= r * r && tc < best) { best = tc; found = true; }
            }
        }
        t = found ? best : 0;
        return found;
    }

    private static bool RayCone(Vector3 o, Vector3 d, out float t)
    {
        float best = float.MaxValue;
        bool found = false;
        void Consider(float tc)
        {
            if (tc <= 1e-4f || tc >= best) return;
            float y = o.Y + tc * d.Y;
            if (y >= -0.5f && y <= 0.5f) { best = tc; found = true; }
        }
        // |o.xz + t d.xz| = 0.5 * (0.5 - (o.y + t d.y)): apex (0,+.5,0), base r=.5 at y=-.5.
        float k = 0.5f - o.Y;
        float a = d.X * d.X + d.Z * d.Z - 0.25f * d.Y * d.Y;
        float b = 2f * (o.X * d.X + o.Z * d.Z) + 0.5f * k * d.Y;
        float c = o.X * o.X + o.Z * o.Z - 0.25f * k * k;
        if (MathF.Abs(a) < 1e-12f)
        {
            if (MathF.Abs(b) > 1e-12f) Consider(-c / b);
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc >= 0)
            {
                float sq = MathF.Sqrt(disc);
                Consider((-b - sq) / (2f * a));
                Consider((-b + sq) / (2f * a));
            }
        }
        if (MathF.Abs(d.Y) > 1e-8f) // base cap
        {
            float tc = (-0.5f - o.Y) / d.Y;
            float x = o.X + tc * d.X, z = o.Z + tc * d.Z;
            if (tc > 1e-4f && x * x + z * z <= 0.25f && tc < best) { best = tc; found = true; }
        }
        t = found ? best : 0;
        return found;
    }

    /// <summary>Unit capsule: cylinder (|y| &lt;= .25, r = .25) + hemispheres at y = ±.25.</summary>
    private static bool RayCapsule(Vector3 o, Vector3 d, out float t)
    {
        const float r = 0.25f;
        float best = float.MaxValue;
        bool found = false;
        void Consider(float tc) { if (tc > 1e-4f && tc < best) { best = tc; found = true; } }
        float a = d.X * d.X + d.Z * d.Z;
        if (a > 1e-12f)
        {
            float b = o.X * d.X + o.Z * d.Z;
            float c = o.X * o.X + o.Z * o.Z - r * r;
            float disc = b * b - a * c;
            if (disc >= 0)
            {
                float sq = MathF.Sqrt(disc);
                float t0 = (-b - sq) / a;
                if (t0 > 1e-4f && Math.Abs(o.Y + t0 * d.Y) <= 0.25f) Consider(t0);
                float t1 = (-b + sq) / a;
                if (t1 > 1e-4f && Math.Abs(o.Y + t1 * d.Y) <= 0.25f) Consider(t1);
            }
        }
        if (RaySphere(o - new Vector3(0, 0.25f, 0), d, r, out float tt)) Consider(tt);
        if (RaySphere(o - new Vector3(0, -0.25f, 0), d, r, out tt)) Consider(tt);
        t = found ? best : 0;
        return found;
    }

    private static bool RayWedge(Vector3 o, Vector3 d, out float t)    {
        // Unit wedge: |x|,|y| <= .5, z >= -.5, y + z <= 0. Clip the ray against its planes.
        float best = float.MaxValue;
        bool found = false;
        if (InsideWedge(o)) { t = 0f; return true; }
        void Hit(float tc)
        {
            if (tc <= 1e-4f || tc >= best) return;
            if (InsideWedge(o + d * tc, 1e-3f)) { best = tc; found = true; }
        }
        if (MathF.Abs(d.X) > 1e-8f) { Hit((-0.5f - o.X) / d.X); Hit((0.5f - o.X) / d.X); }
        if (MathF.Abs(d.Y) > 1e-8f) { Hit((-0.5f - o.Y) / d.Y); Hit((0.5f - o.Y) / d.Y); }
        if (MathF.Abs(d.Z) > 1e-8f) { Hit((-0.5f - o.Z) / d.Z); }
        float ds = d.Y + d.Z; // slope plane y + z = 0
        if (MathF.Abs(ds) > 1e-8f) { Hit(-(o.Y + o.Z) / ds); }
        t = found ? best : 0;
        return found;
    }

    private static bool InsideWedge(Vector3 p, float tol = 0f) =>
        p.X >= -0.5f - tol && p.X <= 0.5f + tol &&
        p.Y >= -0.5f - tol && p.Y <= 0.5f + tol &&
        p.Z >= -0.5f - tol && p.Y + p.Z <= tol;

    /// <summary>Unit stairs: 4 steps ascending from the front (z=+.5, low) to
    /// the back (z=-.5, high). Must match MeshFactory.Stairs(4).</summary>
    private static bool InsideStairs(Vector3 p, float tol = 0f)
    {
        const int n = 4;
        if (p.X < -0.5f - tol || p.X > 0.5f + tol) return false;
        if (p.Y < -0.5f - tol || p.Y > 0.5f + tol) return false;
        if (p.Z < -0.5f - tol || p.Z > 0.5f + tol) return false;
        // Step index from depth: front = step 0 (top at -0.25), back = step 3 (top at +0.5).
        int i = (int)MathF.Floor((0.5f - p.Z) * n);
        if (i < 0) i = 0;
        if (i >= n) i = n - 1;
        float top = -0.5f + (i + 1) / (float)n;
        return p.Y <= top + tol;
    }

    private static bool RayStairs(Vector3 o, Vector3 d, out float t)
    {
        const int n = 4;
        float best = float.MaxValue;
        bool found = false;
        if (InsideStairs(o)) { t = 0f; return true; }
        void Hit(float tc)
        {
            if (tc <= 1e-4f || tc >= best) return;
            if (InsideStairs(o + d * tc, 1e-3f)) { best = tc; found = true; }
        }
        if (MathF.Abs(d.X) > 1e-8f) { Hit((-0.5f - o.X) / d.X); Hit((0.5f - o.X) / d.X); }
        if (MathF.Abs(d.Y) > 1e-8f)
        {
            Hit((-0.5f - o.Y) / d.Y); // bottom
            for (int i = 0; i < n; i++) Hit((-0.5f + (i + 1) / (float)n - o.Y) / d.Y); // treads
        }
        if (MathF.Abs(d.Z) > 1e-8f)
        {
            Hit((-0.5f - o.Z) / d.Z); // back
            Hit((0.5f - o.Z) / d.Z);  // front riser plane (clipped by InsideStairs)
            for (int i = 0; i < n; i++) Hit((0.5f - i / (float)n - o.Z) / d.Z); // risers
        }
        t = found ? best : 0;
        return found;
    }

    /// <summary>Unit pyramid: full base at y=-.5, centered apex at y=+.5.
    /// Must match MeshFactory.Pyramid.</summary>
    private static bool InsidePyramid(Vector3 p, float tol = 0f)
    {
        if (p.Y < -0.5f - tol || p.Y > 0.5f + tol) return false;
        float half = 0.5f * (0.5f - p.Y); // 0.5 at the base, 0 at the apex
        return p.X >= -half - tol && p.X <= half + tol &&
               p.Z >= -half - tol && p.Z <= half + tol;
    }

    private static bool RayPyramid(Vector3 o, Vector3 d, out float t)
    {
        float best = float.MaxValue;
        bool found = false;
        if (InsidePyramid(o)) { t = 0f; return true; }
        void Hit(float tc)
        {
            if (tc <= 1e-4f || tc >= best) return;
            if (InsidePyramid(o + d * tc, 1e-3f)) { best = tc; found = true; }
        }
        if (MathF.Abs(d.Y) > 1e-8f) Hit((-0.5f - o.Y) / d.Y); // base
        // Four side faces: x + 0.5y = 0.25, -x + 0.5y = 0.25, z + 0.5y = 0.25, -z + 0.5y = 0.25.
        float dx, dd;
        dx = d.X + 0.5f * d.Y; if (MathF.Abs(dx) > 1e-8f) Hit((0.25f - (o.X + 0.5f * o.Y)) / dx);
        dd = -d.X + 0.5f * d.Y; if (MathF.Abs(dd) > 1e-8f) Hit((0.25f - (-o.X + 0.5f * o.Y)) / dd);
        dx = d.Z + 0.5f * d.Y; if (MathF.Abs(dx) > 1e-8f) Hit((0.25f - (o.Z + 0.5f * o.Y)) / dx);
        dd = -d.Z + 0.5f * d.Y; if (MathF.Abs(dd) > 1e-8f) Hit((0.25f - (-o.Z + 0.5f * o.Y)) / dd);
        t = found ? best : 0;
        return found;
    }

    private static bool RaySlab(Vector3 o, Vector3 d, Vector3 half, out float t)
    {
        t = 0f;
        float tmax = float.MaxValue;
        return Slab(o.X, d.X, -half.X, half.X, ref t, ref tmax)
            && Slab(o.Y, d.Y, -half.Y, half.Y, ref t, ref tmax)
            && Slab(o.Z, d.Z, -half.Z, half.Z, ref t, ref tmax);
    }

    private static bool Slab(float o, float d, float mn, float mx, ref float t0, ref float t1)
    {
        if (MathF.Abs(d) < 1e-8f) return o >= mn && o <= mx;
        float tA = (mn - o) / d;
        float tB = (mx - o) / d;
        if (tA > tB) (tA, tB) = (tB, tA);
        t0 = Math.Max(t0, tA);
        t1 = Math.Min(t1, tB);
        return t0 <= t1;
    }

    private static readonly Vector3[] AxisColors =
    {
        new(0.90f, 0.22f, 0.22f), // X red
        new(0.28f, 0.85f, 0.32f), // Y green
        new(0.30f, 0.52f, 0.95f), // Z blue
    };

    /// <summary>Arrow under the cursor (0=X, 1=Y, 2=Z) or -1. fbH = framebuffer height px.</summary>
    public int PickGizmoAxis(float mx, float my, float w, float h, float fbH)
    {
        if (Selected == null || w <= 0 || h <= 0) return -1;
        GetPickRay(mx, my, w, h, out var ro, out var rd);
        float gs = GizmoScale(fbH);
        float thresh = Math.Max((GizmoPivot - Position).Length * 0.035f, 0.001f);
        int best = -1;
        float bestD = thresh;
        for (int a = 0; a < 3; a++)
        {
            if (!AxisClosest(GizmoAxis(a), ro, rd, out float axisT, out float rayT, out float dist))
                continue;
            if (rayT < 0 || dist >= bestD)
                continue;
            // Face handles only: grabs near a face center count, the hollow
            // middle doesn't (clicking the part body selects/drags the part).
            float face = HalfExtent(a);
            float at = MathF.Abs(axisT);
            if (at < face - 0.4f * gs || at > face + 1.15f * gs)
                continue;
            bestD = dist;
            best = a;
        }
        return best;
    }

    /// <summary>
    /// Ring under the cursor (0=X, 1=Y, 2=Z) or -1, plus the plane hit point
    /// (for starting a drag). Ring radius is 0.65 in gizmo units; the grab band
    /// is deliberately generous (~30 px) for easy dragging.
    /// </summary>
    public int PickRingAxis(float mx, float my, float w, float h, float fbH, out Vector3 ringPoint)
    {
        ringPoint = Vector3.Zero;
        if (Selected == null || w <= 0 || h <= 0) return -1;
        GetPickRay(mx, my, w, h, out var ro, out var rd);
        float gs = GizmoScale(fbH);
        if (gs <= 0) return -1;
        float thresh = gs * 0.3f;
        int best = -1;
        float bestD = thresh;
        for (int a = 0; a < 3; a++)
        {
            var u = GizmoAxis(a);
            float denom = Vector3.Dot(rd, u);
            if (MathF.Abs(denom) < 1e-6f) continue; // looking edge-on at this ring
            float t = Vector3.Dot(GizmoPivot - ro, u) / denom;
            if (t < 0) continue;
            var p = ro + rd * t;
            float radial = (p - GizmoPivot).Length;
            float d = Math.Abs(radial - 0.65f * gs);
            if (d < bestD)
            {
                bestD = d;
                best = a;
                ringPoint = p;
            }
        }
        return best;
    }

    /// <summary>Closest point on the (infinite) axis line through the selection to a ray.</summary>
    public Vector3 AxisPoint(int axis, Vector3 rayOrigin, Vector3 rayDir)
    {
        var u = GizmoAxis(axis);
        if (!AxisClosest(u, rayOrigin, rayDir, out float axisT, out _, out _))
            return GizmoPivot;
        return GizmoPivot + u * axisT;
    }

    private bool AxisClosest(Vector3 u, Vector3 ro, Vector3 rd, out float axisT, out float rayT, out float dist)
    {
        Vector3 w0 = GizmoPivot - ro;
        float b = Vector3.Dot(rd, u);
        float d = Vector3.Dot(u, w0);
        float e = Vector3.Dot(rd, w0);
        float denom = 1f - b * b;
        axisT = 0;
        rayT = 0;
        dist = 0;
        if (MathF.Abs(denom) < 1e-6f) return false; // looking straight down the axis
        axisT = (b * e - d) / denom;
        rayT = (e - b * d) / denom;
        dist = (GizmoPivot + u * axisT - (ro + rd * rayT)).Length;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (var mesh in _meshes.Values)
            {
                if (mesh.Vbo != 0) GL.DeleteBuffer(mesh.Vbo);
                if (mesh.Vao != 0) GL.DeleteVertexArray(mesh.Vao);
            }
            foreach (var mesh in _importedMeshes.Values)
            {
                if (mesh.Vbo != 0) GL.DeleteBuffer(mesh.Vbo);
                if (mesh.Vao != 0) GL.DeleteVertexArray(mesh.Vao);
            }
            _importedMeshes.Clear();
            if (_skyVao != 0) GL.DeleteVertexArray(_skyVao);
            if (_program != 0) GL.DeleteProgram(_program);
            if (_skyProgram != 0) GL.DeleteProgram(_skyProgram);
            if (_decalProgram != 0) GL.DeleteProgram(_decalProgram);
            if (_decalVbo != 0) GL.DeleteBuffer(_decalVbo);
            if (_decalVao != 0) GL.DeleteVertexArray(_decalVao);
            foreach (var texture in _decalTextures.Values)
                if (texture != 0) GL.DeleteTexture(texture);
            foreach (var texture in _partTextures.Values)
                if (texture != 0) GL.DeleteTexture(texture);
            foreach (var texture in _materialTextures.Values)
                if (texture != 0) GL.DeleteTexture(texture);
            foreach (var texture in _textTextures.Values)
                if (texture != 0) GL.DeleteTexture(texture);
            if (_particleVbo != 0) GL.DeleteBuffer(_particleVbo);
            if (_particleVao != 0) GL.DeleteVertexArray(_particleVao);
            if (_particleProgram != 0) GL.DeleteProgram(_particleProgram);
            if (_gizmoProgram != 0) GL.DeleteProgram(_gizmoProgram);
            if (_depthProgram != 0) GL.DeleteProgram(_depthProgram);
            if (_shadowTex != 0) GL.DeleteTexture(_shadowTex);
            if (_shadowFbo != 0) GL.DeleteFramebuffer(_shadowFbo);
            if (_reflTex != 0) GL.DeleteTexture(_reflTex);
            if (_reflDepth != 0) GL.DeleteRenderbuffer(_reflDepth);
            if (_reflFbo != 0) GL.DeleteFramebuffer(_reflFbo);
            for (int a = 0; a < 3; a++)
            {
                if (_gizmoVbos[a] != 0) GL.DeleteBuffer(_gizmoVbos[a]);
                if (_gizmoVaos[a] != 0) GL.DeleteVertexArray(_gizmoVaos[a]);
                if (_scaleVbos[a] != 0) GL.DeleteBuffer(_scaleVbos[a]);
                if (_scaleVaos[a] != 0) GL.DeleteVertexArray(_scaleVaos[a]);
                if (_ringVbos[a] != 0) GL.DeleteBuffer(_ringVbos[a]);
                if (_ringVaos[a] != 0) GL.DeleteVertexArray(_ringVaos[a]);
            }
            if (_velVbo != 0) GL.DeleteBuffer(_velVbo);
            if (_velVao != 0) GL.DeleteVertexArray(_velVao);
            if (_boxVbo != 0) GL.DeleteBuffer(_boxVbo);
            if (_boxVao != 0) GL.DeleteVertexArray(_boxVao);
            if (_silVbo != 0) GL.DeleteBuffer(_silVbo);
            if (_silVao != 0) GL.DeleteVertexArray(_silVao);
        }
        catch { /* context may be gone */ }
    }
}
