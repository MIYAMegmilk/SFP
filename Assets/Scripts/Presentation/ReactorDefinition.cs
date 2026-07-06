using UnityEngine;

namespace SFP.Presentation
{
    public class ReactorDefinition : MonoBehaviour
    {
        public float MaxPowerOutput = 2000f;
        public float InitialFissionRate = 0f;
        public float InitialTurbineOutput = 0f;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int ReactorIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(1.5f, 2f, 1f));
        }
    }
}
