using UnityEngine;

namespace SFP.Presentation
{
    public class NavigationTerminalDefinition : MonoBehaviour
    {
        public CompartmentDefinition Compartment;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, new Vector3(1f, 1.2f, 0.5f));
        }
    }
}
