using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class TurretDefinition : MonoBehaviour
    {
        public TurretType Type = TurretType.Coilgun;
        public float PowerConsumption = 150f;
        public int InitialAmmo = 50;
        public CompartmentDefinition Compartment;
        [HideInInspector] public int TurretIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(transform.position, new Vector3(1f, 1f, 2f));
        }
    }
}
