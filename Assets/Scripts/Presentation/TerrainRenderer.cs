using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class TerrainRenderer : MonoBehaviour
    {
        public Material RockMaterial;

        GameObject _waterGo;

        void Start()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var rockMat = RockMaterial != null
                ? RockMaterial
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            var streaming = gameObject.AddComponent<ChunkStreamingManager>();
            streaming.RockMaterial = rockMat;

            CreateWaterSurface();
        }

        void CreateWaterSurface()
        {
            var vertices = new[]
            {
                new Vector3(-2048f, 0f, -2048f),
                new Vector3(2048f, 0f, -2048f),
                new Vector3(-2048f, 0f, 2048f),
                new Vector3(2048f, 0f, 2048f),
            };
            var uvs = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            var triangles = new[] { 0, 2, 1, 1, 2, 3 };

            var mesh = new Mesh { name = "WaterSurface" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _waterGo = new GameObject("WaterSurface");
            _waterGo.transform.SetParent(transform, false);
            var mf = _waterGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = _waterGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateWaterMaterial();
        }

        void LateUpdate()
        {
            if (_waterGo == null) return;
            var bridge = SimulationBridge.Instance;
            var sub = bridge != null ? bridge.SubState : null;
            if (sub == null) return;

            _waterGo.transform.position = new Vector3(sub.PositionX, 0f, sub.PositionZ);
        }

        static Material CreateWaterMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.color = new Color(0.05f, 0.25f, 0.45f, 0.35f);
            return mat;
        }
    }
}
