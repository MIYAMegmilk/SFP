using UnityEngine;

namespace SFP.Presentation
{
    public class CompartmentDefinition : MonoBehaviour
    {
        public float FloorY;
        public float Height = 4f;
        public float FloorArea = 10f;
        public float LengthX = 5f;
        public float WidthZ = 5f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
            var center = new Vector3(transform.position.x, FloorY + Height * 0.5f, transform.position.z);
            Gizmos.DrawCube(center, new Vector3(LengthX, Height, WidthZ));
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.6f);
            Gizmos.DrawWireCube(center, new Vector3(LengthX, Height, WidthZ));
        }
    }
}
