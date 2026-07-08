using UnityEngine;

namespace SFP.Presentation
{
    public class SonarDefinition : MonoBehaviour
    {
        public float ActiveRange = 500f;
        public float PowerConsumption = 100f;
        // 1 = standalone 2D sonar, 2 = fused nav+sonar console, 3 = fused + 3D hologram.
        public int Tier = 1;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int SonarIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}
