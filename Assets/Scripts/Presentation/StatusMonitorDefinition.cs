using UnityEngine;

namespace SFP.Presentation
{
    public class StatusMonitorDefinition : MonoBehaviour
    {
        public CompartmentDefinition Compartment;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 0.6f, 0.1f));
        }
    }
}
