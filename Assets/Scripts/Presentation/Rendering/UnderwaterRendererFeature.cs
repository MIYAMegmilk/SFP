using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SFP.Presentation
{
    // Fullscreen underwater compositor: absorption/scattering fog, caustics, god rays and
    // backscatter over the already-rendered scene (design doc §2.3-§2.5). Injected right before
    // URP's post-processing stack so Bloom/ACES see the fogged/lit result, not the raw scene
    // (design doc §2.1).
    public sealed class UnderwaterRendererFeature : ScriptableRendererFeature
    {
        // Shader.Find can be stripped from builds, so this must be a serialized reference.
        // UnderwaterRenderingSetup (Editor) assigns it when the feature is registered on the asset.
        [SerializeField] Shader _underwaterShader;

        Material _material;
        UnderwaterPass _pass;

        public override void Create()
        {
            if (_underwaterShader == null)
                return;

            _material = CoreUtils.CreateEngineMaterial(_underwaterShader);
            _pass = new UnderwaterPass(_material)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null)
                return;

            // Scene view is excluded by default so editing isn't obscured by the effect.
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;

            // CPU-side early reject: skip queuing the pass entirely (cost 0) when the camera
            // isn't underwater (design doc §2.3/§6.4).
            if (UnderwaterEnvironmentController.GlobalSubmergence <= 0.001f)
                return;

            var uw = VolumeManager.instance.stack.GetComponent<UnderwaterVolumeComponent>();
            if (uw == null || !uw.IsActive())
                return;

            _pass.Setup(uw);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
        }
    }

    sealed class UnderwaterPass : ScriptableRenderPass
    {
        const int PassComposite = 0;
        const int PassGodRayMask = 1;
        const int PassGodRayBlur = 2;

        // Values at/below this are treated as "off" for the purpose of keyword stripping -
        // matches the shader's own #if branches so disabled features cost zero ALU.
        const float FeatureThreshold = 0.001f;

        static readonly int GodRayTexId = Shader.PropertyToID("_GodRayTex");

        static readonly int AbsorptionCoeffId = Shader.PropertyToID("_AbsorptionCoeff");
        static readonly int TurbidityId = Shader.PropertyToID("_Turbidity");
        static readonly int KdSunId = Shader.PropertyToID("_KdSun");
        static readonly int ScatterColorId = Shader.PropertyToID("_ScatterColor");
        static readonly int VisibilityFloorId = Shader.PropertyToID("_VisibilityFloor");
        static readonly int MaxViewDistanceId = Shader.PropertyToID("_MaxViewDistance");

        static readonly int CausticsIntensityId = Shader.PropertyToID("_CausticsIntensity");
        static readonly int CausticsScaleId = Shader.PropertyToID("_CausticsScale");
        static readonly int CausticsSpeedId = Shader.PropertyToID("_CausticsSpeed");
        static readonly int CausticsFadeId = Shader.PropertyToID("_CausticsFade");

        static readonly int GodRayIntensityId = Shader.PropertyToID("_GodRayIntensity");
        static readonly int GodRayDecayId = Shader.PropertyToID("_GodRayDecay");
        static readonly int GodRayMaxDepthId = Shader.PropertyToID("_GodRayMaxDepth");

        static readonly int DistortAmplitudeId = Shader.PropertyToID("_DistortAmplitude");
        static readonly int DistortFrequencyId = Shader.PropertyToID("_DistortFrequency");
        static readonly int DistortSpeedId = Shader.PropertyToID("_DistortSpeed");

        static readonly int BackscatterIntensityId = Shader.PropertyToID("_BackscatterIntensity");
        static readonly int BackscatterDensityId = Shader.PropertyToID("_BackscatterDensity");

        const string CausticsKeyword = "_SFP_CAUSTICS";
        const string GodRaysKeyword = "_SFP_GODRAYS";
        const string BackscatterKeyword = "_SFP_BACKSCATTER";

        readonly Material _mat;
        UnderwaterVolumeComponent _settings;

        bool _keywordsInitialized;
        bool _causticsKeywordState;
        bool _godRaysKeywordState;
        bool _backscatterKeywordState;

        public UnderwaterPass(Material mat)
        {
            _mat = mat;
            requiresIntermediateTexture = true; // active color may be the backbuffer, which can't be sampled directly
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Setup(UnderwaterVolumeComponent settings)
        {
            _settings = settings;
        }

        private sealed class CompositePassData
        {
            public Material mat;
            public TextureHandle source;
            public TextureHandle godRay;
        }

        private sealed class GodRaySubPassData
        {
            public Material mat;
            public TextureHandle source;
            public int passIndex;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            PushMaterialParams();

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;

            // God-ray subpasses (1/4 res mask + radial blur), only when the sun is actually
            // visible from underwater - otherwise the whole subpass tree is skipped (design doc §2.5/§3.7).
            TextureHandle godRay = TextureHandle.nullHandle;
            bool godRaysEnabled = _settings.godRayIntensity.value > FeatureThreshold
                                   && UnderwaterEnvironmentController.SunVisibleUnderwater;

            if (godRaysEnabled)
            {
                var godRayDesc = desc;
                godRayDesc.width = Mathf.Max(1, desc.width / 4);
                godRayDesc.height = Mathf.Max(1, desc.height / 4);
                godRayDesc.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;

                TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, godRayDesc, "_SFP_GodRayMask", false);
                TextureHandle blur = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, godRayDesc, "_SFP_GodRayBlur", false);

                AddGodRaySubPass(renderGraph, "SFP GodRay Mask", resourceData.activeColorTexture,
                    resourceData.cameraDepthTexture, mask, PassGodRayMask);
                AddGodRaySubPass(renderGraph, "SFP GodRay Blur", mask,
                    TextureHandle.nullHandle, blur, PassGodRayBlur);

                godRay = blur;
            }

            TextureHandle dest = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, desc, "_SFP_UnderwaterTarget", false);

            using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("SFP Underwater Composite", out var passData))
            {
                passData.mat = _mat;
                passData.source = resourceData.activeColorTexture;
                passData.godRay = godRay;

                builder.UseTexture(passData.source);
                builder.UseTexture(resourceData.cameraDepthTexture);
                if (godRay.IsValid())
                    builder.UseTexture(godRay);

                builder.SetRenderAttachment(dest, 0);

                builder.SetRenderFunc((CompositePassData data, RasterGraphContext ctx) =>
                {
                    if (data.godRay.IsValid())
                        data.mat.SetTexture(GodRayTexId, data.godRay);

                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.mat, PassComposite);
                });
            }

            // Framebuffer swap - no copy-back needed, downstream passes (post-processing) now read this.
            resourceData.cameraColor = dest;
        }

        private void AddGodRaySubPass(RenderGraph renderGraph, string passName, TextureHandle source,
            TextureHandle depth, TextureHandle dest, int passIndex)
        {
            using (var builder = renderGraph.AddRasterRenderPass<GodRaySubPassData>(passName, out var passData))
            {
                passData.mat = _mat;
                passData.source = source;
                passData.passIndex = passIndex;

                builder.UseTexture(source);
                if (depth.IsValid())
                    builder.UseTexture(depth);

                builder.SetRenderAttachment(dest, 0);

                builder.SetRenderFunc((GodRaySubPassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.mat, data.passIndex);
                });
            }
        }

        // Copies the blended VolumeComponent values onto the material and toggles keywords only
        // on change (design doc §4.4 - CoreUtils.EnableKeyword does a string compare internally,
        // so calling it unconditionally every frame is wasted work).
        private void PushMaterialParams()
        {
            _mat.SetVector(AbsorptionCoeffId, _settings.absorptionCoeff.value);
            _mat.SetFloat(TurbidityId, _settings.turbidity.value);
            _mat.SetVector(KdSunId, _settings.kdSun.value);
            _mat.SetColor(ScatterColorId, _settings.scatterColor.value);
            _mat.SetColor(VisibilityFloorId, _settings.visibilityFloor.value);
            _mat.SetFloat(MaxViewDistanceId, _settings.maxViewDistance.value);

            _mat.SetFloat(CausticsIntensityId, _settings.causticsIntensity.value);
            _mat.SetFloat(CausticsScaleId, _settings.causticsScale.value);
            _mat.SetFloat(CausticsSpeedId, _settings.causticsSpeed.value);
            _mat.SetVector(CausticsFadeId, _settings.causticsFade.value);

            _mat.SetFloat(GodRayIntensityId, _settings.godRayIntensity.value);
            _mat.SetFloat(GodRayDecayId, _settings.godRayDecay.value);
            _mat.SetFloat(GodRayMaxDepthId, _settings.godRayMaxDepth.value);

            _mat.SetFloat(DistortAmplitudeId, _settings.distortAmplitude.value);
            _mat.SetFloat(DistortFrequencyId, _settings.distortFrequency.value);
            _mat.SetFloat(DistortSpeedId, _settings.distortSpeed.value);

            _mat.SetFloat(BackscatterIntensityId, _settings.backscatterIntensity.value);
            _mat.SetFloat(BackscatterDensityId, _settings.backscatterDensity.value);

            bool caustics = _settings.causticsIntensity.value > FeatureThreshold;
            bool godRays = _settings.godRayIntensity.value > FeatureThreshold
                           && UnderwaterEnvironmentController.SunVisibleUnderwater;
            bool backscatter = _settings.backscatterIntensity.value > FeatureThreshold;

            if (!_keywordsInitialized || caustics != _causticsKeywordState)
            {
                CoreUtils.SetKeyword(_mat, CausticsKeyword, caustics);
                _causticsKeywordState = caustics;
            }

            if (!_keywordsInitialized || godRays != _godRaysKeywordState)
            {
                CoreUtils.SetKeyword(_mat, GodRaysKeyword, godRays);
                _godRaysKeywordState = godRays;
            }

            if (!_keywordsInitialized || backscatter != _backscatterKeywordState)
            {
                CoreUtils.SetKeyword(_mat, BackscatterKeyword, backscatter);
                _backscatterKeywordState = backscatter;
            }

            _keywordsInitialized = true;
        }
    }
}
