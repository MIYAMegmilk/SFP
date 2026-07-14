using UnityEngine;
using UnityEngine.Rendering;

namespace SFP.Presentation
{
    // Replaces UnderwaterAmbience. Determines per-frame whether the camera is underwater
    // (correctly excluding dry interior compartments, unlike the old camY<0 check), drives
    // RenderSettings ambient/fog, the underwater sun light, and the _SFP* global shader
    // constants consumed by Underwater.shader / UnderwaterRendererFeature.
    public sealed class UnderwaterEnvironmentController : MonoBehaviour
    {
        // Ship hull AABB in design-time coordinates (x 0..24, y 0..24, z 0..6).
        static readonly Vector3 HullMin = new(0f, 0f, 0f);
        static readonly Vector3 HullMax = new(24f, 24f, 6f);

        // Fallback optical constants (mirrors UnderwaterVolumeComponent defaults) for frames
        // where the Volume stack hasn't been populated yet.
        static readonly Vector3 DefaultKdSun = new(0.35f, 0.07f, 0.017f); // Jerlov Type I
        static readonly Color DefaultVisibilityFloor = new(0.004f, 0.010f, 0.016f);
        const float DefaultGodRayMaxDepth = 60f;
        const float DefaultCausticsScale = 3f;

        // ~8Hz exponential smoothing so submergence/interior state cross-fades over 1-2 frames
        // instead of popping when the camera straddles a water surface (design doc §2.4).
        const float SmoothingRate = 8f;

        const float LoopPeriod = 1200f; // seconds; bounds sin/noise time inputs (§3.9 precision)

        static readonly int SubmergenceId = Shader.PropertyToID("_SFPSubmergence");
        static readonly int SunScreenPosId = Shader.PropertyToID("_SFPSunScreenPos");
        static readonly int SunDirWSId = Shader.PropertyToID("_SFPSunDirWS");
        static readonly int SunColorAtDepthId = Shader.PropertyToID("_SFPSunColorAtDepth");
        static readonly int AmbientAtDepthId = Shader.PropertyToID("_SFPAmbientAtDepth");
        static readonly int CausticsOriginId = Shader.PropertyToID("_SFPCausticsOrigin");
        static readonly int LoopTimeId = Shader.PropertyToID("_SFPLoopTime");
        static readonly int HullMinId = Shader.PropertyToID("_SFPHullMin");
        static readonly int HullMaxId = Shader.PropertyToID("_SFPHullMax");
        static readonly int InteriorFloodId = Shader.PropertyToID("_SFPInteriorFlood");

        // Read by UnderwaterRendererFeature to early-reject the pass at zero cost when dry, and
        // by the composite shader (via the global below) to cross-fade the effect in/out.
        public static float GlobalSubmergence { get; private set; }
        public static bool SunVisibleUnderwater { get; private set; }

        float _submergence;
        float _interiorBlend;
        Color _ambientAtDepth;

        Light _underwaterSun;
        Light _worldSun;
        Color _surfaceAmbient;

        void Start()
        {
            // Underwater.shader's Beer-Lambert fog replaces built-in fog entirely; disable it
            // here in case the scene/builder left legacy fog settings enabled.
            RenderSettings.fog = false;

            // Baseline "clear day at the surface" ambient, captured once from whatever the
            // scene was authored with, rather than hardcoding a sky color. UpdateAmbient
            // attenuates relative to this with depth.
            _surfaceAmbient = RenderSettings.ambientLight;

            var sunGo = new GameObject("UnderwaterSun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(65f, 25f, 0f);
            _underwaterSun = sunGo.AddComponent<Light>();
            _underwaterSun.type = LightType.Directional;
            _underwaterSun.shadows = LightShadows.None;
            _underwaterSun.enabled = false;

            FindWorldSun();
        }

        void FindWorldSun()
        {
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional && l != _underwaterSun)
                {
                    _worldSun = l;
                    break;
                }
            }
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            var cam = Camera.main;
            if (bridge == null || cam == null) return;

            float t = 1f - Mathf.Exp(-SmoothingRate * Time.deltaTime);

