using UnityEngine;

namespace SFP.Presentation
{
    // Camera-following particle box for deep-ocean marine snow (design doc §5.1).
    // Particles drift with ocean currents and sink slowly, creating the "floating dust
    // illuminated by headlamps" look essential for deep-sea atmosphere.
    public sealed class MarineSnowController : MonoBehaviour
    {
        const float BoxSize = 30f;
        const int MaxParticles = 2000;
        const float BaseRate = 300f;
        const float Lifetime = 12f;
        // Exaggerated ~100x vs real sinking speed (10-100 m/day) for visual effect
        const float SinkSpeed = -0.02f;
        const float MinDepth = 5f;
        const float FullDepth = 50f;
        const float CurrentUpdateInterval = 2f;

        ParticleSystem _ps;
        float _currentTimer;
        Vector3 _lastCurrentVel;

        void Start()
        {
            var go = new GameObject("MarineSnowParticles");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.maxParticles = MaxParticles;
            main.startLifetime = Lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
            main.startColor = new Color(0.8f, 0.85f, 0.9f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = _ps.emission;
            emission.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(BoxSize, BoxSize, BoxSize);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = 0f;
            vel.y = SinkSpeed;
            vel.z = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.EnableKeyword("_SOFTPARTICLES_ON");
            mat.color = new Color(0.8f, 0.85f, 0.9f, 0.35f);
            renderer.material = mat;
            renderer.minParticleSize = 0.001f;
            renderer.maxParticleSize = 0.01f;
        }

        void Update()
        {
            if (_ps == null) return;

            var bridge = SimulationBridge.Instance;
            var cam = Camera.main;
            if (bridge == null || cam == null) return;

            float submergence = UnderwaterEnvironmentController.GlobalSubmergence;
            float cameraDepth = Mathf.Max(0f, -cam.transform.position.y);

            // Emitter follows camera, offset forward to pre-fill the view
            transform.position = cam.transform.position + cam.transform.forward * 6f;

            // Density: zero near surface, full at depth, off when dry or inside ship
            float depthFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MinDepth, FullDepth, cameraDepth));
            bool insideShip = IsInsideShip(cam, bridge);
            float density = submergence > 0.5f && !insideShip ? depthFactor : 0f;

            var emission = _ps.emission;
            emission.rateOverTime = BaseRate * density;

            // Update ocean current drift periodically
            _currentTimer += Time.deltaTime;
            if (_currentTimer >= CurrentUpdateInterval)
            {
                _currentTimer = 0f;
                UpdateCurrentVelocity(bridge);
            }

            var vel = _ps.velocityOverLifetime;
            vel.x = _lastCurrentVel.x;
            vel.y = SinkSpeed + _lastCurrentVel.y;
            vel.z = _lastCurrentVel.z;
        }

        bool IsInsideShip(Camera cam, SimulationBridge bridge)
        {
            Vector3 sl = bridge.WorldToShip(cam.transform.position);
            return sl.x >= 0f && sl.x <= 24f &&
                   sl.y >= 0f && sl.y <= 18f &&
                   sl.z >= 0f && sl.z <= 6f;
        }

        void UpdateCurrentVelocity(SimulationBridge bridge)
        {
            if (bridge.OceanCurrents == null) return;

            bridge.OceanCurrents.Sample(
                bridge.SubState.PositionX,
                bridge.SubState.PositionZ,
                bridge.SubState.Depth,
                out float cx, out float cz);

            // Scale down for gentle particle drift (currents are in m/s for the submarine)
            _lastCurrentVel = new Vector3(cx * 0.3f, 0f, cz * 0.3f);
        }
    }
}
