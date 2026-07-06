using UnityEngine;

namespace SFP.Presentation
{
    public class EngineDefinition : MonoBehaviour
    {
        public float MaxThrust = 50000f;
        public float PowerConsumption = 200f;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int EngineIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireCube(transform.position, new Vector3(1.5f, 1.5f, 2f));
        }
    }
}
