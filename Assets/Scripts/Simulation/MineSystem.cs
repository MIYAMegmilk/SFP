using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class MineState
    {
        public float X, Z, Depth;
        public float TriggerRadius = 25f;
        public float BaseTriggerRadius = 25f;
        public bool IsAcoustic;
        public bool Exploded;
    }

    public sealed class MineSystem
    {
        readonly List<MineState> _mines = new();

        const int AcousticHashSeed = 90121; // fixed: MineSystem has no seed param

        public IReadOnlyList<MineState> Mines => _mines;

        public void AddMine(float x, float z, float depth)
        {
            int index = _mines.Count;
            _mines.Add(new MineState
            {
                X = x,
                Z = z,
                Depth = depth,
                IsAcoustic = IsAcousticIndex(index),
            });
        }

        public void Tick(float dt, SubmarineState sub, DamageSystem damage)
        {
            for (int i = 0; i < _mines.Count; i++)
            {
                var m = _mines[i];
                if (m.Exploded) continue;

                float effectiveRadius = m.IsAcoustic
                    ? 15f + sub.NoiseLevel * 120f
                    : m.BaseTriggerRadius;
                m.TriggerRadius = effectiveRadius;

                float dx = sub.PositionX - m.X;
                float dz = sub.PositionZ - m.Z;
                float dy = sub.Depth - m.Depth;
                float distSq = dx * dx + dz * dz + dy * dy;
                if (distSq >= effectiveRadius * effectiveRadius) continue;

                m.Exploded = true;
                float distance = (float)Math.Sqrt(distSq);
                damage?.ApplyMineExplosion(distance);
            }
        }

        static bool IsAcousticIndex(int index)
        {
            uint h = Hash(index, 0, AcousticHashSeed);
            return (h % 100u) < 40u;
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
