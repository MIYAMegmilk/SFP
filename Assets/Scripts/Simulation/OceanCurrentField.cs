using System;

namespace SFP.Simulation
{
    /// Realistic ocean current field with layered structure:
    /// - Regional prevailing current (low-freq, map-scale direction)
    /// - Mesoscale eddies (2-5 km period, divergence-free curl noise)
    /// - Ekman spiral in surface layer (wind-driven, rotating with depth)
    /// - Deep thermohaline layer (separate direction, slow)
    /// - Tidal oscillation (semi-diurnal period)
    /// - Temporal drift (eddies shift over time)
    public sealed class OceanCurrentField
    {
        // --- Mesoscale eddies (curl noise, divergence-free) ---
        const float EddyFreq1 = 1f / 3000f;   // ~3 km period
        const float EddyAmp1 = 1.0f;           // m/s
        const float EddyFreq2 = 1f / 1200f;    // ~1.2 km period
        const float EddyAmp2 = 0.4f;           // m/s

        // --- Regional prevailing current ---
        const float RegionalFreq = 1f / 8000f; // direction varies over 8 km
        const float RegionalSpeed = 0.6f;      // m/s base speed

        // --- Ekman layer (wind-driven surface) ---
        const float EkmanDepth = 100f;         // e-folding depth (m)
        // Ekman spiral: current rotates 45° at surface, decays exponentially
        // In northern hemisphere, rotation is clockwise with depth
        const float EkmanSurfaceAngle = 0f;    // radians offset from wind at surface
        const float EkmanRotationRate = (float)(Math.PI / 2.0) / 100f; // rad/m — 90° per 100m

        // --- Deep thermohaline ---
        const float ThermoDepthStart = 300f;   // below this, thermohaline dominates
        const float ThermoDepthFull = 600f;    // fully thermohaline at this depth
        const float ThermoSpeed = 0.08f;       // m/s — slow deep current
        const float ThermoFreq = 1f / 10000f;  // varies direction over 10 km

        // --- Tidal oscillation ---
        // Semi-diurnal tide: real period 12.42h, game-compressed to ~12 min (720s)
        const float TidalPeriod = 720f;
        const float TidalAmplitude = 0.3f;     // m/s oscillation

        // --- Temporal drift ---
        const float DriftSpeed = 0.5f;         // eddies shift at 0.5 m/s (virtual advection)

        // --- Depth structure ---
        const float MaxDepth = 1000f;
        const float DeepFloor = 0.05f;         // minimum factor at max depth

        const float Eps = 1f;

        readonly int _seed;
        float _time;

        public OceanCurrentField(int seed)
        {
            _seed = seed;
        }

        // Max possible speed (for UI normalization): sum of all layer peaks
        public float MaxCurrentSpeed => EddyAmp1 + EddyAmp2 + RegionalSpeed + TidalAmplitude + 0.5f + ThermoSpeed;

        public void AdvanceTime(float dt)
        {
            _time += dt;
        }

        public float Time => _time;

        /// Full current at a world position and depth, including all layers.
        public void Sample(float x, float z, float depth, out float velX, out float velZ)
        {
            float vx = 0f, vz = 0f;

            // Temporal offset: eddies drift over time
            float tx = x + _time * DriftSpeed * 0.7f;
            float tz = z + _time * DriftSpeed * 0.3f;

            // 1) Regional prevailing current (large-scale directional flow)
            float regionAngle = RegionalDirection(x, z);
            float regionFactor = RegionalStrengthFactor(depth);
            vx += (float)Math.Cos(regionAngle) * RegionalSpeed * regionFactor;
            vz += (float)Math.Sin(regionAngle) * RegionalSpeed * regionFactor;

            // 2) Mesoscale eddies (depth-varying: use depth as 3rd noise dimension)
            float depthNoise = depth * 0.003f; // scales depth into noise space
            EddyCurl3D(tx, tz, depthNoise, EddyFreq1, EddyAmp1, _seed, out float e1x, out float e1z);
            EddyCurl3D(tx, tz, depthNoise, EddyFreq2, EddyAmp2, _seed + 7919, out float e2x, out float e2z);
            float eddyFactor = EddyDepthFactor(depth);
            vx += (e1x + e2x) * eddyFactor;
            vz += (e1z + e2z) * eddyFactor;

            // 3) Ekman spiral (surface layer only)
            if (depth < EkmanDepth * 4f)
            {
                EkmanLayer(x, z, depth, out float ekX, out float ekZ);
                vx += ekX;
                vz += ekZ;
            }

            // 4) Deep thermohaline (separate slow current below thermocline)
            if (depth > ThermoDepthStart)
            {
                ThermohalineLayer(x, z, depth, out float thX, out float thZ);
                vx += thX;
                vz += thZ;
            }

            // 5) Tidal oscillation (semi-diurnal, direction varies by location)
            TidalComponent(x, z, out float tidX, out float tidZ);
            float tidalDepthFactor = (float)Math.Max(0.2, 1.0 - depth / 400.0);
            vx += tidX * tidalDepthFactor;
            vz += tidZ * tidalDepthFactor;

            velX = vx;
            velZ = vz;
        }

        /// Raw surface current (no depth attenuation), for backward compat.
        public void SampleRaw(float x, float z, out float velX, out float velZ)
        {
            Sample(x, z, 0f, out velX, out velZ);
        }

        // --- Layer implementations ---

        float RegionalDirection(float x, float z)
        {
            // Very low frequency noise gives slowly varying direction across the map
            float n = ValueNoise2D(x * RegionalFreq, z * RegionalFreq, _seed + 31337);
            return n * (float)(Math.PI * 2.0);
        }

