using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SFP.Presentation
{
    public enum UnderwaterQuality
    {
        Low,
        Medium,
        High
    }

    [Serializable]
    [VolumeComponentMenu("SFP/Underwater")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class UnderwaterVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter intensity = new(1f, 0f, 1f);

        public Vector3Parameter absorptionCoeff = new(new Vector3(0.45f, 0.065f, 0.0044f));

        public ClampedFloatParameter turbidity = new(0.05f, 0f, 2f);

        public Vector3Parameter kdSun = new(new Vector3(0.35f, 0.07f, 0.017f));

        public ColorParameter scatterColor = new(new Color(0.10f, 0.35f, 0.45f, 1f), false);

        public ColorParameter visibilityFloor = new(new Color(0.004f, 0.010f, 0.016f, 1f), true);

        public ClampedFloatParameter maxViewDistance = new(200f, 50f, 500f);

        public ClampedFloatParameter causticsIntensity = new(0.8f, 0f, 2f);

        public ClampedFloatParameter causticsScale = new(3.0f, 0.5f, 10f);

        public ClampedFloatParameter causticsSpeed = new(0.35f, 0f, 2f);

        public Vector2Parameter causticsFade = new(new Vector2(5f, 30f));

        public ClampedFloatParameter godRayIntensity = new(0.6f, 0f, 2f);

        public ClampedFloatParameter godRayDecay = new(0.94f, 0.8f, 0.99f);

        public ClampedFloatParameter godRayMaxDepth = new(60f, 0f, 200f);

        public ClampedFloatParameter distortAmplitude = new(0.0018f, 0f, 0.01f);

        public ClampedFloatParameter distortFrequency = new(22f, 1f, 60f);

        public ClampedFloatParameter distortSpeed = new(1.2f, 0f, 5f);

        public ClampedFloatParameter backscatterIntensity = new(1.0f, 0f, 4f);

        public ClampedFloatParameter backscatterDensity = new(0.02f, 0f, 0.2f);

        public ClampedFloatParameter marineSnowDensity = new(0.6f, 0f, 1f);

        [Tooltip("Quality level: Low, Medium, or High")]
        public UnderwaterQuality quality = UnderwaterQuality.High;

        public bool IsActive() => intensity.value > 0f;
    }
}
