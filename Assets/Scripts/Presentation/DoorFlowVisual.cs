using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class DoorFlowVisual : MonoBehaviour
    {
        const float SmoothTau = 0.25f;
        const float ActivateThreshold = 0.05f;
        const float DeactivateThreshold = 0.02f;
        const float ScrollSpeedScale = 0.5f;
        const float ZOffset = -0.03f;

        static readonly Color CurtainTint = new Color(0.55f, 0.78f, 0.95f, 1f);
        static readonly Color TongueTint = new Color(0.85f, 0.92f, 1f, 1f);

        Opening _opening;
        OpeningDefinition _def;
        Transform _container;

        GameObject _curtainGO;
        GameObject _tongueGO;
        GameObject _splashGO;
        MeshRenderer _curtainRenderer;
        MeshRenderer _tongueRenderer;
        ParticleSystem _splashPs;
        MaterialPropertyBlock _curtainMpb;
        MaterialPropertyBlock _tongueMpb;

        Vector3 _baseDir = Vector3.right;
        float _qSmooth;
        bool _active;
        float _curtainScrollX;
        float _tongueScrollY;

        public void Init(Opening opening, OpeningDefinition def)
        {
            _opening = opening;
            _def = def;

            var containerGO = new GameObject("DoorFlowVisual_Container");
            containerGO.transform.SetParent(transform, false);
            _container = containerGO.transform;

            // Direction is derived once from room layout, then mirrored per-frame by FlowQ's sign.
            Vector3 delta = Vector3.right;
            if (def.CompartmentA != null && def.CompartmentB != null)
            {
                delta = def.CompartmentB.transform.position - def.CompartmentA.transform.position;
                delta.y = 0f;
            }
            _baseDir = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.right;

            BuildCurtain();
            BuildTongue();
            BuildSplash();

            SetActive(false);
        }

        void BuildCurtain()
        {
            _curtainGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _curtainGO.name = "DoorCurtain";
            Destroy(_curtainGO.GetComponent<Collider>());
            _curtainGO.transform.SetParent(_container, false);

            _curtainRenderer = _curtainGO.GetComponent<MeshRenderer>();
            _curtainRenderer.sharedMaterial = FlowMaterials.Streak;
            _curtainMpb = new MaterialPropertyBlock();
        }

        void BuildTongue()
        {
            _tongueGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _tongueGO.name = "DoorSurgeTongue";
            Destroy(_tongueGO.GetComponent<Collider>());
            _tongueGO.transform.SetParent(_container, false);

            _tongueRenderer = _tongueGO.GetComponent<MeshRenderer>();
            _tongueRenderer.sharedMaterial = FlowMaterials.Foam;
            _tongueMpb = new MaterialPropertyBlock();

            // ".mesh" (not ".sharedMesh") forces Unity to clone the built-in Quad mesh so the
            // baked gradient below stays local to this tongue instead of tainting every Quad primitive.
            var mesh = _tongueGO.GetComponent<MeshFilter>().mesh;
            var verts = mesh.vertices;
            var colors = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float t = Mathf.Clamp01(verts[i].y + 0.5f); // 0 at the door edge, 1 at the far edge
                colors[i] = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.05f, t));
            }
            mesh.colors = colors;
        }

        void BuildSplash()
        {
            _splashGO = new GameObject("DoorSplash");
            _splashGO.transform.SetParent(_container, false);

            _splashPs = _splashGO.AddComponent<ParticleSystem>();
            var main = _splashPs.main;
            main.maxParticles = 40;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.2f;
            main.startSize = 0.06f;
            main.startColor = new Color(0.85f, 0.92f, 1f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.6f;

            var emission = _splashPs.emission;
            emission.rateOverTime = 0f;

            var shape = _splashPs.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.boxThickness = Vector3.zero;

            var renderer = _splashGO.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.material.color = new Color(0.85f, 0.92f, 1f, 0.6f);
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (_opening == null || bridge == null) return;

            float dt = Time.deltaTime;
            float qAbs = Mathf.Abs(_opening.FlowQ);
            _qSmooth = Mathf.Lerp(_qSmooth, qAbs, 1f - Mathf.Exp(-dt / SmoothTau));

            float sign = _opening.FlowQ >= 0f ? 1f : -1f;
            Vector3 flowDir = _baseDir * sign;

            int upId = _opening.FlowQ >= 0f ? _opening.CompartmentA : _opening.CompartmentB;
            int downId = _opening.FlowQ >= 0f ? _opening.CompartmentB : _opening.CompartmentA;
            float upY = GetWaterY(bridge, upId);
            float downY = GetWaterY(bridge, downId);

            float openBottom = _opening.CenterY - _opening.Height * 0.5f;
            float openTop = _opening.CenterY + _opening.Height * 0.5f;

            bool submerged = downY >= openTop && upY >= openTop;
            float threshold = _active ? DeactivateThreshold : ActivateThreshold;
            bool shouldBeActive = !submerged && _qSmooth > threshold;

            if (shouldBeActive != _active)
                SetActive(shouldBeActive);

            if (!_active) return;

            float doorWidth = _opening.Area / _opening.Height;
            UpdateCurtain(flowDir, sign, doorWidth, upY, downY, openBottom, openTop, dt);
            UpdateTongueAndSplash(flowDir, sign, doorWidth, downY, dt);
        }

        float GetWaterY(SimulationBridge bridge, int compartmentId)
        {
            return compartmentId == Opening.Sea
                ? bridge.Graph.SeaLevelY
                : bridge.GetInterpolatedWaterLevelY(compartmentId);
        }

        void UpdateCurtain(Vector3 flowDir, float sign, float doorWidth,
            float upY, float downY, float openBottom, float openTop, float dt)
        {
            float curtainBottom = Mathf.Max(downY, openBottom);
            float curtainTop = Mathf.Min(upY, openTop);
            float curtainH = curtainTop - curtainBottom;

            if (curtainH < 0.03f)
            {
                _curtainRenderer.enabled = false;
                return;
            }
            _curtainRenderer.enabled = true;

            Vector3 doorPos = transform.position;
            Vector3 center = new Vector3(doorPos.x, (curtainTop + curtainBottom) * 0.5f, doorPos.z + ZOffset);

            _curtainGO.transform.SetPositionAndRotation(center, Quaternion.LookRotation(flowDir, Vector3.up));
            _curtainGO.transform.localScale = new Vector3(doorWidth, curtainH, 1f);

            float velocity = Mathf.Abs(_opening.FlowVelocity);
            _curtainScrollX += sign * velocity * dt * ScrollSpeedScale;

            var color = CurtainTint;
            color.a = Mathf.Clamp(_qSmooth / 3f, 0.15f, 0.85f);

            _curtainMpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, _curtainScrollX, 0f));
            _curtainMpb.SetColor("_BaseColor", color);
            _curtainRenderer.SetPropertyBlock(_curtainMpb);
        }

        void UpdateTongueAndSplash(Vector3 flowDir, float sign, float doorWidth, float downY, float dt)
        {
            float velocity = Mathf.Abs(_opening.FlowVelocity);
            float length = Mathf.Clamp(0.6f * velocity, 0.4f, 3.0f);
            float width = doorWidth * 1.4f;

            var downDef = _opening.FlowQ >= 0f ? _def.CompartmentB : _def.CompartmentA;
            float surfaceY = (downDef != null ? Mathf.Max(downY, downDef.FloorY) : downY) + 0.03f;

            Vector3 doorPos = transform.position;
            Vector3 doorXZ = new Vector3(doorPos.x, 0f, doorPos.z);

            // Forward/upwards are swapped versus the curtain: this pins the quad's normal to world
            // up (lying flat) while its local Y axis - the length dimension - follows flowDir.
            var rot = Quaternion.LookRotation(Vector3.up, flowDir);

            Vector3 center = doorXZ + flowDir * (length * 0.5f);
            center.y = surfaceY;
            center.z += ZOffset;

            _tongueGO.transform.SetPositionAndRotation(center, rot);
            _tongueGO.transform.localScale = new Vector3(width, length, 1f);

            _tongueScrollY += velocity * dt * ScrollSpeedScale;

            var color = TongueTint;
            color.a = Mathf.Clamp(_qSmooth / 3f, 0.15f, 0.85f);

            _tongueMpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, _tongueScrollY));
            _tongueMpb.SetColor("_BaseColor", color);
            _tongueRenderer.SetPropertyBlock(_tongueMpb);

            Vector3 leadingEdge = doorXZ + flowDir * length;
            leadingEdge.y = surfaceY + 0.02f;
            leadingEdge.z += ZOffset;

            _splashGO.transform.SetPositionAndRotation(leadingEdge, rot);

            var shape = _splashPs.shape;
            shape.scale = new Vector3(width, 0.05f, 0.05f);

            var emission = _splashPs.emission;
            emission.rateOverTime = Mathf.Lerp(6f, 28f, Mathf.InverseLerp(ActivateThreshold, 3f, _qSmooth));
        }

        void SetActive(bool active)
        {
            _active = active;
            if (_curtainRenderer != null) _curtainRenderer.enabled = active;
            if (_tongueRenderer != null) _tongueRenderer.enabled = active;
            if (_splashGO != null) _splashGO.SetActive(active);
        }

        void OnDestroy()
        {
            if (_container != null) Destroy(_container.gameObject);
        }
    }
}
