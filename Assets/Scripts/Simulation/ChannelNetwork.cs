using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public static class ChannelNetwork
    {
        public const float SectorSize = 1024f;
        public const float NodeJitterFraction = 0.3f;
        public const int SubdivisionsPerEdge = 6;
        public const float PolylineJitter = 20f;
        public const float ForceRadius = 60f;
        public const float BlendWidth = 40f;
        public const float InfluenceRadius = ForceRadius + BlendWidth;

        public static (float X, float Z) GetNode(int sx, int sz, int seed)
        {
            float centerX = (sx + 0.5f) * SectorSize;
            float centerZ = (sz + 0.5f) * SectorSize;
            float jx = (SimHash.HashToFloat(sx, sz, seed + 70001) - 0.5f) * 2f * NodeJitterFraction * SectorSize;
            float jz = (SimHash.HashToFloat(sx, sz, seed + 70002) - 0.5f) * 2f * NodeJitterFraction * SectorSize;
            return (centerX + jx, centerZ + jz);
        }

        public static float DistanceToNetwork(float wx, float wz, int seed)
        {
            int sx = (int)Math.Floor(wx / SectorSize);
            int sz = (int)Math.Floor(wz / SectorSize);

            float minDist = float.MaxValue;

            for (int dsx = -1; dsx <= 1; dsx++)
            {
                for (int dsz = -1; dsz <= 1; dsz++)
                {
                    int csx = sx + dsx;
                    int csz = sz + dsz;

                    // +X edge: from (csx,csz) to (csx+1,csz)
                    float d = DistanceToEdge(wx, wz, csx, csz, csx + 1, csz, 0, seed);
                    if (d < minDist) minDist = d;

                    // +Z edge: from (csx,csz) to (csx,csz+1)
                    d = DistanceToEdge(wx, wz, csx, csz, csx, csz + 1, 1, seed);
                    if (d < minDist) minDist = d;
                }
            }

            return minDist;
        }

        public static void GetNearbyNodes(float wx, float wz, int seed, List<(float X, float Z)> results)
        {
            results.Clear();
            int sx = (int)Math.Floor(wx / SectorSize);
            int sz = (int)Math.Floor(wz / SectorSize);

            for (int dsx = -1; dsx <= 1; dsx++)
            {
                for (int dsz = -1; dsz <= 1; dsz++)
                {
                    results.Add(GetNode(sx + dsx, sz + dsz, seed));
                }
            }
        }

        static float DistanceToEdge(float px, float pz, int sx0, int sz0, int sx1, int sz1, int axis, int seed)
        {
            var a = GetNode(sx0, sz0, seed);
            var b = GetNode(sx1, sz1, seed);

            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            float dirX = len > 0.001f ? dx / len : 1f;
            float dirZ = len > 0.001f ? dz / len : 0f;
            float perpX = -dirZ;
            float perpZ = dirX;

            // Edge id for hash: combine both sector coords and axis
            int edgeId = (int)(SimHash.Hash(sx0 * 1000003 + sx1, sz0 * 1000003 + sz1, axis + 90001));

            float prevX = a.X, prevZ = a.Z;
            float minDist = float.MaxValue;

            for (int s = 1; s <= SubdivisionsPerEdge; s++)
            {
                float t = (float)s / SubdivisionsPerEdge;
                float bx = a.X + dx * t;
                float bz = a.Z + dz * t;
                if (s < SubdivisionsPerEdge)
                {
                    float jitterT = (float)Math.Sin(t * Math.PI);
                    float jh = SimHash.HashToFloat(edgeId + s * 97, 13, seed + 8807) * 2f - 1f;
                    float offset = jh * PolylineJitter * jitterT;
                    bx += perpX * offset;
                    bz += perpZ * offset;
                }

                float d = DistanceToSegment(px, pz, prevX, prevZ, bx, bz);
                if (d < minDist) minDist = d;

                prevX = bx;
                prevZ = bz;
            }

            return minDist;
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
    }
}
