using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public static class MapGenerator
    {
        const float BaseFreq = 1f / 420f;
        const float RidgeFreq = 1f / 600f;
        const float SpawnRadius = 120f;
        const float SpawnDepth = 450f;
        const float CaveFreq = 1f / 550f;
        const float MineMinSpawnDist = 350f;

        public static void SampleTerrain(int seed, float wx, float wz,
                                         out float floorDepth, out float ceilingDepth)
        {
            // 1. Base depth: fBm
            float baseNoise = Fbm(wx * BaseFreq, wz * BaseFreq, seed, 4);
            float depth = 350f + baseNoise * 250f;

            // 2. Ridged peaks
            float ridge = RidgedFbm(wx * RidgeFreq, wz * RidgeFreq, seed + 9973, 4);
            if (ridge > 0.55f)
            {
                float t = Smoothstep(Clamp01((ridge - 0.55f) / 0.45f));
                float peakDepth = 150f - t * 90f;
                depth = Lerp(depth, peakDepth, t);
            }

            // 3. Border falloff — deleted (infinite world, no edges)

            // 4. Spawn basin: around channel network sector (0,0) node
            var spawn = GetSpawnPoint(seed);
            float dx = wx - spawn.X;
            float dz = wz - spawn.Z;
            float distToSpawn = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distToSpawn < SpawnRadius)
            {
                float t = Smoothstep(Clamp01(1f - distToSpawn / SpawnRadius));
                float basinDepth = Math.Max(depth, SpawnDepth);
                depth = Lerp(depth, basinDepth, t);
            }

            // 5. Ceiling cave noise
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

            // 6. Channel carve via ChannelNetwork
            float chanDist = ChannelNetwork.DistanceToNetwork(wx, wz, seed);
            if (chanDist < ChannelNetwork.ForceRadius + ChannelNetwork.BlendWidth)
            {
                float ft = chanDist <= ChannelNetwork.ForceRadius
                    ? 1f
                    : Smoothstep(Clamp01(1f - (chanDist - ChannelNetwork.ForceRadius) / ChannelNetwork.BlendWidth));
                float forcedFloor = Math.Max(depth, 350f);
                depth = Lerp(depth, forcedFloor, ft);
                ceiling = Lerp(ceiling, 0f, ft);
            }

            floorDepth = depth;
            ceilingDepth = ceiling;
        }

        public static float SampleFloorDepth(int seed, float wx, float wz)
        {
            SampleTerrain(seed, wx, wz, out float floor, out _);
            return floor;
        }

        public static float SampleCeilingDepth(int seed, float wx, float wz)
        {
            SampleTerrain(seed, wx, wz, out _, out float ceiling);
            return ceiling;
        }

        public static TerrainChunk GenerateChunk(int seed, ChunkCoord coord)
        {
            int vps = TerrainChunk.VertsPerSide;
            var chunk = new TerrainChunk
            {
                Coord = coord,
                FloorDepth = new float[vps * vps],
                CeilingDepth = new float[vps * vps],
                HasCeiling = false
            };

            float ox = coord.OriginX;
            float oz = coord.OriginZ;

            for (int vz = 0; vz < vps; vz++)
            {
                for (int vx = 0; vx < vps; vx++)
                {
                    float wx = ox + vx * MapConstants.CellSize;
                    float wz = oz + vz * MapConstants.CellSize;
                    SampleTerrain(seed, wx, wz, out float floor, out float ceiling);
                    int idx = vz * vps + vx;
                    chunk.FloorDepth[idx] = floor;
                    chunk.CeilingDepth[idx] = ceiling;
                    if (ceiling > 0.01f) chunk.HasCeiling = true;
                }
            }

            return chunk;
        }

        public static void GenerateMinesForChunk(int seed, ChunkCoord coord, ProceduralMapData map,
                                                  List<(float X, float Z, float Depth)> results)
        {
            results.Clear();

            int cx = coord.X;
            int cz = coord.Z;

            // ~0.4 probability per chunk
            if (SimHash.HashToFloat(cx, cz, seed + 60013) >= 0.4f) return;

            var spawn = GetSpawnPoint(seed);
            float chunkOriginX = coord.OriginX;
            float chunkOriginZ = coord.OriginZ;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float hx = SimHash.HashToFloat(cx * 1000 + attempt, cz, seed + 60014);
                float hz = SimHash.HashToFloat(cx, cz * 1000 + attempt, seed + 60015);
                float mx = chunkOriginX + hx * MapConstants.ChunkSize;
                float mz = chunkOriginZ + hz * MapConstants.ChunkSize;

                float dxSpawn = mx - spawn.X;
                float dzSpawn = mz - spawn.Z;
                if (dxSpawn * dxSpawn + dzSpawn * dzSpawn < MineMinSpawnDist * MineMinSpawnDist) continue;

                float floor = map.GetFloorDepthAt(mx, mz);
                if (floor < 150f) continue;

                float ceilingVal = map.GetCeilingDepthAt(mx, mz);
                float minDepth = 80f;
                if (ceilingVal > 0f) minDepth = Math.Max(minDepth, ceilingVal + 20f);
                float maxDepth = floor - 40f;
                if (maxDepth <= minDepth) continue;

                float dh = SimHash.HashToFloat(cx + attempt * 73, cz + attempt * 37, seed + 60016);
                float depth = Lerp(minDepth, maxDepth, dh);
                results.Add((mx, mz, depth));
                return; // one mine per chunk
            }
        }

        public static (float X, float Z) GetSpawnPoint(int seed)
        {
            return ChannelNetwork.GetNode(0, 0, seed);
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

        // Keep private copies of hash/math helpers to avoid churn in existing callers
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
