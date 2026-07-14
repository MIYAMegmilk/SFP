namespace SFP.Simulation
{
    /// ADCP (Acoustic Doppler Current Profiler) — measures ocean current velocity
    /// at multiple depth bins using Doppler shift of acoustic pulses.
    public sealed class ADCPState
    {
        public int PowerNodeId = -1;
        public float PowerConsumption = 50f;
        public bool IsActive = true;

        // Measurement output: current velocity at the ship's position
        public float MeasuredVelX;
        public float MeasuredVelZ;
        public float MeasuredSpeed;
        public float MeasuredBearing;

        // Depth-bin profile (bins from surface down to max range)
        public const int BinCount = 8;
        public float MaxRange = 600f;
        public float[] BinVelX = new float[BinCount];
        public float[] BinVelZ = new float[BinCount];
        public float[] BinSpeed = new float[BinCount];
        public float[] BinDepth = new float[BinCount];

        // History ring buffer for tidal pattern visualization
        public const int HistorySize = 120;
        // Record interval in seconds (720s tidal period / 120 samples = 6s per sample)
        const float RecordInterval = 6f;
        public float[] HistorySpeed = new float[HistorySize];
        public float[] HistoryBearing = new float[HistorySize];
        public float[] HistoryVelX = new float[HistorySize];
        public float[] HistoryVelZ = new float[HistorySize];
        public int HistoryHead;
        public int HistoryCount;
        float _recordTimer;

        // Tidal prediction: estimated period and next reversal time
        public float EstimatedTidalPeriod;
        public float TimeSinceLastReversal;
        float _prevBearing;
        float _reversalAccumulator;
        int _reversalCount;

        public void Tick(float dt, OceanCurrentField field, float shipX, float shipZ, float shipDepth, PowerGrid power)
        {
            bool hasPower = true;
            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node != null) node.Consumption = PowerConsumption;
                hasPower = node != null && node.IsActive;
            }

            if (!IsActive || !hasPower)
            {
                MeasuredVelX = 0f;
                MeasuredVelZ = 0f;
                MeasuredSpeed = 0f;
                MeasuredBearing = 0f;
                for (int i = 0; i < BinCount; i++)
                {
                    BinVelX[i] = 0f;
                    BinVelZ[i] = 0f;
                    BinSpeed[i] = 0f;
                }
                return;
            }

            // Sample current at ship position
            field.Sample(shipX, shipZ, shipDepth, out float vx, out float vz);
            MeasuredVelX = vx;
            MeasuredVelZ = vz;
            MeasuredSpeed = (float)System.Math.Sqrt(vx * vx + vz * vz);
            MeasuredBearing = (float)(System.Math.Atan2(vx, vz) * (180.0 / System.Math.PI));
            if (MeasuredBearing < 0f) MeasuredBearing += 360f;

            // Profile: sample at depth bins from shipDepth down to shipDepth + MaxRange
            float binSize = MaxRange / BinCount;
            for (int i = 0; i < BinCount; i++)
            {
                float binDepth = shipDepth + binSize * (i + 0.5f);
                BinDepth[i] = binDepth;
                field.Sample(shipX, shipZ, binDepth, out float bvx, out float bvz);
                BinVelX[i] = bvx;
                BinVelZ[i] = bvz;
                BinSpeed[i] = (float)System.Math.Sqrt(bvx * bvx + bvz * bvz);
            }

            // History recording
            _recordTimer += dt;
            if (_recordTimer >= RecordInterval)
            {
                _recordTimer = 0f;
                HistorySpeed[HistoryHead] = MeasuredSpeed;
                HistoryBearing[HistoryHead] = MeasuredBearing;
                HistoryVelX[HistoryHead] = MeasuredVelX;
                HistoryVelZ[HistoryHead] = MeasuredVelZ;
                HistoryHead = (HistoryHead + 1) % HistorySize;
                if (HistoryCount < HistorySize) HistoryCount++;
            }

            // Tidal reversal detection
            TimeSinceLastReversal += dt;
            float bearingDelta = MeasuredBearing - _prevBearing;
            if (bearingDelta > 180f) bearingDelta -= 360f;
            if (bearingDelta < -180f) bearingDelta += 360f;
            // A reversal: large bearing change (>90°) accumulated over history
            if (System.Math.Abs(bearingDelta) > 2f && HistoryCount > 2)
            {
                // Check if bearing has flipped ~180° from 10 samples ago
                int oldIdx = (HistoryHead - 10 + HistorySize) % HistorySize;
                if (HistoryCount >= 10)
                {
                    float oldBearing = HistoryBearing[oldIdx];
                    float angleDiff = MeasuredBearing - oldBearing;
                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;
                    if (System.Math.Abs(angleDiff) > 120f && TimeSinceLastReversal > 60f)
                    {
                        _reversalAccumulator += TimeSinceLastReversal;
                        _reversalCount++;
                        if (_reversalCount > 0)
                            EstimatedTidalPeriod = (_reversalAccumulator / _reversalCount) * 2f;
                        TimeSinceLastReversal = 0f;
                    }
                }
            }
            _prevBearing = MeasuredBearing;
        }
    }
}
