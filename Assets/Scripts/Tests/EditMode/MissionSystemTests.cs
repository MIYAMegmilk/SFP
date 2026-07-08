using System.Collections.Generic;
using NUnit.Framework;
using SFP.Simulation;

namespace SFP.Tests
{
    [TestFixture]
    public class MissionSystemTests
    {
        struct QueuedTarget
        {
            public MissionKind Kind;
            public float X, Z;
            public string Label;
        }

        // Drains the mission queue by teleporting a stub sub onto each target in turn,
        // recording targets in order. Does not rely on any API beyond the public contract.
        static List<QueuedTarget> DrainQueue(MissionSystem ms)
        {
            var result = new List<QueuedTarget>();
            var sub = new SubmarineState();
            int guard = 0;
            while (ms.Current != null && guard++ < 64)
            {
                var m = ms.Current;
                result.Add(new QueuedTarget { Kind = m.Kind, X = m.TargetX, Z = m.TargetZ, Label = m.Label });
                sub.PositionX = m.TargetX;
                sub.PositionZ = m.TargetZ;
                float dt = m.Kind == MissionKind.HoldPosition ? m.HoldSeconds + 1f : 0.1f;
                ms.Tick(dt, sub);
            }
            return result;
        }

        [Test]
        public void BuildQueue_IsDeterministic_ForSameSeedAndMap()
        {
            var map = MapGenerator.Generate(21);
            var a = new MissionSystem(21, map);
            var b = new MissionSystem(21, map);

            var targetsA = DrainQueue(a);
            var targetsB = DrainQueue(b);

            Assert.AreEqual(targetsA.Count, targetsB.Count);
            Assert.Greater(targetsA.Count, 0);
            for (int i = 0; i < targetsA.Count; i++)
            {
                Assert.AreEqual(targetsA[i].Kind, targetsB[i].Kind, $"index {i}");
                Assert.AreEqual(targetsA[i].X, targetsB[i].X, 1e-6f, $"index {i}");
                Assert.AreEqual(targetsA[i].Z, targetsB[i].Z, 1e-6f, $"index {i}");
                Assert.AreEqual(targetsA[i].Label, targetsB[i].Label, $"index {i}");
            }
        }

        [Test]
        public void ReachPoint_CompletesInsideRadius_AndAdvancesCurrent()
        {
            var map = MapGenerator.Generate(11);
            var missions = new MissionSystem(11, map);
            var first = missions.Current;
            Assert.IsNotNull(first);
            Assert.AreEqual(MissionKind.ReachPoint, first.Kind);

            var sub = new SubmarineState { PositionX = first.TargetX, PositionZ = first.TargetZ };
            missions.Tick(0.1f, sub);

            Assert.AreEqual(1, missions.CompletedCount);
            Assert.AreNotSame(first, missions.Current);
        }

        [Test]
        public void ReachPoint_DoesNotComplete_WhenOutsideRadius()
        {
            var map = MapGenerator.Generate(11);
            var missions = new MissionSystem(11, map);
            var first = missions.Current;

            var sub = new SubmarineState
            {
                PositionX = first.TargetX + first.Radius + 50f,
                PositionZ = first.TargetZ,
            };
            missions.Tick(0.1f, sub);

            Assert.AreEqual(0, missions.CompletedCount);
            Assert.AreSame(first, missions.Current);
        }

        [Test]
        public void HoldPosition_ProgressAccumulatesInsideRadius_ResetsOutside_CompletesAtThreshold()
        {
            // Tiny world: channel waypoint generation degenerates to just the spawn point,
            // so the only mission is the final HoldPosition (its deep point also falls back
            // to spawn, since the border margin exceeds the world size).
            var map = MapGenerator.Generate(3, worldSize: 200f, cellSize: 8f);
            var missions = new MissionSystem(3, map);

            var hold = missions.Current;
            Assert.IsNotNull(hold);
            Assert.AreEqual(MissionKind.HoldPosition, hold.Kind);

            var sub = new SubmarineState { PositionX = hold.TargetX, PositionZ = hold.TargetZ };

            missions.Tick(10f, sub);
            Assert.AreEqual(10f, hold.HoldProgress, 1e-4f);

            sub.PositionX = hold.TargetX + hold.Radius + 50f;
            missions.Tick(1f, sub);
            Assert.AreEqual(0f, hold.HoldProgress, 1e-4f);

            sub.PositionX = hold.TargetX;
            missions.Tick(hold.HoldSeconds, sub);

            Assert.IsNull(missions.Current);
            Assert.AreEqual(1, missions.CompletedCount);
        }

        [Test]
        public void AllTargets_AreInsideMapBorders_AndOverDeepWater()
        {
            var map = MapGenerator.Generate(5);
            var missions = new MissionSystem(5, map);
            var targets = DrainQueue(missions);

            Assert.Greater(targets.Count, 0);
            foreach (var t in targets)
            {
                Assert.GreaterOrEqual(t.X, 0f);
                Assert.LessOrEqual(t.X, map.WorldSizeX);
                Assert.GreaterOrEqual(t.Z, 0f);
                Assert.LessOrEqual(t.Z, map.WorldSizeZ);

                float floor = map.GetFloorDepthAt(t.X, t.Z);
                Assert.GreaterOrEqual(floor, 200f, $"target ({t.X},{t.Z})");
            }
        }
    }
}
