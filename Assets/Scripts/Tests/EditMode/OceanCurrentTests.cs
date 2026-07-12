using System;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class OceanCurrentTests
    {
        [Test]
        public void Sample_IsDeterministic_ForSameSeed()
        {
            var a = new OceanCurrentField(42);
            var b = new OceanCurrentField(42);

            for (int i = 0; i < 20; i++)
            {
                float x = 100f + i * 77f;
                float z = 200f + i * 53f;
                float depth = i * 15f;

                a.Sample(x, z, depth, out float ax, out float az);
                b.Sample(x, z, depth, out float bx, out float bz);

                Assert.AreEqual(ax, bx, 1e-6f, $"velX mismatch at i={i}");
                Assert.AreEqual(az, bz, 1e-6f, $"velZ mismatch at i={i}");
            }
        }

        [Test]
        public void Sample_MagnitudeNeverExceedsMax()
        {
            var field = new OceanCurrentField(7);
            var rng = new Random(1234);

            const int Count = 1000;
            for (int i = 0; i < Count; i++)
            {
                float x = (float)rng.NextDouble() * 10000f;
                float z = (float)rng.NextDouble() * 10000f;
                float depth = (float)rng.NextDouble() * 800f;

                field.Sample(x, z, depth, out float vx, out float vz);
                float mag = (float)Math.Sqrt(vx * vx + vz * vz);

                Assert.LessOrEqual(mag, field.MaxCurrentSpeed + 0.1f, $"exceeded max at i={i}");
            }
        }

        [Test]
        public void Sample_HasMeaningfulMagnitude()
        {
            var field = new OceanCurrentField(7);
            var rng = new Random(1234);

            double total = 0.0;
            const int Count = 1000;
            for (int i = 0; i < Count; i++)
            {
                float x = (float)rng.NextDouble() * 10000f;
                float z = (float)rng.NextDouble() * 10000f;
                float depth = (float)rng.NextDouble() * 100f;

                field.Sample(x, z, depth, out float vx, out float vz);
                float mag = (float)Math.Sqrt(vx * vx + vz * vz);
                total += mag;
            }

            double average = total / Count;
            Assert.GreaterOrEqual(average, 0.3, $"average magnitude too low: {average}");
        }

        [Test]
        public void DepthAttenuation_DeepCurrentWeakerThanSurface()
        {
            var field = new OceanCurrentField(7);

            // Sample multiple points to get statistical comparison
            var rng = new Random(42);
            double shallowTotal = 0, deepTotal = 0;
            const int Count = 200;
            for (int i = 0; i < Count; i++)
            {
                float x = (float)rng.NextDouble() * 8000f;
                float z = (float)rng.NextDouble() * 8000f;
                field.Sample(x, z, 10f, out float sx, out float sz);
                field.Sample(x, z, 700f, out float dx, out float dz);
                shallowTotal += Math.Sqrt(sx * sx + sz * sz);
                deepTotal += Math.Sqrt(dx * dx + dz * dz);
            }

            Assert.Greater(shallowTotal, deepTotal,
                "Deep currents should be weaker on average than shallow");
            Assert.Less(deepTotal / shallowTotal, 0.5,
                "Deep current should be less than 50% of shallow on average");
        }

        [Test]
        public void DepthProfile_DirectionVariesWithDepth()
        {
            var field = new OceanCurrentField(7);

            // At a single position, different depths should yield different directions
            float x = 2000f, z = 3000f;
            field.Sample(x, z, 10f, out float v10x, out float v10z);
            field.Sample(x, z, 200f, out float v200x, out float v200z);
            field.Sample(x, z, 500f, out float v500x, out float v500z);

            // Compute angles
            double angle10 = Math.Atan2(v10z, v10x);
            double angle200 = Math.Atan2(v200z, v200x);
            double angle500 = Math.Atan2(v500z, v500x);

            // At least one pair should differ by more than 10 degrees
            double diff1 = Math.Abs(angle10 - angle200);
            double diff2 = Math.Abs(angle200 - angle500);
            double diff3 = Math.Abs(angle10 - angle500);
            double maxDiff = Math.Max(diff1, Math.Max(diff2, diff3));
            if (maxDiff > Math.PI) maxDiff = 2 * Math.PI - maxDiff;

            Assert.Greater(maxDiff, 10.0 * Math.PI / 180.0,
                "Current direction should vary meaningfully across depth layers");
        }

        [Test]
        public void TimeAdvance_ChangesCurrent()
        {
            var field = new OceanCurrentField(7);
            float x = 1000f, z = 1000f, depth = 50f;

            field.Sample(x, z, depth, out float vx0, out float vz0);

            // Advance by half a tidal period
            field.AdvanceTime(360f);
            field.Sample(x, z, depth, out float vx1, out float vz1);

            // Should be different due to tidal oscillation + eddy drift
            float diff = (float)Math.Sqrt((vx1 - vx0) * (vx1 - vx0) + (vz1 - vz0) * (vz1 - vz0));
            Assert.Greater(diff, 0.01f, "Current should change over time");
        }

        [Test]
        public void ComputeDepthFactor_ReturnsOneAtSurface()
        {
            Assert.AreEqual(1f, OceanCurrentField.ComputeDepthFactor(0f), 1e-5f);
            Assert.AreEqual(1f, OceanCurrentField.ComputeDepthFactor(100f), 1e-5f);
        }

        [Test]
        public void ComputeDepthFactor_DecaysWithDepth()
        {
            float f200 = OceanCurrentField.ComputeDepthFactor(200f);
            float f600 = OceanCurrentField.ComputeDepthFactor(600f);
            float f1000 = OceanCurrentField.ComputeDepthFactor(1000f);

            Assert.AreEqual(1f, f200, 1e-5f);
            Assert.Less(f600, f200);
            Assert.LessOrEqual(f1000, f600);
            Assert.GreaterOrEqual(f1000, 0.05f);
        }

        [Test]
        public void SubmarineDrifts_WithZeroThrust_WhenOceanCurrentApplied()
        {
            var sub = new SubmarineState { DryMass = 20000f, HullVolume = 20f };
            var graph = new CompartmentGraph();

            const float Dt = 1f / 30f;
            const float Duration = 30f;
            int steps = (int)Math.Round(Duration / Dt);

            for (int i = 0; i < steps; i++)
                sub.Tick(Dt, graph, 0f, 1.0f, 0f);

            Assert.AreEqual(30f, sub.PositionX, 0.5f);
            Assert.AreEqual(0f, sub.PositionZ, 0.5f);
        }

        [Test]
        public void TwoArgTick_StillWorks_WithNewOptionalParams()
        {
            var sub = new SubmarineState { DryMass = 20000f, HullVolume = 20f };
            var graph = new CompartmentGraph();

            sub.Tick(1f / 30f, graph);

            Assert.AreEqual(0f, sub.PositionX, 1e-5f);
            Assert.AreEqual(0f, sub.PositionZ, 1e-5f);
        }
    }
}
