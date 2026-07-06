using UnityEngine;

namespace SFP.Presentation
{
    public class BallastTankDefinition : MonoBehaviour
    {
        public float PumpRate = 0.3f;
        public float PowerConsumption = 40f;
        public CompartmentDefinition BallastCompartment;
        [HideInInspector] public int BallastIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 0.5f, 1f);
            Gizmos.DrawWireCube(transform.position, new Vector3(2f, 1f, 3f));
        }
    }
}
