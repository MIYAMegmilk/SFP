using UnityEngine;

namespace SFP.Presentation
{
    public class JunctionBoxDefinition : MonoBehaviour
    {
        public float MaxLoad = 500f;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int JunctionBoxIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.6f, 0.8f, 0.3f));
        }
    }
}
