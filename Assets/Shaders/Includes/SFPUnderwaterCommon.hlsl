#ifndef SFP_UNDERWATER_COMMON_INCLUDED
#define SFP_UNDERWATER_COMMON_INCLUDED

// Shared by Assets/Shaders/Underwater.shader. Callers must already have included, in order:
//   Core.hlsl (ComputeWorldSpacePosition, UNITY_MATRIX_I_VP, PI, ...)
//   Blit.hlsl (sampler_LinearClamp via GlobalSamplers.hlsl)
//   DeclareDepthTexture.hlsl (SampleSceneDepth)
// See Underwater.shader's SubShader-level HLSLINCLUDE block for the include order.

// --- Material parameters (per-material CBUFFER; pushed every frame from the blended
// UnderwaterVolumeComponent by UnderwaterPass.PushMaterialParams, design doc §4.4).
// No Properties block: this material is only ever created via CoreUtils.CreateEngineMaterial
// and driven entirely from C#, never from the inspector.
CBUFFER_START(UnityPerMaterial)
    // --- Optical (physical layer) ---
    float3 _AbsorptionCoeff;      // a_RGB [m^-1], default (0.45, 0.065, 0.0044) - Pope & Fry 1997 pure water
    float  _Turbidity;            // b [m^-1] scattering coeff, default 0.05 (coastal water, ~50m visibility)
    float3 _KdSun;                // Kd_RGB [m^-1], default (0.35, 0.07, 0.017) - Jerlov Type I open ocean
    float3 _ScatterColor;         // single-scattering albedo color (normalized HDR), default (0.10, 0.35, 0.45)
    float3 _VisibilityFloor;      // gameplay-layer minimum luminance (no physical basis - Subnautica lesson), default (0.004, 0.010, 0.016)
    float  _MaxViewDistance;      // virtual distance [m] assigned to sky (depth=far) pixels, default 200

    // --- Caustics ---
    float  _CausticsIntensity;    // 0..2, default 0.8
    float  _CausticsScale;        // pattern period [m], default 3.0
    float  _CausticsSpeed;        // [1/s], default 0.35
    float2 _CausticsFade;         // (fadeStart, fadeEnd) depth [m], default (5, 30) - effective range 0-30m

    // --- God rays ---
    float  _GodRayIntensity;      // default 0.6
    float  _GodRayDecay;          // per-sample decay along the radial blur, default 0.94
    float  _GodRayMaxDepth;       // effective camera depth [m], default 60

    // --- Refraction distortion ---
    float  _DistortAmplitude;     // UV amplitude, default 0.0018 (~2px @1080p)
    float  _DistortFrequency;     // [rad/uv], default 22
    float  _DistortSpeed;         // [rad/s], default 1.2

    // --- Backscatter ---
    float  _BackscatterIntensity; // default 1.0
    float  _BackscatterDensity;   // particle scattering density [m^-1], default 0.02
CBUFFER_END

// --- Globals (Shader.SetGlobalX, not part of the material CBUFFER). Owned by
// UnderwaterEnvironmentController unless noted otherwise. Design doc §3.3. ---
float    _SFPSubmergence;        // 0..1 camera submergence, ~8Hz exponential smoothed (crossfade at the surface)
float4   _SFPSunScreenPos;       // xy: sun viewport pos, z: dot(-sunDir, cam.forward) (>0 => in front), w: unused
float3   _SFPSunDirWS;           // sun direction (light -> ground)
float3   _SFPSunColorAtDepth;    // sunColor * exp(-Kd * cameraDepth), computed once per frame on the CPU
float3   _SFPAmbientAtDepth;     // depth-attenuated ambient (physical value, before _VisibilityFloor is applied)
float2   _SFPCausticsOrigin;     // world XZ snapped to a caustics-period grid, keeps caustics UVs small (see below)
float    _SFPLoopTime;           // Time.timeSinceLevelLoad % LOOP; avoids sin/noise precision loss over long sessions

// Owned by ShipRootDriver, already published every frame; reused here unchanged (same
// pattern as SFPLitCutout.shader's hull-cutout mask).
float4x4 _SFPShipWorldToLocal;
float3   _SFPHullMin;            // ship hull AABB, ship-local space, default (0, 0, 0)
float3   _SFPHullMax;            // default (24, 18, 6) - matches CLAUDE.md's design-time coordinate range
float    _SFPInteriorFlood;      // 0 = open-ocean mode (dry interior behind breaches is not fogged), 1 = flooded interior

// Backscatter spotlights (headlamp + hull floodlights), top 4 selected/sorted by
// BackscatterLightManager (design doc §3.10/§6.3).
#define MAX_BACKSCATTER_LIGHTS 4
float4 _SFPBkLightPosRange[MAX_BACKSCATTER_LIGHTS]; // xyz: light worldPos, w: range [m]
float4 _SFPBkLightDirAngle[MAX_BACKSCATTER_LIGHTS]; // xyz: light forward dir, w: cos(outerAngle/2)
float4 _SFPBkLightColor[MAX_BACKSCATTER_LIGHTS];    // rgb: color * intensity (tracks SubmarineLight flicker), w: unused
int    _SFPBkLightCount;

