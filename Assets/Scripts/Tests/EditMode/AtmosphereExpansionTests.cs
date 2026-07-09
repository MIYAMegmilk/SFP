using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class GasFlowMathTests
    {
        [Test]
        public void AirDensity_AtStandardConditions_ReturnsRho0()
        {
            float rho = GasFlowMath.AirDensity(1f, GasFlowMath.T0);
            Assert.AreEqual(GasFlowMath.Rho0, rho, 0.01f);
        }

        [Test]
        public void PressureAtm_StandardAmount_ReturnsOneAtm()
        {
            float p = GasFlowMath.PressureAtm(100f, 100f, GasFlowMath.T0);
            Assert.AreEqual(1f, p, 0.001f);
        }

        [Test]
        public void PressureAtm_HighTemperature_IncresesPressure()
        {
            float p = GasFlowMath.PressureAtm(100f, 100f, 350f);
            Assert.Greater(p, 1f);
            float expected = 350f / GasFlowMath.T0;
            Assert.AreEqual(expected, p, 0.01f);
        }

        [Test]
        public void PressureAtm_Clamped()
        {
            float pLow = GasFlowMath.PressureAtm(0.01f, 100f, GasFlowMath.T0);
            Assert.AreEqual(0.25f, pLow, 0.001f);

            float pHigh = GasFlowMath.PressureAtm(100000f, 100f, GasFlowMath.T0);
            Assert.AreEqual(AirPressureMath.MaxPressureAtm, pHigh, 0.001f);
        }

        [Test]
        public void MassFlow_SubsonicCase()
        {
            float mdot = GasFlowMath.MassFlowKgS(0.05f, 1.5f, 1.0f, GasFlowMath.T0);
            Assert.Greater(mdot, 5f);
            Assert.Less(mdot, 25f);
        }

        [Test]
        public void MassFlow_ChokedCase_IndependentOfDownstream()
        {
            float mdot1 = GasFlowMath.MassFlowKgS(0.05f, 3.0f, 1.0f, GasFlowMath.T0);
            float mdot2 = GasFlowMath.MassFlowKgS(0.05f, 3.0f, 0.5f, GasFlowMath.T0);
            Assert.AreEqual(mdot1, mdot2, 0.01f);
        }

        [Test]
        public void MassFlow_NoPressureDiff_ReturnsZero()
        {
            float mdot = GasFlowMath.MassFlowKgS(0.05f, 1.0f, 1.0f, GasFlowMath.T0);
            Assert.AreEqual(0f, mdot, 0.001f);
        }

        [Test]
        public void EqualizeTransfer_BalancesPressure()
        {
            float nA = 200f, vA = 100f, tA = GasFlowMath.T0;
            float nB = 100f, vB = 100f, tB = GasFlowMath.T0;
            float x = GasFlowMath.EqualizeTransferStd(nA, vA, tA, nB, vB, tB);
            float pAfterA = GasFlowMath.PressureAtm(nA - x, vA, tA);
            float pAfterB = GasFlowMath.PressureAtm(nB + x, vB, tB);
            Assert.AreEqual(pAfterA, pAfterB, 0.01f);
        }

        [Test]
        public void SeaTemperature_SurfaceWarm_DeepCold()
        {
            float tSurface = GasFlowMath.SeaTemperatureK(0f);
            float tDeep = GasFlowMath.SeaTemperatureK(1000f);
            Assert.Greater(tSurface, 290f);
            Assert.Less(tDeep, 280f);
        }

        [Test]
        public void ExternalPressure_IncreasesWithDepth()
        {
            float p0 = GasFlowMath.ExternalPressureAtm(0f, 0f);
            Assert.AreEqual(1f, p0, 0.01f);
            float p100 = GasFlowMath.ExternalPressureAtm(100f, 0f);
            Assert.Greater(p100, 10f);
        }
    }

    [TestFixture]
    public class GasFlowSystemTests
    {
        CompartmentGraph TwoRoomGraph(float volume = 216f)
        {
            var g = new CompartmentGraph { SeaLevelY = 200f };
            g.AddCompartment(0f, 4f, volume / 4f);
            g.AddCompartment(0f, 4f, volume / 4f);
            return g;
        }

        [Test]
        public void ClosedDoor_NoPressureChange()
        {
            var g = TwoRoomGraph();
            g.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, false);
            g.GetCompartment(0).AirAmount = 432f; // 2 atm
            var atmo = new AtmosphereSystem(g);
            var gf = new GasFlowSystem(g, atmo);
            float p0Before = g.GetCompartment(0).AirPressureAtm;
            gf.Tick(1f / 30f);
            // Pressure should be recomputed but not equalized
            Assert.Greater(g.GetCompartment(0).AirPressureAtm, 1.5f);
        }

        [Test]
        public void OpenDoor_PressureEqualizes()
        {
            var g = TwoRoomGraph();
            g.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, true);
            g.GetCompartment(0).AirAmount = 432f;
            var atmo = new AtmosphereSystem(g);
            var gf = new GasFlowSystem(g, atmo);
            for (int i = 0; i < 150; i++)
                gf.Tick(1f / 30f);
            float pA = g.GetCompartment(0).AirPressureAtm;
            float pB = g.GetCompartment(1).AirPressureAtm;
            Assert.AreEqual(pA, pB, 0.05f);
        }

        [Test]
        public void AirAmountConserved()
        {
            var g = TwoRoomGraph();
            g.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, true);
            g.GetCompartment(0).AirAmount = 400f;
            g.GetCompartment(1).AirAmount = 200f;
            float totalBefore = g.GetCompartment(0).AirAmount + g.GetCompartment(1).AirAmount;
            var atmo = new AtmosphereSystem(g);
            var gf = new GasFlowSystem(g, atmo);
            for (int i = 0; i < 100; i++)
                gf.Tick(1f / 30f);
            float totalAfter = g.GetCompartment(0).AirAmount + g.GetCompartment(1).AirAmount;
            Assert.AreEqual(totalBefore, totalAfter, 1f);
        }

        [Test]
        public void BlowoutImpulse_AppliedToSubmarine()
        {
            var g = TwoRoomGraph();
            var opening = g.AddOpening(OpeningKind.Breach, Opening.Sea, 0, 0.05f, 5f, 0.5f, true);
            opening.NormalX = 1f;
            g.GetCompartment(0).AirAmount = 432f;
            g.SeaLevelY = 0f; // on surface
            var atmo = new AtmosphereSystem(g);
            var sub = new SubmarineState { DryMass = 1000000f };
            var gf = new GasFlowSystem(g, atmo) { Submarine = sub };
            gf.Tick(1f / 30f);
            Assert.Less(sub.GasForceLocalX, 0f);
        }

        [Test]
        public void O2Advection_HighPressureCarriesComposition()
        {
            var g = TwoRoomGraph();
            g.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, true);
            g.GetCompartment(0).AirAmount = 432f;
            var atmo = new AtmosphereSystem(g);
            atmo.SetOxygenLevel(0, 1.0f);
            atmo.SetOxygenLevel(1, 0.5f);
            var gf = new GasFlowSystem(g, atmo);
            for (int i = 0; i < 60; i++)
                gf.Tick(1f / 30f);
            Assert.Greater(atmo.GetOxygenLevel(1), 0.6f);
        }
    }

    [TestFixture]
    public class AtmosphereCo2Tests
    {
        [Test]
        public void Co2InitializedToAmbient()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            Assert.AreEqual(0.0004f, atmo.GetCo2Level(0), 0.0001f);
        }

        [Test]
        public void CrewProducesCo2()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            atmo.CrewCompartmentId = 0;
            float before = atmo.GetCo2Level(0);
            for (int i = 0; i < 30; i++)
                atmo.Tick(1f / 30f);
            Assert.Greater(atmo.GetCo2Level(0), before);
        }

        [Test]
        public void Co2DiffusesThroughOpenDoor()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            g.AddCompartment(0f, 4f, 54f);
            g.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, true);
            var atmo = new AtmosphereSystem(g);
            atmo.SetCo2Level(0, 0.1f);
            atmo.SetCo2Level(1, 0.0f);
            for (int i = 0; i < 600; i++)
                atmo.Tick(1f / 30f);
            float co2A = atmo.GetCo2Level(0);
            float co2B = atmo.GetCo2Level(1);
            Assert.Less(System.Math.Abs(co2A - co2B), 0.03f);
        }

        [Test]
        public void ScrubberReducesCo2()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 2f, 5f);
            var atmo = new AtmosphereSystem(g);
            atmo.SetCo2Level(0, 0.1f);
            var scrubber = new CO2ScrubberState
            {
                TargetCompartmentId = 0,
                ProcessRate = 1.0f,
                Efficiency = 0.95f,
            };
            for (int i = 0; i < 900; i++)
                scrubber.Tick(1f / 30f, g, atmo, null);
            Assert.Less(atmo.GetCo2Level(0), 0.01f);
        }

        [Test]
        public void ScrubberRespectsMinCo2Floor()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            atmo.SetCo2Level(0, 0.001f);
            var scrubber = new CO2ScrubberState
            {
                TargetCompartmentId = 0,
                MinCo2 = 0.0004f,
            };
            for (int i = 0; i < 300; i++)
                scrubber.Tick(1f / 30f, g, atmo, null);
            Assert.GreaterOrEqual(atmo.GetCo2Level(0), 0.0004f);
        }

        [Test]
        public void FireProducesCo2()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            var fire = new FireSystem(g);
            fire.StartFire(0, 0.5f);
            float before = atmo.GetCo2Level(0);
            for (int i = 0; i < 30; i++)
                fire.Tick(1f / 30f, atmo);
            Assert.Greater(atmo.GetCo2Level(0), before);
        }

        [Test]
        public void FireSmotheredByHighCo2()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            var fire = new FireSystem(g);
            fire.StartFire(0, 0.5f);
            atmo.SetCo2Level(0, 0.2f);
            for (int i = 0; i < 90; i++)
                fire.Tick(1f / 30f, atmo);
            Assert.Less(fire.GetFireIntensity(0), 0.01f);
        }
    }

    [TestFixture]
    public class VentSystemTests
    {
        [Test]
        public void PoweredVent_EqualizesO2()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 2f, 5f);
            g.AddCompartment(0f, 2f, 5f);
            var atmo = new AtmosphereSystem(g);
            atmo.SetOxygenLevel(0, 1.0f);
            atmo.SetOxygenLevel(1, 0.5f);
            var vent = new VentState
            {
                CompartmentA = 0,
                CompartmentB = 1,
                FanFlowRate = 1.5f,
                DuctY = 1.5f,
            };
            for (int i = 0; i < 300; i++)
                vent.Tick(1f / 30f, g, atmo, null);
            float diff = System.Math.Abs(atmo.GetOxygenLevel(0) - atmo.GetOxygenLevel(1));
            Assert.Less(diff, 0.05f);
        }

        [Test]
        public void DisabledVent_NoChange()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            atmo.SetOxygenLevel(0, 1.0f);
            atmo.SetOxygenLevel(1, 0.5f);
            var vent = new VentState
            {
                CompartmentA = 0,
                CompartmentB = 1,
                IsEnabled = false,
                DuctY = 3f,
            };
            vent.Tick(1f / 30f, g, atmo, null);
            Assert.AreEqual(1.0f, atmo.GetOxygenLevel(0), 0.001f);
            Assert.AreEqual(0.5f, atmo.GetOxygenLevel(1), 0.001f);
        }

        [Test]
        public void DamagedVent_NoChange()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 4f, 54f);
            g.AddCompartment(0f, 4f, 54f);
            var atmo = new AtmosphereSystem(g);
            atmo.SetOxygenLevel(0, 1.0f);
            atmo.SetOxygenLevel(1, 0.5f);
            var vent = new VentState
            {
                CompartmentA = 0,
                CompartmentB = 1,
                Condition = 0f,
                DuctY = 3f,
            };
            vent.Tick(1f / 30f, g, atmo, null);
            Assert.AreEqual(1.0f, atmo.GetOxygenLevel(0), 0.001f);
        }

        [Test]
        public void FloodedDuct_NoChange()
        {
            var g = new CompartmentGraph();
            var c = g.AddCompartment(0f, 4f, 54f);
            g.AddCompartment(0f, 4f, 54f);
            c.WaterVolume = c.Volume * 0.9f;
            var atmo = new AtmosphereSystem(g);
            atmo.SetOxygenLevel(0, 1.0f);
            atmo.SetOxygenLevel(1, 0.5f);
            var vent = new VentState
            {
                CompartmentA = 0,
                CompartmentB = 1,
                DuctY = 3f,
            };
            vent.Tick(1f / 30f, g, atmo, null);
            Assert.AreEqual(1.0f, atmo.GetOxygenLevel(0), 0.001f);
        }

        [Test]
        public void Vent_MixesTemperature()
        {
            var g = new CompartmentGraph();
            g.AddCompartment(0f, 2f, 5f);
            g.AddCompartment(0f, 2f, 5f);
            g.GetCompartment(0).TemperatureK = 350f;
            g.GetCompartment(1).TemperatureK = 293f;
            var atmo = new AtmosphereSystem(g);
            var vent = new VentState
            {
                CompartmentA = 0,
                CompartmentB = 1,
                FanFlowRate = 1.5f,
                DuctY = 1.5f,
            };
            for (int i = 0; i < 300; i++)
                vent.Tick(1f / 30f, g, atmo, null);
            float diff = System.Math.Abs(g.GetCompartment(0).TemperatureK - g.GetCompartment(1).TemperatureK);
            Assert.Less(diff, 5f);
        }
    }

    [TestFixture]
    public class TemperatureSystemTests
    {
        [Test]
        public void FireHeatsRoom()
        {
            var g = new CompartmentGraph { SeaLevelY = 200f };
            g.AddCompartment(0f, 2f, 5f);
            var fire = new FireSystem(g);
            var atmo = new AtmosphereSystem(g);
            fire.StartFire(0, 1.0f);
            var temp = new TemperatureSystem(g);
            float before = g.GetCompartment(0).TemperatureK;
            for (int i = 0; i < 150; i++)
            {
                fire.Tick(1f / 30f, atmo);
                temp.Tick(1f / 30f, fire);
            }
            Assert.Greater(g.GetCompartment(0).TemperatureK, before + 1f);
        }

        [Test]
        public void HullCoolsTowardSeaTemp()
        {
            var g = new CompartmentGraph { SeaLevelY = 200f };
            g.AddCompartment(0f, 2f, 5f);
            g.GetCompartment(0).TemperatureK = 350f;
            var temp = new TemperatureSystem(g);
            temp.SetExteriorArea(0, 50f);
            for (int i = 0; i < 30000; i++)
                temp.Tick(1f / 30f, null);
            Assert.Less(g.GetCompartment(0).TemperatureK, 310f);
        }

        [Test]
        public void OpenDoor_ConductsFasterThanClosed()
        {
            var gOpen = new CompartmentGraph { SeaLevelY = 200f };
            gOpen.AddCompartment(0f, 4f, 54f);
            gOpen.AddCompartment(0f, 4f, 54f);
            gOpen.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, true);
            gOpen.GetCompartment(0).TemperatureK = 350f;
            gOpen.GetCompartment(1).TemperatureK = 293f;

            var gClosed = new CompartmentGraph { SeaLevelY = 200f };
            gClosed.AddCompartment(0f, 4f, 54f);
            gClosed.AddCompartment(0f, 4f, 54f);
            gClosed.AddOpening(OpeningKind.Door, 0, 1, 2f, 2f, 4f, false);
            gClosed.GetCompartment(0).TemperatureK = 350f;
            gClosed.GetCompartment(1).TemperatureK = 293f;

            var tempOpen = new TemperatureSystem(gOpen);
            var tempClosed = new TemperatureSystem(gClosed);

            for (int i = 0; i < 30; i++)
            {
                tempOpen.Tick(1f / 30f, null);
                tempClosed.Tick(1f / 30f, null);
            }

            float diffOpen = System.Math.Abs(gOpen.GetCompartment(0).TemperatureK - gOpen.GetCompartment(1).TemperatureK);
            float diffClosed = System.Math.Abs(gClosed.GetCompartment(0).TemperatureK - gClosed.GetCompartment(1).TemperatureK);
            Assert.Less(diffOpen, diffClosed);
        }

        [Test]
        public void TemperatureClampedToRange()
        {
            var g = new CompartmentGraph { SeaLevelY = 200f };
            g.AddCompartment(0f, 4f, 54f);
            g.GetCompartment(0).TemperatureK = 2000f;
            var temp = new TemperatureSystem(g);
            temp.Tick(1f / 30f, null);
            Assert.LessOrEqual(g.GetCompartment(0).TemperatureK, 1273f);
        }

        [Test]
        public void IdealGasCoupling_HeatedSealedRoom()
        {
            var g = new CompartmentGraph { SeaLevelY = 200f };
            g.AddCompartment(0f, 4f, 54f);
            var c = g.GetCompartment(0);
            c.TemperatureK = 350f;
            float p = GasFlowMath.PressureAtm(c.AirAmount, c.AirVolume, c.TemperatureK);
            Assert.Greater(p, 1.1f);
        }
    }
}
