using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class Pump : MonoBehaviour
    {
        public CompartmentDefinition TargetCompartment;
        // m³/s. Fixed bilge pump in each compartment (portable pumps are 2×).
        public float PumpRate = 1f;
        public bool IsActive = true;
        public float MaxPumpDepth = 800f;
        public float PowerConsumption = 50f;

        int _powerNodeId = -1;

        void Start()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid != null)
            {
                var node = bridge.PowerGrid.AddNode(0f, PowerConsumption);
                _powerNodeId = node.Id;
            }
        }

        void Update()
        {
            if (!IsActive || TargetCompartment == null) return;
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            if (_powerNodeId >= 0)
            {
                var node = bridge.PowerGrid.GetNode(_powerNodeId);
                if (node != null)
                {
                    node.IsEnabled = IsActive;
                    if (!node.IsActive) return;
                }
            }

            if (TryGetComponent<DeviceDegradation>(out var deg) && !deg.IsFunctional)
                return;

            int id = bridge.GetCompartmentId(TargetCompartment);
            if (id < 0) return;

            float depthFactor = 1f;
            var sub = bridge.SubState;
            if (sub != null)
                depthFactor = Mathf.Clamp01(1f - sub.Depth / MaxPumpDepth);
            if (depthFactor <= 0f) return;

            float voltageScale = bridge.PowerGrid != null
                ? Mathf.Clamp01(bridge.PowerGrid.GridVoltage)
                : 1f;
            float effectiveRate = PumpRate * depthFactor * voltageScale;

            var grid = bridge.WaterSystem?.GetGrid(id);
            if (grid != null)
            {
                if (grid.TotalVolume() < 0.001f) return;
                float remove = Mathf.Min(effectiveRate * Time.deltaTime, grid.TotalVolume());
                grid.AddWaterUniform(-remove);
            }
            else
            {
                var c = bridge.Graph.GetCompartment(id);
                c.WaterVolume = Mathf.Max(0f, c.WaterVolume - effectiveRate * Time.deltaTime);
            }
        }
    }
}
