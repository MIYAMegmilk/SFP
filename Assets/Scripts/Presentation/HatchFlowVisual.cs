using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class HatchFlowVisual : MonoBehaviour
    {
        const float SmoothTau = 0.25f;
        const float ActivateThreshold = 0.05f;
        const float DeactivateThreshold = 0.02f;
        const float CrossfadeTime = 0.3f;
        const float MinWidth = 0.15f;
        const float MaxWidth = 2.5f;
        const float ZOffset = -0.03f;

        Opening _opening;
        OpeningDefinition _def;
        CompartmentDefinition _upperDef;
        CompartmentDefinition _lowerDef;
        bool _aIsUpper;
        int _upperId = -1;
        int _lowerId = -1;
        float _width;

        GameObject _fallQuadA;
        GameObject _fallQuadB;
        GameObject _splashQuad;
        GameObject _boilQuad;
        MeshRenderer _fallRendA;
        MeshRenderer _fallRendB;
        MeshRenderer _splashRend;
        MeshRenderer _boilRend;
        MaterialPropertyBlock _mpbFallA;
        MaterialPropertyBlock _mpbFallB;
        MaterialPropertyBlock _mpbSplash;
        MaterialPropertyBlock _mpbBoil;
        ParticleSystem _splashPs;

        float _qSmooth;
        bool _active;
        float _modeWeight; // 0 = falling column, 1 = boiling surface
        float _uvOffsetY;
        float _boilAngle;

        public void Init(Opening opening, OpeningDefinition def)
        {
            _opening = opening;
            _def = def;

            // A hatch's two sides aren't inherently "upper"/"lower" in the sim data, so we
            // derive that from floor height so we know which way is "down" for this instance.
            _aIsUpper = def.CompartmentA != null &&
                        (def.CompartmentB == null || def.CompartmentA.FloorY > def.CompartmentB.FloorY);
            _upperDef = _aIsUpper ? def.CompartmentA : def.CompartmentB;
            _lowerDef = _aIsUpper ? def.CompartmentB : def.CompartmentA;

            var bridge = SimulationBridge.Instance;
            _upperId = _upperDef != null && bridge != null ? bridge.GetCompartmentId(_upperDef) : -1;
            _lowerId = _lowerDef != null && bridge != null ? bridge.GetCompartmentId(_lowerDef) : -1;

            _width = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(opening.Area, 0.01f)), MinWidth, MaxWidth);

            BuildFallingColumn();
            BuildSplash();
            BuildBoil();

            SetVisible(false);
        }

        GameObject NewQuad(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        void BuildFallingColumn()
        {
            _fallQuadA = NewQuad("HatchFallA", FlowMaterials.Streak);
            _fallQuadB = NewQuad("HatchFallB", FlowMaterials.Streak);
            // Two vertical planes 90 degrees apart read as a solid column from any horizontal angle.
            _fallQuadB.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _fallRendA = _fallQuadA.GetComponent<MeshRenderer>();
            _fallRendB = _fallQuadB.GetComponent<MeshRenderer>();
            _mpbFallA = new MaterialPropertyBlock();
            _mpbFallB = new MaterialPropertyBlock();
        }

        void BuildSplash()
        {
            _splashQuad = NewQuad("HatchSplash", FlowMaterials.Foam);
            _splashQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _splashRend = _splashQuad.GetComponent<MeshRenderer>();
            _mpbSplash = new MaterialPropertyBlock();

            var psGo = new GameObject("HatchSplashParticles");
            psGo.transform.SetParent(transform, false);

            _splashPs = psGo.AddComponent<ParticleSystem>();
            var main = _splashPs.main;
            main.maxParticles = 60;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.5f;
            main.startSize = 0.05f;
            main.startColor = new Color(0.8f, 0.9f, 1f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;

            var emission = _splashPs.emission;
            emission.rateOverTime = 0f;

            var shape = _splashPs.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.08f;

            psGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = FlowMaterials.Foam;
        }

        void BuildBoil()
        {
            _boilQuad = NewQuad("HatchBoil", FlowMaterials.Foam);
            _boilQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _boilRend = _boilQuad.GetComponent<MeshRenderer>();
            _mpbBoil = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (_opening == null) return;
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            float dt = Time.deltaTime;
            _qSmooth = Mathf.Lerp(_qSmooth, Mathf.Abs(_opening.FlowQ), 1f - Mathf.Exp(-dt / SmoothTau));

            if (!_active && _qSmooth > ActivateThreshold) _active = true;
            else if (_active && _qSmooth < DeactivateThreshold) _active = false;

            if (!_active)
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            // FlowQ > 0 means A -> B; remap to "positive = flowing toward the upper room".
            float flowToUpper = _aIsUpper ? -_opening.FlowQ : _opening.FlowQ;
            float modeTarget = flowToUpper > 0f ? 1f : 0f;
            _modeWeight = Mathf.MoveTowards(_modeWeight, modeTarget, dt / CrossfadeTime);

            float baseAlpha = Mathf.Clamp(_qSmooth / 3f, 0.15f, 0.85f);
            float downAlpha = baseAlpha * (1f - _modeWeight);
            float upAlpha = baseAlpha * _modeWeight;

            float upperWaterY = GetWaterLevel(_upperDef, _upperId, bridge);
            float lowerWaterY = GetWaterLevel(_lowerDef, _lowerId, bridge);
            float lowerFloorY = _lowerDef != null ? _lowerDef.FloorY : lowerWaterY;

            Vector3 hatchXZ = transform.position;
            float hatchBottom = _opening.CenterY - _opening.Height * 0.5f;
            float impactY = Mathf.Max(lowerWaterY, lowerFloorY) + 0.03f;
            // Keep a minimum air gap so the column never degenerates once the lower room floods up to the hatch.
            float fallBottomY = Mathf.Min(impactY, hatchBottom - 0.05f);

            UpdateFallingColumn(hatchBottom, fallBottomY, downAlpha, hatchXZ, dt);
            UpdateSplash(fallBottomY, downAlpha, hatchXZ);
            UpdateBoil(upperWaterY, upAlpha, hatchXZ, dt);
        }

        float GetWaterLevel(CompartmentDefinition compDef, int id, SimulationBridge bridge)
        {
            if (compDef == null) return bridge.Graph.SeaLevelY;
            return id >= 0 ? bridge.GetInterpolatedWaterLevelY(id) : compDef.FloorY;
        }

        void UpdateFallingColumn(float fallTop, float fallBottomY, float alpha, Vector3 hatchXZ, float dt)
        {
            float fallHeight = Mathf.Max(fallTop - fallBottomY, 0.05f);
            float speed = 1.4f * Mathf.Sqrt(9.81f * fallHeight);
            _uvOffsetY -= speed * dt;

            var center = new Vector3(hatchXZ.x, (fallTop + fallBottomY) * 0.5f, hatchXZ.z + ZOffset);
            _fallQuadA.transform.position = center;
            _fallQuadA.transform.localScale = new Vector3(_width, fallHeight, 1f);
            _fallQuadB.transform.position = center;
            _fallQuadB.transform.localScale = new Vector3(_width, fallHeight, 1f);

            var color = new Color(0.45f, 0.65f, 0.85f, alpha);
            ApplyScroll(_fallRendA, _mpbFallA, color);
            ApplyScroll(_fallRendB, _mpbFallB, color);

            bool visible = alpha > 0.01f;
            _fallRendA.enabled = visible;
            _fallRendB.enabled = visible;
        }

        void ApplyScroll(MeshRenderer r, MaterialPropertyBlock mpb, Color color)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            mpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, _uvOffsetY));
            r.SetPropertyBlock(mpb);
        }

        void UpdateSplash(float impactY, float alpha, Vector3 hatchXZ)
        {
            var pos = new Vector3(hatchXZ.x, impactY, hatchXZ.z + ZOffset);
            _splashQuad.transform.position = pos;

            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 4f);
            float size = _width * 1.2f * pulse;
            _splashQuad.transform.localScale = new Vector3(size, size, 1f);

            _splashRend.GetPropertyBlock(_mpbSplash);
            _mpbSplash.SetColor("_BaseColor", new Color(0.75f, 0.88f, 1f, alpha));
            _splashRend.SetPropertyBlock(_mpbSplash);
            _splashRend.enabled = alpha > 0.01f;

            _splashPs.transform.position = pos;
            var emission = _splashPs.emission;
            emission.rateOverTime = Mathf.Lerp(0f, 40f, alpha / 0.85f);
        }

        void UpdateBoil(float upperWaterY, float alpha, Vector3 hatchXZ, float dt)
        {
            _boilAngle += dt * 20f;
            var pos = new Vector3(hatchXZ.x, upperWaterY + 0.03f, hatchXZ.z + ZOffset);
            _boilQuad.transform.position = pos;
            _boilQuad.transform.rotation = Quaternion.Euler(90f, _boilAngle, 0f);
            float size = _width * 1.4f;
            _boilQuad.transform.localScale = new Vector3(size, size, 1f);

            _boilRend.GetPropertyBlock(_mpbBoil);
            _mpbBoil.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f, alpha));
            _boilRend.SetPropertyBlock(_mpbBoil);
            _boilRend.enabled = alpha > 0.01f;
        }

        void SetVisible(bool visible)
        {
            _fallRendA.enabled = visible;
            _fallRendB.enabled = visible;
            _splashRend.enabled = visible;
            _boilRend.enabled = visible;
            if (!visible)
            {
                var emission = _splashPs.emission;
                emission.rateOverTime = 0f;
            }
        }
    }
}
