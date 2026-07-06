using UnityEngine;

namespace SFP.Presentation
{
    public class FabricatorDefinition : MonoBehaviour
    {
        public bool IsMedical;
        public float PowerConsumption = 80f;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int FabricatorIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = IsMedical ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawWireCube(transform.position, new Vector3(1f, 1.5f, 0.8f));
        }
    }
}
