using UnityEngine;

namespace SFP.Presentation
{
    public class ADCPDefinition : MonoBehaviour
    {
        public float PowerConsumption = 50f;
        public float MaxRange = 600f;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int ADCPIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }
}
