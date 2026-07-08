using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum CreatureBehavior { Patrol, Approach, Attack, Flee }

    public sealed class CreatureState
    {
        public int Id;
        public float X, Z, Depth;
        public float VelX, VelZ, VelDepth;   // for visual orientation
        public float Health = 100f;
        public CreatureBehavior Behavior;
        public bool IsDead;                   // Health <= 0; stays in list, ignored by logic

        // internal per-creature state (not part of the presentation contract)
        internal float WanderHeading;
        internal float AttackTimer;
        internal float FleeTimer;
    }

    public sealed class CreatureSystem
    {
        const float SpawnMinDistFromMapSpawn = 500f;
        const float SpawnBorderMargin = 150f;
        const float SpawnMinWaterColumn = 100f;
        const float SpawnBandEdgeMargin = 10f;

        const float MoveBorderMargin = 100f;
        const float DepthBandMargin = 20f;

        const float PatrolSpeed = 3f;
        const float ApproachSpeed = 8f;
        const float FleeSpeed = 10f;
        const float AttackLatchRate = 4f; // fraction of the gap closed per second while latched

        const float DetectionBaseRadius = 150f;
        const float DetectionNoiseScale = 600f;
        const float ActiveSonarAttractRadius = 900f;
        const float ApproachGiveUpDist = 1000f;
        const float AttackEnterDist = 15f;
        const float AttackGiveUpDist = 30f;

        const float FleeHealthThreshold = 30f;
        const float FleeDuration = 20f;
        const float BiteInterval = 2.5f;
        const float BiteMagnitude = 1.6f;

        const float WanderTurnRate = 25f; // deg/s max drift
        const float WanderLookAhead = 20f;
        const float WanderAvoidTurn = 150f;

        readonly List<CreatureState> _creatures = new();
        readonly int _seed;
        readonly MapData _map;
        int _tickCounter;

        public int CreatureCount = 6;

        public IReadOnlyList<CreatureState> Creatures => _creatures;

        public CreatureSystem(int seed, MapData map)
        {
            _seed = seed;
            _map = map;
            Spawn();
        }

        void Spawn()
        {
            _creatures.Clear();
            if (_map == null) return;

            float usableX = Math.Max(0f, _map.WorldSizeX - 2f * SpawnBorderMargin);
            float usableZ = Math.Max(0f, _map.WorldSizeZ - 2f * SpawnBorderMargin);

            int attempt = 0;
            int id = 0;
            while (_creatures.Count < CreatureCount && attempt < 8000)
            {
                float hx = HashToFloat(attempt, 501, _seed + 71011);
                float hz = HashToFloat(attempt, 502, _seed + 71011);
                float cx = SpawnBorderMargin + hx * usableX;
                float cz = SpawnBorderMargin + hz * usableZ;
                attempt++;

                float dxSpawn = cx - _map.SpawnX;
                float dzSpawn = cz - _map.SpawnZ;
                if (dxSpawn * dxSpawn + dzSpawn * dzSpawn < SpawnMinDistFromMapSpawn * SpawnMinDistFromMapSpawn)
                    continue;

                float floor = _map.GetFloorDepthAt(cx, cz);
                float ceiling = _map.GetCeilingDepthAt(cx, cz);
                float bandTop = ceiling > 0f ? ceiling : 0f;
                if (floor - bandTop < SpawnMinWaterColumn) continue;

                float lo = bandTop + SpawnBandEdgeMargin;
                float hi = floor - SpawnBandEdgeMargin;
                if (hi <= lo) continue;

                float mid = (lo + hi) * 0.5f;
                float dh = HashToFloat(attempt, 503, _seed + 71011);
                float depth = mid + (dh - 0.5f) * (hi - lo) * 0.3f;
                depth = Math.Clamp(depth, lo, hi);

                float dw = HashToFloat(id, 504, _seed + 71011);

                _creatures.Add(new CreatureState
                {
                    Id = id++,
                    X = cx,
                    Z = cz,
                    Depth = depth,
                    Health = 100f,
                    Behavior = CreatureBehavior.Patrol,
                    WanderHeading = dw * 360f,
                });
            }
        }

        public void Tick(float dt, SubmarineState sub, DamageSystem damage, TerrainModel terrain, bool activeSonarPinging, OceanCurrentField currents = null)
        {
            _tickCounter++;

            for (int i = 0; i < _creatures.Count; i++)
            {
                var c = _creatures[i];
                if (c.IsDead) continue;

                float dx = sub.PositionX - c.X;
                float dz = sub.PositionZ - c.Z;
                float dDepth = sub.Depth - c.Depth;
                float dist2D = (float)Math.Sqrt(dx * dx + dz * dz);
                float rangeMetric = dist2D + Math.Abs(dDepth); // "3D" measure per design: 2D dist + |depth delta|

                if (c.Health < FleeHealthThreshold && c.Behavior != CreatureBehavior.Flee)
                {
                    c.Behavior = CreatureBehavior.Flee;
                    c.FleeTimer = 0f;
                }

                switch (c.Behavior)
                {
                    case CreatureBehavior.Patrol:
                        TickPatrol(c, dt, terrain);
                        bool detected = dist2D < DetectionBaseRadius + sub.NoiseLevel * DetectionNoiseScale;
                        if (!detected && activeSonarPinging && dist2D < ActiveSonarAttractRadius)
                            detected = true;
                        if (detected)
                            c.Behavior = CreatureBehavior.Approach;
                        break;

                    case CreatureBehavior.Approach:
                        TickApproach(c, dt, dx, dz, dDepth);
                        if (rangeMetric <= AttackEnterDist)
                            c.Behavior = CreatureBehavior.Attack;
                        else if (dist2D > ApproachGiveUpDist)
                            c.Behavior = CreatureBehavior.Patrol;
                        break;

                    case CreatureBehavior.Attack:
                        TickAttack(c, dt, dx, dz, dDepth, damage);
                        if (rangeMetric > AttackGiveUpDist)
                            c.Behavior = CreatureBehavior.Approach;
                        break;

                    case CreatureBehavior.Flee:
                        TickFlee(c, dt, dx, dz, dDepth);
                        c.FleeTimer += dt;
                        if (c.FleeTimer >= FleeDuration)
                        {
                            c.Behavior = CreatureBehavior.Patrol;
                            c.FleeTimer = 0f;
                        }
                        break;
                }

                if (currents != null)
                {
                    currents.Sample(c.X, c.Z, c.Depth, out float curX, out float curZ);
                    c.X += curX * dt;
                    c.Z += curZ * dt;
                }

                ClampToWorldAndBand(c, terrain);
            }
        }

        void TickPatrol(CreatureState c, float dt, TerrainModel terrain)
        {
            float noise = HashToSigned(c.Id, _tickCounter, _seed + 90211);
            c.WanderHeading += noise * WanderTurnRate * dt;

            if (terrain != null)
            {
                float rad0 = c.WanderHeading * ((float)Math.PI / 180f);
                float lookX = c.X + (float)Math.Sin(rad0) * WanderLookAhead;
                float lookZ = c.Z + (float)Math.Cos(rad0) * WanderLookAhead;
                float floorAhead = terrain.GetFloorDepthAt(lookX, lookZ);
                float ceilingAhead = terrain.GetCeilingDepthAt(lookX, lookZ);
                bool blocked = c.Depth >= floorAhead - DepthBandMargin
                    || (ceilingAhead > 0f && c.Depth <= ceilingAhead + DepthBandMargin);
                if (blocked)
                    c.WanderHeading += WanderAvoidTurn + noise * 60f;
            }

            float rad = c.WanderHeading * ((float)Math.PI / 180f);
            float vx = (float)Math.Sin(rad) * PatrolSpeed;
            float vz = (float)Math.Cos(rad) * PatrolSpeed;
            c.X += vx * dt;
            c.Z += vz * dt;
            c.VelX = vx;
            c.VelZ = vz;
            c.VelDepth = 0f;
        }

        void TickApproach(CreatureState c, float dt, float dx, float dz, float dDepth)
        {
            float len = (float)Math.Sqrt(dx * dx + dz * dz + dDepth * dDepth);
            if (len < 0.01f) return;

            float vx = dx / len * ApproachSpeed;
            float vz = dz / len * ApproachSpeed;
            float vd = dDepth / len * ApproachSpeed;
            c.X += vx * dt;
            c.Z += vz * dt;
            c.Depth += vd * dt;
            c.VelX = vx;
            c.VelZ = vz;
            c.VelDepth = vd;
        }

        void TickAttack(CreatureState c, float dt, float dx, float dz, float dDepth, DamageSystem damage)
        {
            float t = Math.Min(1f, AttackLatchRate * dt);
            float vx = dx * t / Math.Max(dt, 1e-5f);
            float vz = dz * t / Math.Max(dt, 1e-5f);
            float vd = dDepth * t / Math.Max(dt, 1e-5f);
            c.X += dx * t;
            c.Z += dz * t;
            c.Depth += dDepth * t;
            c.VelX = vx;
            c.VelZ = vz;
            c.VelDepth = vd;

            c.AttackTimer += dt;
            if (c.AttackTimer >= BiteInterval)
            {
                c.AttackTimer = 0f;
                damage?.ApplyCreatureBite(BiteMagnitude);
            }
        }

        void TickFlee(CreatureState c, float dt, float dx, float dz, float dDepth)
        {
            float awayX = -dx, awayZ = -dz, awayD = -dDepth;
            float len = (float)Math.Sqrt(awayX * awayX + awayZ * awayZ + awayD * awayD);
            if (len < 0.01f) { awayX = 1f; awayZ = 0f; awayD = 0f; len = 1f; }

            float vx = awayX / len * FleeSpeed;
            float vz = awayZ / len * FleeSpeed;
            float vd = awayD / len * FleeSpeed;
            c.X += vx * dt;
            c.Z += vz * dt;
            c.Depth += vd * dt;
            c.VelX = vx;
            c.VelZ = vz;
            c.VelDepth = vd;
        }

        void ClampToWorldAndBand(CreatureState c, TerrainModel terrain)
        {
            if (_map != null)
            {
                c.X = Math.Clamp(c.X, MoveBorderMargin, Math.Max(MoveBorderMargin, _map.WorldSizeX - MoveBorderMargin));
                c.Z = Math.Clamp(c.Z, MoveBorderMargin, Math.Max(MoveBorderMargin, _map.WorldSizeZ - MoveBorderMargin));
            }

            if (terrain == null) return;
            float floor = terrain.GetFloorDepthAt(c.X, c.Z);
            float ceiling = terrain.GetCeilingDepthAt(c.X, c.Z);
            float bandMin = ceiling > 0f ? ceiling + DepthBandMargin : DepthBandMargin;
            float bandMax = floor - DepthBandMargin;
            if (bandMax < bandMin) bandMax = bandMin;
            c.Depth = Math.Clamp(c.Depth, bandMin, bandMax);
        }

        public void TakeDamage(int creatureId, float amount)
        {
            var c = Find(creatureId);
            if (c == null || c.IsDead) return;

            c.Health -= amount;
            if (c.Health <= 0f)
            {
                c.Health = 0f;
                c.IsDead = true;
                return;
            }

            c.Behavior = CreatureBehavior.Flee;
            c.FleeTimer = 0f;
        }

        CreatureState Find(int creatureId)
        {
            for (int i = 0; i < _creatures.Count; i++)
                if (_creatures[i].Id == creatureId) return _creatures[i];
            return null;
        }

        static float HashToFloat(int x, int z, int seed)
        {
            uint h = Hash(x, z, seed);
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }

        // Signed noise in [-1, 1], deterministic per (creatureId, tickCounter, seed).
        static float HashToSigned(int creatureId, int tickCounter, int seed)
        {
            return HashToFloat(creatureId, tickCounter, seed) * 2f - 1f;
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
