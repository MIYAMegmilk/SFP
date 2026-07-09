using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum MissionKind { ReachPoint, HoldPosition }
    public enum MissionPhase { Outbound, Returning }

    public sealed class Mission
    {
        public MissionKind Kind;
        public float TargetX, TargetZ;
        public float Radius = 100f;
        public float HoldSeconds;
        public float HoldProgress;
        public string Label;
    }

    public sealed class MissionSystem
    {
        const float HoldPositionSeconds = 30f;
        const float DeepPointMinFloor = 400f;
        const int WaypointsPerRound = 3;

        readonly ProceduralMapData _map;
        readonly float _baseX, _baseZ;
        int _seed;

        readonly List<Mission> _missions = new();
        int _currentIndex;

        public MissionPhase Phase { get; private set; } = MissionPhase.Outbound;
        public int Round { get; private set; } = 1;
        public Mission Current => _currentIndex < _missions.Count ? _missions[_currentIndex] : null;
        public int CompletedCount => _currentIndex;
        public int TotalCount => _missions.Count;

        public MissionSystem(int seed, ProceduralMapData map)
        {
            _seed = seed;
            _map = map;
            _baseX = map?.SpawnX ?? 0f;
            _baseZ = map?.SpawnZ ?? 0f;

            GenerateRound();
        }

        void GenerateRound()
        {
            _missions.Clear();
            _currentIndex = 0;
            Phase = MissionPhase.Outbound;

            if (_map == null) return;

            var waypoints = PickWaypoints();
            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                bool isSurvey = (i == waypoints.Count - 1) && Round % 2 == 0;
                if (isSurvey)
                {
                    _missions.Add(new Mission
                    {
                        Kind = MissionKind.HoldPosition,
                        TargetX = wp.X,
                        TargetZ = wp.Z,
                        HoldSeconds = HoldPositionSeconds,
                        Label = $"Survey site {(char)('A' + i)}",
                    });
                }
                else
                {
                    _missions.Add(new Mission
                    {
                        Kind = MissionKind.ReachPoint,
                        TargetX = wp.X,
                        TargetZ = wp.Z,
                        Label = $"Navigate to point {(char)('A' + i)}",
                    });
                }
            }
        }

        List<(float X, float Z)> PickWaypoints()
        {
            var result = new List<(float X, float Z)>();
            var used = new HashSet<long>();
            var nodeList = new List<(float X, float Z)>();

            float headingDeg = HashToFloat(Round, 1, _seed) * 360f;
            float headingRad = headingDeg * ((float)Math.PI / 180f);

            for (int i = 0; i < WaypointsPerRound; i++)
            {
                float dist = 700f * (i + 1);
                float spreadDeg = (HashToFloat(Round, 2 + i, _seed) - 0.5f) * 50f;
                float totalRad = (headingDeg + spreadDeg) * ((float)Math.PI / 180f);

                float candidateX = _baseX + (float)Math.Sin(totalRad) * dist;
                float candidateZ = _baseZ + (float)Math.Cos(totalRad) * dist;

                // Snap to nearest channel node
                _map.GetNearbyChannelNodes(candidateX, candidateZ, nodeList);

                float bestDist = float.MaxValue;
                (float X, float Z) bestNode = (candidateX, candidateZ);
                bool found = false;

                for (int n = 0; n < nodeList.Count; n++)
                {
                    var node = nodeList[n];
                    // Use integer key to identify unique nodes for dedup
                    int sx = (int)Math.Floor(node.X / MapConstants.ChunkSize);
                    int sz = (int)Math.Floor(node.Z / MapConstants.ChunkSize);
                    long nodeKey = ((long)sx << 32) | (uint)sz;
                    if (used.Contains(nodeKey)) continue;

                    float dx = node.X - candidateX;
                    float dz = node.Z - candidateZ;
                    float d = dx * dx + dz * dz;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestNode = node;
                        found = true;
                    }
                }

                if (found)
                {
                    int bsx = (int)Math.Floor(bestNode.X / MapConstants.ChunkSize);
                    int bsz = (int)Math.Floor(bestNode.Z / MapConstants.ChunkSize);
                    used.Add(((long)bsx << 32) | (uint)bsz);
                }

                result.Add(bestNode);
            }

            // On even rounds, replace the last target with a deep point
            if (Round % 2 == 0 && result.Count > 0)
            {
                float lastDist = 700f * WaypointsPerRound;
                var deep = FindDeepPoint(_seed + Round * 7919, headingDeg, lastDist);
                result[result.Count - 1] = deep;
            }

            return result;
        }

        (float X, float Z) FindDeepPoint(int seed, float headingDeg, float lastDist)
        {
            (float X, float Z) best = (_baseX, _baseZ);
            float bestFloor = float.MinValue;

            for (int attempt = 0; attempt < 64; attempt++)
            {
                float hAngle = HashToFloat(attempt, 71, seed + 90071);
                float hRadius = HashToFloat(attempt, 72, seed + 90071);

                // Annulus [0.8, 1.2] * lastDist
                float r = lastDist * (0.8f + hRadius * 0.4f);
                // Within headingDeg +/- 45 degrees
                float angleDeg = headingDeg + (hAngle - 0.5f) * 90f;
                float angleRad = angleDeg * ((float)Math.PI / 180f);

                float mx = _baseX + (float)Math.Sin(angleRad) * r;
                float mz = _baseZ + (float)Math.Cos(angleRad) * r;

                float floor = _map.GetFloorDepthAt(mx, mz);
                if (floor > bestFloor)
                {
                    bestFloor = floor;
                    best = (mx, mz);
                }
                if (floor >= DeepPointMinFloor) return (mx, mz);
            }
            return best;
        }

        void StartReturn()
        {
            Phase = MissionPhase.Returning;
            _missions.Clear();
            _currentIndex = 0;
            _missions.Add(new Mission
            {
                Kind = MissionKind.ReachPoint,
                TargetX = _baseX,
                TargetZ = _baseZ,
                Radius = 120f,
                Label = "Return to base",
            });
        }

        void StartNextRound()
        {
            Round++;
            _seed += 13;
            GenerateRound();
        }

        public float DistanceToTarget(SubmarineState sub)
        {
            var m = Current;
            if (m == null || sub == null) return 0f;
            float dx = m.TargetX - sub.PositionX;
            float dz = m.TargetZ - sub.PositionZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public float BearingToTarget(SubmarineState sub)
        {
            var m = Current;
            if (m == null || sub == null) return 0f;
            float dx = m.TargetX - sub.PositionX;
            float dz = m.TargetZ - sub.PositionZ;
            float bearing = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
            if (bearing < 0f) bearing += 360f;
            return bearing;
        }

        public void Tick(float dt, SubmarineState sub)
        {
            var m = Current;
            if (m == null || sub == null)
            {
                if (m == null && Phase == MissionPhase.Outbound)
                    StartReturn();
                else if (m == null && Phase == MissionPhase.Returning)
                    StartNextRound();
                return;
            }

            float dist = DistanceToTarget(sub);
            switch (m.Kind)
            {
                case MissionKind.ReachPoint:
                    if (dist < m.Radius) _currentIndex++;
                    break;

                case MissionKind.HoldPosition:
                    if (dist < m.Radius)
                    {
                        m.HoldProgress += dt;
                        if (m.HoldProgress >= m.HoldSeconds) _currentIndex++;
                    }
                    else
                    {
                        m.HoldProgress = 0f;
                    }
                    break;
            }

            // Check if we just completed the last mission in this phase
            if (_currentIndex >= _missions.Count)
            {
                if (Phase == MissionPhase.Outbound)
                    StartReturn();
                else if (Phase == MissionPhase.Returning)
                    StartNextRound();
            }
        }

        static float HashToFloat(int x, int z, int seed)
        {
            uint h = Hash(x, z, seed);
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }

        static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u;
                h += (uint)z * 668265263u;
                h += (uint)seed * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
