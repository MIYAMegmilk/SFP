using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public static class TerrainChunkMeshBuilder
    {
        const float CeilingEpsilon = 0.01f;

        public static void FillFloorMesh(ProceduralMapData map, TerrainChunk tile, Mesh mesh)
        {
            int vps = TerrainChunk.VertsPerSide;
            var vertices = new Vector3[vps * vps];
            var uvs = new Vector2[vps * vps];

            float ox = tile.Coord.OriginX;
            float oz = tile.Coord.OriginZ;

            for (int vz = 0; vz < vps; vz++)
            {
                for (int vx = 0; vx < vps; vx++)
                {
                    float wx = ox + vx * MapConstants.CellSize;
                    float wz = oz + vz * MapConstants.CellSize;
                    int idx = vz * vps + vx;
                    vertices[idx] = new Vector3(wx, -tile.FloorDepth[idx], wz);
                    uvs[idx] = new Vector2(wx / 256f, wz / 256f);
                }
            }

            var triangles = new int[(vps - 1) * (vps - 1) * 6];
            int t = 0;
            for (int vz = 0; vz < vps - 1; vz++)
            {
                for (int vx = 0; vx < vps - 1; vx++)
                {
                    int i0 = vz * vps + vx;
                    int i1 = i0 + 1;
                    int i2 = i0 + vps;
                    int i3 = i2 + 1;
                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;
                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            // Analytical normals to avoid seams at chunk borders
            var normals = new Vector3[vps * vps];
            float eps = MapConstants.CellSize;
            for (int vz = 0; vz < vps; vz++)
            {
                for (int vx = 0; vx < vps; vx++)
                {
                    float wx = ox + vx * eps;
                    float wz = oz + vz * eps;
                    float hL = -map.GetFloorDepthAt(wx - eps, wz);
                    float hR = -map.GetFloorDepthAt(wx + eps, wz);
                    float hD = -map.GetFloorDepthAt(wx, wz - eps);
                    float hU = -map.GetFloorDepthAt(wx, wz + eps);
                    var n = new Vector3(hL - hR, 2f * eps, hD - hU).normalized;
                    normals[vz * vps + vx] = n;
                }
            }
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }

        public static void FillCeilingMesh(ProceduralMapData map, TerrainChunk tile, Mesh mesh)
        {
            int vps = TerrainChunk.VertsPerSide;
            int cells = vps - 1;

            float ox = tile.Coord.OriginX;
            float oz = tile.Coord.OriginZ;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int vz = 0; vz < cells; vz++)
            {
                for (int vx = 0; vx < cells; vx++)
                {
                    float c00 = tile.CeilingDepth[vz * vps + vx];
                    float c10 = tile.CeilingDepth[vz * vps + vx + 1];
                    float c01 = tile.CeilingDepth[(vz + 1) * vps + vx];
                    float c11 = tile.CeilingDepth[(vz + 1) * vps + vx + 1];

                    // Skip cells where all 4 corners have no ceiling
                    if (c00 <= CeilingEpsilon && c10 <= CeilingEpsilon &&
                        c01 <= CeilingEpsilon && c11 <= CeilingEpsilon)
                        continue;

                    float worldX0 = ox + vx * MapConstants.CellSize;
                    float worldX1 = ox + (vx + 1) * MapConstants.CellSize;
                    float worldZ0 = oz + vz * MapConstants.CellSize;
                    float worldZ1 = oz + (vz + 1) * MapConstants.CellSize;

                    // Open corners (no ceiling) get stretched above the surface
                    float y00 = c00 > CeilingEpsilon ? -c00 : 8f;
                    float y10 = c10 > CeilingEpsilon ? -c10 : 8f;
                    float y01 = c01 > CeilingEpsilon ? -c01 : 8f;
                    float y11 = c11 > CeilingEpsilon ? -c11 : 8f;

                    int i0 = vertices.Count;
                    vertices.Add(new Vector3(worldX0, y00, worldZ0));
                    vertices.Add(new Vector3(worldX1, y10, worldZ0));
                    vertices.Add(new Vector3(worldX0, y01, worldZ1));
                    vertices.Add(new Vector3(worldX1, y11, worldZ1));

                    uvs.Add(new Vector2(worldX0 / 256f, worldZ0 / 256f));
                    uvs.Add(new Vector2(worldX1 / 256f, worldZ0 / 256f));
                    uvs.Add(new Vector2(worldX0 / 256f, worldZ1 / 256f));
                    uvs.Add(new Vector2(worldX1 / 256f, worldZ1 / 256f));

                    int i1 = i0 + 1;
                    int i2 = i0 + 2;
                    int i3 = i0 + 3;

                    // Reversed winding so faces point down
                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);

                    triangles.Add(i1);
                    triangles.Add(i3);
                    triangles.Add(i2);
                }
            }

            mesh.Clear();
            if (vertices.Count == 0) return;

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
