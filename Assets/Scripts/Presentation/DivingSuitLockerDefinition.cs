using UnityEngine;

namespace SFP.Presentation
{
    public class DivingSuitLockerDefinition : MonoBehaviour
    {
        public int SuitCount = 2;
        public CompartmentDefinition Compartment;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 1.8f, 0.5f));
        }
    }
}
