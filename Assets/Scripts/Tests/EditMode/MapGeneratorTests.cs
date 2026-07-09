using System;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class MapGeneratorTests
    {
        [Test]
        public void GenerateChunk_IsDeterministic_ForSameSeed()
        {
            var coord = new ChunkCoord(3, -2);
            var a = MapGenerator.GenerateChunk(42, coord);
            var b = MapGenerator.GenerateChunk(42, coord);

            Assert.AreEqual(a.FloorDepth.Length, b.FloorDepth.Length);
            for (int i = 0; i < a.FloorDepth.Length; i++)
                Assert.AreEqual(a.FloorDepth[i], b.FloorDepth[i], 1e-6f);
            for (int i = 0; i < a.CeilingDepth.Length; i++)
                Assert.AreEqual(a.CeilingDepth[i], b.CeilingDepth[i], 1e-6f);
        }

        [Test]
        public void SpawnPoint_IsDeepEnough()
        {
            var map = new ProceduralMapData(7);
            float depth = map.GetFloorDepthAt(map.SpawnX, map.SpawnZ);
            Assert.GreaterOrEqual(depth, 400f);
        }

        [Test]
        public void AdjacentChunks_ShareBorderVertices()
        {
            int seed = 42;
            var left = MapGenerator.GenerateChunk(seed, new ChunkCoord(0, 0));
            var right = MapGenerator.GenerateChunk(seed, new ChunkCoord(1, 0));

            int vps = TerrainChunk.VertsPerSide;
            for (int vz = 0; vz < vps; vz++)
            {
                float leftBorder = left.FloorDepth[vz * vps + (vps - 1)];
                float rightBorder = right.FloorDepth[vz * vps + 0];
                Assert.AreEqual(leftBorder, rightBorder, 1e-6f, $"floor seam at vz={vz}");

                float leftCeil = left.CeilingDepth[vz * vps + (vps - 1)];
                float rightCeil = right.CeilingDepth[vz * vps + 0];
                Assert.AreEqual(leftCeil, rightCeil, 1e-6f, $"ceiling seam at vz={vz}");
            }
        }

        [Test]
        public void AdjacentChunks_ShareBorderVertices_ZAxis()
        {
            int seed = 42;
            var bottom = MapGenerator.GenerateChunk(seed, new ChunkCoord(0, 0));
            var top = MapGenerator.GenerateChunk(seed, new ChunkCoord(0, 1));

            int vps = TerrainChunk.VertsPerSide;
            for (int vx = 0; vx < vps; vx++)
            {
                float bottomBorder = bottom.FloorDepth[(vps - 1) * vps + vx];
                float topBorder = top.FloorDepth[0 * vps + vx];
                Assert.AreEqual(bottomBorder, topBorder, 1e-6f, $"floor seam at vx={vx}");
            }
        }

        [Test]
        public void GetFloorDepthAt_MatchesBilinearOfChunk()
        {
            var map = new ProceduralMapData(7);
            var coord = new ChunkCoord(1, 1);
            var chunk = map.GetChunk(coord);

            int vps = TerrainChunk.VertsPerSide;
            int vx = 15, vz = 15;
            float expected = chunk.FloorDepth[vz * vps + vx];
            float wx = coord.OriginX + vx * MapConstants.CellSize;
            float wz = coord.OriginZ + vz * MapConstants.CellSize;
            float sampled = map.GetFloorDepthAt(wx, wz);

            Assert.AreEqual(expected, sampled, 1e-3f);
        }

        [Test]
        public void GetFloorDepthAt_BilinearInterpolation_IsBetweenNeighbors()
        {
            var map = new ProceduralMapData(7);
            var coord = new ChunkCoord(0, 0);
            var chunk = map.GetChunk(coord);

            int vps = TerrainChunk.VertsPerSide;
            int vx = 10, vz = 10;
            float d00 = chunk.FloorDepth[vz * vps + vx];
            float d10 = chunk.FloorDepth[vz * vps + (vx + 1)];

            float wx = coord.OriginX + (vx + 0.5f) * MapConstants.CellSize;
            float wz = coord.OriginZ + vz * MapConstants.CellSize;
            float sampled = map.GetFloorDepthAt(wx, wz);

            float lo = Math.Min(d00, d10);
            float hi = Math.Max(d00, d10);
            Assert.GreaterOrEqual(sampled, lo - 1e-4f);
            Assert.LessOrEqual(sampled, hi + 1e-4f);
        }

        [Test]
        public void ChannelNetwork_NodesHaveOpenWater()
        {
            var map = new ProceduralMapData(7);
            var nodes = new System.Collections.Generic.List<(float X, float Z)>();
            map.GetNearbyChannelNodes(map.SpawnX, map.SpawnZ, nodes);

            Assert.Greater(nodes.Count, 0);
            foreach (var node in nodes)
            {
                float floor = map.GetFloorDepthAt(node.X, node.Z);
                Assert.GreaterOrEqual(floor, 300f, $"node ({node.X},{node.Z}) floor too shallow");
            }
        }

        [Test]
        public void Mines_AreValidPlacements()
        {
            var map = new ProceduralMapData(7);
            var results = new System.Collections.Generic.List<(float X, float Z, float Depth)>();
            int totalMines = 0;

            for (int cx = -3; cx <= 3; cx++)
            {
                for (int cz = -3; cz <= 3; cz++)
                {
                    map.GetMinesForChunk(new ChunkCoord(cx, cz), results);
                    foreach (var mine in results)
                    {
                        totalMines++;
                        float floor = map.GetFloorDepthAt(mine.X, mine.Z);
                        Assert.Less(mine.Depth, floor, $"mine depth below floor at ({mine.X},{mine.Z})");
                        Assert.Greater(mine.Depth, 0f, $"mine above surface at ({mine.X},{mine.Z})");
                    }
                }
            }
        }

        [Test]
        public void Ceiling_NeverFullySealsWaterColumn()
        {
            var chunk = MapGenerator.GenerateChunk(7, new ChunkCoord(0, 0));
            int vps = TerrainChunk.VertsPerSide;

            for (int i = 0; i < vps * vps; i++)
            {
                if (chunk.CeilingDepth[i] > 0f)
                    Assert.Less(chunk.CeilingDepth[i], chunk.FloorDepth[i], $"vertex {i}");
            }
        }
    }
}
