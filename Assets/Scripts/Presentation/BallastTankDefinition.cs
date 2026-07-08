using UnityEngine;

namespace SFP.Presentation
{
    public class BallastTankDefinition : MonoBehaviour
    {
        // External MBT parameters — see BallastTankState (fraction/s, m³).
        public float PumpRate = 0.1f;
        public float Capacity = 240f;
        public float InitialFillLevel = 0.5f;
        public float PowerConsumption = 40f;
        [HideInInspector] public int BallastIndex = -1;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 0.5f, 1f);
            Gizmos.DrawWireCube(transform.position, new Vector3(2f, 1f, 3f));
        }
    }
}
