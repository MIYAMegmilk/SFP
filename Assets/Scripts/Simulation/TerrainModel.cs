using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum TerrainHit { Free, Floor, Ceiling }

    public sealed class TerrainModel
    {
        public float SeaFloorDepth = 600f;
        public ProceduralMapData Map;

        public struct Obstacle
        {
            public float X, Z, Radius, TopDepth;
        }

        readonly List<Obstacle> _obstacles = new();

        public void AddObstacle(float x, float z, float radius, float topDepth)
        {
            _obstacles.Add(new Obstacle { X = x, Z = z, Radius = radius, TopDepth = topDepth });
        }

        public float GetFloorDepthAt(float x, float z)
        {
            float floor = Map != null ? Map.GetFloorDepthAt(x, z) : SeaFloorDepth;
            for (int i = 0; i < _obstacles.Count; i++)
            {
                var o = _obstacles[i];
                float dx = x - o.X;
                float dz = z - o.Z;
                if (dx * dx + dz * dz <= o.Radius * o.Radius)
                    floor = Math.Min(floor, o.TopDepth);
            }
            return floor;
        }

        public float GetCeilingDepthAt(float x, float z)
        {
            return Map != null ? Map.GetCeilingDepthAt(x, z) : 0f;
        }

        public TerrainHit Probe(float x, float z, float depth)
        {
            float floor = GetFloorDepthAt(x, z);
            if (depth >= floor) return TerrainHit.Floor;

            float ceiling = GetCeilingDepthAt(x, z);
            if (Map != null && ceiling > 0f && depth <= ceiling) return TerrainHit.Ceiling;

            return TerrainHit.Free;
        }
    }
}
