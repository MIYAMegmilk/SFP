using UnityEngine;
using UnityEngine.Rendering;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class PhysicsWaterEmitter : MonoBehaviour
    {
        const float SmoothTau = 0.25f;
        const float ActivateQ = 0.05f;
        const float DeactivateQ = 0.02f;
        const float SurfaceEpsilon = 0.03f;
        const float WallOffset = 0.08f;
        const int MaxSurfaceSplashPerFrame = 6;

        public LayerMask CollisionMask = ~0;

        struct Profile
        {
            public float ParticlesPerM3, MinRate, MaxRate;
            public float MinSpeed, MaxSpeed;
            public float SizeMin, SizeMax, Lifetime;
            public float Spread, NoisePerV, NoiseFreq, SplashProb, TrailRatio;
            public bool Stretched;
        }

        static Profile GetProfile(OpeningKind kind)
        {
            switch (kind)
            {
                case OpeningKind.Hatch:
                    return new Profile
                    {
                        ParticlesPerM3 = 130f, MinRate = 30f, MaxRate = 260f,
                        MinSpeed = 1f, MaxSpeed = 6f,
                        SizeMin = 0.06f, SizeMax = 0.12f, Lifetime = 1.6f,
                        Spread = 0.12f, NoisePerV = 0.04f, NoiseFreq = 2.5f,
                        SplashProb = 0.35f, TrailRatio = 0.45f
                    };
                case OpeningKind.Breach:
                    return new Profile
                    {
                        ParticlesPerM3 = 160f, MinRate = 60f, MaxRate = 400f,
                        MinSpeed = 6f, MaxSpeed = 16f,
                        SizeMin = 0.035f, SizeMax = 0.07f, Lifetime = 1.2f,
                        Spread = 0.02f, NoisePerV = 0.10f, NoiseFreq = 4f,
                        SplashProb = 0.50f, Stretched = true
                    };
                default: // Door
                    return new Profile
                    {
                        ParticlesPerM3 = 110f, MinRate = 25f, MaxRate = 220f,
                        MinSpeed = 0.4f, MaxSpeed = 8f,
                        SizeMin = 0.05f, SizeMax = 0.10f, Lifetime = 2.0f,
                        Spread = 0.06f, NoisePerV = 0.05f, NoiseFreq = 2.5f,
                        SplashProb = 0.30f, TrailRatio = 0.30f
                    };
            }
        }

        Opening _opening;
        OpeningDefinition _def;
        Profile _p;
        Vector3 _positiveFlowDir = Vector3.right;

        ParticleSystem _ps;
        ParticleSystem _splash;
        Transform _jetTf;
        ParticleSystem.Particle[] _buf;
        float _qSmooth;
        bool _active;

        static Material s_dropletMat;
        static Texture2D s_dropletTex;

        public void Init(Opening opening, OpeningDefinition def, Vector3 positiveFlowDir)
        {
            _opening = opening;
            _def = def;
            _p = GetProfile(opening.Kind);
            if (positiveFlowDir.sqrMagnitude > 1e-6f)
                _positiveFlowDir = positiveFlowDir.normalized;

            BuildJet();
            BuildSplash();
            _buf = new ParticleSystem.Particle[_ps.main.maxParticles];
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void BuildJet()
        {
            var go = new GameObject("WaterJet");
            go.transform.SetParent(transform, false);
            _jetTf = go.transform;
            _ps = go.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(_p.Lifetime * 0.85f, _p.Lifetime * 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_p.MinSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(_p.SizeMin, _p.SizeMax);
            main.startColor = Color.white;
            main.maxParticles = Mathf.CeilToInt(_p.MaxRate * _p.Lifetime * 1.3f);

            var emission = _ps.emission;
            emission.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.randomDirectionAmount = _p.Spread;
            if (_opening.Kind == OpeningKind.Breach)
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 4f;
                shape.radius = Mathf.Max(0.03f, Mathf.Sqrt(_opening.Area / Mathf.PI));
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.boxThickness = Vector3.zero;
            }

            var col = _ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.High;
            col.dampen = new ParticleSystem.MinMaxCurve(0.30f, 0.50f);
            col.bounce = new ParticleSystem.MinMaxCurve(0.15f, 0.30f);
            col.lifetimeLoss = 0.12f;
            col.minKillSpeed = 0f;
            col.radiusScale = 0.5f;
            col.collidesWith = CollisionMask;
            col.enableDynamicColliders = false;
            col.maxCollisionShapes = 256;
            col.sendCollisionMessages = false;

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.1f;
            noise.frequency = _p.NoiseFreq;
            noise.scrollSpeed = 1.2f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var colLife = _ps.colorOverLifetime;
            colLife.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.88f, 0.94f, 1f), 0f),
                    new GradientColorKey(new Color(0.45f, 0.68f, 0.92f), 0.35f),
                    new GradientColorKey(new Color(0.40f, 0.62f, 0.90f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.85f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
            colLife.color = g;

            var sizeLife = _ps.sizeOverLifetime;
            sizeLife.enabled = true;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0.85f), new Keyframe(0.15f, 1f),
                new Keyframe(0.8f, 1f), new Keyframe(1f, 0.55f));
            sizeLife.size = new ParticleSystem.MinMaxCurve(1f, curve);

            if (_p.TrailRatio > 0f)
            {
                var trails = _ps.trails;
                trails.enabled = true;
                trails.mode = ParticleSystemTrailMode.PerParticle;
                trails.ratio = _p.TrailRatio;
                trails.lifetime = 0.12f;
                trails.minVertexDistance = 0.08f;
                trails.dieWithParticles = true;
                trails.inheritParticleColor = true;
                trails.sizeAffectsWidth = true;
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f,
                    AnimationCurve.Linear(0f, 1f, 1f, 0f));
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = GetDropletMaterial();
            if (_p.TrailRatio > 0f)
                r.trailMaterial = GetDropletMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (_p.Stretched)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = 1.8f;
                r.velocityScale = 0.04f;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }

        void BuildSplash()
        {
            var go = new GameObject("WaterSplash");
            go.transform.SetParent(_jetTf, false);
            _splash = go.AddComponent<ParticleSystem>();

            var main = _splash.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new Color(0.92f, 0.96f, 1f, 0.8f);
            main.maxParticles = 300;

            var emission = _splash.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 4, 1, 0.01f) });

            var shape = _splash.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            var colLife = _splash.colorOverLifetime;
            colLife.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            colLife.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = GetDropletMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var sub = _ps.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(_splash, ParticleSystemSubEmitterType.Collision,
                ParticleSystemSubEmitterProperties.InheritNothing, _p.SplashProb);
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (_opening == null || bridge == null || _ps == null) return;

            float dt = Time.deltaTime;
            _qSmooth = Mathf.Lerp(_qSmooth, Mathf.Abs(_opening.FlowQ),
                1f - Mathf.Exp(-dt / SmoothTau));

            float sign = _opening.FlowQ >= 0f ? 1f : -1f;
            int upId = sign >= 0f ? _opening.CompartmentA : _opening.CompartmentB;
            int downId = sign >= 0f ? _opening.CompartmentB : _opening.CompartmentA;
            float upY = WaterY(bridge, upId);
            float downY = WaterY(bridge, downId);
            float openBottom = _opening.CenterY - _opening.Height * 0.5f;
            float openTop = _opening.CenterY + _opening.Height * 0.5f;

            float upFloor = upId == Opening.Sea ? float.MinValue : FloorY(bridge, upId);
            bool sourceHasWater = upId == Opening.Sea || (upY - upFloor) > 0.005f;
            bool submerged = upY >= openTop && downY >= openTop;
            float threshold = _active ? DeactivateQ : ActivateQ;
            bool shouldRun = _opening.IsOpen && !submerged && sourceHasWater && _qSmooth > threshold;

            if (shouldRun != _active)
            {
                _active = shouldRun;
                if (_active) _ps.Play(true);
                else _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (!_active) return;

            float qNorm = Mathf.Clamp01(_qSmooth / 1.5f);

            Vector3 flowDir = ComputeFlowDir(bridge, sign);
            AimAndShape(flowDir, upY, downY, openBottom, openTop, qNorm);

            float v = Mathf.Lerp(_p.MinSpeed, _p.MaxSpeed, qNorm);
            var main = _ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(v * 0.9f, v * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(_p.SizeMin * 0.5f, _p.SizeMin, qNorm),
                Mathf.Lerp(_p.SizeMax * 0.5f, _p.SizeMax, qNorm));
            main.gravityModifier = Mathf.Lerp(0.6f, 1f, qNorm);

            var emission = _ps.emission;
            emission.rateOverTime = Mathf.Lerp(_p.MinRate, _p.MaxRate, qNorm);

            var noise = _ps.noise;
            noise.strength = Mathf.Lerp(0.03f, Mathf.Min(v * _p.NoisePerV, 0.9f), qNorm);
        }

        Vector3 ComputeFlowDir(SimulationBridge bridge, float sign)
        {
            if (_opening.Kind == OpeningKind.Hatch)
            {
                float floorA = FloorY(bridge, _opening.CompartmentA);
                float floorB = FloorY(bridge, _opening.CompartmentB);
                float receivingFloor = sign >= 0f ? floorB : floorA;
                float sourceFloor = sign >= 0f ? floorA : floorB;
                return receivingFloor <= sourceFloor ? Vector3.down : Vector3.up;
            }
            return _positiveFlowDir * sign;
        }

        void AimAndShape(Vector3 flowDir, float upY, float downY,
            float openBottom, float openTop, float qNorm)
        {
            Vector3 origin = transform.position;
            var shape = _ps.shape;
            float scale = Mathf.Lerp(0.15f, 1f, qNorm);

            switch (_opening.Kind)
            {
                case OpeningKind.Door:
                {
                    float bottom = Mathf.Max(downY, openBottom);
                    float top = Mathf.Min(Mathf.Max(upY, bottom + 0.05f), openTop);
                    float wettedH = Mathf.Clamp(top - bottom, 0.05f, _opening.Height);
                    float width = _opening.Area / _opening.Height;

                    Vector3 center = origin + flowDir * WallOffset;
                    center.y = (top + bottom) * 0.5f;
                    _jetTf.SetPositionAndRotation(center,
                        Quaternion.LookRotation(flowDir, Vector3.up));
                    shape.scale = new Vector3(width * scale, wettedH, 0.06f);
                    break;
                }
                case OpeningKind.Hatch:
                {
                    float side = Mathf.Sqrt(Mathf.Max(_opening.Area, 0.01f)) * scale;
                    Vector3 center = origin + flowDir * WallOffset;
                    _jetTf.SetPositionAndRotation(center,
                        Quaternion.LookRotation(flowDir, Vector3.forward));
                    shape.scale = new Vector3(side, side, 0.05f);
                    break;
                }
                case OpeningKind.Breach:
                {
                    _jetTf.SetPositionAndRotation(origin + flowDir * WallOffset,
                        Quaternion.LookRotation(flowDir, Vector3.up));
                    shape.radius = Mathf.Max(0.03f,
                        Mathf.Sqrt(_opening.Area / Mathf.PI) * scale);
                    break;
                }
            }
        }

        void LateUpdate()
        {
            if (_ps == null || _ps.particleCount == 0) return;
            var bridge = SimulationBridge.Instance;
            if (bridge == null || _opening == null) return;

            float sign = _opening.FlowQ >= 0f ? 1f : -1f;
            int receivingId = sign >= 0f ? _opening.CompartmentB : _opening.CompartmentA;
            float waterY = WaterY(bridge, receivingId);
            float floorY = FloorY(bridge, receivingId);
            if (waterY <= floorY + 0.02f) return;

            int n = _ps.GetParticles(_buf);
            int splashes = 0;
            bool dirty = false;
            for (int i = 0; i < n; i++)
            {
                if (_buf[i].position.y > waterY + SurfaceEpsilon) continue;
                if (splashes < MaxSurfaceSplashPerFrame && _buf[i].velocity.y < -0.5f)
                {
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(_buf[i].position.x, waterY + 0.01f, _buf[i].position.z),
                        velocity = new Vector3(
                            Random.Range(-0.3f, 0.3f),
                            Random.Range(0.7f, 1.3f),
                            Random.Range(-0.3f, 0.3f)),
                        applyShapeToPosition = false
                    };
                    _splash.Emit(ep, 2);
                    splashes++;
                }
                _buf[i].remainingLifetime = -1f;
                dirty = true;
            }
            if (dirty) _ps.SetParticles(_buf, n);
        }

        static float WaterY(SimulationBridge bridge, int compartmentId)
        {
            return compartmentId == Opening.Sea
                ? bridge.Graph.SeaLevelY
                : bridge.GetInterpolatedWaterLevelY(compartmentId);
        }

        static float FloorY(SimulationBridge bridge, int compartmentId)
        {
            return compartmentId == Opening.Sea
                ? float.MinValue
                : bridge.Graph.GetCompartment(compartmentId).FloorY;
        }

        static Material GetDropletMaterial()
        {
            if (s_dropletMat != null) return s_dropletMat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            var m = new Material(shader);
            m.SetTexture("_BaseMap", GetDropletTexture());
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            s_dropletMat = m;
            return m;
        }

        static Texture2D GetDropletTexture()
        {
            if (s_dropletTex != null) return s_dropletTex;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            s_dropletTex = tex;
            return s_dropletTex;
        }

        void OnDestroy()
        {
            if (_jetTf != null) Destroy(_jetTf.gameObject);
        }
    }
}
