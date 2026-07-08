using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum MissionKind { ReachPoint, HoldPosition }

    public sealed class Mission
    {
        public MissionKind Kind;
        public float TargetX, TargetZ;
        public float Radius = 100f;
        public float HoldSeconds;        // HoldPosition only (e.g. 30)
        public float HoldProgress;
        public string Label;             // "Reach waypoint 3", "Survey site B"
    }

    public sealed class MissionSystem
    {
        const float HoldPositionSeconds = 30f;
        const float DeepPointMinFloor = 400f;
        const float DeepPointBorderMargin = 300f;
        const int DeepPointMaxAttempts = 4000;

        readonly List<Mission> _missions = new();
        int _currentIndex;

        public Mission Current => _currentIndex < _missions.Count ? _missions[_currentIndex] : null;
        public int CompletedCount => _currentIndex;
        public int TotalCount => _missions.Count;

        public MissionSystem(int seed, MapData map)
        {
            var waypoints = map?.ChannelWaypoints;
            if (waypoints != null)
            {
                for (int i = 1; i < waypoints.Count; i++)
                {
                    var wp = waypoints[i];
                    _missions.Add(new Mission
                    {
                        Kind = MissionKind.ReachPoint,
                        TargetX = wp.X,
                        TargetZ = wp.Z,
                        Label = $"Reach waypoint {i}",
                    });
                }
            }

            if (map != null)
            {
                var deep = FindDeepPoint(seed, map);
                _missions.Add(new Mission
                {
                    Kind = MissionKind.HoldPosition,
                    TargetX = deep.X,
                    TargetZ = deep.Z,
                    HoldSeconds = HoldPositionSeconds,
                    Label = "Survey site B",
                });
            }
        }

        public float DistanceToTarget(SubmarineState sub)
        {
            var m = Current;
            if (m == null || sub == null) return 0f;
            float dx = m.TargetX - sub.PositionX;
            float dz = m.TargetZ - sub.PositionZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public void Tick(float dt, SubmarineState sub)
        {
            var m = Current;
            if (m == null || sub == null) return;

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
