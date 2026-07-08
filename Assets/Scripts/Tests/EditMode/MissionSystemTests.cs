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

        static List<QueuedTarget> DrainOutbound(MissionSystem ms)
        {
            var result = new List<QueuedTarget>();
            var sub = new SubmarineState();
            int guard = 0;
            while (ms.Current != null && ms.Phase == MissionPhase.Outbound && guard++ < 64)
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

            var targetsA = DrainOutbound(a);
            var targetsB = DrainOutbound(b);

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
        public void OutboundComplete_TransitionsToReturning()
        {
            var map = MapGenerator.Generate(11);
            var missions = new MissionSystem(11, map);
            Assert.AreEqual(MissionPhase.Outbound, missions.Phase);

            var sub = new SubmarineState();
            int guard = 0;
            while (missions.Phase == MissionPhase.Outbound && guard++ < 64)
            {
                var m = missions.Current;
                if (m == null) break;
                sub.PositionX = m.TargetX;
                sub.PositionZ = m.TargetZ;
                float dt = m.Kind == MissionKind.HoldPosition ? m.HoldSeconds + 1f : 0.1f;
                missions.Tick(dt, sub);
            }

            Assert.AreEqual(MissionPhase.Returning, missions.Phase);
            Assert.IsNotNull(missions.Current);
            Assert.AreEqual("Return to base", missions.Current.Label);
        }

        [Test]
        public void ReturnComplete_AdvancesToNextRound()
        {
            var map = MapGenerator.Generate(11);
            var missions = new MissionSystem(11, map);
            Assert.AreEqual(1, missions.Round);

            var sub = new SubmarineState();
            int guard = 0;
            int startRound = missions.Round;
            while (missions.Round == startRound && guard++ < 128)
            {
                var m = missions.Current;
                if (m == null) { missions.Tick(0.1f, sub); continue; }
                sub.PositionX = m.TargetX;
                sub.PositionZ = m.TargetZ;
                float dt = m.Kind == MissionKind.HoldPosition ? m.HoldSeconds + 1f : 0.1f;
                missions.Tick(dt, sub);
            }

            Assert.AreEqual(2, missions.Round);
            Assert.AreEqual(MissionPhase.Outbound, missions.Phase);
            Assert.IsNotNull(missions.Current);
        }

        [Test]
        public void AllTargets_AreInsideMapBorders_AndOverDeepWater()
        {
            var map = MapGenerator.Generate(5);
            var missions = new MissionSystem(5, map);
            var targets = DrainOutbound(missions);

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

        [Test]
        public void DifferentRounds_ProduceDifferentWaypointOrder()
        {
            var map = MapGenerator.Generate(42);
            var missions = new MissionSystem(42, map);
            var round1 = DrainOutbound(missions);

            var sub = new SubmarineState();
            // Complete return phase
            while (missions.Round == 1)
            {
                var m = missions.Current;
                if (m == null) { missions.Tick(0.1f, sub); continue; }
                sub.PositionX = m.TargetX;
                sub.PositionZ = m.TargetZ;
                float dt = m.Kind == MissionKind.HoldPosition ? m.HoldSeconds + 1f : 0.1f;
                missions.Tick(dt, sub);
            }

            var round2 = DrainOutbound(missions);

            Assert.AreEqual(round1.Count, round2.Count);
            bool anyDifferent = false;
            for (int i = 0; i < round1.Count; i++)
            {
                if (System.Math.Abs(round1[i].X - round2[i].X) > 1f ||
                    System.Math.Abs(round1[i].Z - round2[i].Z) > 1f)
                {
                    anyDifferent = true;
                    break;
                }
            }
            Assert.IsTrue(anyDifferent, "Round 2 should have different waypoint selection/order than round 1");
        }
    }
}
