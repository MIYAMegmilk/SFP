using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class OpeningDefinition : MonoBehaviour
    {
        public OpeningKind Kind = OpeningKind.Door;
        public CompartmentDefinition CompartmentA;
        public CompartmentDefinition CompartmentB;
        public float Area = 1f;
        public float Height = 2f;
        public bool IsOpen = true;
        public bool IsGasSealed;
        [HideInInspector] public int SimIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = IsOpen ? Color.green : Color.red;
            Gizmos.DrawWireCube(transform.position, new Vector3(Area / Height, Height, 0.1f));
        }
    }
}
