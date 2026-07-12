using System;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class AirlockCycleTests
    {
        const float Dt = 1f / 30f;

        CompartmentGraph _graph;
        SubmarineState _sub;
        AirlockState _airlock;
        Opening _innerDoor;
        Opening _outerHatch;
        Opening _floodValve;

        void Setup(float depth)
        {
            _graph = new CompartmentGraph();
            _graph.AddCompartment(0f, 6f, 36f);
            _graph.AddCompartment(0f, 6f, 36f);
            _graph.SeaLevelY = depth;

            _innerDoor = _graph.AddOpening(OpeningKind.Door, 0, 1, 1.5f, 1.5f, 3f, isOpen: false);
            _outerHatch = _graph.AddOpening(OpeningKind.Hatch, 1, Opening.Sea, 0.8f, 0.5f, 1f, isOpen: false);
            _floodValve = _graph.AddOpening(OpeningKind.Hatch, 1, Opening.Sea, 0.2f, 0.2f, 0.4f, isOpen: false);
            _floodValve.IsGasSealed = true;

            _sub = new SubmarineState { Depth = depth };

            _airlock = new AirlockState
            {
                CompartmentId = 1,
                InnerDoorOpeningId = _innerDoor.Id,
                OuterHatchOpeningId = _outerHatch.Id,
                FloodValveOpeningId = _floodValve.Id
            };
        }

        Compartment AirlockComp => _graph.GetCompartment(1);

        float AmbientAtm => GasFlowMath.ExternalPressureAtm(_graph.SeaLevelY, _outerHatch.CenterY);

        void SimulateFloodEquilibrium()
        {
            // Boyle's law: at equilibrium P_air = P_ambient, WaterFraction = 1 - 1/P_ambient
            float pAmb = AmbientAtm;
            float feq = pAmb > 1f ? 1f - 1f / pAmb : 0f;
            var comp = AirlockComp;
            comp.WaterVolume = feq * comp.Volume;
            comp.AirPressureAtm = pAmb;
        }

        void SimulatePartialFlood(float fraction)
        {
            var comp = AirlockComp;
            comp.WaterVolume = fraction * comp.Volume;
            // Boyle: P = N/V_air * (T/T0), N = initial air amount = Volume (at 1 atm)
            float airVol = comp.Volume - comp.WaterVolume;
            if (airVol < comp.Volume * AirPressureMath.MinAirFraction)
                airVol = comp.Volume * AirPressureMath.MinAirFraction;
            comp.AirPressureAtm = GasFlowMath.PressureAtm(comp.Volume, airVol, comp.TemperatureK);
        }

        void SimulateDrained()
        {
            var comp = AirlockComp;
            comp.WaterVolume = 0.3f;
            comp.AirPressureAtm = 1.0f;
        }

        // === Phase: Dry ===

        [Test]
        public void InitialPhase_IsDry()
        {
            Setup(200f);
            Assert.AreEqual(AirlockPhase.Dry, _airlock.Phase);
        }

        [Test]
        public void Dry_InnerDoorUnlocked_OuterHatchLocked()
        {
            Setup(200f);
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.IsFalse(_innerDoor.IsLocked);
            Assert.IsTrue(_outerHatch.IsLocked);
            Assert.IsTrue(_floodValve.IsLocked);
        }

        [Test]
        public void RequestFlood_FailsIfInnerDoorOpen()
        {
            Setup(200f);
            _innerDoor.IsOpen = true;
            Assert.IsFalse(_airlock.RequestFlood(_graph));
            Assert.AreEqual(AirlockPhase.Dry, _airlock.Phase);
        }

        [Test]
        public void RequestFlood_SucceedsWhenDoorClosed()
        {
            Setup(200f);
            Assert.IsTrue(_airlock.RequestFlood(_graph));
            Assert.AreEqual(AirlockPhase.Flooding, _airlock.Phase);
        }

        // === Phase: Flooding ===

        [Test]
        public void Flooding_LocksInnerDoor_OpensFloodValve()
        {
            Setup(200f);
            _airlock.RequestFlood(_graph);
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.IsTrue(_innerDoor.IsLocked);
            Assert.IsFalse(_innerDoor.IsOpen);
            Assert.IsTrue(_floodValve.IsOpen);
            Assert.IsTrue(_outerHatch.IsLocked);
        }

        [Test]
        public void Flooding_TransitionsToFlooded_WhenPressureEqualized()
        {
            Setup(200f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.AreEqual(AirlockPhase.Flooded, _airlock.Phase);
        }

        [Test]
        public void Flooding_TransitionsToFlooded_WhenWaterFractionReached()
        {
            Setup(10f);
            _airlock.RequestFlood(_graph);
            // f_eq at 10m ≈ 0.487. Set water just above threshold.
            float feq = 1f - 1f / AmbientAtm;
            SimulatePartialFlood(feq);
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.AreEqual(AirlockPhase.Flooded, _airlock.Phase);
        }

        [Test]
        public void Flooding_StaysFlooding_WhenBelowThreshold()
        {
            Setup(200f);
            _airlock.RequestFlood(_graph);
            SimulatePartialFlood(0.1f);
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.AreEqual(AirlockPhase.Flooding, _airlock.Phase);
        }

        // === Phase: Flooded ===

        [Test]
        public void Flooded_OuterHatchUnlocked_InnerStillLocked()
        {
            Setup(10f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.AreEqual(AirlockPhase.Flooded, _airlock.Phase);

            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.IsFalse(_outerHatch.IsLocked, "Outer hatch should be unlocked when flooded");
            Assert.IsTrue(_outerHatch.IsOpen, "Outer hatch should auto-open when flooded");
            Assert.IsTrue(_innerDoor.IsLocked, "Inner door should stay locked when flooded");
        }

        [Test]
        public void RequestDrain_FailsIfNotFlooded()
        {
            Setup(200f);
            Assert.IsFalse(_airlock.RequestDrain());
            _airlock.RequestFlood(_graph);
            Assert.IsFalse(_airlock.RequestDrain());
        }

        [Test]
        public void RequestDrain_SucceedsWhenFlooded()
        {
            Setup(10f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.AreEqual(AirlockPhase.Flooded, _airlock.Phase);
            Assert.IsTrue(_airlock.RequestDrain());
            Assert.AreEqual(AirlockPhase.Draining, _airlock.Phase);
        }

        // === Phase: Draining ===

        [Test]
        public void Draining_LocksAllOpenings()
        {
            Setup(10f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            _airlock.RequestDrain();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.IsTrue(_innerDoor.IsLocked);
            Assert.IsTrue(_outerHatch.IsLocked);
            Assert.IsTrue(_floodValve.IsLocked);
        }

        [Test]
        public void Draining_RemovesWater_NoPowerGrid()
        {
            Setup(10f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            _airlock.RequestDrain();

            float volBefore = AirlockComp.WaterVolume;
            _airlock.Tick(Dt, _graph, null, _sub, null);
            float volAfter = AirlockComp.WaterVolume;

            Assert.Less(volAfter, volBefore, "Draining should remove water");
        }

        [Test]
        public void Draining_ReturnsToDry_WhenWaterLow()
        {
            Setup(10f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            _airlock.RequestDrain();

            SimulateDrained();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            Assert.AreEqual(AirlockPhase.Dry, _airlock.Phase);
        }

        [Test]
        public void DrainRate_SlowsWithDepth()
        {
            // At 10m depth
            Setup(10f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            _airlock.RequestDrain();
            float volBefore10 = AirlockComp.WaterVolume;
            _airlock.Tick(Dt, _graph, null, _sub, null);
            float removed10 = volBefore10 - AirlockComp.WaterVolume;

            // At 400m depth
            Setup(400f);
            _airlock.RequestFlood(_graph);
            SimulateFloodEquilibrium();
            _airlock.Tick(Dt, _graph, null, _sub, null);
            _airlock.RequestDrain();
            float volBefore400 = AirlockComp.WaterVolume;
            _airlock.Tick(Dt, _graph, null, _sub, null);
            float removed400 = volBefore400 - AirlockComp.WaterVolume;

            Assert.Greater(removed10, removed400,
                "Drain should be faster at 10m than at 400m depth");
        }

        // === Cross-cutting ===

        [Test]
        public void IsLocked_PreventsDoorToggle()
        {
            var graph = new CompartmentGraph();
            graph.AddCompartment(0f, 6f, 36f);
            graph.AddCompartment(0f, 6f, 36f);
            var door = graph.AddOpening(OpeningKind.Door, 0, 1, 1.5f, 1.5f, 3f, isOpen: false);
            door.IsLocked = true;
            Assert.IsTrue(door.IsLocked);
            Assert.IsFalse(door.IsOpen);
        }

        [Test]
        public void IsGasSealed_BlocksGasFlowThroughValve()
        {
            Setup(200f);
            var atm = new AtmosphereSystem(_graph);
            var gasFlow = new GasFlowSystem(_graph, atm) { Submarine = _sub };

            _floodValve.IsOpen = true;
            float airBefore = AirlockComp.AirAmount;
            gasFlow.Tick(Dt);
            float airAfter = AirlockComp.AirAmount;

            Assert.AreEqual(airBefore, airAfter, 0.001f,
                "Gas should not flow through a gas-sealed valve");
        }
    }
}
