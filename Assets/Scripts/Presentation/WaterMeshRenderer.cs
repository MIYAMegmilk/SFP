using UnityEngine;
using UnityEngine.Rendering;
using SFP.Simulation;

namespace SFP.Presentation
{
    [RequireComponent(typeof(CompartmentDefinition))]
    public class WaterMeshRenderer : MonoBehaviour
    {
        CompartmentDefinition _def;
        int _compartmentId = -1;
        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        GameObject _meshGo;
        GameObject _frontGo;
        MeshFilter _frontFilter;
        Mesh _frontMesh;

        Vector3[] _verts;
        Vector3[] _normals;
        int _vertsX, _vertsZ;
        Vector3 _parentPos;

        Vector3[] _frontVerts;

        static Material s_waterMat;

        void Start()
        {
            _def = GetComponent<CompartmentDefinition>();
        }

        void EnsureMesh(ShallowWaterGrid grid)
        {
            if (_mesh != null) return;

            _parentPos = transform.position;
            _vertsX = grid.ResX + 1;
            _vertsZ = grid.ResZ + 1;
            int vertCount = _vertsX * _vertsZ;

            _verts = new Vector3[vertCount];
            _normals = new Vector3[vertCount];

            for (int x = 0; x < _vertsX; x++)
            {
                for (int z = 0; z < _vertsZ; z++)
                {
                    float lx = grid.OriginX + x * grid.CellSize - _parentPos.x;
                    float lz = grid.OriginZ + z * grid.CellSize - _parentPos.z;
                    float ly = grid.FloorY - _parentPos.y;
                    _verts[x * _vertsZ + z] = new Vector3(lx, ly, lz);
                    _normals[x * _vertsZ + z] = Vector3.up;
                }
            }

            int quadCount = grid.ResX * grid.ResZ;
            int[] tris = new int[quadCount * 6];
            int ti = 0;
            for (int x = 0; x < grid.ResX; x++)
            {
                for (int z = 0; z < grid.ResZ; z++)
                {
                    int bl = x * _vertsZ + z;
                    int br = (x + 1) * _vertsZ + z;
                    int tl = x * _vertsZ + z + 1;
                    int tr = (x + 1) * _vertsZ + z + 1;

                    tris[ti++] = bl;
                    tris[ti++] = tl;
                    tris[ti++] = tr;
                    tris[ti++] = bl;
                    tris[ti++] = tr;
                    tris[ti++] = br;
                }
            }

            Vector2[] uv = new Vector2[vertCount];
            for (int x = 0; x < _vertsX; x++)
                for (int z = 0; z < _vertsZ; z++)
                    uv[x * _vertsZ + z] = new Vector2((float)x / grid.ResX, (float)z / grid.ResZ);

            _mesh = new Mesh();
            _mesh.name = "WaterSurface";
            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.uv = uv;
            _mesh.triangles = tris;

            _meshGo = new GameObject("WaterMesh");
            _meshGo.transform.SetParent(transform, false);
            _meshGo.transform.localPosition = Vector3.zero;
            _meshGo.transform.localRotation = Quaternion.identity;
            _meshGo.transform.localScale = Vector3.one;

            _filter = _meshGo.AddComponent<MeshFilter>();
            _filter.mesh = _mesh;
            _renderer = _meshGo.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = GetWaterMaterial();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            BuildFrontFace(grid);
        }

        void BuildFrontFace(ShallowWaterGrid grid)
        {
            int vertsPerCol = 2;
            int cols = grid.ResX + 1;
            _frontVerts = new Vector3[cols * vertsPerCol];

            float lz = grid.OriginZ - _parentPos.z;
            float ly = grid.FloorY - _parentPos.y;
            for (int x = 0; x < cols; x++)
            {
                float lx = grid.OriginX + x * grid.CellSize - _parentPos.x;
                _frontVerts[x * 2 + 0] = new Vector3(lx, ly, lz);
                _frontVerts[x * 2 + 1] = new Vector3(lx, ly, lz);
            }

            int[] tris = new int[(cols - 1) * 6];
            int ti = 0;
            for (int x = 0; x < cols - 1; x++)
            {
                int bl = x * 2;
                int tl = x * 2 + 1;
                int br = (x + 1) * 2;
                int tr = (x + 1) * 2 + 1;
                tris[ti++] = bl;
                tris[ti++] = tl;
                tris[ti++] = tr;
                tris[ti++] = bl;
                tris[ti++] = tr;
                tris[ti++] = br;
            }

            _frontMesh = new Mesh();
            _frontMesh.name = "WaterFront";
            _frontMesh.vertices = _frontVerts;
            _frontMesh.triangles = tris;
            _frontMesh.RecalculateNormals();

            _frontGo = new GameObject("WaterFront");
            _frontGo.transform.SetParent(transform, false);
            _frontGo.transform.localPosition = Vector3.zero;
            _frontGo.transform.localRotation = Quaternion.identity;
            _frontGo.transform.localScale = Vector3.one;

            var ff = _frontGo.AddComponent<MeshFilter>();
            ff.mesh = _frontMesh;
            var fr = _frontGo.AddComponent<MeshRenderer>();
            fr.sharedMaterial = GetWaterMaterial();
            fr.shadowCastingMode = ShadowCastingMode.Off;
            fr.receiveShadows = false;
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || bridge.WaterSystem == null) return;

