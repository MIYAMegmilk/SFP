using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class CrewSpawnDefinition : MonoBehaviour
    {
        public CrewJobKind Job = CrewJobKind.Captain;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}
