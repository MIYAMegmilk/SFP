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
            sub.DryMass = 1_000_000f; // keep horizontal accel tiny so speed stays ~0 this tick
            var graph = new CompartmentGraph();

            sub.Tick(Dt, graph, 50000f);

            Assert.AreEqual(0.7f, sub.NoiseLevel, 0.01f);
        }

        [Test]
        public void NoiseLevel_ClampedToOne()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();

            // dt = 0 isolates the NoiseLevel formula from position/velocity integration.
            sub.Tick(0f, graph, 100000f);

            Assert.AreEqual(1f, sub.NoiseLevel, 1e-6f);
        }

        [Test]
        public void AcousticAssignment_IsDeterministicAcrossRuns()
        {
            var a = new MineSystem();
            var b = new MineSystem();
            for (int i = 0; i < 20; i++)
            {
                a.AddMine(i * 10f, 0f, 0f);
                b.AddMine(i * 10f, 0f, 0f);
            }

            for (int i = 0; i < 20; i++)
                Assert.AreEqual(a.Mines[i].IsAcoustic, b.Mines[i].IsAcoustic,
                    $"Mine {i} acoustic flag should be stable across independent runs");
        }

        [Test]
        public void AcousticAssignment_MatchesExpectedPatternAndRoughlyFortyPercent()
        {
            var mines = new MineSystem();
            for (int i = 0; i < 20; i++)
                mines.AddMine(i * 10f, 0f, 0f);

            bool[] expected =
            {
                false, false, true, false, true, false, false, true, true, true,
                false, true, false, false, false, false, false, false, true, false,
            };

            int acousticCount = 0;
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(expected[i], mines.Mines[i].IsAcoustic, $"Mine {i} acoustic flag mismatch");
                if (mines.Mines[i].IsAcoustic) acousticCount++;
            }

            Assert.AreEqual(7, acousticCount, "Expected roughly 40% of 20 mines to be acoustic");
        }

        static SubmarineState LoudSub()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();
            sub.Tick(0f, graph, 100000f); // NoiseLevel clamps to 1
            return sub;
        }

        static SubmarineState SilentSub()
        {
            var sub = new SubmarineState();
            var graph = new CompartmentGraph();
            sub.Tick(Dt, graph, 0f); // NoiseLevel stays 0
            return sub;
        }

        [Test]
        public void AcousticMine_TriggersAtOneHundredMetersForLoudSub()
        {
            var mines = new MineSystem();
            mines.AddMine(0f, 0f, 0f); // index 0: contact
            mines.AddMine(10f, 0f, 0f); // index 1: contact
            mines.AddMine(100f, 0f, 0f); // index 2: acoustic

            var sub = LoudSub(); // sits at origin, NoiseLevel = 1
            var damage = new DamageSystem(new CompartmentGraph(), sub);

            Assert.IsTrue(mines.Mines[2].IsAcoustic, "Precondition: mine 2 must be acoustic");

            mines.Tick(Dt, sub, damage);

            Assert.IsTrue(mines.Mines[2].Exploded, "Acoustic mine should trigger at 100m for a loud sub (effective radius 135m)");
        }

        [Test]
        public void AcousticMine_DoesNotTriggerAtOneHundredMetersForSilentSub()
        {
            var mines = new MineSystem();
            mines.AddMine(0f, 0f, 0f); // index 0: contact
            mines.AddMine(10f, 0f, 0f); // index 1: contact
            mines.AddMine(100f, 0f, 0f); // index 2: acoustic

            var sub = SilentSub(); // sits at origin, NoiseLevel = 0
            var damage = new DamageSystem(new CompartmentGraph(), sub);

            Assert.IsTrue(mines.Mines[2].IsAcoustic, "Precondition: mine 2 must be acoustic");

            mines.Tick(Dt, sub, damage);

            Assert.IsFalse(mines.Mines[2].Exploded, "Acoustic mine should NOT trigger at 100m for a silent sub (effective radius 15m)");
        }

        [Test]
        public void ContactMine_RadiusUnaffectedByNoise()
        {
            var mines = new MineSystem();
            mines.AddMine(500f, 0f, 0f); // index 0: contact, far from sub so it never explodes

            var sub = LoudSub(); // NoiseLevel = 1
            var damage = new DamageSystem(new CompartmentGraph(), sub);

            Assert.IsFalse(mines.Mines[0].IsAcoustic, "Precondition: mine 0 must be a contact mine");

            mines.Tick(Dt, sub, damage);

            Assert.AreEqual(25f, mines.Mines[0].TriggerRadius, 1e-6f);
            Assert.IsFalse(mines.Mines[0].Exploded);
        }
    }
}
