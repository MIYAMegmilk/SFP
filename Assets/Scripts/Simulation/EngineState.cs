namespace SFP.Simulation
{
    public sealed class EngineState
    {
        public int PowerNodeId = -1;
        public int CompartmentId = -1;
        public float MaxThrust = 50000f;
        public float ThrottleSetting;
        public float PowerConsumption = 200f;

        public float CurrentThrust { get; private set; }

        public void Tick(float dt, PowerGrid power)
        {
            float powerEff = 1f;
            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive)
                {
                    CurrentThrust = 0f;
                    return;
                }
                powerEff = power.GridVoltage > 1f ? 1f : power.GridVoltage;
            }

            CurrentThrust = MaxThrust * ThrottleSetting * powerEff;
        }
    }
}
