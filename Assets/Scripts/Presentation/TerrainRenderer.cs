using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class TerrainRenderer : MonoBehaviour
    {
        public Material RockMaterial;

        const int MaxCellsPerChunk = 60;
        const float CeilingEpsilon = 0.01f;

        void Start()
        {
            var map = SimulationBridge.Instance != null ? SimulationBridge.Instance.Map : null;
            if (map == null) return;

            var rockMat = RockMaterial != null
                ? RockMaterial
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            BuildChunks(map, rockMat);
            BuildCeilingChunks(map, rockMat);
            BuildWaterSurface(map);
        }

        void BuildChunks(MapData map, Material rockMat)
        {
            int chunksX = Mathf.CeilToInt((float)map.CellsX / MaxCellsPerChunk);
            int chunksZ = Mathf.CeilToInt((float)map.CellsZ / MaxCellsPerChunk);
            int chunkCellsX = Mathf.CeilToInt((float)map.CellsX / chunksX);
            int chunkCellsZ = Mathf.CeilToInt((float)map.CellsZ / chunksZ);

            for (int cz = 0; cz < chunksZ; cz++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    int startVx = cx * chunkCellsX;
                    int endVx = Mathf.Min(startVx + chunkCellsX, map.CellsX);
                    int startVz = cz * chunkCellsZ;
                    int endVz = Mathf.Min(startVz + chunkCellsZ, map.CellsZ);
                    BuildChunk(map, rockMat, startVx, endVx, startVz, endVz, cx, cz);
                }
            }
        }

        void BuildChunk(MapData map, Material rockMat, int startVx, int endVx, int startVz, int endVz, int cx, int cz)
        {
            int vertsX = endVx - startVx + 1;
            int vertsZ = endVz - startVz + 1;

            var vertices = new Vector3[vertsX * vertsZ];
            var uvs = new Vector2[vertsX * vertsZ];

            for (int vz = 0; vz < vertsZ; vz++)
            {
                for (int vx = 0; vx < vertsX; vx++)
                {
                    int cellVx = startVx + vx;
                    int cellVz = startVz + vz;
                    float worldX = map.OriginX + cellVx * map.CellSize;
                    float worldZ = map.OriginZ + cellVz * map.CellSize;
                    float depth = map.GetFloorDepthAt(worldX, worldZ);

                    int idx = vz * vertsX + vx;
                    vertices[idx] = new Vector3(worldX, -depth, worldZ);
                    uvs[idx] = new Vector2((float)cellVx / map.CellsX, (float)cellVz / map.CellsZ);
                }
            }

            var triangles = new int[(vertsX - 1) * (vertsZ - 1) * 6];
            int t = 0;
            for (int vz = 0; vz < vertsZ - 1; vz++)
            {
                for (int vx = 0; vx < vertsX - 1; vx++)
                {
                    int i0 = vz * vertsX + vx;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsX;
                    int i3 = i2 + 1;

                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;

                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            var mesh = new Mesh { name = $"TerrainChunk_{cx}_{cz}" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var chunkGo = new GameObject($"Chunk_{cx}_{cz}");
            chunkGo.transform.SetParent(transform, false);
            var mf = chunkGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = chunkGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rockMat;
            var mc = chunkGo.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        void BuildCeilingChunks(MapData map, Material rockMat)
        {
            if (map.CeilingDepth == null) return;

            int chunksX = Mathf.CeilToInt((float)map.CellsX / MaxCellsPerChunk);
            int chunksZ = Mathf.CeilToInt((float)map.CellsZ / MaxCellsPerChunk);
            int chunkCellsX = Mathf.CeilToInt((float)map.CellsX / chunksX);
            int chunkCellsZ = Mathf.CeilToInt((float)map.CellsZ / chunksZ);

            for (int cz = 0; cz < chunksZ; cz++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    int startVx = cx * chunkCellsX;
                    int endVx = Mathf.Min(startVx + chunkCellsX, map.CellsX);
                    int startVz = cz * chunkCellsZ;
                    int endVz = Mathf.Min(startVz + chunkCellsZ, map.CellsZ);
                    BuildCeilingChunk(map, rockMat, startVx, endVx, startVz, endVz, cx, cz);
                }
            }
        }

        void BuildCeilingChunk(MapData map, Material rockMat, int startVx, int endVx, int startVz, int endVz, int cx, int cz)
        {
            // Skip chunks with no ceiling rock at all — most of the map.
            bool anyCeiling = false;
            for (int z = startVz; z < endVz && !anyCeiling; z++)
            {
                for (int x = startVx; x < endVx; x++)
                {
                    if (map.CeilingDepth[z * map.CellsX + x] > CeilingEpsilon)
                    {
                        anyCeiling = true;
                        break;
                    }
                }
            }
            if (!anyCeiling) return;

            int vertsX = endVx - startVx + 1;
            int vertsZ = endVz - startVz + 1;

            // Sample ceiling depth at every grid vertex once, up front.
            var ceilingSamples = new float[vertsX * vertsZ];
            for (int vz = 0; vz < vertsZ; vz++)
            {
                for (int vx = 0; vx < vertsX; vx++)
                {
                    int cellVx = startVx + vx;
                    int cellVz = startVz + vz;
                    float worldX = map.OriginX + cellVx * map.CellSize;
                    float worldZ = map.OriginZ + cellVz * map.CellSize;
                    ceilingSamples[vz * vertsX + vx] = map.GetCeilingDepthAt(worldX, worldZ);
                }
            }

            // Emit a quad per cell only if at least one of its 4 corners has overhead rock;
            // cells that are fully open water are skipped so no sheet forms over open water
            // (that sheet used to cut through the ship interior). Emitted quads keep their
            // open corners stretched up above the surface, hidden from underwater view; this
            // forms natural rock-curtain shapes at cave mouths. Vertices are duplicated per
            // quad rather than shared across the grid, which is fine at this mesh scale.
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int vz = 0; vz < vertsZ - 1; vz++)
            {
                for (int vx = 0; vx < vertsX - 1; vx++)
                {
                    float c00 = ceilingSamples[vz * vertsX + vx];
                    float c10 = ceilingSamples[vz * vertsX + vx + 1];
                    float c01 = ceilingSamples[(vz + 1) * vertsX + vx];
                    float c11 = ceilingSamples[(vz + 1) * vertsX + vx + 1];

                    if (c00 <= CeilingEpsilon && c10 <= CeilingEpsilon && c01 <= CeilingEpsilon && c11 <= CeilingEpsilon)
                        continue;

                    int cellVx = startVx + vx;
                    int cellVz = startVz + vz;
                    float worldX0 = map.OriginX + cellVx * map.CellSize;
                    float worldX1 = map.OriginX + (cellVx + 1) * map.CellSize;
                    float worldZ0 = map.OriginZ + cellVz * map.CellSize;
                    float worldZ1 = map.OriginZ + (cellVz + 1) * map.CellSize;

                    float y00 = c00 > CeilingEpsilon ? -c00 : 8f;
                    float y10 = c10 > CeilingEpsilon ? -c10 : 8f;
                    float y01 = c01 > CeilingEpsilon ? -c01 : 8f;
                    float y11 = c11 > CeilingEpsilon ? -c11 : 8f;

                    int i0 = vertices.Count;
                    vertices.Add(new Vector3(worldX0, y00, worldZ0));
                    vertices.Add(new Vector3(worldX1, y10, worldZ0));
                    vertices.Add(new Vector3(worldX0, y01, worldZ1));
                    vertices.Add(new Vector3(worldX1, y11, worldZ1));

                    uvs.Add(new Vector2((float)cellVx / map.CellsX, (float)cellVz / map.CellsZ));
                    uvs.Add(new Vector2((float)(cellVx + 1) / map.CellsX, (float)cellVz / map.CellsZ));
                    uvs.Add(new Vector2((float)cellVx / map.CellsX, (float)(cellVz + 1) / map.CellsZ));
                    uvs.Add(new Vector2((float)(cellVx + 1) / map.CellsX, (float)(cellVz + 1) / map.CellsZ));

                    int i1 = i0 + 1;
                    int i2 = i0 + 2;
                    int i3 = i0 + 3;

                    // Reversed winding vs. the floor chunk so faces point down.
                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);

                    triangles.Add(i1);
                    triangles.Add(i3);
                    triangles.Add(i2);
                }
            }

            if (vertices.Count == 0) return;

            var mesh = new Mesh { name = $"CeilingChunk_{cx}_{cz}" };
            mesh.vertices = vertices.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var chunkGo = new GameObject($"CeilingChunk_{cx}_{cz}");
            chunkGo.transform.SetParent(transform, false);
            var mf = chunkGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = chunkGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rockMat;
            var mc = chunkGo.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        void BuildWaterSurface(MapData map)
        {
            float sizeX = map.WorldSizeX;
            float sizeZ = map.WorldSizeZ;

            var vertices = new[]
            {
                new Vector3(map.OriginX, 0f, map.OriginZ),
                new Vector3(map.OriginX + sizeX, 0f, map.OriginZ),
                new Vector3(map.OriginX, 0f, map.OriginZ + sizeZ),
                new Vector3(map.OriginX + sizeX, 0f, map.OriginZ + sizeZ),
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

            var waterGo = new GameObject("WaterSurface");
            waterGo.transform.SetParent(transform, false);
            var mf = waterGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = waterGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateWaterMaterial();
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
