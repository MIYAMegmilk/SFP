using UnityEngine;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class Pump : MonoBehaviour
    {
        public CompartmentDefinition TargetCompartment;
        public float PumpRate = 0.1f;
        public bool IsActive = true;

        void Update()
        {
            if (!IsActive || TargetCompartment == null) return;
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            int id = bridge.GetCompartmentId(TargetCompartment);
            if (id < 0) return;

            var c = bridge.Graph.GetCompartment(id);
            c.WaterVolume = Mathf.Max(0f, c.WaterVolume - PumpRate * Time.deltaTime);
        }
    }
}