        float RegionalStrengthFactor(float depth)
        {
            // Regional current strongest at 50-200m (below wave mixing, above thermocline)
            if (depth < 50f) return (float)(0.5 + 0.5 * depth / 50.0);
            if (depth < 200f) return 1f;
            return (float)Math.Max(0.1, 1.0 - (depth - 200.0) / 600.0);
        }

        void EkmanLayer(float x, float z, float depth, out float vx, out float vz)
        {
            // Wind direction varies slowly across map
            float windAngle = ValueNoise2D(x * (1f / 6000f), z * (1f / 6000f), _seed + 4217)
                * (float)(Math.PI * 2.0);

            // Ekman spiral: speed decays exponentially, direction rotates with depth
            float decay = (float)Math.Exp(-depth / EkmanDepth);
            float rotation = EkmanRotationRate * depth;
            float angle = windAngle + EkmanSurfaceAngle + rotation;

            // Surface wind-driven current ~0.5 m/s at surface
            float speed = 0.5f * decay;
            vx = (float)Math.Cos(angle) * speed;
            vz = (float)Math.Sin(angle) * speed;
        }

        void ThermohalineLayer(float x, float z, float depth, out float vx, out float vz)
        {
            // Blend factor: 0 at ThermoDepthStart, 1 at ThermoDepthFull
            float blend = (float)Math.Min(1.0, (depth - ThermoDepthStart) / (ThermoDepthFull - ThermoDepthStart));

            // Deep current direction (different noise seed, very low frequency)
            float angle = ValueNoise2D(x * ThermoFreq, z * ThermoFreq, _seed + 99991)
                * (float)(Math.PI * 2.0);

            // Thermohaline can oppose surface current — this is intentional
            float speed = ThermoSpeed * blend;
            vx = (float)Math.Cos(angle) * speed;
            vz = (float)Math.Sin(angle) * speed;
        }

        void TidalComponent(float x, float z, out float vx, out float vz)
        {
            // Tidal direction varies by location (coastal topography effect)
            float tidalAngle = ValueNoise2D(x * (1f / 4000f), z * (1f / 4000f), _seed + 55555)
                * (float)(Math.PI * 2.0);

            // Semi-diurnal oscillation
            float phase = _time * (float)(2.0 * Math.PI / TidalPeriod);
            float strength = (float)Math.Sin(phase) * TidalAmplitude;

            vx = (float)Math.Cos(tidalAngle) * strength;
            vz = (float)Math.Sin(tidalAngle) * strength;
        }

        // Eddy strength varies with depth — strong at surface, weakens below 400m
        static float EddyDepthFactor(float depth)
        {
            if (depth < 200f) return 1f;
            return (float)Math.Max(0.15, 1.0 - (depth - 200.0) / 600.0);
        }

        // --- 3D curl noise for depth-varying eddies ---
        // Uses depth as a third noise dimension so eddy patterns genuinely differ between layers.
        // Curl is taken in the XZ plane: vel = (dPhi/dz, -dPhi/dx) where Phi = noise(x, z, d).
        void EddyCurl3D(float x, float z, float d, float freq, float amplitude, int seed,
            out float velX, out float velZ)
        {
            float phiXp = ValueNoise3D((x + Eps) * freq, z * freq, d, seed);
            float phiXm = ValueNoise3D((x - Eps) * freq, z * freq, d, seed);
            float phiZp = ValueNoise3D(x * freq, (z + Eps) * freq, d, seed);
            float phiZm = ValueNoise3D(x * freq, (z - Eps) * freq, d, seed);

            float scale = amplitude / freq;
            float gradX = (phiXp - phiXm) / (2f * Eps) * scale;
            float gradZ = (phiZp - phiZm) / (2f * Eps) * scale;

            velX = gradZ;
            velZ = -gradX;
        }

        // --- Noise utilities ---

        static float ValueNoise3D(float x, float z, float d, int seed)
        {
            int x0 = (int)Math.Floor(x);
            int z0 = (int)Math.Floor(z);
            int d0 = (int)Math.Floor(d);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            int d1 = d0 + 1;
            float tx = Smoothstep(x - x0);
            float tz = Smoothstep(z - z0);
            float td = Smoothstep(d - d0);

            float n000 = HashToFloat3D(x0, z0, d0, seed);
            float n100 = HashToFloat3D(x1, z0, d0, seed);
            float n010 = HashToFloat3D(x0, z1, d0, seed);
            float n110 = HashToFloat3D(x1, z1, d0, seed);
            float n001 = HashToFloat3D(x0, z0, d1, seed);
            float n101 = HashToFloat3D(x1, z0, d1, seed);
            float n011 = HashToFloat3D(x0, z1, d1, seed);
            float n111 = HashToFloat3D(x1, z1, d1, seed);

            float nx00 = Lerp(n000, n100, tx);
            float nx10 = Lerp(n010, n110, tx);
            float nx01 = Lerp(n001, n101, tx);
            float nx11 = Lerp(n011, n111, tx);

            float nz0 = Lerp(nx00, nx10, tz);
            float nz1 = Lerp(nx01, nx11, tz);

            return Lerp(nz0, nz1, td);
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

        public static float ComputeDepthFactor(float depth)
        {
            // Legacy helper for external use. Now depth handling is internal per-layer.
            if (depth < 200f) return 1f;
            return (float)Math.Max(DeepFloor, 1.0 - (depth - 200.0) / 800.0);
        }

        static float HashToFloat(int x, int z, int seed)
        {
            uint h = WangHash(x, z, seed);
            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }

        static float HashToFloat3D(int x, int z, int d, int seed)
        {
            uint h = WangHash3D(x, z, d, seed);
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

        static uint WangHash3D(int x, int z, int d, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u;
                h += (uint)z * 668265263u;
                h += (uint)d * 2654435761u;
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