// --- Gradient noise (identical implementation to WaterSurface.shader's hash2/noise; not
// factored into a shared file to avoid touching that existing shader - design doc §3.1). ---
float2 hash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    return lerp(lerp(dot(hash2(i), f),
                     dot(hash2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                lerp(dot(hash2(i + float2(0, 1)), f - float2(0, 1)),
                     dot(hash2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
}

// --- Depth reconstruction -> world position -> underwater ray (design doc §3.4) ---
struct WaterRay
{
    float3 posWS;      // fragment world position
    float  viewDist;   // camera -> fragment distance [m] (sky uses _MaxViewDistance)
    float  fragDepth;  // fragment water depth [m] = max(0, -posWS.y); sea level is world Y=0
    bool   isSky;
    bool   insideHull; // inside the ship hull AABB (a dry interior seen through a breach, maybe)
};

WaterRay ReconstructRay(float2 uv)
{
    WaterRay r;
    float raw = SampleSceneDepth(uv);

#if UNITY_REVERSED_Z
    r.isSky = raw <= 1e-7;
#else
    r.isSky = raw >= 1.0 - 1e-7;
#endif

    r.posWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);

    float3 rel = r.posWS - _WorldSpaceCameraPos;
    r.viewDist = length(rel);

    if (r.isSky)
    {
        // Sky = water extending to infinity along the view ray. Replace with a virtual
        // point so the fog saturates fully instead of leaving a hole at the horizon.
        float3 dir = normalize(rel);
        r.viewDist = _MaxViewDistance;
        r.posWS = _WorldSpaceCameraPos + dir * r.viewDist;
    }

    r.fragDepth = max(0.0, -r.posWS.y);

    float3 sl = mul(_SFPShipWorldToLocal, float4(r.posWS, 1)).xyz;
    // 0.2m margin: the hull plating itself is still treated as "water side" fog.
    r.insideHull = all(sl > _SFPHullMin + 0.2) && all(sl < _SFPHullMax - 0.2);
    return r;
}

// Ray / axis-aligned-box slab test (Kay & Kajiya 1986), used to shorten the fogged path
// for pixels behind the hull plating (design doc §3.4). rayOrigin/rayDir and the box must
// already be in the same space; rayDir components equal to 0 are fine (1/0 -> +-inf is
// well-defined IEEE arithmetic and correctly disables that axis' slab test). Returns the
// entry distance along the ray, clamped to >=0, or a large sentinel if the ray misses the
// box (or the box is fully behind the origin) so that callers taking min() with another
// distance are unaffected.
float RayAABBEnter(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
{
    float3 invDir = 1.0 / rayDir;
    float3 t0 = (boxMin - rayOrigin) * invDir;
    float3 t1 = (boxMax - rayOrigin) * invDir;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);
    float tEnter = max(max(tMin.x, tMin.y), tMin.z);
    float tExit = min(min(tMax.x, tMax.y), tMax.z);
    return (tExit >= max(tEnter, 0.0)) ? max(tEnter, 0.0) : 1e6;
}

// Distance the view ray actually spends "in water" before reaching the fragment. A
// fragment inside the hull AABB is a dry interior glimpsed through a breach/open hatch -
// it should only be fogged up to the hull surface, not all the way to the fragment
// (design doc §3.4). When the interior itself is flooded (_SFPInteriorFlood==1) the
// shortcut is disabled and the full view distance is fogged again - an approximation that
// does not model a partially flooded compartment's actual air/water boundary.
float WaterPathLength(WaterRay r, float3 camPosWS)
{
    if (!r.insideHull)
        return r.viewDist;

    // The hull AABB is authored in ship-local space and the ship can yaw (ShipRootDriver),
    // so the ray must be transformed into ship-local space before the slab test.
    // ShipRootDriver only rotates and translates (no scale), so this preserves the
    // world-space travel distance.
    float3 rayDirWS = normalize(r.posWS - camPosWS);
    float3 originSL = mul(_SFPShipWorldToLocal, float4(camPosWS, 1)).xyz;
    float3 tipSL = mul(_SFPShipWorldToLocal, float4(camPosWS + rayDirWS, 1)).xyz;

    float tEnter = RayAABBEnter(originSL, tipSL - originSL, _SFPHullMin, _SFPHullMax);
    return lerp(min(tEnter, r.viewDist), r.viewDist, _SFPInteriorFlood);
}

// Henyey-Greenstein phase function (Henyey & Greenstein 1941). g in (-1,1); g>0 biases
// scattering forward along the light direction. Marine particulates scatter predominantly
// forward (g ~ 0.5-0.8, Petzold 1972 turbid-water measurements), hence g=0.5 for the
// backscatter light-cone effect (design doc §3.10).
float HGPhase(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * PI * pow(max(denom, 1e-4), 1.5));
}

#endif // SFP_UNDERWATER_COMMON_INCLUDED
