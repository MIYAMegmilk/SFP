using UnityEngine;

namespace SFP.Presentation
{
    public class BatteryDefinition : MonoBehaviour
    {
        public float MaxCharge = 1000f;
        public float InitialCharge = 500f;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int BatteryIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.3f, 1f);
            Gizmos.DrawWireCube(transform.position, new Vector3(1f, 0.6f, 0.5f));
        }
    }
}