            if (_compartmentId < 0)
                _compartmentId = bridge.GetCompartmentId(_def);
            if (_compartmentId < 0) return;

            var grid = bridge.WaterSystem.GetGrid(_compartmentId);
            if (grid == null) return;

            EnsureMesh(grid);

            bool hasWater = grid.TotalVolume() > 0.001f;
            _meshGo.SetActive(hasWater);
            _frontGo.SetActive(hasWater);
            if (!hasWater) return;

            _maskActive = grid.TotalVolume() < 4f;

            UpdateTopMesh(grid);
            UpdateFrontMesh(grid);
        }

        void UpdateTopMesh(ShallowWaterGrid grid)
        {
            for (int x = 0; x < _vertsX; x++)
            {
                for (int z = 0; z < _vertsZ; z++)
                {
                    float h = SampleCornerHeight(grid, x, z);
                    int vi = x * _vertsZ + z;
                    _verts[vi].y = grid.FloorY + h - _parentPos.y;

                    float dhdx = 0f, dhdz = 0f;
                    if (x > 0 && x < _vertsX - 1)
                        dhdx = (SampleCornerHeight(grid, x + 1, z) - SampleCornerHeight(grid, x - 1, z))
                               / (2f * grid.CellSize);
                    if (z > 0 && z < _vertsZ - 1)
                        dhdz = (SampleCornerHeight(grid, x, z + 1) - SampleCornerHeight(grid, x, z - 1))
                               / (2f * grid.CellSize);

                    var n = new Vector3(-dhdx, 1f, -dhdz).normalized;
                    _normals[vi] = n;
                }
            }

            _mesh.vertices = _verts;
            _mesh.normals = _normals;
            _mesh.RecalculateBounds();
        }

        void UpdateFrontMesh(ShallowWaterGrid grid)
        {
            for (int x = 0; x < _vertsX; x++)
            {
                float h = SampleCornerHeight(grid, x, 0);
                float lx = grid.OriginX + x * grid.CellSize - _parentPos.x;
                float lz = grid.OriginZ - _parentPos.z;
                float ly = grid.FloorY - _parentPos.y;
                _frontVerts[x * 2 + 0] = new Vector3(lx, ly, lz);
                _frontVerts[x * 2 + 1] = new Vector3(lx, ly + h, lz);
            }

            _frontMesh.vertices = _frontVerts;
            _frontMesh.RecalculateNormals();
            _frontMesh.RecalculateBounds();
        }

        bool _maskActive;

        float SampleCornerHeight(ShallowWaterGrid grid, int cx, int cz)
        {
            float sum = 0f;
            int count = 0;
            if (cx > 0 && cz > 0 && !(_maskActive && grid.OpeningMask[grid.Idx(cx - 1, cz - 1)]))
            {
                sum += grid.H[grid.Idx(cx - 1, cz - 1)];
                count++;
            }
            if (cx > 0 && cz < grid.ResZ && !(_maskActive && grid.OpeningMask[grid.Idx(cx - 1, cz)]))
            {
                sum += grid.H[grid.Idx(cx - 1, cz)];
                count++;
            }
            if (cx < grid.ResX && cz > 0 && !(_maskActive && grid.OpeningMask[grid.Idx(cx, cz - 1)]))
            {
                sum += grid.H[grid.Idx(cx, cz - 1)];
                count++;
            }
            if (cx < grid.ResX && cz < grid.ResZ && !(_maskActive && grid.OpeningMask[grid.Idx(cx, cz)]))
            {
                sum += grid.H[grid.Idx(cx, cz)];
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        static Material GetWaterMaterial()
        {
            if (s_waterMat != null) return s_waterMat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            s_waterMat = new Material(shader);
            s_waterMat.SetFloat("_Surface", 1f);
            s_waterMat.SetFloat("_Blend", 0f);
            s_waterMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            s_waterMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            s_waterMat.SetInt("_ZWrite", 0);
            s_waterMat.SetFloat("_Smoothness", 0.9f);
            s_waterMat.SetColor("_BaseColor", new Color(0.1f, 0.4f, 0.8f, 0.6f));
            s_waterMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_waterMat.renderQueue = (int)RenderQueue.Transparent;
            s_waterMat.SetFloat("_Cull", 0f);
            return s_waterMat;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_frontMesh != null) Destroy(_frontMesh);
            if (_meshGo != null) Destroy(_meshGo);
            if (_frontGo != null) Destroy(_frontGo);
        }
    }
}
