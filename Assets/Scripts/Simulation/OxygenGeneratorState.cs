namespace SFP.Simulation
{
    public sealed class OxygenGeneratorState
    {
        public int PowerNodeId = -1;
        public int TargetCompartmentId = -1;
        public float ProductionRate = 0.05f;
        public bool IsEnabled = true;

        public void Tick(float dt, AtmosphereSystem atmosphere, PowerGrid power)
        {
            if (!IsEnabled || TargetCompartmentId < 0) return;

            if (PowerNodeId >= 0)
            {
                var node = power?.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
            }

            atmosphere.AddOxygen(TargetCompartmentId, ProductionRate * dt);
        }
    }
}