            float submergenceTarget = ComputeSubmergence(cam, bridge);
            _submergence = Mathf.Lerp(_submergence, submergenceTarget, t);
            GlobalSubmergence = _submergence;

            float interiorTarget = ComputeInteriorBlend(cam, bridge);
            _interiorBlend = Mathf.Lerp(_interiorBlend, interiorTarget, t);

            var uw = GetVolumeComponent();
            float cameraDepth = Mathf.Max(0f, -cam.transform.position.y); // sea surface is world Y=0

            UpdateUnderwaterSun(uw, cameraDepth);
            UpdateAmbient(uw, cameraDepth);
            UpdateSunVisibility(uw, cameraDepth);

            // UnderwaterRendererFeature skips its pass entirely below this threshold, so skip
            // the (cheap but non-zero) global constant pushes too when dry.
            if (_submergence > 0.001f)
                PushGlobalShaderConstants(cam, uw, cameraDepth);
        }

        float ComputeSubmergence(Camera cam, SimulationBridge bridge)
        {
            Vector3 wp = cam.transform.position;
            Vector3 sl = bridge.WorldToShip(wp);

            bool insideHull = sl.x >= HullMin.x && sl.x <= HullMax.x &&
                               sl.y >= HullMin.y && sl.y <= HullMax.y &&
                               sl.z >= HullMin.z && sl.z <= HullMax.z;

            if (!insideHull)
                return wp.y < 0f ? 1f : 0f; // sea surface is world Y=0

            int comp = bridge.FindCompartmentAt(sl);
            if (comp < 0) return 0f; // gap in hull -> treat as dry

            float waterY = bridge.GetInterpolatedWaterLevelY(comp); // ship-local
            return sl.y < waterY ? 1f : 0f; // submerged in flooded compartment
        }

        // Same hull/compartment test as ComputeSubmergence, reporting whether the camera is
        // specifically inside a flooded interior compartment (vs. the open ocean). Re-walks the
        // compartment list once more per frame for the single camera; cost is negligible.
        float ComputeInteriorBlend(Camera cam, SimulationBridge bridge)
        {
            Vector3 sl = bridge.WorldToShip(cam.transform.position);

            bool insideHull = sl.x >= HullMin.x && sl.x <= HullMax.x &&
                               sl.y >= HullMin.y && sl.y <= HullMax.y &&
                               sl.z >= HullMin.z && sl.z <= HullMax.z;
            if (!insideHull) return 0f; // exterior ocean, not interior

            int comp = bridge.FindCompartmentAt(sl);
            if (comp < 0) return 0f;

            float waterY = bridge.GetInterpolatedWaterLevelY(comp);
            return sl.y < waterY ? 1f : 0f;
        }

        static UnderwaterVolumeComponent GetVolumeComponent()
        {
            var stack = VolumeManager.instance.stack;
            return stack != null ? stack.GetComponent<UnderwaterVolumeComponent>() : null;
        }

        void UpdateUnderwaterSun(UnderwaterVolumeComponent uw, float cameraDepth)
        {
            if (_underwaterSun == null) return;

            Vector3 kd = uw != null ? uw.kdSun.value : DefaultKdSun;

            // Gameplay-tuned surface brightness (Subnautica lesson, design doc §0.2); physical
            // falloff with depth uses the green-channel Kd as the overall brightness reference.
            const float SurfaceIntensity = 1.1f;
            float intensity = SurfaceIntensity * Mathf.Exp(-kd.y * cameraDepth);

            if (intensity < 0.02f)
            {
                _underwaterSun.enabled = false;
                return;
            }

            _underwaterSun.enabled = true;
            _underwaterSun.intensity = intensity;

            // Blue-shift: attenuate each channel by its own Kd, then renormalize so hue carries
            // the color shift while `intensity` above alone carries the brightness falloff.
            Vector3 atten = new(
                Mathf.Exp(-kd.x * cameraDepth),
                Mathf.Exp(-kd.y * cameraDepth),
                Mathf.Exp(-kd.z * cameraDepth));
            float maxChannel = Mathf.Max(atten.x, Mathf.Max(atten.y, atten.z));
            if (maxChannel > 1e-5f) atten /= maxChannel;
            _underwaterSun.color = new Color(atten.x, atten.y, atten.z);
        }

