// Full-screen underwater compositor: absorption/scattering fog, caustics, god rays and
// backscatter light cones over the already-rendered scene (design doc §2-§3). Invoked via
// Blitter.BlitTexture from UnderwaterPass (RenderPassEvent.BeforeRenderingPostProcessing),
// never drawn on regular geometry, hence no Properties block and no Cull/lighting setup.
Shader "SFP/Underwater"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Includes/SFPUnderwaterCommon.hlsl"
        ENDHLSL

        // Pass 0: main composite - distortion, Beer-Lambert fog, caustics, backscatter, god rays.
        Pass
        {
            Name "UnderwaterComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma multi_compile_local_fragment _ _SFP_GODRAYS
            #pragma multi_compile_local_fragment _ _SFP_CAUSTICS
            #pragma multi_compile_local_fragment _ _SFP_BACKSCATTER

            TEXTURE2D_X(_GodRayTex);

            // Beer-Lambert absorption + single-scattering, homogeneous-medium closed form
            // (design doc §3.5):
            //   L(d) = L0*e^(-c*d) + (b/c)*Lamb*(1 - e^(-c*d)),  c = a + b
            float3 ApplyWaterFog(float3 sceneColor, WaterRay r, float waterPath)
            {
                float3 c = _AbsorptionCoeff + _Turbidity;   // beam attenuation coefficient c=a+b [m^-1]
                float3 transmittance = exp(-c * waterPath); // Beer-Lambert (Pope & Fry 1997)

                // In-scattered light reaching the fragment: sunlight/ambient that survived down
                // to this depth, weighted by the single-scattering albedo omega = b/c.
                // _ScatterColor carries the spectral-shape correction.
                float3 omega = _Turbidity / max(c, 1e-4);
                float3 inscatterLight = _SFPSunColorAtDepth + _SFPAmbientAtDepth;
                float3 inscatter = omega * _ScatterColor * inscatterLight;

                // Gameplay layer: floor against total blackness (art-directed, no physical
                // basis - Subnautica lesson, design doc §0.2).
                inscatter = max(inscatter, _VisibilityFloor);

                return sceneColor * transmittance + inscatter * (1.0 - transmittance);
            }

            // Two animated gradient-noise layers, min()'d together so only points bright in
            // both layers survive - this is what produces the caustic "mesh" look.
            float CausticLayer(float2 uv, float t, float2 drift)
            {
                float n = noise(uv + drift * t);
                return n * 0.5 + 0.5;
            }

            float3 Caustics(WaterRay r)
            {
                // Depth gate: full strength at 5m, zero at 30m (effective range 0-30m).
                float fade = 1.0 - smoothstep(_CausticsFade.x, _CausticsFade.y, r.fragDepth);
                if (fade <= 0.0 || r.isSky || r.insideHull)
                    return float3(0, 0, 0);

                // Origin is snapped to a coarse world-space grid on the CPU side so the UV fed
                // into noise() stays small near the camera regardless of world position
                // (float precision at large XZ - design doc §3.9).
                float2 cuv = (r.posWS.xz - _SFPCausticsOrigin) / _CausticsScale;
                float t = _SFPLoopTime * _CausticsSpeed;

                float c1 = CausticLayer(cuv, t, float2(0.8, 0.6));
                float c2 = CausticLayer(cuv * 1.31, t, float2(-0.7, 0.9)); // 1.31 ratio avoids repeating interference
                float pattern = pow(min(c1, c2), 4.0) * 4.0;              // pow4 sharpens the mesh

                // Upward-facing surfaces catch more light from above; normal is approximated
                // from screen-space derivatives of the reconstructed world position.
                float3 nWS = normalize(cross(ddy(r.posWS), ddx(r.posWS)));
                float upFactor = saturate(nWS.y);

                // Sun's own depth attenuation already zeroes this out past ~90m (design doc §6.1).
                return _SFPSunColorAtDepth * pattern * upFactor * fade * _CausticsIntensity;
            }

            // Analytic ray / infinite-double-cone intersection (apex = light position, axis =
            // light forward, half-angle from cosOuter = dirAngle.w). Returns the ray-parameter
            // segment [t0,t1] on the cone's front nappe, clipped to the light's range and to
            // t>=0; t1<t0 signals "no intersection". Approximation: does not special-case a ray
            // passing exactly through the apex, which is visually irrelevant (the apex sits at
            // the light bulb itself) - design doc §3.10.
            float2 RayConeIntersect(float3 ro, float3 rd, float4 posRange, float4 dirAngle)
            {
                float3 co = ro - posRange.xyz;
                float3 v = dirAngle.xyz;
                float cos2 = dirAngle.w * dirAngle.w;

                float rdV = dot(rd, v);
                float coV = dot(co, v);
                float a = rdV * rdV - cos2;   // rd is unit length
                float b = 2.0 * (rdV * coV - cos2 * dot(rd, co));
                float c = coV * coV - cos2 * dot(co, co);

                float disc = b * b - 4.0 * a * c;
                if (abs(a) < 1e-6 || disc < 0.0)
                    return float2(0, -1);

                float sq = sqrt(disc);
                float2 t = float2(-b - sq, -b + sq) / (2.0 * a);
                if (t.x > t.y)
                    t = t.yx;

                // Discard the mirror nappe behind the apex.
                if (dot(co + t.x * rd, v) <= 0.0)
                    t.x = t.y;
                if (dot(co + t.y * rd, v) <= 0.0)
                    return float2(0, -1);

                t = max(t, 0.0);
                t.y = min(t.y, posRange.w);
                return t;
            }

            // Analytic spotlight scattering: for each of up to 4 selected lights (headlamp +
            // hull floodlights), march 6 samples through the ray/cone intersection segment and
            // integrate in-scattered light (design doc §3.10 - the Tyndall-effect light cone).
            float3 Backscatter(WaterRay r)
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(r.posWS - ro);
                float3 total = 0;

                for (int li = 0; li < _SFPBkLightCount; li++)
                {
                    float2 seg = RayConeIntersect(ro, rd, _SFPBkLightPosRange[li], _SFPBkLightDirAngle[li]);
                    seg.y = min(seg.y, r.viewDist); // clip to scene depth (occlusion)
                    if (seg.y <= seg.x)
                        continue;

                    const int STEPS = 6;
                    float dt = (seg.y - seg.x) / STEPS;
                    [loop]
                    for (int s = 0; s < STEPS; s++)
                    {
                        float3 p = ro + rd * (seg.x + (s + 0.5) * dt);
                        float3 toL = p - _SFPBkLightPosRange[li].xyz;
                        float distL = length(toL);

                        // Inverse-square falloff + the light's own water attenuation (one-way only).
                        float3 reach = _SFPBkLightColor[li].rgb
                                     * exp(-(_AbsorptionCoeff + _Turbidity) * distL)
                                     / max(distL * distL, 0.25);

                        float ang = dot(normalize(toL), _SFPBkLightDirAngle[li].xyz);
                        float cone = smoothstep(_SFPBkLightDirAngle[li].w,
                                                lerp(_SFPBkLightDirAngle[li].w, 1.0, 0.5), ang);

                        // Marine particles scatter forward (g=0.5, Petzold 1972).
                        float ph = HGPhase(dot(rd, normalize(-toL)), 0.5);

                        total += reach * cone * ph * _BackscatterDensity * dt;
                    }
                }

                // Return-trip attenuation to the camera is approximated by the segment midpoint
                // reach term above (diagonal approximation of the double integral).
                return total * _BackscatterIntensity;
            }

            half4 FragComposite(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;

                // Refraction: two sine-wave UV offsets; amplitude fades with submergence so the
                // effect vanishes exactly as the camera crosses the water surface.
                float t = _SFPLoopTime * _DistortSpeed;
                float2 wave = float2(
                    sin(uv.y * _DistortFrequency + t) + sin(uv.y * _DistortFrequency * 2.3 + t * 1.7) * 0.5,
                    sin(uv.x * _DistortFrequency * 1.2 - t * 1.1));
                float2 duv = uv + wave * _DistortAmplitude * _SFPSubmergence;

                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, duv).rgb;

                // Depth (and hence WaterRay) is sampled at the distorted UV too, so the fog
                // boundary lines up with the color it blends with - otherwise a color/depth
                // mismatch bleeds at silhouette edges.
                WaterRay r = ReconstructRay(duv);
                float waterPath = WaterPathLength(r, _WorldSpaceCameraPos);

            #if _SFP_CAUSTICS
                // Caustics light the surface before it is attenuated by the view-ray fog.
                sceneColor += Caustics(r);
            #endif

                float3 fogged = ApplyWaterFog(sceneColor, r, waterPath);

            #if _SFP_BACKSCATTER
                fogged += Backscatter(r);
            #endif

            #if _SFP_GODRAYS
                // Sampled at the undistorted uv: the god-ray buffer already encodes its own
                // screen-space blur direction toward the sun.
                fogged += SAMPLE_TEXTURE2D_X(_GodRayTex, sampler_LinearClamp, uv).rgb * _GodRayIntensity;
            #endif

                // Cross-fade at the water surface (a couple of frames while _SFPSubmergence
                // relaxes) instead of a hard cut, avoiding a pop when the camera crosses Y=0.
                return half4(lerp(sceneColor, fogged, _SFPSubmergence), 1);
            }
            ENDHLSL
        }

        // Pass 1: extract bright, sun-ward, far/sky pixels at 1/4 resolution (design doc §3.7).
        Pass
        {
            Name "GodRayMask"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGodRayMask

            // 1 when raw depth is at (or effectively at) the far plane - i.e. this pixel is the
            // light source itself (sky/sun), not just something lit by ambient light.
            float SkyOrBeyond(float raw, float threshold)
            {
                float eyeDepth = LinearEyeDepth(raw, _ZBufferParams);
                return step(threshold * _ProjectionParams.z, eyeDepth);
            }

            half4 FragGodRayMask(Varyings i) : SV_Target
            {
                float raw = SampleSceneDepth(i.texcoord);
                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord).rgb;

                float farMask = SkyOrBeyond(raw, 0.9);
                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                float bright = saturate(lum - 0.15); // below-threshold pixels are not a light source

                // Screen positions far from the sun contribute nothing, preventing off-screen
                // light sources from leaking rays in.
                float sunDist = distance(i.texcoord, _SFPSunScreenPos.xy);
                float sunMask = saturate(1.0 - sunDist * 1.5) * step(0.0, _SFPSunScreenPos.z);

                return half4(col * farMask * bright * sunMask, 1);
            }
            ENDHLSL
        }

        // Pass 2: 24-tap radial blur toward the sun's screen position (design doc §3.7).
        Pass
        {
            Name "GodRayBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGodRayBlur

            half4 FragGodRayBlur(Varyings i) : SV_Target
            {
                const int SAMPLES = 24;
                float2 dir = (_SFPSunScreenPos.xy - i.texcoord) / SAMPLES;
                float2 uv = i.texcoord;
                float3 acc = 0;
                float weight = 1.0;

                [unroll]
                for (int s = 0; s < SAMPLES; s++)
                {
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * weight;
                    weight *= _GodRayDecay; // 0.94^24 ~= 0.23, a natural-looking falloff
                    uv += dir;
                }

                return half4(acc / SAMPLES, 1);
            }
            ENDHLSL
        }
    }
}
