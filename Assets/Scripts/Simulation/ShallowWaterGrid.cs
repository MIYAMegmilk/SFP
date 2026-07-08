using System;

namespace SFP.Simulation
{
    public sealed class ShallowWaterGrid
    {
        public readonly int Id;
        public readonly int ResX, ResZ;
        public readonly float CellSize;
        public readonly float FloorY, RoomHeight;
        public readonly float OriginX, OriginZ;

        public readonly float[] H;
        public readonly float[] FluxL, FluxR, FluxF, FluxB;
        public readonly float[] VelX, VelZ;

        const float Gravity = 9.81f;
        const float MinH = 0.0001f;

        public ShallowWaterGrid(int id, float floorY, float roomHeight,
            float originX, float originZ, float lengthX, float widthZ, float cellSize)
        {
            Id = id;
            CellSize = cellSize;
            FloorY = floorY;
            RoomHeight = roomHeight;
            OriginX = originX;
            OriginZ = originZ;

            ResX = Math.Max(1, (int)Math.Round(lengthX / cellSize));
            ResZ = Math.Max(1, (int)Math.Round(widthZ / cellSize));

            int n = ResX * ResZ;
            H = new float[n];
            FluxL = new float[n];
            FluxR = new float[n];
            FluxF = new float[n];
            FluxB = new float[n];
            VelX = new float[n];
            VelZ = new float[n];
            OpeningMask = new bool[n];
        }

        public readonly bool[] OpeningMask;

