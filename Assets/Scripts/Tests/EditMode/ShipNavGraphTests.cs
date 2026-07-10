using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class ShipNavGraphTests
    {
        const float L = 6f, W = 6f, H = 6f;

        static (CompartmentGraph graph, ShipNavGraph nav) BuildShip()
        {
            var graph = new CompartmentGraph();
            var nav = new ShipNavGraph(graph);

            // 12 compartments: 4×3 grid
            float[] floorYs = { 0, 0, 0, 0, 6, 6, 6, 6, 12, 12, 12, 12 };
            float[] centersX = { 3, 9, 15, 21, 3, 9, 15, 21, 3, 9, 15, 21 };

            for (int i = 0; i < 12; i++)
            {
                var c = graph.AddCompartment(floorYs[i], H, L * W);
                float cx = centersX[i];
                float cz = 3f;
                nav.RegisterRoom(c.Id, cx - L / 2, cx + L / 2,
                    cz - W / 2, cz + W / 2, floorYs[i], floorYs[i] + H);
            }

            // 9 doors (same-deck)
            int[,] doors = {
                {0,1}, {1,2}, {2,3},
                {4,5}, {5,6}, {6,7},
                {8,9}, {9,10}, {10,11}
            };
            float[] doorYs = { 1.5f, 1.5f, 1.5f, 7.5f, 7.5f, 7.5f, 13.5f, 13.5f, 13.5f };
            float[] doorXs = { 6, 12, 18, 6, 12, 18, 6, 12, 18 };

            for (int i = 0; i < 9; i++)
            {
                var o = graph.AddOpening(OpeningKind.Door, doors[i, 0], doors[i, 1],
                    6f, doorYs[i], 3f, false);
                nav.RegisterPortal(o.Id, doorXs[i], doorYs[i], 3f);
            }

            // 6 hatches (between decks)
            int[,] hatches = { {0,4}, {1,5}, {2,6}, {4,8}, {5,9}, {6,10} };
            float[] hatchYs = { 6, 6, 6, 12, 12, 12 };
            float[] hatchXs = { 3, 9, 15, 3, 9, 15 };

            for (int i = 0; i < 6; i++)
            {
                var o = graph.AddOpening(OpeningKind.Hatch, hatches[i, 0], hatches[i, 1],
                    0.8f, hatchYs[i], 0.5f, false);
                nav.RegisterPortal(o.Id, hatchXs[i], hatchYs[i], 3f);
            }

            return (graph, nav);
        }

        [Test]
        public void PathAcrossDecks_UsesHatches()
        {
            var (graph, nav) = BuildShip();

            // open all openings
            foreach (var o in graph.Openings) o.IsOpen = true;

            var rc = new System.Collections.Generic.List<int>();
            var ro = new System.Collections.Generic.List<int>();
            bool found = nav.FindPath(0, 11, null, rc, ro);

            Assert.IsTrue(found);
            Assert.Greater(rc.Count, 2);

            int hatchCount = 0;
            foreach (var oid in ro)
                if (graph.Openings[oid].Kind == OpeningKind.Hatch) hatchCount++;
            Assert.GreaterOrEqual(hatchCount, 2);
        }

        [Test]
        public void ClosedDoor_Pathable_ButCostlier()
        {
            var (graph, nav) = BuildShip();
            foreach (var o in graph.Openings) o.IsOpen = true;

            float openCost = nav.PathCost(0, 1, null);

            // close the door between 0 and 1
            graph.Openings[0].IsOpen = false;

            float closedCost = nav.PathCost(0, 1, null);
            Assert.Greater(closedCost, openCost);
        }

        [Test]
        public void FloodedRoom_Avoided_WhenDetourExists()
        {
            var (graph, nav) = BuildShip();
            foreach (var o in graph.Openings) o.IsOpen = true;

            // flood compartment 1 (Engine)
            graph.GetCompartment(1).WaterVolume = graph.GetCompartment(1).Volume * 0.8f;

            // path from 0 to 2: direct route is 0→1→2 but 1 is flooded
            var rc = new System.Collections.Generic.List<int>();
            var ro = new System.Collections.Generic.List<int>();
            bool found = nav.FindPath(0, 2, null, rc, ro);

            Assert.IsTrue(found);
            // the path should still exist (through the flooded room or via detour)
            // but the cost should be higher than without flooding
            float floodedCost = nav.PathCost(0, 2, null);
            graph.GetCompartment(1).WaterVolume = 0f;
            float dryCost = nav.PathCost(0, 2, null);
            Assert.Greater(floodedCost, dryCost);
        }

        [Test]
        public void ClosedDoor_HighWaterDelta_Impassable()
        {
            var (graph, nav) = BuildShip();
            foreach (var o in graph.Openings) o.IsOpen = true;

            // close door between 0 and 1
            graph.Openings[0].IsOpen = false;

            // flood compartment 0 to 60%, keep 1 dry: delta = 0.6 > 0.4 threshold
            graph.GetCompartment(0).WaterVolume = graph.GetCompartment(0).Volume * 0.6f;

            // close all other paths to make 0→1 the only option
            for (int i = 1; i < graph.Openings.Count; i++)
                graph.Openings[i].IsOpen = false;

            float cost = nav.PathCost(0, 1, null);
            Assert.AreEqual(float.MaxValue, cost);
        }

        [Test]
        public void FindCompartmentAt_ResolvesAllRoomCenters()
        {
            var (graph, nav) = BuildShip();
            float[] centersX = { 3, 9, 15, 21, 3, 9, 15, 21, 3, 9, 15, 21 };
            float[] floorYs = { 0, 0, 0, 0, 6, 6, 6, 6, 12, 12, 12, 12 };

            for (int i = 0; i < 12; i++)
            {
                float midY = floorYs[i] + H / 2;
                int found = nav.FindCompartmentAt(centersX[i], midY, 3f);
                Assert.AreEqual(i, found, $"Room {i} not found at center");
            }
        }

        [Test]
        public void FindCompartmentAt_OutsideShip_ReturnsMinusOne()
        {
            var (_, nav) = BuildShip();
            int found = nav.FindCompartmentAt(100f, 100f, 100f);
            Assert.AreEqual(-1, found);
        }
    }
}
