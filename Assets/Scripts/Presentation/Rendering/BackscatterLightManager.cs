using System.Collections.Generic;
using UnityEngine;

namespace SFP.Presentation
{
    // Selects the top-4 spot lights affecting the camera and pushes their data to
    // the _SFPBkLight* global shader arrays consumed by Underwater.shader's backscatter
    // pass (design doc §3.10, §6.3). Lights self-register via RegisterLight/UnregisterLight.
    public sealed class BackscatterLightManager : MonoBehaviour
    {
        const int MaxLights = 4;

        static readonly int CountId = Shader.PropertyToID("_SFPBkLightCount");
        static readonly int PosRangeId = Shader.PropertyToID("_SFPBkLightPosRange");
        static readonly int DirAngleId = Shader.PropertyToID("_SFPBkLightDirAngle");
        static readonly int ColorId = Shader.PropertyToID("_SFPBkLightColor");

        public static BackscatterLightManager Instance { get; private set; }

        readonly List<Light> _registered = new();
        readonly Vector4[] _posRange = new Vector4[MaxLights];
        readonly Vector4[] _dirAngle = new Vector4[MaxLights];
        readonly Vector4[] _color = new Vector4[MaxLights];

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void RegisterLight(Light light)
        {
            if (Instance != null && light != null && !Instance._registered.Contains(light))
                Instance._registered.Add(light);
        }

        public static void UnregisterLight(Light light)
        {
            if (Instance != null)
                Instance._registered.Remove(light);
        }

        void LateUpdate()
        {
            if (UnderwaterEnvironmentController.GlobalSubmergence <= 0.001f)
            {
                Shader.SetGlobalInt(CountId, 0);
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                Shader.SetGlobalInt(CountId, 0);
                return;
            }

            // Remove destroyed lights
            _registered.RemoveAll(l => l == null);

            // Sort by distance to camera (closest first), take top MaxLights
            Vector3 camPos = cam.transform.position;
            _registered.Sort((a, b) =>
            {
                float dA = (a.transform.position - camPos).sqrMagnitude;
                float dB = (b.transform.position - camPos).sqrMagnitude;
                return dA.CompareTo(dB);
            });

            int count = 0;
            for (int i = 0; i < _registered.Count && count < MaxLights; i++)
            {
                var light = _registered[i];
                if (!light.enabled || light.type != LightType.Spot)
                    continue;

                _posRange[count] = new Vector4(
                    light.transform.position.x,
                    light.transform.position.y,
                    light.transform.position.z,
                    light.range);

                _dirAngle[count] = new Vector4(
                    light.transform.forward.x,
                    light.transform.forward.y,
                    light.transform.forward.z,
                    Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad));

                _color[count] = new Vector4(
                    light.color.r * light.intensity,
                    light.color.g * light.intensity,
                    light.color.b * light.intensity,
                    1f);

                count++;
            }

            Shader.SetGlobalInt(CountId, count);
            Shader.SetGlobalVectorArray(PosRangeId, _posRange);
            Shader.SetGlobalVectorArray(DirAngleId, _dirAngle);
            Shader.SetGlobalVectorArray(ColorId, _color);
        }
    }
}