        public void MarkOpeningCell(int x, int z)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nx < ResX && nz >= 0 && nz < ResZ)
                        OpeningMask[Idx(nx, nz)] = true;
                }
            }
        }

        public int CellCount => ResX * ResZ;
        public int Idx(int x, int z) => x * ResZ + z;

        public float TotalVolume()
        {
            float area = CellSize * CellSize;
            float sum = 0f;
            for (int i = 0; i < H.Length; i++)
                sum += H[i];
            return sum * area;
        }

        public float MaxHeight()
        {
            float max = 0f;
            for (int i = 0; i < H.Length; i++)
                if (H[i] > max) max = H[i];
            return max;
        }

        public void ZeroAll()
        {
            Array.Clear(H, 0, H.Length);
            Array.Clear(FluxL, 0, FluxL.Length);
            Array.Clear(FluxR, 0, FluxR.Length);
            Array.Clear(FluxF, 0, FluxF.Length);
            Array.Clear(FluxB, 0, FluxB.Length);
            Array.Clear(VelX, 0, VelX.Length);
            Array.Clear(VelZ, 0, VelZ.Length);
        }

        public float AverageHeight()
        {
            float sum = 0f;
            for (int i = 0; i < H.Length; i++)
                sum += H[i];
            return sum / H.Length;
        }

        public void AddWaterUniform(float volume)
        {
            if (volume < 0f)
            {
                float totalH = 0f;
                for (int i = 0; i < H.Length; i++) totalH += H[i];
                if (totalH <= 0f) return;

                float cellArea = CellSize * CellSize;
                float removeH = Math.Min(-volume / cellArea, totalH) / H.Length;
                for (int i = 0; i < H.Length; i++)
                    H[i] = Math.Max(0f, H[i] - removeH);
                return;
            }

            float dh = volume / (ResX * ResZ * CellSize * CellSize);
            for (int i = 0; i < H.Length; i++)
                H[i] = Math.Max(0f, H[i] + dh);
        }

        public void AddWaterAtCell(int x, int z, float volume)
        {
            float area = CellSize * CellSize;
            int idx = Idx(x, z);
            H[idx] = Math.Max(0f, H[idx] + volume / area);
        }

        public void UpdateFluxInternal(float dt, float damping)
        {
            float maxFluxAccel = CellSize * CellSize * 0.5f / dt;

            for (int x = 0; x < ResX; x++)
            {
                for (int z = 0; z < ResZ; z++)
                {
                    int i = Idx(x, z);
                    float h = H[i];

                    if (x > 0)
                    {
                        float hn = H[Idx(x - 1, z)];
                        float hm = Math.Min((h + hn) * 0.5f, 1.5f);
                        float accel = dt * Gravity * (h - hn) * hm;
                        accel = Math.Clamp(accel, -maxFluxAccel, maxFluxAccel);
                        FluxL[i] = Math.Max(0f, FluxL[i] + accel);
                    }
                    else
                    {
                        FluxL[i] = 0f;
                    }

                    if (x < ResX - 1)
                    {
                        float hn = H[Idx(x + 1, z)];
                        float hm = Math.Min((h + hn) * 0.5f, 1.5f);
                        float accel = dt * Gravity * (h - hn) * hm;
                        accel = Math.Clamp(accel, -maxFluxAccel, maxFluxAccel);
                        FluxR[i] = Math.Max(0f, FluxR[i] + accel);
                    }
                    else
                    {
                        FluxR[i] = 0f;
                    }

                    if (z > 0)
                    {
                        float hn = H[Idx(x, z - 1)];
                        float hm = Math.Min((h + hn) * 0.5f, 1.5f);
                        float accel = dt * Gravity * (h - hn) * hm;
                        accel = Math.Clamp(accel, -maxFluxAccel, maxFluxAccel);
                        FluxB[i] = Math.Max(0f, FluxB[i] + accel);
                    }
                    else
                    {
                        FluxB[i] = 0f;
                    }

                    if (z < ResZ - 1)
                    {
                        float hn = H[Idx(x, z + 1)];
                        float hm = Math.Min((h + hn) * 0.5f, 1.5f);
                        float accel = dt * Gravity * (h - hn) * hm;
                        accel = Math.Clamp(accel, -maxFluxAccel, maxFluxAccel);
                        FluxF[i] = Math.Max(0f, FluxF[i] + accel);
                    }
                    else
                    {
                        FluxF[i] = 0f;
                    }

                    float damp = 1f - damping * dt;
                    FluxL[i] *= damp;
                    FluxR[i] *= damp;
                    FluxF[i] *= damp;
                    FluxB[i] *= damp;
                }
            }
        }

        public void ScaleFlux(float dt)
        {
            float cellArea = CellSize * CellSize;
            for (int i = 0; i < H.Length; i++)
            {
                float totalOut = (FluxL[i] + FluxR[i] + FluxF[i] + FluxB[i]) * dt;
                float available = H[i] * cellArea;
                if (totalOut > available && totalOut > 1e-10f)
                {
                    float scale = available / totalOut;
                    FluxL[i] *= scale;
                    FluxR[i] *= scale;
                    FluxF[i] *= scale;
                    FluxB[i] *= scale;
                }
            }
        }

        public void ApplyFlux(float dt)
        {
            float invArea = 1f / (CellSize * CellSize);
            for (int x = 0; x < ResX; x++)
            {
                for (int z = 0; z < ResZ; z++)
                {
                    int i = Idx(x, z);
                    float inflow = 0f;

                    if (x > 0) inflow += FluxR[Idx(x - 1, z)];
                    if (x < ResX - 1) inflow += FluxL[Idx(x + 1, z)];
                    if (z > 0) inflow += FluxF[Idx(x, z - 1)];
                    if (z < ResZ - 1) inflow += FluxB[Idx(x, z + 1)];

                    float outflow = FluxL[i] + FluxR[i] + FluxF[i] + FluxB[i];
                    float dv = dt * (inflow - outflow);
                    H[i] = Math.Max(0f, H[i] + dv * invArea);

                    float maxH = RoomHeight;
                    if (H[i] > maxH) H[i] = maxH;
                }
            }
        }

        public void ComputeVelocity()
        {
            for (int x = 0; x < ResX; x++)
            {
                for (int z = 0; z < ResZ; z++)
                {
                    int i = Idx(x, z);
                    float h = H[i];
                    if (h < MinH)
                    {
                        VelX[i] = 0f;
                        VelZ[i] = 0f;
                        continue;
                    }

                    float fxIn = x > 0 ? FluxR[Idx(x - 1, z)] : 0f;
                    float fxOut = FluxR[i];
                    float fxInR = x < ResX - 1 ? FluxL[Idx(x + 1, z)] : 0f;
                    float fxOutL = FluxL[i];

                    float fzIn = z > 0 ? FluxF[Idx(x, z - 1)] : 0f;
                    float fzOut = FluxF[i];
                    float fzInB = z < ResZ - 1 ? FluxB[Idx(x, z + 1)] : 0f;
                    float fzOutB = FluxB[i];

                    float denom = h * CellSize;
                    VelX[i] = (fxIn - fxOutL + fxOut - fxInR) / (2f * denom);
                    VelZ[i] = (fzIn - fzOutB + fzOut - fzInB) / (2f * denom);
                }
            }
        }

        public void SampleFlow(float worldX, float worldZ, out float velX, out float velZ, out float waterHeight)
        {
            float gx = (worldX - OriginX) / CellSize - 0.5f;
            float gz = (worldZ - OriginZ) / CellSize - 0.5f;

            gx = Math.Clamp(gx, 0f, ResX - 1);
            gz = Math.Clamp(gz, 0f, ResZ - 1);

            int x0 = (int)Math.Floor(gx);
            int z0 = (int)Math.Floor(gz);
            int x1 = Math.Min(x0 + 1, ResX - 1);
            int z1 = Math.Min(z0 + 1, ResZ - 1);

            float tx = gx - x0;
            float tz = gz - z0;

            int i00 = Idx(x0, z0);
            int i10 = Idx(x1, z0);
            int i01 = Idx(x0, z1);
            int i11 = Idx(x1, z1);

            velX = Bilerp(VelX[i00], VelX[i10], VelX[i01], VelX[i11], tx, tz);
            velZ = Bilerp(VelZ[i00], VelZ[i10], VelZ[i01], VelZ[i11], tx, tz);
            waterHeight = Bilerp(H[i00], H[i10], H[i01], H[i11], tx, tz);
        }

        static float Bilerp(float v00, float v10, float v01, float v11, float tx, float tz)
        {
            float a = v00 + (v10 - v00) * tx;
            float b = v01 + (v11 - v01) * tx;
            return a + (b - a) * tz;
        }
    }
}
