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
        public void Sample_MagnitudeNeverExceedsMax_AndAverageIsMeaningful()
        {
            var field = new OceanCurrentField(7);
            var rng = new Random(1234);

            double total = 0.0;
            const int Count = 1000;
            for (int i = 0; i < Count; i++)
            {
                float x = (float)rng.NextDouble() * 4000f;
                float z = (float)rng.NextDouble() * 4000f;
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
            var field = new OceanCurrentField(7);

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
            Assert.AreEqual(0.1f, OceanCurrentField.ComputeDepthFactor(2000f), 1e-5f);
        }

        [Test]
        public void Sample_DepthOnly_ReducesRawCurrentByExpectedFactor()
        {
            var field = new OceanCurrentField(7);

            float x = 500f, z = 500f, depth = 200f;
            field.SampleRaw(x, z, out float rawX, out float rawZ);
            field.Sample(x, z, depth, out float dampedX, out float dampedZ);

            float expectedFactor = OceanCurrentField.ComputeDepthFactor(depth);

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
