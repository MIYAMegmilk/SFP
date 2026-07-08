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
        const float DeepPointBorderMargin = 300f;
        const int DeepPointMaxAttempts = 4000;
        const int WaypointsPerRound = 3;

        readonly MapData _map;
        readonly float _baseX, _baseZ;
        int _seed;

        readonly List<Mission> _missions = new();
        int _currentIndex;

        public MissionPhase Phase { get; private set; } = MissionPhase.Outbound;
        public int Round { get; private set; } = 1;
        public Mission Current => _currentIndex < _missions.Count ? _missions[_currentIndex] : null;
        public int CompletedCount => _currentIndex;
        public int TotalCount => _missions.Count;

        public MissionSystem(int seed, MapData map)
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
            var allWaypoints = _map.ChannelWaypoints;
            if (allWaypoints == null || allWaypoints.Count <= 1) return new List<(float, float)>();

            // Exclude spawn (index 0), shuffle the rest deterministically per round
            var candidates = new List<(float X, float Z)>();
            for (int i = 1; i < allWaypoints.Count; i++)
                candidates.Add(allWaypoints[i]);

            // Fisher-Yates with hash-based determinism
            int shuffleSeed = _seed + Round * 7919;
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = (int)(Hash(i, Round, shuffleSeed) % (uint)(i + 1));
                var tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
            }

            int count = Math.Min(WaypointsPerRound, candidates.Count);

            // On even rounds, try to place the last waypoint at a deep point for survey
            if (Round % 2 == 0)
            {
                var deep = FindDeepPoint(shuffleSeed, _map);
                if (count > 0)
                    candidates[count - 1] = deep;
            }

            return candidates.GetRange(0, count);
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

        static (float X, float Z) FindDeepPoint(int seed, MapData map)
        {
            float usableX = map.WorldSizeX - 2f * DeepPointBorderMargin;
            float usableZ = map.WorldSizeZ - 2f * DeepPointBorderMargin;
            if (usableX <= 0f || usableZ <= 0f) return (map.SpawnX, map.SpawnZ);

            (float X, float Z) best = (map.SpawnX, map.SpawnZ);
            float bestFloor = float.MinValue;
            for (int attempt = 0; attempt < DeepPointMaxAttempts; attempt++)
            {
                float hx = HashToFloat(attempt, 71, seed + 90071);
                float hz = HashToFloat(attempt, 72, seed + 90071);
                float mx = DeepPointBorderMargin + hx * usableX;
                float mz = DeepPointBorderMargin + hz * usableZ;

                float floor = map.GetFloorDepthAt(mx, mz);
                if (floor > bestFloor)
                {
                    bestFloor = floor;
                    best = (mx, mz);
                }
                if (floor >= DeepPointMinFloor) return (mx, mz);
            }
            return best;
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
