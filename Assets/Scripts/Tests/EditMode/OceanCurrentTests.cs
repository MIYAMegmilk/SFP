using System;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class OceanCurrentTests
    {
        const float WorldSize = 2000f;

        [Test]
        public void Sample_IsDeterministic_ForSameSeedAndWorldSize()
        {
            var a = new OceanCurrentField(42, WorldSize);
            var b = new OceanCurrentField(42, WorldSize);

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
        public void Sample_MagnitudeNeverExceedsMax_AndAverageIsMeaningful()
        {
            var field = new OceanCurrentField(7, WorldSize);
            var rng = new Random(1234);

            double total = 0.0;
            const int Count = 1000;
            for (int i = 0; i < Count; i++)
            {
                // Stay well inside the map so border damping doesn't dominate the average.
                float x = 300f + (float)rng.NextDouble() * (WorldSize - 600f);
                float z = 300f + (float)rng.NextDouble() * (WorldSize - 600f);
                float depth = (float)rng.NextDouble() * 100f;

                field.Sample(x, z, depth, out float vx, out float vz);
                float mag = (float)Math.Sqrt(vx * vx + vz * vz);

                Assert.LessOrEqual(mag, field.MaxCurrentSpeed + 1e-4f, $"exceeded max at i={i}");
                total += mag;
            }

            double average = total / Count;
            Assert.GreaterOrEqual(average, 0.3, $"average magnitude too low: {average}");
        }

        [Test]
        public void DepthAttenuation_WeakensCurrentBelowFortyPercent()
        {
            var field = new OceanCurrentField(7, WorldSize);

            field.Sample(500f, 500f, 0f, out float shallowX, out float shallowZ);
            field.Sample(500f, 500f, 600f, out float deepX, out float deepZ);

            float shallowMag = (float)Math.Sqrt(shallowX * shallowX + shallowZ * shallowZ);
            float deepMag = (float)Math.Sqrt(deepX * deepX + deepZ * deepZ);

            Assert.Greater(shallowMag, 0f);
            Assert.LessOrEqual(deepMag, shallowMag * 0.4f + 1e-5f);
        }

        [Test]
        public void ComputeDepthFactor_MatchesExpectedFormula()
        {
            Assert.AreEqual(1f, OceanCurrentField.ComputeDepthFactor(0f), 1e-5f);
            Assert.AreEqual(0.25f, OceanCurrentField.ComputeDepthFactor(600f), 1e-5f);
            Assert.AreEqual(0.1f, OceanCurrentField.ComputeDepthFactor(2000f), 1e-5f); // floor
        }

        [Test]
        public void ComputeBorderFactor_TapersNearEdges()
        {
            float nearEdge = OceanCurrentField.ComputeBorderFactor(50f, 1000f, WorldSize);
            float interior = OceanCurrentField.ComputeBorderFactor(500f, 1000f, WorldSize);
            float onEdge = OceanCurrentField.ComputeBorderFactor(0f, 1000f, WorldSize);

            Assert.AreEqual(0.5f, nearEdge, 1e-5f);
            Assert.AreEqual(1f, interior, 1e-5f);
            Assert.AreEqual(0f, onEdge, 1e-5f);
        }

        [Test]
        public void Sample_NearEdge_IsBoundedByBorderDamping()
        {
            var field = new OceanCurrentField(7, WorldSize);

            // Within 100m of the x=0 edge: border factor is exactly 0.5 by formula.
            field.Sample(50f, 1000f, 200f, out float vx, out float vz);
            float mag = (float)Math.Sqrt(vx * vx + vz * vz);

            float borderFactor = OceanCurrentField.ComputeBorderFactor(50f, 1000f, WorldSize);
            float depthFactor = OceanCurrentField.ComputeDepthFactor(200f);
            float maxAllowed = field.MaxCurrentSpeed * borderFactor * depthFactor;

            Assert.LessOrEqual(mag, maxAllowed + 1e-4f);
        }

        [Test]
        public void Sample_BorderDamping_ReducesRawCurrentByExpectedFactor()
        {
            var field = new OceanCurrentField(7, WorldSize);

            float x = 50f, z = 1000f, depth = 200f;
            field.SampleRaw(x, z, out float rawX, out float rawZ);
            field.Sample(x, z, depth, out float dampedX, out float dampedZ);

            float expectedFactor = OceanCurrentField.ComputeBorderFactor(x, z, WorldSize)
                * OceanCurrentField.ComputeDepthFactor(depth);

            Assert.AreEqual(rawX * expectedFactor, dampedX, 1e-5f);
            Assert.AreEqual(rawZ * expectedFactor, dampedZ, 1e-5f);
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
