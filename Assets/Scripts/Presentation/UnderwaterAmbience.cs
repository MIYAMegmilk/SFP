using UnityEngine;

namespace SFP.Presentation
{
    public class UnderwaterAmbience : MonoBehaviour
    {
        static readonly Color ShallowAmbient = new Color(0.16f, 0.32f, 0.42f);
        static readonly Color DeepAmbient = new Color(0.02f, 0.05f, 0.09f);
        static readonly Color DeepFogColor = new Color(0.015f, 0.09f, 0.16f);

        bool _originalsCaptured;
        bool _isUnderwater;

        bool _origFog;
        FogMode _origFogMode;
        Color _origFogColor;
        float _origFogDensity;
        Color _origAmbientLight;

        Light _underwaterSun;

        Camera _modifiedCamera;
        CameraClearFlags _origClearFlags;
        Color _origBackgroundColor;

        void Start()
        {
            _origFog = RenderSettings.fog;
            _origFogMode = RenderSettings.fogMode;
            _origFogColor = RenderSettings.fogColor;
            _origFogDensity = RenderSettings.fogDensity;
            _origAmbientLight = RenderSettings.ambientLight;
            _originalsCaptured = true;

            var sunGo = new GameObject("UnderwaterSun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(65f, 25f, 0f);
            _underwaterSun = sunGo.AddComponent<Light>();
            _underwaterSun.type = LightType.Directional;
            _underwaterSun.color = new Color(0.35f, 0.65f, 0.8f);
            _underwaterSun.shadows = LightShadows.None;
            _underwaterSun.enabled = false;
        }

        void Update()
        {
            if (!_originalsCaptured) return;

            var cam = Camera.main;
            if (cam == null) return;

            float camY = cam.transform.position.y;
            bool underwater = camY < 0f;

            if (underwater)
            {
                float depthFactor = Mathf.Clamp01(-camY / 500f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = DeepFogColor;
                RenderSettings.fogDensity = Mathf.Lerp(0.006f, 0.028f, Mathf.Clamp01(-camY / 600f));
                RenderSettings.ambientLight = Color.Lerp(ShallowAmbient, DeepAmbient, depthFactor);

                if (_underwaterSun != null)
                {
                    _underwaterSun.enabled = true;
                    _underwaterSun.intensity = Mathf.Lerp(1.1f, 0.08f, depthFactor);
                }

                // The active main camera can switch while underwater (player/spectator);
                // restore the previous one before taking over the new one.
                if (_modifiedCamera != cam)
                {
                    RestoreCameraBackground();
                    _modifiedCamera = cam;
                    _origClearFlags = cam.clearFlags;
                    _origBackgroundColor = cam.backgroundColor;
                }
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;

                _isUnderwater = true;
            }
            else if (_isUnderwater)
            {
                RenderSettings.fog = _origFog;
                RenderSettings.fogMode = _origFogMode;
                RenderSettings.fogColor = _origFogColor;
                RenderSettings.fogDensity = _origFogDensity;
                RenderSettings.ambientLight = _origAmbientLight;

                if (_underwaterSun != null)
                    _underwaterSun.enabled = false;

                RestoreCameraBackground();
                _isUnderwater = false;
            }
        }

        void RestoreCameraBackground()
        {
            if (_modifiedCamera != null)
            {
                _modifiedCamera.clearFlags = _origClearFlags;
                _modifiedCamera.backgroundColor = _origBackgroundColor;
            }
            _modifiedCamera = null;
        }
    }
}
