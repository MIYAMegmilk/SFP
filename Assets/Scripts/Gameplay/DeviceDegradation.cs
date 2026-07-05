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
        public bool IsFunctional => Condition > 0f;

        void Update()
        {
            if (Condition <= 0f) return;
            var bridge = SimulationBridge.Instance;
            if (bridge == null || Compartment == null) return;

            int id = bridge.GetCompartmentId(Compartment);
            if (id < 0) return;

            float waterY = bridge.GetInterpolatedWaterLevelY(id);
            bool submerged = transform.position.y < waterY;
            float rate = submerged ? SubmergedDegradation : NormalDegradation;
            Condition = Mathf.Max(0f, Condition - rate * Time.deltaTime);
        }
    }
}
