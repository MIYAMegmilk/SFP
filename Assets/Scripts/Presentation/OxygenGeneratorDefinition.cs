using UnityEngine;

namespace SFP.Presentation
{
    public class OxygenGeneratorDefinition : MonoBehaviour
    {
        public float ProductionRate = 0.05f;
        public float PowerConsumption = 80f;
        public CompartmentDefinition TargetCompartment;
        [HideInInspector] public int GeneratorIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 1.2f, 0.6f));
        }
    }
}
