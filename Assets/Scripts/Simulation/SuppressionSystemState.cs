namespace SFP.Simulation
{
    public sealed class SuppressionSystemState
    {
        public int PowerNodeId = -1;
        public int TargetCompartmentId = -1;
        public float ExtinguishRate = 0.5f;
        public float WaterReserve = 100f;
        public float PowerConsumption = 30f;
        public bool IsActive;

        public void Tick(float dt, FireSystem fire, PowerGrid power)
        {
            if (!IsActive || TargetCompartmentId < 0 || WaterReserve <= 0f) return;

            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
            }

            float intensity = fire.GetFireIntensity(TargetCompartmentId);
            if (intensity <= 0f) return;

            float suppress = ExtinguishRate * dt;
            fire.Extinguish(TargetCompartmentId, suppress);
            WaterReserve -= suppress * 10f;
            if (WaterReserve < 0f) WaterReserve = 0f;
        }
    }
}
