using UnityEngine;

namespace SFP.Presentation
{
    public class CO2ScrubberDefinition : MonoBehaviour
    {
        public float ProcessRate = 1.0f;
        public float Efficiency = 0.95f;
        public float PowerConsumption = 60f;
        public CompartmentDefinition TargetCompartment;
        [HideInInspector] public int ScrubberIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.8f);
            Gizmos.DrawWireCube(transform.position, new Vector3(0.6f, 1.0f, 0.6f));
        }
    }
}
