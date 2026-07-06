using UnityEngine;

namespace SFP.Presentation
{
    public class SuppressionSystemDefinition : MonoBehaviour
    {
        public float ExtinguishRate = 0.5f;
        public float WaterReserve = 100f;
        public float PowerConsumption = 30f;
        public CompartmentDefinition TargetCompartment;
        [HideInInspector] public int SuppressionIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f);
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
}
