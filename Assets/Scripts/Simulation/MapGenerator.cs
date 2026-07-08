using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public static class MapGenerator
    {
        const float BaseFreq = 1f / 420f;
        const float RidgeFreq = 1f / 600f;
        const float BorderWidth = 120f;
        const float SpawnRadius = 120f;
        const float SpawnDepth = 450f;

        // caves / ceiling
        const float CaveFreq = 1f / 550f;

        // guaranteed channel network
        const float ChannelMinSpacing = 400f;
        const float ChannelBorderMargin = 150f;
        const float ChannelJitter = 20f;
        const int ChannelSubdivisions = 6;
        const float ChannelForceRadius = 60f;
        const float ChannelBlendWidth = 40f;

        // mines
        const int MineCount = 24;
        const float MineMinSpawnDist = 350f;
        const float MineBorderMargin = 150f;

        public static MapData Generate(int seed, float worldSize = 2000f, float cellSize = 8f)
        {
            int cells = Math.Max(2, (int)Math.Round(worldSize / cellSize));
            var map = new MapData
            {
                CellsX = cells,
                CellsZ = cells,
                CellSize = cellSize,
                OriginX = 0f,
                OriginZ = 0f,
                Seed = seed,
                FloorDepth = new float[cells * cells],
                CeilingDepth = new float[cells * cells],
                SpawnX = worldSize * 0.3f,
                SpawnZ = worldSize * 0.5f,
            };

            var waypoints = GenerateChannelWaypoints(seed, worldSize, map.SpawnX, map.SpawnZ);
            var channelSegments = BuildChannelSegments(waypoints, seed);

            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    float wx = x * cellSize;
                    float wz = z * cellSize;

                    float baseNoise = Fbm(wx * BaseFreq, wz * BaseFreq, seed, 4);
                    float depth = 350f + baseNoise * 250f;

                    float ridge = RidgedFbm(wx * RidgeFreq, wz * RidgeFreq, seed + 9973, 4);
                    if (ridge > 0.55f)
                    {
                        float t = Smoothstep(Clamp01((ridge - 0.55f) / 0.45f));
                        float peakDepth = 150f - t * 90f;
                        depth = Lerp(depth, peakDepth, t);
                    }

                    float distToEdge = Math.Min(Math.Min(wx, worldSize - wx), Math.Min(wz, worldSize - wz));
                    if (distToEdge < BorderWidth)
                    {
                        float t = Smoothstep(Clamp01(1f - distToEdge / BorderWidth));
                        depth = Lerp(depth, -50f, t);
                    }

                    float dx = wx - map.SpawnX;
                    float dz = wz - map.SpawnZ;
                    float distToSpawn = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (distToSpawn < SpawnRadius)
                    {
                        float t = Smoothstep(Clamp01(1f - distToSpawn / SpawnRadius));
                        float basinDepth = Math.Max(depth, SpawnDepth);
                        depth = Lerp(depth, basinDepth, t);
                    }

                    // Ceiling rock (caves/canyons/tunnels): only where the seabed is actually
                    // open water (border walls already block, keep CeilingDepth = 0 there).
                    float ceiling = 0f;
                    if (depth > 0f)
                    {
                        float caveNoise = Fbm(wx * CaveFreq, wz * CaveFreq, seed + 55555, 4);
                        if (caveNoise > 0.55f)
                        {
                            float t = Smoothstep(Clamp01((caveNoise - 0.55f) / 0.45f));
                            float gapNoise = Fbm(wx * CaveFreq * 1.3f, wz * CaveFreq * 1.3f, seed + 66666, 3);
                            float gap = Lerp(40f, 90f, gapNoise);
                            float ceilingTarget = Math.Max(0f, depth - gap);
                            ceiling = Lerp(0f, ceilingTarget, t);
                        }
                    }

                    // Guaranteed channel network: force clear water near the waypoint polyline.
                    float chanDist = float.MaxValue;
                    for (int i = 0; i < channelSegments.Count; i++)
                    {
                        var seg = channelSegments[i];
                        float d = DistanceToSegment(wx, wz, seg.AX, seg.AZ, seg.BX, seg.BZ);
                        if (d < chanDist) chanDist = d;
                    }
                    if (chanDist < ChannelForceRadius + ChannelBlendWidth)
                    {
                        float ft = chanDist <= ChannelForceRadius
                            ? 1f
                            : Smoothstep(Clamp01(1f - (chanDist - ChannelForceRadius) / ChannelBlendWidth));
                        float forcedFloor = Math.Max(depth, 350f);
                        depth = Lerp(depth, forcedFloor, ft);
                        ceiling = Lerp(ceiling, 0f, ft);
                    }

                    map.FloorDepth[z * cells + x] = depth;
                    map.CeilingDepth[z * cells + x] = ceiling;
                }
            }

            map.ChannelWaypoints = waypoints;
            map.MineSpots = GenerateMines(map, seed, worldSize);

            return map;
        }

        static List<(float X, float Z)> GenerateChannelWaypoints(int seed, float worldSize, float spawnX, float spawnZ)
        {
            var pts = new List<(float X, float Z)> { (spawnX, spawnZ) };
            int count = 4 + (int)(Hash(0, 0, seed + 31337) % 3);
            float usable = worldSize - 2f * ChannelBorderMargin;
            if (usable <= 0f) return pts;

            int attempt = 0;
            while (pts.Count < count && attempt < 2000)
            {
                float hx = HashToFloat(attempt, 0, seed + 4001);
                float hz = HashToFloat(attempt, 1, seed + 4001);
                float cx = ChannelBorderMargin + hx * usable;
                float cz = ChannelBorderMargin + hz * usable;
                attempt++;

                bool ok = true;
                for (int i = 0; i < pts.Count; i++)
                {
                    float dx = cx - pts[i].X;
                    float dz = cz - pts[i].Z;
                    if (dx * dx + dz * dz < ChannelMinSpacing * ChannelMinSpacing) { ok = false; break; }
                }
                if (ok) pts.Add((cx, cz));
            }
            return pts;
        }

        static List<(float AX, float AZ, float BX, float BZ)> BuildChannelSegments(List<(float X, float Z)> waypoints, int seed)
        {
            var segs = new List<(float AX, float AZ, float BX, float BZ)>();
            for (int w = 1; w < waypoints.Count; w++)
            {
                var a = waypoints[w - 1];
                var b = waypoints[w];
                float dx = b.X - a.X;
                float dz = b.Z - a.Z;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                float dirX = len > 0.001f ? dx / len : 1f;
                float dirZ = len > 0.001f ? dz / len : 0f;
                float perpX = -dirZ;
                float perpZ = dirX;

                float px = a.X, pz = a.Z;
                for (int s = 1; s <= ChannelSubdivisions; s++)
                {
                    float t = (float)s / ChannelSubdivisions;
                    float bx = a.X + dx * t;
                    float bz = a.Z + dz * t;
                    if (s < ChannelSubdivisions)
                    {
                        float jitterT = (float)Math.Sin(t * Math.PI);
                        float jh = HashToFloat(w * 97 + s, 13, seed + 8807) * 2f - 1f;
                        float offset = jh * ChannelJitter * jitterT;
                        bx += perpX * offset;
                        bz += perpZ * offset;
                    }
                    segs.Add((px, pz, bx, bz));
                    px = bx; pz = bz;
                }
            }
            return segs;
        }

        static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax;
            float dz = bz - az;
            float lenSq = dx * dx + dz * dz;
            float t = lenSq > 0.0001f ? ((px - ax) * dx + (pz - az) * dz) / lenSq : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            float cx = ax + dx * t;
            float cz = az + dz * t;
            float ex = px - cx;
            float ez = pz - cz;
            return (float)Math.Sqrt(ex * ex + ez * ez);
        }

        static List<(float X, float Z, float Depth)> GenerateMines(MapData map, int seed, float worldSize)
        {
            var mines = new List<(float X, float Z, float Depth)>();
            float usable = worldSize - 2f * MineBorderMargin;
            if (usable <= 0f) return mines;

            int attempt = 0;
            while (mines.Count < MineCount && attempt < 6000)
            {
                float hx = HashToFloat(attempt, 51, seed + 60013);
                float hz = HashToFloat(attempt, 52, seed + 60013);
                float mx = MineBorderMargin + hx * usable;
                float mz = MineBorderMargin + hz * usable;
                attempt++;

                float dxSpawn = mx - map.SpawnX;
                float dzSpawn = mz - map.SpawnZ;
                if (dxSpawn * dxSpawn + dzSpawn * dzSpawn < MineMinSpawnDist * MineMinSpawnDist) continue;

                float floor = map.GetFloorDepthAt(mx, mz);
                if (floor < 150f) continue;

                float ceiling = map.GetCeilingDepthAt(mx, mz);
                float minDepth = 80f;
                if (ceiling > 0f) minDepth = Math.Max(minDepth, ceiling + 20f);
                float maxDepth = floor - 40f;
                if (maxDepth <= minDepth) continue;

                float dh = HashToFloat(attempt, 53, seed + 60013);
                float depth = Lerp(minDepth, maxDepth, dh);
                mines.Add((mx, mz, depth));
            }
            return mines;
        }

        static float Fbm(float x, float z, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise2D(x * freq, z * freq, seed + i * 101) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        static float RidgedFbm(float x, float z, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = ValueNoise2D(x * freq, z * freq, seed + i * 101);
                float ridge = 1f - Math.Abs(2f * n - 1f);
                ridge *= ridge;
                sum += ridge * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        static float ValueNoise2D(float x, float z, int seed)
        {
            int x0 = (int)Math.Floor(x);
            int z0 = (int)Math.Floor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = Smoothstep(x - x0);
            float tz = Smoothstep(z - z0);

            float n00 = HashToFloat(x0, z0, seed);
            float n10 = HashToFloat(x1, z0, seed);
            float n01 = HashToFloat(x0, z1, seed);
            float n11 = HashToFloat(x1, z1, seed);

            float nx0 = Lerp(n00, n10, tx);
            float nx1 = Lerp(n01, n11, tx);
            return Lerp(nx0, nx1, tz);
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

        static float Smoothstep(float t)
        {
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        static float Clamp01(float t)
        {
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
