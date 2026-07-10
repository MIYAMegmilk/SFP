namespace SFP.Simulation
{
    public sealed class BilgePumpState
    {
        public int PowerNodeId = -1;
        public int CompartmentId = -1;
        public bool IsActive;
        public bool IsFunctional = true;
        public float PumpRate = 1f;
        public float MaxPumpDepth = 800f;
        public float PowerConsumption = 50f;

        public void Tick(float dt, CompartmentGraph graph, ShallowWaterSystem water,
                         SubmarineState sub, PowerGrid power)
        {
            if (power != null && PowerNodeId >= 0)
            {
                var node = power.GetNode(PowerNodeId);
                if (node != null)
                {
                    node.IsEnabled = IsActive;
                    if (!node.IsActive) return;
                }
            }

            if (!IsActive || !IsFunctional) return;
            if (CompartmentId < 0) return;

            float depthFactor = 1f;
            if (sub != null)
            {
                depthFactor = 1f - sub.Depth / MaxPumpDepth;
                if (depthFactor < 0f) depthFactor = 0f;
                if (depthFactor > 1f) depthFactor = 1f;
            }
            if (depthFactor <= 0f) return;

            float voltageScale = 1f;
            if (power != null)
            {
                voltageScale = power.GridVoltage;
                if (voltageScale < 0f) voltageScale = 0f;
                if (voltageScale > 1f) voltageScale = 1f;
            }

            float effectiveRate = PumpRate * depthFactor * voltageScale;

            var grid = water?.GetGrid(CompartmentId);
            if (grid != null)
            {
                float total = grid.TotalVolume();
                if (total < 0.001f) return;
                float remove = effectiveRate * dt;
                if (remove > total) remove = total;
                grid.AddWaterUniform(-remove);
            }
            else if (graph != null)
            {
                var c = graph.GetCompartment(CompartmentId);
                float next = c.WaterVolume - effectiveRate * dt;
                c.WaterVolume = next > 0f ? next : 0f;
            }
        }
    }
}
