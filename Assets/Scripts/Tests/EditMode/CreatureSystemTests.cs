using System;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class CreatureSystemTests
    {
        const float Dt = 1f / 30f;

        static ProceduralMapData DefaultMap() => new ProceduralMapData(7);

        static SubmarineState SubAtSpawn(ProceduralMapData map)
        {
            return new SubmarineState
            {
                PositionX = map.SpawnX,
                PositionZ = map.SpawnZ,
                Depth = 300f,
            };
        }

        static CreatureSystem SpawnCreatures(int seed, ProceduralMapData map, SubmarineState sub, int count = 6)
        {
            var cs = new CreatureSystem(seed, map);
            for (int i = 0; i < 200 && cs.Creatures.Count < count; i++)
                cs.Tick(5.1f, sub, null, null, false);
            return cs;
        }

        [Test]
        public void NoCreatures_AtConstruction()
        {
            var map = DefaultMap();
            var cs = new CreatureSystem(99, map);
            Assert.AreEqual(0, cs.Creatures.Count);
        }

        [Test]
        public void Spawn_IsDeterministic_ForSameSeedAndPosition()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);

            var a = new CreatureSystem(99, map);
            var b = new CreatureSystem(99, map);

            for (int i = 0; i < 60; i++)
            {
                a.Tick(5.1f, sub, null, null, false);
                b.Tick(5.1f, sub, null, null, false);
            }

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
        public void Spawn_InRingAroundSub()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(123, map, sub);

            Assert.Greater(cs.Creatures.Count, 0);

            for (int i = 0; i < cs.Creatures.Count; i++)
            {
                var c = cs.Creatures[i];
                float dx = c.X - sub.PositionX;
                float dz = c.Z - sub.PositionZ;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                Assert.GreaterOrEqual(dist, 399f, $"creature {i} too close");
                Assert.LessOrEqual(dist, 701f, $"creature {i} too far");
            }
        }

        [Test]
        public void Despawn_RemovesCreaturesBeyondRadius()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(5, map, sub);
            Assert.Greater(cs.Creatures.Count, 0);

            var c = cs.Creatures[0];
            c.X = sub.PositionX + 1300f;

            int countBefore = cs.Creatures.Count;
            cs.Tick(Dt, sub, null, null, false);

            Assert.Less(cs.Creatures.Count, countBefore);
        }

        [Test]
        public void CountNeverExceedsMax()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = new CreatureSystem(10, map) { MaxActiveCreatures = 4 };

            for (int i = 0; i < 200; i++)
                cs.Tick(5.1f, sub, null, null, false);

            Assert.LessOrEqual(cs.Creatures.Count, 4);
        }

        [Test]
        public void Approach_MovesCloserToStationarySub_OverTicks()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(5, map, sub);
            Assert.Greater(cs.Creatures.Count, 0);

            var c = cs.Creatures[0];
            c.Behavior = CreatureBehavior.Approach;

            float dxBefore = sub.PositionX - c.X;
            float dzBefore = sub.PositionZ - c.Z;
            float distBefore = (float)Math.Sqrt(dxBefore * dxBefore + dzBefore * dzBefore);

            for (int i = 0; i < 20; i++)
                cs.Tick(Dt, sub, null, null, false);

            float dxAfter = sub.PositionX - c.X;
            float dzAfter = sub.PositionZ - c.Z;
            float distAfter = (float)Math.Sqrt(dxAfter * dxAfter + dzAfter * dzAfter);

            Assert.Less(distAfter, distBefore);
        }

        [Test]
        public void TakeDamage_BelowThirtyHealth_SetsFlee_AndZero_KillsAndIsIgnored()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(9, map, sub);
            Assert.Greater(cs.Creatures.Count, 0);

            var c = cs.Creatures[0];
            int id = c.Id;

            cs.TakeDamage(id, 75f);
            Assert.AreEqual(25f, c.Health, 1e-4f);
            Assert.AreEqual(CreatureBehavior.Flee, c.Behavior);
            Assert.IsFalse(c.IsDead);

            cs.TakeDamage(id, 25f);
            Assert.AreEqual(0f, c.Health, 1e-4f);
            Assert.IsTrue(c.IsDead);

            float xBefore = c.X, zBefore = c.Z, depthBefore = c.Depth;
            cs.Tick(Dt, sub, null, null, true);

            Assert.AreEqual(xBefore, c.X, 1e-6f);
            Assert.AreEqual(zBefore, c.Z, 1e-6f);
            Assert.AreEqual(depthBefore, c.Depth, 1e-6f);
        }

        [Test]
        public void TurretTryFire_HitsCreatureDeadAhead_AndConsumesAmmo()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(3, map, sub);
            Assert.Greater(cs.Creatures.Count, 0);

            var target = cs.Creatures[0];
            target.X = sub.PositionX;
            target.Z = sub.PositionZ + 300f;
            target.Depth = sub.Depth;

            var turret = new TurretState();
            turret.Tick(2f, null);

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
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(3, map, sub);
            Assert.Greater(cs.Creatures.Count, 0);

            var target = cs.Creatures[0];
            target.X = sub.PositionX;
            target.Z = sub.PositionZ + 300f;
            target.Depth = sub.Depth;
            target.IsDead = false;

            var turret = new TurretState();
            turret.Tick(2f, null);

            bool fired = turret.TryFire(90f, sub, cs, out int hitId, out float hitDist);

            Assert.IsTrue(fired);
            Assert.AreEqual(-1, hitId);
        }

        [Test]
        public void TurretTryFire_ReturnsFalse_WhenOutOfAmmo()
        {
            var map = DefaultMap();
            var sub = SubAtSpawn(map);
            var cs = SpawnCreatures(3, map, sub);

            var turret = new TurretState { AmmoCount = 0 };
            turret.Tick(2f, null);

            bool fired = turret.TryFire(0f, sub, cs, out int hitId, out float hitDist);

            Assert.IsFalse(fired);
            Assert.AreEqual(-1, hitId);
        }
    }
}
