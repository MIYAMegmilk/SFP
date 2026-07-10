using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class CrewSystemTests
    {
        const float Dt = 1f / 30f;
        const float L = 6f, W = 6f, H = 6f;

        struct TestShip
        {
            public CompartmentGraph Graph;
            public ShipNavGraph Nav;
            public DamageSystem Damage;
            public FireSystem Fire;
            public AtmosphereSystem Atmosphere;
            public TemperatureSystem Temperature;
            public PowerGrid Power;
            public SubmarineState Sub;
            public CrewSystem Crew;
        }

        static TestShip BuildTestShip(int seed = 42)
        {
            var graph = new CompartmentGraph();
            var nav = new ShipNavGraph(graph);
            var sub = new SubmarineState { Depth = 200f };

            float[] floorYs = { 0, 0, 0, 0, 6, 6, 6, 6, 12, 12, 12, 12 };
            float[] centersX = { 3, 9, 15, 21, 3, 9, 15, 21, 3, 9, 15, 21 };

            for (int i = 0; i < 12; i++)
            {
                var c = graph.AddCompartment(floorYs[i], H, L * W);
                nav.RegisterRoom(c.Id, centersX[i] - L / 2, centersX[i] + L / 2,
                    0, W, floorYs[i], floorYs[i] + H);
            }

            // 9 doors + 6 hatches (all open)
            int[,] doors = { {0,1},{1,2},{2,3},{4,5},{5,6},{6,7},{8,9},{9,10},{10,11} };
            float[] doorYs = { 1.5f, 1.5f, 1.5f, 7.5f, 7.5f, 7.5f, 13.5f, 13.5f, 13.5f };
            float[] doorXs = { 6, 12, 18, 6, 12, 18, 6, 12, 18 };
            for (int i = 0; i < 9; i++)
            {
                var o = graph.AddOpening(OpeningKind.Door, doors[i, 0], doors[i, 1],
                    6f, doorYs[i], 3f, true);
                nav.RegisterPortal(o.Id, doorXs[i], doorYs[i], 3f);
            }

            int[,] hatches = { {0,4},{1,5},{2,6},{4,8},{5,9},{6,10} };
            float[] hatchYs = { 6, 6, 6, 12, 12, 12 };
            float[] hatchXs = { 3, 9, 15, 3, 9, 15 };
            for (int i = 0; i < 6; i++)
            {
                var o = graph.AddOpening(OpeningKind.Hatch, hatches[i, 0], hatches[i, 1],
                    0.8f, hatchYs[i], 0.5f, true);
                nav.RegisterPortal(o.Id, hatchXs[i], hatchYs[i], 3f);
            }

            var damage = new DamageSystem(graph, sub);
            var fire = new FireSystem(graph);
            var atmosphere = new AtmosphereSystem(graph);
            var temperature = new TemperatureSystem(graph);
            var power = new PowerGrid();
            power.UnlimitedPower = true;

            var crew = new CrewSystem(graph, nav, seed);

            return new TestShip
            {
                Graph = graph, Nav = nav, Damage = damage,
                Fire = fire, Atmosphere = atmosphere,
                Temperature = temperature, Power = power,
                Sub = sub, Crew = crew,
            };
        }

        void TickN(TestShip s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Crew.Tick(Dt, s.Damage, s.Fire, s.Atmosphere, s.Temperature, s.Power);
        }

        [Test]
        public void SpawnCrew_AssignsCompartment()
        {
            var s = BuildTestShip();
            var c = s.Crew.SpawnCrew(9f, 6f, 3f);
            Assert.AreEqual(5, c.CompartmentId);
            Assert.AreEqual(0, c.Id);
        }

        [Test]
        public void Idle_Crew_Stays_Alive()
        {
            var s = BuildTestShip();
            s.Crew.SpawnCrew(9f, 6f, 3f);

            TickN(s, 300); // 10 seconds

            var c = s.Crew.Crew[0];
            Assert.IsFalse(c.IsDead);
            Assert.AreEqual(100f, c.Health);
        }

        [Test]
        public void Fire_Prioritized_Over_Pump()
        {
            var s = BuildTestShip();
            var c = s.Crew.SpawnCrew(9f, 6f, 3f); // room 5

            // add a pump in room 5 (inactive, water present)
            var pump = new BilgePumpState { CompartmentId = 5, IsActive = false, IsFunctional = true };
            s.Crew.RegisterPump(pump, 9f, 6f, 3f);
            s.Graph.GetCompartment(5).WaterVolume = s.Graph.GetCompartment(5).Volume * 0.1f;

            // start fire in room 5
            s.Fire.StartFire(5, 0.5f);

            TickN(s, 10);

            Assert.AreEqual(CrewTaskKind.FightFire, c.Task);
        }

        [Test]
        public void PumpTask_Toggles_Pump()
        {
            var s = BuildTestShip();
            var c = s.Crew.SpawnCrew(9f, 6f, 3f); // room 5

            var pump = new BilgePumpState { CompartmentId = 5, IsActive = false, IsFunctional = true };
            s.Crew.RegisterPump(pump, 9f, 6f, 3f);
            s.Graph.GetCompartment(5).WaterVolume = s.Graph.GetCompartment(5).Volume * 0.1f;

            // tick until pump is activated (crew walks to pump then toggles)
            for (int i = 0; i < 300; i++)
            {
                s.Crew.Tick(Dt, s.Damage, s.Fire, s.Atmosphere, s.Temperature, s.Power);
                if (pump.IsActive) break;
            }

            Assert.IsTrue(pump.IsActive);
        }

        [Test]
        public void Crew_Drowns_In_Sealed_Flooded_Room()
        {
            var s = BuildTestShip();
            var c = s.Crew.SpawnCrew(9f, 6f, 3f); // room 5

            // seal all doors around room 5 and flood it fully
            foreach (var o in s.Graph.Openings) o.IsOpen = false;
            s.Graph.GetCompartment(5).WaterVolume = s.Graph.GetCompartment(5).Volume;

            // ~30s breath + ~25s health drain at 4 HP/s = ~55s total
            for (int i = 0; i < 2000; i++)
            {
                s.Crew.Tick(Dt, s.Damage, s.Fire, s.Atmosphere, s.Temperature, s.Power);
                if (c.IsDead) break;
            }

            Assert.IsTrue(c.IsDead);

            // verify death event emitted
            bool gotEvent = false;
            while (s.Crew.TryDequeueEvent(out var evt))
            {
                if (evt.Kind == CrewEventKind.CrewDied && evt.CrewId == c.Id)
                    gotEvent = true;
            }
            Assert.IsTrue(gotEvent);
        }

        [Test]
        public void Order_Overrides_Autonomy()
        {
            var s = BuildTestShip();
            var c = s.Crew.SpawnCrew(9f, 6f, 3f); // room 5

            s.Crew.IssueOrder(0, CrewOrderKind.MoveTo, -1, 3f, 6f, 3f);

            TickN(s, 10);

            Assert.AreEqual(CrewTaskKind.MoveTo, c.Task);
        }

        [Test]
        public void CancelOrder_AllowsAutonomy()
        {
            var s = BuildTestShip();
            var c = s.Crew.SpawnCrew(9f, 6f, 3f);

            s.Crew.IssueOrder(0, CrewOrderKind.MoveTo, -1, 3f, 6f, 3f);
            TickN(s, 10);
            Assert.AreEqual(CrewTaskKind.MoveTo, c.Task);

            s.Crew.CancelOrder(0);
            TickN(s, 30);

            Assert.AreNotEqual(CrewTaskKind.MoveTo, c.Task);
        }

        [Test]
        public void Determinism_SameSeed_SameTrajectory()
        {
            var s1 = BuildTestShip(77);
            s1.Crew.SpawnCrew(9f, 6f, 3f);
            s1.Fire.StartFire(4, 0.4f);
            s1.Graph.GetCompartment(6).WaterVolume = s1.Graph.GetCompartment(6).Volume * 0.2f;

            var s2 = BuildTestShip(77);
            s2.Crew.SpawnCrew(9f, 6f, 3f);
            s2.Fire.StartFire(4, 0.4f);
            s2.Graph.GetCompartment(6).WaterVolume = s2.Graph.GetCompartment(6).Volume * 0.2f;

            for (int i = 0; i < 600; i++)
            {
                s1.Crew.Tick(Dt, s1.Damage, s1.Fire, s1.Atmosphere, s1.Temperature, s1.Power);
                s2.Crew.Tick(Dt, s2.Damage, s2.Fire, s2.Atmosphere, s2.Temperature, s2.Power);
            }

            var c1 = s1.Crew.Crew[0];
            var c2 = s2.Crew.Crew[0];
            Assert.AreEqual(c1.X, c2.X, 1e-4f, "X diverged");
            Assert.AreEqual(c1.Y, c2.Y, 1e-4f, "Y diverged");
            Assert.AreEqual(c1.Z, c2.Z, 1e-4f, "Z diverged");
            Assert.AreEqual(c1.Health, c2.Health, 1e-4f, "Health diverged");
            Assert.AreEqual(c1.Task, c2.Task, "Task diverged");
        }

        [Test]
        public void ClosedDoors_Get_Opened_On_Path()
        {
            var s = BuildTestShip();
            // close all doors
            foreach (var o in s.Graph.Openings) o.IsOpen = false;

            // spawn in room 5, order to room 4 (door between them)
            var c = s.Crew.SpawnCrew(9f, 6f, 3f);
            s.Crew.IssueOrder(0, CrewOrderKind.MoveTo, -1, 3f, 6f, 3f);

            // tick long enough for crew to reach the door and open it
            for (int i = 0; i < 300; i++)
            {
                s.Crew.Tick(Dt, s.Damage, s.Fire, s.Atmosphere, s.Temperature, s.Power);
            }

            // the door between 4 and 5 (opening index 3) should now be open
            bool anyDoorOpened = false;
            foreach (var o in s.Graph.Openings)
            {
                if (o.Kind == OpeningKind.Door && o.IsOpen)
                {
                    anyDoorOpened = true;
                    break;
                }
            }
            Assert.IsTrue(anyDoorOpened, "Crew should open doors on their path");
        }

        [Test]
        public void Proficiency_Affects_Task_Selection()
        {
            var s = BuildTestShip();
            // Both in room 5 — pump in room 5, fire in adjacent room 4 (equidistant)
            var eng = s.Crew.SpawnCrew(9f, 6f, 2f, CrewJobKind.Engineer);
            var dmc = s.Crew.SpawnCrew(9f, 6f, 4f, CrewJobKind.DamageControl);

            var pump = new BilgePumpState { CompartmentId = 5, IsActive = false, IsFunctional = true };
            s.Crew.RegisterPump(pump, 10f, 6f, 3f);
            s.Graph.GetCompartment(5).WaterVolume = s.Graph.GetCompartment(5).Volume * 0.15f;

            // fire in adjacent room 4 — crew not in fire room so no flee
            s.Fire.StartFire(4, 0.5f);

            TickN(s, 10);

            // Engineer (pump 1.3, fire 0.5) should prefer local pump over distant fire
            Assert.AreEqual(CrewTaskKind.OperatePump, eng.Task,
                "Engineer should prefer pump over fire");
            // DamageControl (fire 1.5, pump 0.8) should prefer fire over pump
            Assert.AreEqual(CrewTaskKind.FightFire, dmc.Task,
                "DamageControl should prefer fire over pump");
        }

        [Test]
        public void Proficiency_Affects_Work_Speed()
        {
            // DamageControl extinguishes faster than Engineer
            var sDmc = BuildTestShip(100);
            sDmc.Crew.SpawnCrew(3f, 0f, 3f, CrewJobKind.DamageControl); // room 0
            sDmc.Fire.StartFire(0, 0.5f);

            var sEng = BuildTestShip(100);
            sEng.Crew.SpawnCrew(3f, 0f, 3f, CrewJobKind.Engineer); // room 0
            sEng.Fire.StartFire(0, 0.5f);

            // tick both for 5 seconds — both should be fighting fire
            for (int i = 0; i < 150; i++)
            {
                sDmc.Crew.Tick(Dt, sDmc.Damage, sDmc.Fire, sDmc.Atmosphere, sDmc.Temperature, sDmc.Power);
                sEng.Crew.Tick(Dt, sEng.Damage, sEng.Fire, sEng.Atmosphere, sEng.Temperature, sEng.Power);
            }

            float fireDmc = sDmc.Fire.GetFireIntensity(0);
            float fireEng = sEng.Fire.GetFireIntensity(0);
            // DamageControl (1.5x fire) should have reduced fire more than Engineer (0.5x)
            Assert.Less(fireDmc, fireEng,
                "DamageControl should extinguish faster than Engineer");
        }

        [Test]
        public void Collision_Avoidance_Separates_Crew()
        {
            var s = BuildTestShip();
            var c1 = s.Crew.SpawnCrew(9f, 6f, 3f);
            var c2 = s.Crew.SpawnCrew(9f, 6f, 3f);

            TickN(s, 5);

            float dx = c2.X - c1.X;
            float dz = c2.Z - c1.Z;
            float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
            Assert.GreaterOrEqual(dist, 0.5f,
                "Crew at same position should be pushed apart");
        }

        [Test]
        public void Job_Label_Returns_Abbreviation()
        {
            Assert.AreEqual("CPT", CrewJob.GetLabel(CrewJobKind.Captain));
            Assert.AreEqual("ENG", CrewJob.GetLabel(CrewJobKind.Engineer));
            Assert.AreEqual("MEC", CrewJob.GetLabel(CrewJobKind.Mechanic));
            Assert.AreEqual("DMC", CrewJob.GetLabel(CrewJobKind.DamageControl));
        }

        [Test]
        public void Proficiency_NonTask_Returns_One()
        {
            Assert.AreEqual(1f, CrewJob.GetProficiency(CrewJobKind.Engineer, CrewTaskKind.Idle));
            Assert.AreEqual(1f, CrewJob.GetProficiency(CrewJobKind.Mechanic, CrewTaskKind.MoveTo));
            Assert.AreEqual(1f, CrewJob.GetProficiency(CrewJobKind.DamageControl, CrewTaskKind.Flee));
        }
    }
}
