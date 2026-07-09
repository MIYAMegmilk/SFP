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
        public long PersistentId;
    }

    public sealed class MineSystem
    {
        public const float ActiveRadius = 768f;

        const int AcousticHashSeed = 90121;

        readonly int _seed;
        readonly ProceduralMapData _map;
        readonly Dictionary<long, MineState> _mineDict = new();
        readonly HashSet<long> _exploded = new();
        readonly List<MineState> _mines = new();

        ChunkCoord _lastCenter;
        bool _initialized;

        public IReadOnlyList<MineState> Mines => _mines;

        public MineSystem(int seed, ProceduralMapData map)
        {
            _seed = seed;
            _map = map;
        }

        public void Tick(float dt, SubmarineState sub, DamageSystem damage)
        {
            if (_map != null)
            {
                var center = ChunkCoord.FromWorld(sub.PositionX, sub.PositionZ);
                if (!_initialized || center.X != _lastCenter.X || center.Z != _lastCenter.Z)
                {
                    _initialized = true;
                    _lastCenter = center;
                    RebuildActiveSet(center);
                }
            }

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
                _exploded.Add(m.PersistentId);
                float distance = (float)Math.Sqrt(distSq);
                damage?.ApplyMineExplosion(distance);
            }
        }

        void RebuildActiveSet(ChunkCoord center)
        {
            // Mark all current entries for potential removal
            var keepIds = new HashSet<long>();
            var tempList = new List<(float X, float Z, float Depth)>();

            for (int dz = -3; dz <= 3; dz++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    var coord = new ChunkCoord(center.X + dx, center.Z + dz);
                    _map.GetMinesForChunk(coord, tempList);

                    for (int i = 0; i < tempList.Count; i++)
                    {
                        long persistentId = (coord.Key << 4) | (long)i;
                        if (_exploded.Contains(persistentId)) continue;

                        keepIds.Add(persistentId);

                        if (!_mineDict.ContainsKey(persistentId))
                        {
                            var mine = tempList[i];
                            _mineDict[persistentId] = new MineState
                            {
                                X = mine.X,
                                Z = mine.Z,
                                Depth = mine.Depth,
                                IsAcoustic = IsAcousticIndex(persistentId),
                                PersistentId = persistentId,
                            };
                        }
                    }
                }
            }

            // Remove entries that are no longer in the neighborhood
            var toRemove = new List<long>();
            foreach (var kvp in _mineDict)
            {
                if (!keepIds.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                _mineDict.Remove(toRemove[i]);

            // Rebuild the flat list
            _mines.Clear();
            foreach (var kvp in _mineDict)
                _mines.Add(kvp.Value);
        }

        static bool IsAcousticIndex(long persistentId)
        {
            uint h = Hash((int)(persistentId & 0xFFFFFFFF), (int)(persistentId >> 32), AcousticHashSeed);
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
