using System;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class CreatureSystemTests
    {
        const float Dt = 1f / 30f;

        static MapData DefaultMap() => MapGenerator.Generate(7);

        [Test]
        public void Spawn_IsDeterministic_ForSameSeedAndMap()
        {
            var mapA = MapGenerator.Generate(11);
            var mapB = MapGenerator.Generate(11);

            var a = new CreatureSystem(99, mapA);
            var b = new CreatureSystem(99, mapB);

            Assert.AreEqual(a.Creatures.Count, b.Creatures.Count);
            Assert.Greater(a.Creatures.Count, 0);

            for (int i = 0; i < a.Creatures.Count; i++)
            {
                Assert.AreEqual(a.Creatures[i].X, b.Creatures[i].X, 1e-4f);
                Assert.AreEqual(a.Creatures[i].Z, b.Creatures[i].Z, 1e-4f);
                Assert.AreEqual(a.Creatures[i].Depth, b.Creatures[i].Depth, 1e-4f);
            }
        }

        [Test]
        public void Spawn_RespectsMinDistanceFromMapSpawnAndBorders()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(123, map);

            Assert.Greater(cs.Creatures.Count, 0);

            for (int i = 0; i < cs.Creatures.Count; i++)
            {
                var c = cs.Creatures[i];

                float dx = c.X - map.SpawnX;
                float dz = c.Z - map.SpawnZ;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                Assert.GreaterOrEqual(dist, 499.9f, $"creature {i} too close to map spawn");

                Assert.GreaterOrEqual(c.X, 149.9f, $"creature {i} X too close to border");
                Assert.LessOrEqual(c.X, map.WorldSizeX - 149.9f, $"creature {i} X too close to border");
                Assert.GreaterOrEqual(c.Z, 149.9f, $"creature {i} Z too close to border");
                Assert.LessOrEqual(c.Z, map.WorldSizeZ - 149.9f, $"creature {i} Z too close to border");
            }
        }

        [Test]
        public void Approach_MovesCloserToStationarySub_OverTicks()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(5, map);
            Assert.Greater(cs.Creatures.Count, 0);

            var c = cs.Creatures[0];
            var sub = new SubmarineState { PositionX = 1000f, PositionZ = 1000f, Depth = 300f };

            c.X = 1500f;
            c.Z = 1000f;
            c.Depth = 300f;
            c.Behavior = CreatureBehavior.Approach;

            float dxBefore = sub.PositionX - c.X;
            float dzBefore = sub.PositionZ - c.Z;
            float distBefore = (float)Math.Sqrt(dxBefore * dxBefore + dzBefore * dzBefore);

            for (int i = 0; i < 20; i++)
                cs.Tick(Dt, sub, null, null, false);

            float dxAfter = sub.PositionX - c.X;
            float dzAfter = sub.PositionZ - c.Z;
            float distAfter = (float)Math.Sqrt(dxAfter * dxAfter + dzAfter * dzAfter);

            Assert.Less(distAfter, distBefore, "Approach behavior should move the creature closer to the sub");
        }

        [Test]
        public void TakeDamage_BelowThirtyHealth_SetsFlee_AndZero_KillsAndIsIgnored()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(9, map);
            Assert.Greater(cs.Creatures.Count, 0);

            var c = cs.Creatures[0];
            int id = c.Id;

            cs.TakeDamage(id, 75f); // 100 -> 25, below 30
            Assert.AreEqual(25f, c.Health, 1e-4f);
            Assert.AreEqual(CreatureBehavior.Flee, c.Behavior);
            Assert.IsFalse(c.IsDead);

            cs.TakeDamage(id, 25f); // 25 -> 0
            Assert.AreEqual(0f, c.Health, 1e-4f);
            Assert.IsTrue(c.IsDead);

            float xBefore = c.X, zBefore = c.Z, depthBefore = c.Depth;
            var sub = new SubmarineState { PositionX = c.X, PositionZ = c.Z, Depth = c.Depth };
            cs.Tick(Dt, sub, null, null, true);

            Assert.AreEqual(xBefore, c.X, 1e-6f, "Dead creature must be ignored by Tick");
            Assert.AreEqual(zBefore, c.Z, 1e-6f, "Dead creature must be ignored by Tick");
            Assert.AreEqual(depthBefore, c.Depth, 1e-6f, "Dead creature must be ignored by Tick");
        }

        [Test]
        public void TurretTryFire_HitsCreatureDeadAhead_AndConsumesAmmo()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(3, map);
            Assert.Greater(cs.Creatures.Count, 0);

            var target = cs.Creatures[0];
            var sub = new SubmarineState { PositionX = 0f, PositionZ = 0f, Depth = 100f };
            target.X = 0f;
            target.Z = 300f; // dead ahead, bearing 0 = north/+Z
            target.Depth = sub.Depth;

            var turret = new TurretState();
            turret.Tick(2f, null); // charge cooldown fully, no power node -> always powered

            bool fired = turret.TryFire(0f, sub, cs, out int hitId, out float hitDist);

            Assert.IsTrue(fired);
            Assert.AreEqual(target.Id, hitId);
            Assert.AreEqual(300f, hitDist, 0.01f);
            Assert.AreEqual(49, turret.AmmoCount);
        }

        [Test]
        public void TurretTryFire_MissesWhenBearingIsNinetyDegreesOff()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(3, map);
            Assert.Greater(cs.Creatures.Count, 0);

            var target = cs.Creatures[0];
            var sub = new SubmarineState { PositionX = 0f, PositionZ = 0f, Depth = 100f };
            target.X = 0f;
            target.Z = 300f; // bearing 0 relative to sub
            target.Depth = sub.Depth;
            target.IsDead = false;

            var turret = new TurretState();
            turret.Tick(2f, null);

            bool fired = turret.TryFire(90f, sub, cs, out int hitId, out float hitDist);

            Assert.IsTrue(fired, "A shot should still fire even if it misses");
            Assert.AreEqual(-1, hitId);
        }

        [Test]
        public void TurretTryFire_ReturnsFalse_WhenOutOfAmmo()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(3, map);

            var sub = new SubmarineState { PositionX = 0f, PositionZ = 0f, Depth = 100f };
            var turret = new TurretState { AmmoCount = 0 };
            turret.Tick(2f, null);

            bool fired = turret.TryFire(0f, sub, cs, out int hitId, out float hitDist);

            Assert.IsFalse(fired);
            Assert.AreEqual(-1, hitId);
        }
    }
}
