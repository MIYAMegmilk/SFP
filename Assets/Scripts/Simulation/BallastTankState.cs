namespace SFP.Simulation
{
    // External main ballast tank (saddle tank outside the pressure hull). Its water adds to
    // the boat's mass via SubmarineState.ExternalBallastVolume but never enters the
    // compartment graph — interior flooding stays a damage-control problem only.
    public sealed class BallastTankState
    {
        public int PowerNodeId = -1;
        // One-compartment standard: full blow from neutral (50%) must exceed the largest
        // compartment (216 m³), so 2 tanks × 240 m³ × 0.5 = 240 m³ of reserve buoyancy.
        public float Capacity = 240f;
        public float TargetFillLevel = 0.5f;
        // Tank fraction per second. 0.1/s floods/vents a full tank in 10 s — an emergency
        // MBT blow on a real boat completes in seconds to tens of seconds.
        public float PumpRate = 0.1f;
        public float PowerConsumption = 40f;
        public float CurrentFillLevel = 0.5f;

        public float WaterVolume => CurrentFillLevel * Capacity;

        public void Tick(float dt, PowerGrid power)
        {
            float diff = TargetFillLevel - CurrentFillLevel;
            if (System.Math.Abs(diff) < 0.001f) return;

            float powerEff = 1f;
            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
                powerEff = power.GridVoltage > 1f ? 1f : power.GridVoltage;
            }

            float step = PumpRate * powerEff * dt;
            if (diff > 0f)
                CurrentFillLevel = System.Math.Min(CurrentFillLevel + step, TargetFillLevel);
            else
                CurrentFillLevel = System.Math.Max(CurrentFillLevel - step, TargetFillLevel);
        }
    }
}
