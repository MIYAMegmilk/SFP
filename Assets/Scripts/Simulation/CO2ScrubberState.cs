namespace SFP.Simulation
{
    public sealed class CO2ScrubberState
    {
        public int PowerNodeId = -1;
        public int TargetCompartmentId = -1;
        // m³/s of room air processed through the sorbent bed
        public float ProcessRate = 1.0f;
        // Fraction of CO2 removed from processed air
        public float Efficiency = 0.95f;
        // Ambient CO2 floor (400 ppm)
        public float MinCo2 = 0.0004f;
        public bool IsEnabled = true;

        public void Tick(float dt, CompartmentGraph graph, AtmosphereSystem atmo, PowerGrid power)
        {
            if (!IsEnabled || TargetCompartmentId < 0 || atmo == null) return;

            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
            }

            var comp = graph.GetCompartment(TargetCompartmentId);
            if (comp == null) return;

            float co2 = atmo.GetCo2Level(TargetCompartmentId);
            if (co2 <= MinCo2) return;

            float airVol = comp.AirVolume;
            if (airVol <= 0f) return;

            // Exponential decay: dCO2 = -co2 * (ProcessRate * Efficiency / AirVolume) * dt
            float removal = co2 * (ProcessRate * Efficiency / airVol) * dt;
            float newCo2 = co2 - removal;
            if (newCo2 < MinCo2) newCo2 = MinCo2;
            atmo.SetCo2Level(TargetCompartmentId, newCo2);
        }
    }
}
