using UnityEngine;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class DeviceDegradation : MonoBehaviour
    {
        public CompartmentDefinition Compartment;
        public float Condition = 100f;
        public float NormalDegradation = 0.01f;
        public float SubmergedDegradation = 0.5f;
        public float FireDamageRate = 3f;
        public float RepairRate = 5f;
        public bool IsFunctional => Condition > 0f;

        int _powerNodeId = -1;

        public void SetPowerNodeId(int id) => _powerNodeId = id;

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || Compartment == null) return;

            int id = bridge.GetCompartmentId(Compartment);
            if (id < 0) return;

            float waterY = bridge.GetInterpolatedWaterLevelY(id);
            bool submerged = transform.position.y < waterY;

            if (Condition > 0f)
            {
                float rate = submerged ? SubmergedDegradation : NormalDegradation;
                float fi = bridge.FireSystem?.GetFireIntensity(id) ?? 0f;
                if (fi > 0f) rate += FireDamageRate * fi;
                Condition = Mathf.Max(0f, Condition - rate * Time.deltaTime);
            }

            if (_powerNodeId >= 0 && bridge.PowerGrid != null)
            {
                var node = bridge.PowerGrid.GetNode(_powerNodeId);
                if (node != null)
                    node.IsEnabled = IsFunctional;
            }
        }
    }
}
