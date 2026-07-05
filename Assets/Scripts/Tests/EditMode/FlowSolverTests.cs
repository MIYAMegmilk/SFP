using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class FlowSolverTests
    {
        const float Dt = 1f / 30f;

        static CompartmentGraph TwoRoomGraph(float waterA, float waterB,
            float floorY = 0f, float height = 4f, float floorArea = 10f)
        {
            var graph = new CompartmentGraph();
            var a = graph.AddCompartment(floorY, height, floorArea);
            var b = graph.AddCompartment(floorY, height, floorArea);
            a.WaterVolume = waterA;
            b.WaterVolume = waterB;
            graph.AddOpening(OpeningKind.Door, a.Id, b.Id,
                area: 1f, centerY: floorY + height * 0.5f, height: height);
            return graph;
        }

        [Test]
        public void MassConservation_TwoCompartments()
        {
            var graph = TwoRoomGraph(waterA: 20f, waterB: 0f);
            float totalBefore = graph.GetCompartment(0).WaterVolume
                              + graph.GetCompartment(1).WaterVolume;

            var solver = new FlowSolver();
            for (int i = 0; i < 300; i++)
                solver.Tick(graph, Dt);

            float totalAfter = graph.GetCompartment(0).WaterVolume
                             + graph.GetCompartment(1).WaterVolume;

            Assert.AreEqual(totalBefore, totalAfter, 1e-3f,
                "Total water must be conserved across ticks");
        }

        [Test]
        public void MassConservation_ThreeCompartments()
        {
            var graph = new CompartmentGraph();
            var a = graph.AddCompartment(0f, 4f, 10f);
            var b = graph.AddCompartment(0f, 4f, 10f);
            var c = graph.AddCompartment(0f, 4f, 10f);
            a.WaterVolume = 30f;
            graph.AddOpening(OpeningKind.Door, a.Id, b.Id, 1f, 2f, 4f);
            graph.AddOpening(OpeningKind.Door, b.Id, c.Id, 1f, 2f, 4f);

            float totalBefore = a.WaterVolume + b.WaterVolume + c.WaterVolume;

            var solver = new FlowSolver();
            for (int i = 0; i < 300; i++)
                solver.Tick(graph, Dt);

            float totalAfter = a.WaterVolume + b.WaterVolume + c.WaterVolume;
            Assert.AreEqual(totalBefore, totalAfter, 1e-3f);
        }

        [Test]
        public void Clamp_SenderDoesNotGoNegative()
        {
            var graph = TwoRoomGraph(waterA: 0.001f, waterB: 0f);

            var solver = new FlowSolver();
            for (int i = 0; i < 100; i++)
                solver.Tick(graph, Dt);

            Assert.GreaterOrEqual(graph.GetCompartment(0).WaterVolume, 0f);
            Assert.GreaterOrEqual(graph.GetCompartment(1).WaterVolume, 0f);
        }

        [Test]
        public void Clamp_ReceiverDoesNotOverflow()
        {
            var graph = TwoRoomGraph(waterA: 40f, waterB: 39.99f);

            var solver = new FlowSolver();
            for (int i = 0; i < 100; i++)
                solver.Tick(graph, Dt);

            var b = graph.GetCompartment(1);
            Assert.LessOrEqual(b.WaterVolume, b.Volume + 1e-6f);
        }

        [Test]
        public void WaterLevel_EmptyCompartment()
        {
            var graph = new CompartmentGraph();
            var c = graph.AddCompartment(floorY: 5f, height: 4f, floorArea: 10f);
            c.WaterVolume = 0f;

            Assert.AreEqual(5f, c.WaterLevelY, 1e-6f);
        }

        [Test]
        public void WaterLevel_FullCompartment()
        {
            var graph = new CompartmentGraph();
            var c = graph.AddCompartment(floorY: 5f, height: 4f, floorArea: 10f);
            c.WaterVolume = c.Volume;

            Assert.AreEqual(9f, c.WaterLevelY, 1e-6f);
        }

        [Test]
        public void WaterLevel_HalfFull()
        {
            var graph = new CompartmentGraph();
            var c = graph.AddCompartment(floorY: 0f, height: 4f, floorArea: 10f);
            c.WaterVolume = c.Volume * 0.5f;

            Assert.AreEqual(2f, c.WaterLevelY, 1e-6f);
        }

        [Test]
        public void TwoRooms_WaterFlowsFromHighToLow()
        {
            var graph = TwoRoomGraph(waterA: 20f, waterB: 0f);
            var solver = new FlowSolver();

            solver.Tick(graph, Dt);

            Assert.Less(graph.GetCompartment(0).WaterVolume, 20f,
                "Water should leave the higher compartment");
            Assert.Greater(graph.GetCompartment(1).WaterVolume, 0f,
                "Water should enter the lower compartment");
        }

        [Test]
        public void TwoRooms_ConvergesToEquilibrium()
        {
            var graph = TwoRoomGraph(waterA: 30f, waterB: 10f);
            var solver = new FlowSolver();

            for (int i = 0; i < 3000; i++)
                solver.Tick(graph, Dt);

            float a = graph.GetCompartment(0).WaterVolume;
            float b = graph.GetCompartment(1).WaterVolume;
            Assert.AreEqual(a, b, 0.1f,
                "Equal-sized compartments should reach equal water levels");
        }

        [Test]
        public void ClosedDoor_BlocksFlow()
        {
            var graph = TwoRoomGraph(waterA: 20f, waterB: 0f);
            graph.Openings[0].IsOpen = false;

            var solver = new FlowSolver();
            solver.Tick(graph, Dt);

            Assert.AreEqual(20f, graph.GetCompartment(0).WaterVolume, 1e-6f);
            Assert.AreEqual(0f, graph.GetCompartment(1).WaterVolume, 1e-6f);
        }

        [Test]
        public void SeaBreach_FloodsCompartment()
        {
            var graph = new CompartmentGraph();
            graph.SeaLevelY = 10f;
            var c = graph.AddCompartment(floorY: 0f, height: 4f, floorArea: 10f);
            graph.AddOpening(OpeningKind.Breach, Opening.Sea, c.Id,
                area: 0.05f, centerY: 1f, height: 0.5f);

            var solver = new FlowSolver();
            for (int i = 0; i < 30; i++)
                solver.Tick(graph, Dt);

            Assert.Greater(c.WaterVolume, 0f, "Sea breach should flood compartment");
        }
    }
}
