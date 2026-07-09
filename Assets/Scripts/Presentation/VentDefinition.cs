using UnityEngine;

namespace SFP.Presentation
{
    public class VentDefinition : MonoBehaviour
    {
        public CompartmentDefinition CompartmentA;
        public CompartmentDefinition CompartmentB;
        public float DuctArea = 0.1f;
        public float FanFlowRate = 1.5f;
        public float PowerConsumption = 25f;
        [HideInInspector] public int VentIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.6f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }
}
