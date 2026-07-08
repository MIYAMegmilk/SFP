using System;

namespace SFP.Simulation
{
    public sealed class OceanCurrentField
    {
        const float Freq1 = 1f / 500f;
        const float Amp1 = 1.5f;
        const float Freq2 = 1f / 200f;
        const float Amp2 = 0.6f;

        const float Eps = 1f;
        const float DepthFalloff = 800f;
        const float BorderRange = 100f;

        readonly int _seed;
        readonly float _worldSize;

        public OceanCurrentField(int seed, float worldSize)
        {
            _seed = seed;
            _worldSize = worldSize;
        }

        public float MaxCurrentSpeed => Amp1 + Amp2;

        // Full current at a world position, including depth attenuation and border damping.
        public void Sample(float x, float z, float depth, out float velX, out float velZ)
        {
            SampleRaw(x, z, out float vx, out float vz);

            float depthFactor = ComputeDepthFactor(depth);
            float borderFactor = ComputeBorderFactor(x, z, _worldSize);

            velX = vx * depthFactor * borderFactor;
            velZ = vz * depthFactor * borderFactor;
        }

        // Curl-noise current before depth/border shaping (still clamped to MaxCurrentSpeed).
        public void SampleRaw(float x, float z, out float velX, out float velZ)
        {
            OctaveCurl(x, z, Freq1, Amp1, _seed, out float vx1, out float vz1);
            OctaveCurl(x, z, Freq2, Amp2, _seed + 7919, out float vx2, out float vz2);

            float vx = vx1 + vx2;
            float vz = vz1 + vz2;

            float mag = (float)Math.Sqrt(vx * vx + vz * vz);
            if (mag > Amp1 + Amp2 && mag > 0f)
            {
                float clamp = (Amp1 + Amp2) / mag;
                vx *= clamp;
                vz *= clamp;
            }

            velX = vx;
            velZ = vz;
        }

        public static float ComputeDepthFactor(float depth)
        {
            return (float)Math.Max(0.1, 1.0 - depth / DepthFalloff);
        }

        public static float ComputeBorderFactor(float x, float z, float worldSize)
        {
            float distToEdge = Math.Min(Math.Min(x, worldSize - x), Math.Min(z, worldSize - z));
            float t = (float)Math.Min(1.0, distToEdge / BorderRange);
            return t < 0f ? 0f : t;
        }

        // Divergence-free flow: current = (dPhi/dz, -dPhi/dx) for scalar potential Phi.
        static void OctaveCurl(float x, float z, float freq, float amplitude, int seed, out float velX, out float velZ)
        {
            float phiXp = ValueNoise2D((x + Eps) * freq, z * freq, seed);
            float phiXm = ValueNoise2D((x - Eps) * freq, z * freq, seed);
            float phiZp = ValueNoise2D(x * freq, (z + Eps) * freq, seed);
            float phiZm = ValueNoise2D(x * freq, (z - Eps) * freq, seed);

            // Raw finite differences over Eps world-meters are tiny relative to the noise
            // period (1/freq); rescale by amplitude/freq to land in the target amplitude.
            float scale = amplitude / freq;
            float gradX = (phiXp - phiXm) / (2f * Eps) * scale;
            float gradZ = (phiZp - phiZm) / (2f * Eps) * scale;

            velX = gradZ;
            velZ = -gradX;
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
            uint h = WangHash(x, z, seed);
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }

        static uint WangHash(int x, int z, int seed)
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

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
