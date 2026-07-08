using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class WaterFlowSampleTests
    {
        static ShallowWaterGrid MakeGrid()
        {
            // id 0, floorY 0, height 4, origin (0,0), 4x4m room, 1m cells -> 4x4 grid
            return new ShallowWaterGrid(0, floorY: 0f, roomHeight: 4f,
                originX: 0f, originZ: 0f, lengthX: 4f, widthZ: 4f, cellSize: 1f);
        }

        [Test]
        public void SampleFlow_AtCellCenter_ReturnsCellValues()
        {
            var grid = MakeGrid();
            int idx = grid.Idx(1, 1);
            grid.VelX[idx] = 2f;
            grid.VelZ[idx] = -1f;
            grid.H[idx] = 0.5f;

            // Cell (1,1) center is at origin + (1.5, 1.5) in world space.
            grid.SampleFlow(1.5f, 1.5f, out float velX, out float velZ, out float h);

            Assert.AreEqual(2f, velX, 1e-5f);
            Assert.AreEqual(-1f, velZ, 1e-5f);
            Assert.AreEqual(0.5f, h, 1e-5f);
        }

        [Test]
        public void SampleFlow_AtMidpointBetweenCells_ReturnsAverage()
        {
            var grid = MakeGrid();
            int idxA = grid.Idx(1, 1);
            int idxB = grid.Idx(2, 1);
            grid.VelX[idxA] = 2f;
            grid.VelX[idxB] = 4f;
            grid.H[idxA] = 0.2f;
            grid.H[idxB] = 0.6f;

            // Midpoint between cell (1,1) center (1.5,1.5) and cell (2,1) center (2.5,1.5).
            grid.SampleFlow(2f, 1.5f, out float velX, out _, out float h);

            Assert.AreEqual(3f, velX, 1e-5f);
            Assert.AreEqual(0.4f, h, 1e-5f);
        }

        [Test]
        public void SampleFlow_OutOfBounds_ClampsWithoutException()
        {
            var grid = MakeGrid();
            int idx = grid.Idx(0, 0);
            grid.VelX[idx] = 1f;
            grid.VelZ[idx] = 1f;
            grid.H[idx] = 0.3f;

            Assert.DoesNotThrow(() =>
            {
                grid.SampleFlow(-100f, -100f, out float velX, out float velZ, out float h);
                Assert.AreEqual(1f, velX, 1e-5f);
                Assert.AreEqual(1f, velZ, 1e-5f);
                Assert.AreEqual(0.3f, h, 1e-5f);
            });

            Assert.DoesNotThrow(() =>
            {
                grid.SampleFlow(1000f, 1000f, out float velX, out float velZ, out float h);
            });
        }
    }
}
