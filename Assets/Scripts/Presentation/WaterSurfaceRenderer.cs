using UnityEngine;

namespace SFP.Presentation
{
    [RequireComponent(typeof(CompartmentDefinition))]
    public class WaterSurfaceRenderer : MonoBehaviour
    {
        public Material WaterMaterial;

        CompartmentDefinition _def;
        GameObject _topQuad;
        GameObject _frontQuad;
        int _compartmentId = -1;

        void Start()
        {
            _def = GetComponent<CompartmentDefinition>();
            CreateQuads();
        }

        void CreateQuads()
        {
            // Horizontal water surface (top)
            _topQuad = CreateQuad($"WaterTop_{_def.name}",
                Quaternion.Euler(90f, 0f, 0f),
                new Vector3(_def.LengthX, _def.WidthZ, 1f));

            // Vertical front face (south side, Z-)
            _frontQuad = CreateQuad($"WaterFront_{_def.name}",
                Quaternion.identity,
                Vector3.one); // scale set dynamically
        }

        GameObject CreateQuad(string name, Quaternion localRot, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform);
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            Destroy(go.GetComponent<Collider>());

            if (WaterMaterial != null)
            {
                var r = go.GetComponent<MeshRenderer>();
                r.material = WaterMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            go.SetActive(false);
            return go;
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            if (_compartmentId < 0)
                _compartmentId = bridge.GetCompartmentId(_def);
            if (_compartmentId < 0) return;

            var compartment = bridge.Graph.GetCompartment(_compartmentId);
            bool hasWater = compartment.WaterVolume > 0.001f;
            _topQuad.SetActive(hasWater);
            _frontQuad.SetActive(hasWater);

            if (hasWater)
            {
                float waterY = bridge.GetInterpolatedWaterLevelY(_compartmentId);
                float waterHeight = waterY - _def.FloorY;

                // Top quad at water surface
                var topPos = transform.position;
                topPos.y = waterY;
                _topQuad.transform.position = topPos;

                // Front quad: vertical rectangle from floor to water level, on south side
                float frontZ = transform.position.z - _def.WidthZ * 0.5f;
                _frontQuad.transform.localScale = new Vector3(_def.LengthX, waterHeight, 1f);
                _frontQuad.transform.position = new Vector3(
                    transform.position.x,
                    _def.FloorY + waterHeight * 0.5f,
                    frontZ);
            }
        }
    }
}
