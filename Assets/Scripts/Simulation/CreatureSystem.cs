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
        const float SpawnRadiusMin = 400f;
        const float SpawnRadiusMax = 700f;
        const float DespawnRadius = 1200f;
        const float SpawnCooldown = 5f;
        const float SpawnMinWaterColumn = 100f;
        const float SpawnBandEdgeMargin = 10f;

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
        readonly ProceduralMapData _map;
        int _tickCounter;

        public int MaxActiveCreatures = 6;
        float _spawnTimer;
        int _spawnCounter;
        int _nextId;

        public IReadOnlyList<CreatureState> Creatures => _creatures;

        public CreatureSystem(int seed, ProceduralMapData map)
        {
            _seed = seed;
            _map = map;
        }

        public void Tick(float dt, SubmarineState sub, DamageSystem damage, TerrainModel terrain, bool activeSonarPinging, OceanCurrentField currents = null)
        {
            _tickCounter++;

            // Despawn pass: remove creatures too far from the sub
            for (int i = _creatures.Count - 1; i >= 0; i--)
            {
                var c = _creatures[i];
                float dx = sub.PositionX - c.X;
                float dz = sub.PositionZ - c.Z;
                float dist2D = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist2D > DespawnRadius)
                {
                    _creatures.RemoveAt(i);
                }
            }

            // Spawn pass: try to spawn new creatures near the sub
            _spawnTimer += dt;
            if (_spawnTimer >= SpawnCooldown && _creatures.Count < MaxActiveCreatures && _map != null)
            {
                _spawnTimer = 0f;

                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float hAngle = HashToFloat(_spawnCounter, 601, _seed + 71011);
                    _spawnCounter++;
                    float hRadius = HashToFloat(_spawnCounter, 602, _seed + 71011);
                    _spawnCounter++;

                    float angle = hAngle * 2f * (float)Math.PI;
                    float r = SpawnRadiusMin + hRadius * (SpawnRadiusMax - SpawnRadiusMin);
                    float cx = sub.PositionX + (float)Math.Cos(angle) * r;
                    float cz = sub.PositionZ + (float)Math.Sin(angle) * r;

                    float floor = _map.GetFloorDepthAt(cx, cz);
                    float ceiling = _map.GetCeilingDepthAt(cx, cz);
                    float bandTop = ceiling > 0f ? ceiling : 0f;
                    if (floor - bandTop < SpawnMinWaterColumn) continue;

                    float lo = bandTop + SpawnBandEdgeMargin;
                    float hi = floor - SpawnBandEdgeMargin;
                    if (hi <= lo) continue;

                    float mid = (lo + hi) * 0.5f;
                    float dh = HashToFloat(_spawnCounter, 603, _seed + 71011);
                    _spawnCounter++;
                    float depth = mid + (dh - 0.5f) * (hi - lo) * 0.3f;
                    depth = Math.Clamp(depth, lo, hi);

                    float dw = HashToFloat(_nextId, 504, _seed + 71011);

                    _creatures.Add(new CreatureState
                    {
                        Id = _nextId++,
                        X = cx,
                        Z = cz,
                        Depth = depth,
                        Health = 100f,
                        Behavior = CreatureBehavior.Patrol,
                        WanderHeading = dw * 360f,
                    });
                    break;
                }
            }

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

                ClampToDepthBand(c, terrain);
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

        void ClampToDepthBand(CreatureState c, TerrainModel terrain)
        {
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
