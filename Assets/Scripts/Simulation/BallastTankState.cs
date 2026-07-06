namespace SFP.Simulation
{
    public sealed class BallastTankState
    {
        public int PowerNodeId = -1;
        public int CompartmentId = -1;
        public float TargetFillLevel;
        public float PumpRate = 0.3f;
        public float PowerConsumption = 40f;

        public float CurrentFillLevel { get; private set; }

        public void Tick(float dt, CompartmentGraph graph, PowerGrid power)
        {
            if (CompartmentId < 0) return;

            var comp = graph.GetCompartment(CompartmentId);
            if (comp == null) return;

            CurrentFillLevel = comp.WaterFraction;
            float diff = TargetFillLevel - CurrentFillLevel;
            if (System.Math.Abs(diff) < 0.005f) return;

            float powerEff = 1f;
            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
                powerEff = power.GridVoltage > 1f ? 1f : power.GridVoltage;
            }

            float pumpAmount = PumpRate * powerEff * dt;

            if (diff > 0f)
            {
                float toAdd = diff < pumpAmount ? diff * comp.Volume : pumpAmount;
                comp.WaterVolume += toAdd;
                if (comp.WaterVolume > comp.Volume)
                    comp.WaterVolume = comp.Volume;
            }
            else
            {
                float toRemove = -diff < pumpAmount ? -diff * comp.Volume : pumpAmount;
                comp.WaterVolume -= toRemove;
                if (comp.WaterVolume < 0f) comp.WaterVolume = 0f;
            }
        }
    }
}
