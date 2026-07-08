using UnityEngine;

namespace SFP.Presentation
{
    public class NavigationTerminalDefinition : MonoBehaviour
    {
        // 1 = standalone helm, 2/3 = fused nav+sonar console (see SonarDefinition.Tier).
        public int Tier = 1;
        public CompartmentDefinition Compartment;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, new Vector3(1f, 1.2f, 0.5f));
        }
    }
}
