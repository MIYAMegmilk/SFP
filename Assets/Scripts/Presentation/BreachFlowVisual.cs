using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    // Visualizes water jetting through a breach: an airborne parabolic jet that
    // slams into the floor/water below, or a bubble column once the room's own
    // water level rises above the breach and swallows it.
    public class BreachFlowVisual : MonoBehaviour
    {
        const float Gravity = 9.81f;
        const float SmoothTau = 0.25f;
        const float ActivateThreshold = 0.05f;
        const float DeactivateThreshold = 0.02f;

        static readonly Vector3 ZOffset = new Vector3(0f, 0f, -0.03f); // cutaway visibility nudge

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        Opening _opening;
        SimulationBridge _bridge;
        MaterialPropertyBlock _mpb;

        float _qSmooth;
        bool _active;

        GameObject _root;
        GameObject _jetGroup;
        GameObject _submergedGroup;

        readonly Transform[] _jetSegs = new Transform[3];
        readonly MeshRenderer[] _jetRenderers = new MeshRenderer[3];

        Transform _foamQuad;
        MeshRenderer _foamRenderer;

        ParticleSystem _splashPs;
        ParticleSystem _bubblePs;

        public void Init(Opening opening)
        {
            _opening = opening;
            _bridge = SimulationBridge.Instance;
            _mpb = new MaterialPropertyBlock();

            BuildRoot();
            _root.SetActive(false);
        }

        void BuildRoot()
        {
            _root = new GameObject("FlowVisual");
            _root.transform.SetParent(transform, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;

            BuildJetGroup();
            BuildFoam();
            BuildSubmergedGroup();
        }

        void BuildJetGroup()
        {
            _jetGroup = new GameObject("Jet");
            _jetGroup.transform.SetParent(_root.transform, false);

            for (int i = 0; i < 3; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "JetSeg" + i;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(_jetGroup.transform, false);

                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = FlowMaterials.Streak;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _jetSegs[i] = go.transform;
                _jetRenderers[i] = mr;
            }

            BuildSplash();
        }

        void BuildSplash()
        {
            var go = new GameObject("Splash");
            go.transform.SetParent(_jetGroup.transform, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // emit along local +Y (world up)

            _splashPs = go.AddComponent<ParticleSystem>();
            var main = _splashPs.main;
            main.maxParticles = 80;
            main.startLifetime = 0.5f;
            main.startSpeed = 2.5f;
            main.startSize = 0.06f;
            main.startColor = new Color(0.85f, 0.92f, 1f, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.loop = true;

            var emission = _splashPs.emission;
            emission.rateOverTime = 0f;

            var shape = _splashPs.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.05f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.material.color = new Color(0.85f, 0.92f, 1f, 0.8f);
        }

        void BuildFoam()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Foam";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat/horizontal

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = FlowMaterials.Foam;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _foamQuad = go.transform;
            _foamRenderer = mr;
        }

        void BuildSubmergedGroup()
        {
            _submergedGroup = new GameObject("Submerged");
            _submergedGroup.transform.SetParent(_root.transform, false);

            var go = new GameObject("Bubbles");
            go.transform.SetParent(_submergedGroup.transform, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // emit along local +Y (world up)

            _bubblePs = go.AddComponent<ParticleSystem>();
            var main = _bubblePs.main;
            main.maxParticles = 150;
            main.startLifetime = 1f;
            main.startSpeed = 0.8f;
            main.startSize = 0.05f;
            main.startColor = new Color(0.8f, 0.9f, 1f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.loop = true;

            var emission = _bubblePs.emission;
            emission.rateOverTime = 0f;

            var shape = _bubblePs.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.05f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.material.color = new Color(0.8f, 0.9f, 1f, 0.6f);
        }

        void Update()
        {
            if (_opening == null || _bridge == null) return;

            float dt = Time.deltaTime;
            _qSmooth = Mathf.Lerp(_qSmooth, Mathf.Abs(_opening.FlowQ), 1f - Mathf.Exp(-dt / SmoothTau));

            if (_qSmooth > ActivateThreshold) _active = true;
            else if (_qSmooth < DeactivateThreshold) _active = false;

            if (_root.activeSelf != _active)
                _root.SetActive(_active);

            if (!_active) return;

            ComputeDownstream(out float waterY, out float floorY, out Vector3 jetDir);

            bool submerged = waterY > _opening.CenterY + 0.05f;
            float alpha = Mathf.Clamp(_qSmooth / 2f, 0.3f, 0.95f);
            float radius = Mathf.Sqrt(Mathf.Max(_opening.Area, 0.0001f) / Mathf.PI);
            float v0 = Mathf.Max(Mathf.Abs(_opening.FlowVelocity), 0.15f);

            _jetGroup.SetActive(!submerged);
            _submergedGroup.SetActive(submerged);

            if (submerged)
                UpdateSubmerged(waterY, alpha, v0);
            else
                UpdateJet(jetDir, v0, radius, waterY, floorY, alpha);
        }

        // Positive FlowQ flows A->B; per breach construction, CompartmentA sits on the
        // +transform.forward side and CompartmentB sits on the -transform.forward side.
        // For sea breaches this yields the documented jetDir = -transform.forward (into
        // the room). For internal breaches it naturally flows from the higher-water side
        // to the lower one, since that's exactly what FlowQ's sign already encodes.
        void ComputeDownstream(out float waterY, out float floorY, out Vector3 jetDir)
        {
            bool aToB = _opening.FlowQ >= 0f;
            int downstreamId = aToB ? _opening.CompartmentB : _opening.CompartmentA;
            jetDir = aToB ? -transform.forward : transform.forward;

            if (downstreamId == Opening.Sea)
            {
                waterY = _bridge.Graph.SeaLevelY;
                floorY = -10000f; // sea has no floor to hit; its water level always dominates
            }
            else
            {
                waterY = _bridge.GetInterpolatedWaterLevelY(downstreamId);
                floorY = _bridge.Graph.GetCompartment(downstreamId).FloorY;
            }
        }

        void UpdateJet(Vector3 jetDir, float v0, float radius, float waterY, float floorY, float alpha)
        {
            Vector3 origin = transform.position;
            float targetY = Mathf.Max(waterY, floorY);
            float vy = jetDir.y * v0;
            float t = SolveTimeToY(origin.y, vy, targetY);

            Vector3 p0 = origin;
            Vector3 p1 = Sample(origin, jetDir, v0, t / 3f);
            Vector3 p2 = Sample(origin, jetDir, v0, 2f * t / 3f);
            Vector3 p3 = Sample(origin, jetDir, v0, t);

            float scroll = Time.time * v0 * 0.5f;

            PlaceCylinder(_jetSegs[0], _jetRenderers[0], p0, p1, radius * Mathf.Lerp(1f, 1.4f, 1f / 6f), alpha, scroll);
            PlaceCylinder(_jetSegs[1], _jetRenderers[1], p1, p2, radius * Mathf.Lerp(1f, 1.4f, 3f / 6f), alpha, scroll);
            PlaceCylinder(_jetSegs[2], _jetRenderers[2], p2, p3, radius * Mathf.Lerp(1f, 1.4f, 5f / 6f), alpha, scroll);

            Vector3 impact = p3 + ZOffset;
            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 8f);
            _foamQuad.position = impact;
            _foamQuad.localScale = Vector3.one * Mathf.Max(radius * 3f, 0.4f) * pulse;
            SetFoamAlpha(alpha * 0.9f);

            _splashPs.transform.position = impact;
            var emission = _splashPs.emission;
            emission.rateOverTime = Mathf.Lerp(20f, 150f, Mathf.InverseLerp(0.05f, 2f, _qSmooth));
            var main = _splashPs.main;
            main.startColor = new Color(0.85f, 0.92f, 1f, alpha);
        }

        void UpdateSubmerged(float waterY, float alpha, float v0)
        {
            Vector3 origin = transform.position;
            float gap = Mathf.Max(waterY - origin.y, 0.1f);
            float speed = Mathf.Clamp(v0 * 0.3f, 0.4f, 2f);

            _bubblePs.transform.position = origin + ZOffset;
            var main = _bubblePs.main;
            main.startSpeed = speed;
            main.startLifetime = gap / speed;
            main.startColor = new Color(0.8f, 0.9f, 1f, alpha * 0.7f);
            var emission = _bubblePs.emission;
            emission.rateOverTime = _qSmooth * 40f;

            Vector3 surfacePos = new Vector3(origin.x, waterY, origin.z) + ZOffset;
            float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 5f);
            _foamQuad.position = surfacePos;
            _foamQuad.localScale = Vector3.one * Mathf.Max(Mathf.Sqrt(_opening.Area) * 2.5f, 0.4f) * pulse;
            SetFoamAlpha(alpha * 0.8f);
        }

        void PlaceCylinder(Transform cyl, MeshRenderer mr, Vector3 a, Vector3 b, float radius, float alpha, float scroll)
        {
            a += ZOffset;
            b += ZOffset;
            Vector3 diff = b - a;
            float len = Mathf.Max(diff.magnitude, 0.01f);
            Vector3 dir = diff / len;

            // Built-in cylinder primitive: height 2 (scale.y=1), diameter 1 (scale.x/z=1)
            cyl.position = (a + b) * 0.5f;
            cyl.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            cyl.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);

            mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, new Color(0.6f, 0.8f, 0.98f, alpha));
            _mpb.SetVector(BaseMapStId, new Vector4(1f, 2f, 0f, scroll)); // scroll UV V along axis
            mr.SetPropertyBlock(_mpb);
        }

        void SetFoamAlpha(float alpha)
        {
            _foamRenderer.GetPropertyBlock(_mpb);
            var c = FlowMaterials.Foam.GetColor(BaseColorId);
            c.a = alpha;
            _mpb.SetColor(BaseColorId, c);
            _foamRenderer.SetPropertyBlock(_mpb);
        }

        static Vector3 Sample(Vector3 origin, Vector3 dir, float v0, float t)
        {
            return origin + dir * v0 * t + 0.5f * Gravity * t * t * Vector3.down;
        }

        static float SolveTimeToY(float y0, float vy, float targetY)
        {
            float dy = y0 - targetY;
            if (dy <= 0.01f) return 0.15f; // breach already at/above target level

            float a = 0.5f * Gravity;
            float b = -vy;
            float c = -dy;
            float disc = Mathf.Max(b * b - 4f * a * c, 0f);
            float t = (-b + Mathf.Sqrt(disc)) / (2f * a);
            return Mathf.Max(t, 0.1f);
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }
    }
}