        void UpdateAmbient(UnderwaterVolumeComponent uw, float cameraDepth)
        {
            Vector3 kd = uw != null ? uw.kdSun.value : DefaultKdSun;
            Color floor = uw != null ? uw.visibilityFloor.value : DefaultVisibilityFloor;

            _ambientAtDepth = new Color(
                _surfaceAmbient.r * Mathf.Exp(-kd.x * cameraDepth),
                _surfaceAmbient.g * Mathf.Exp(-kd.y * cameraDepth),
                _surfaceAmbient.b * Mathf.Exp(-kd.z * cameraDepth));

            // Gameplay visibility floor: prevents pitch-black ambient at extreme depth (§0.2).
            RenderSettings.ambientLight = new Color(
                Mathf.Max(_ambientAtDepth.r, floor.r),
                Mathf.Max(_ambientAtDepth.g, floor.g),
                Mathf.Max(_ambientAtDepth.b, floor.b));
        }

        void UpdateSunVisibility(UnderwaterVolumeComponent uw, float cameraDepth)
        {
            float godRayMaxDepth = uw != null ? uw.godRayMaxDepth.value : DefaultGodRayMaxDepth;
            // transform.forward is the light's travel direction; the sun is above the horizon
            // when that direction points downward (negative world Y).
            bool aboveHorizon = _worldSun != null && _worldSun.transform.forward.y < 0f;
            SunVisibleUnderwater = _submergence > 0.5f && cameraDepth < godRayMaxDepth && aboveHorizon;
        }

        void PushGlobalShaderConstants(Camera cam, UnderwaterVolumeComponent uw, float cameraDepth)
        {
            Shader.SetGlobalFloat(SubmergenceId, _submergence);

            Vector3 sunDir = _worldSun != null ? _worldSun.transform.forward : Vector3.down;
            Vector3 sunWorldPos = cam.transform.position - sunDir * 1000f;
            Vector3 sunViewport = cam.WorldToViewportPoint(sunWorldPos);
            float facing = Vector3.Dot(-sunDir, cam.transform.forward);
            Shader.SetGlobalVector(SunScreenPosId, new Vector4(sunViewport.x, sunViewport.y, facing, 0f));
            Shader.SetGlobalVector(SunDirWSId, sunDir);

            Vector3 kd = uw != null ? uw.kdSun.value : DefaultKdSun;
            Color sunColor = _worldSun != null ? _worldSun.color * _worldSun.intensity : Color.white;
            Color sunAtDepth = new(
                sunColor.r * Mathf.Exp(-kd.x * cameraDepth),
                sunColor.g * Mathf.Exp(-kd.y * cameraDepth),
                sunColor.b * Mathf.Exp(-kd.z * cameraDepth));
            Shader.SetGlobalColor(SunColorAtDepthId, sunAtDepth);
            Shader.SetGlobalColor(AmbientAtDepthId, _ambientAtDepth);

            // Precision: snap the caustics UV origin to a coarse grid so the fragment shader only
            // ever deals with small world-space offsets near the camera, regardless of how far
            // the ship has traveled from the origin (§3.9).
            float causticsScale = uw != null ? uw.causticsScale.value : DefaultCausticsScale;
            float grid = causticsScale * 64f;
            Vector2 causticsOrigin = new(
                Mathf.Floor(cam.transform.position.x / grid) * grid,
                Mathf.Floor(cam.transform.position.z / grid) * grid);
            Shader.SetGlobalVector(CausticsOriginId, causticsOrigin);

            Shader.SetGlobalFloat(LoopTimeId, Time.timeSinceLevelLoad % LoopPeriod);
            Shader.SetGlobalVector(HullMinId, HullMin);
            Shader.SetGlobalVector(HullMaxId, HullMax);
            Shader.SetGlobalFloat(InteriorFloodId, _interiorBlend);
        }
    }
}
