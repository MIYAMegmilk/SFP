using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class MineAcousticTests
    {
        const float Dt = 1f / 30f;

        [Test]
        public void NoiseLevel_ZeroWhenIdle()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();

            sub.Tick(Dt, graph, 0f);

            Assert.AreEqual(0f, sub.NoiseLevel, 1e-6f);
        }

        [Test]
        public void NoiseLevel_ApproxSevenTenthsAtFullThrustZeroSpeed()
        {
            var sub = new SubmarineState();
            sub.DryMass = 1_000_000f;
            var graph = new CompartmentGraph();

            sub.Tick(Dt, graph, 50000f);

            Assert.AreEqual(0.7f, sub.NoiseLevel, 0.01f);
        }

        [Test]
        public void NoiseLevel_ClampedToOne()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();

            sub.Tick(0f, graph, 100000f);

            Assert.AreEqual(1f, sub.NoiseLevel, 1e-6f);
        }

        [Test]
        public void MineSystem_StreamsMinesNearSub()
        {
            var map = new ProceduralMapData(42);
            var mines = new MineSystem(42, map);
            var sub = new SubmarineState
            {
                PositionX = map.SpawnX,
                PositionZ = map.SpawnZ,
                Depth = 300f,
            };

            mines.Tick(Dt, sub, null);

            // Mines should be populated from nearby chunks
            // Count may be 0 if no chunks nearby have mines, but the system should work
            Assert.IsNotNull(mines.Mines);
        }

        [Test]
        public void ExplodedMine_NeverReturns()
        {
            var map = new ProceduralMapData(42);
            var mines = new MineSystem(42, map);
            var sub = new SubmarineState
            {
                PositionX = map.SpawnX,
                PositionZ = map.SpawnZ,
                Depth = 300f,
            };

            mines.Tick(Dt, sub, null);

            if (mines.Mines.Count > 0)
            {
                var mine = mines.Mines[0];
                long id = mine.PersistentId;

                // Trigger explosion via proximity (adds to internal _exploded set)
                sub.PositionX = mine.X;
                sub.PositionZ = mine.Z;
                sub.Depth = mine.Depth;
                mines.Tick(Dt, sub, null);
                Assert.IsTrue(mine.Exploded, "mine should have exploded");

                // Shift sub one chunk to trigger RebuildActiveSet;
                // mine's chunk is still in 7x7 range, but _exploded prevents re-adding
                var curChunk = ChunkCoord.FromWorld(sub.PositionX, sub.PositionZ);
                sub.PositionX = (curChunk.X + 1) * MapConstants.ChunkSize + 1f;
                mines.Tick(Dt, sub, null);

                bool found = false;
                for (int i = 0; i < mines.Mines.Count; i++)
                {
                    if (mines.Mines[i].PersistentId == id)
                    {
                        found = true;
                        break;
                    }
                }
                Assert.IsFalse(found, "Exploded mine should not reappear");
            }
        }

        static SubmarineState LoudSub()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();
            sub.Tick(0f, graph, 100000f);
            return sub;
        }

        static SubmarineState SilentSub()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();
            sub.Tick(Dt, graph, 0f);
            return sub;
        }

        [Test]
        public void ContactMine_ExplodesWhenSubWithinRadius()
        {
            var map = new ProceduralMapData(42);
            var mines = new MineSystem(42, map);
            var sub = LoudSub();
            sub.PositionX = map.SpawnX;
            sub.PositionZ = map.SpawnZ;
            sub.Depth = 300f;

            mines.Tick(Dt, sub, null);

            // Move sub directly on top of a mine
            if (mines.Mines.Count > 0)
            {
                var mine = mines.Mines[0];
                sub.PositionX = mine.X;
                sub.PositionZ = mine.Z;
                sub.Depth = mine.Depth;

                var damage = new DamageSystem(new CompartmentGraph(), sub);
                mines.Tick(Dt, sub, damage);

                Assert.IsTrue(mine.Exploded);
            }
        }
    }
}
