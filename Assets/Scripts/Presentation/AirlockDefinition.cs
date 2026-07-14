using UnityEngine;

namespace SFP.Presentation
{
    public class AirlockDefinition : MonoBehaviour
    {
        public CompartmentDefinition Compartment;
        public OpeningDefinition InnerDoor;
        public OpeningDefinition OuterHatch;
        public OpeningDefinition FloodValve;
        public OpeningDefinition FloorHatch;
        public float PowerConsumption = 200f;
        [HideInInspector] public int AirlockIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }
}
