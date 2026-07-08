using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class MapGeneratorTests
    {
        [Test]
        public void Generate_IsDeterministic_ForSameSeed()
        {
            var a = MapGenerator.Generate(42, worldSize: 400f, cellSize: 8f);
            var b = MapGenerator.Generate(42, worldSize: 400f, cellSize: 8f);

            Assert.AreEqual(a.FloorDepth.Length, b.FloorDepth.Length);
            for (int i = 0; i < a.FloorDepth.Length; i++)
                Assert.AreEqual(a.FloorDepth[i], b.FloorDepth[i], 1e-6f);
        }

        [Test]
        public void SpawnPoint_IsDeepEnough()
        {
            var map = MapGenerator.Generate(7);
            float depth = map.GetFloorDepthAt(map.SpawnX, map.SpawnZ);
            Assert.GreaterOrEqual(depth, 400f);
        }

        [Test]
        public void BorderCells_AreAboveSeaLevel()
        {
            var map = MapGenerator.Generate(7);

            for (int x = 0; x < map.CellsX; x++)
            {
                Assert.LessOrEqual(map.FloorDepth[0 * map.CellsX + x], 0f, $"z=0 x={x}");
                Assert.LessOrEqual(map.FloorDepth[(map.CellsZ - 1) * map.CellsX + x], 0f, $"z=max x={x}");
            }
            for (int z = 0; z < map.CellsZ; z++)
            {
                Assert.LessOrEqual(map.FloorDepth[z * map.CellsX + 0], 0f, $"x=0 z={z}");
                Assert.LessOrEqual(map.FloorDepth[z * map.CellsX + (map.CellsX - 1)], 0f, $"x=max z={z}");
            }
        }

        [Test]
        public void GetFloorDepthAt_AtCellCorner_MatchesArrayValue()
        {
            var map = MapGenerator.Generate(7, worldSize: 400f, cellSize: 8f);

            int x0 = 15, z0 = 15;
            float expected = map.FloorDepth[z0 * map.CellsX + x0];
            float sampled = map.GetFloorDepthAt(x0 * map.CellSize, z0 * map.CellSize);

            Assert.AreEqual(expected, sampled, 1e-3f);
        }

        [Test]
        public void GetFloorDepthAt_BilinearInterpolation_IsBetweenNeighbors()
        {
            var map = MapGenerator.Generate(7, worldSize: 400f, cellSize: 8f);

            int x0 = 10, z0 = 10;
            float d00 = map.FloorDepth[z0 * map.CellsX + x0];
            float d10 = map.FloorDepth[z0 * map.CellsX + (x0 + 1)];

            float sampled = map.GetFloorDepthAt((x0 + 0.5f) * map.CellSize, z0 * map.CellSize);

            float lo = System.Math.Min(d00, d10);
            float hi = System.Math.Max(d00, d10);
            Assert.GreaterOrEqual(sampled, lo - 1e-4f);
            Assert.LessOrEqual(sampled, hi + 1e-4f);
        }

        [Test]
        public void Generate_CeilingIsDeterministic_ForSameSeed()
        {
            var a = MapGenerator.Generate(42, worldSize: 400f, cellSize: 8f);
            var b = MapGenerator.Generate(42, worldSize: 400f, cellSize: 8f);

            Assert.AreEqual(a.CeilingDepth.Length, b.CeilingDepth.Length);
            for (int i = 0; i < a.CeilingDepth.Length; i++)
                Assert.AreEqual(a.CeilingDepth[i], b.CeilingDepth[i], 1e-6f);
        }

        [Test]
        public void ChannelNetwork_IsNavigable()
        {
            var map = MapGenerator.Generate(7);

            Assert.GreaterOrEqual(map.ChannelWaypoints.Count, 4);

            for (int w = 1; w < map.ChannelWaypoints.Count; w++)
            {
                var a = map.ChannelWaypoints[w - 1];
                var b = map.ChannelWaypoints[w];

                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    float x = a.X + (b.X - a.X) * t;
                    float z = a.Z + (b.Z - a.Z) * t;

                    Assert.AreEqual(0f, map.GetCeilingDepthAt(x, z), 1e-3f, $"w={w} t={t}");
                    Assert.GreaterOrEqual(map.GetFloorDepthAt(x, z), 300f, $"w={w} t={t}");
                }
            }
        }

        [Test]
        public void Mines_AreValidPlacements()
        {
            var map = MapGenerator.Generate(7);

            Assert.Greater(map.MineSpots.Count, 0);

            foreach (var mine in map.MineSpots)
            {
                float dx = mine.X - map.SpawnX;
                float dz = mine.Z - map.SpawnZ;
                float distToSpawn = (float)System.Math.Sqrt(dx * dx + dz * dz);
                Assert.GreaterOrEqual(distToSpawn, 350f);

                float floor = map.GetFloorDepthAt(mine.X, mine.Z);
                float ceiling = map.GetCeilingDepthAt(mine.X, mine.Z);
                Assert.Less(mine.Depth, floor);
                Assert.Greater(mine.Depth, ceiling);
            }
        }

        [Test]
        public void Ceiling_NeverFullySealsWaterColumn()
        {
            var map = MapGenerator.Generate(7);

            for (int i = 0; i < map.CeilingDepth.Length; i++)
            {
                if (map.CeilingDepth[i] > 0f)
                    Assert.Less(map.CeilingDepth[i], map.FloorDepth[i], $"cell {i}");
            }
        }
    }
}
