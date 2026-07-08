using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class BuiltStructure : MonoBehaviour
    {
        public int GridX, GridY, GridZ;
        public int FaceType;

        public GridKey GetKey() => new GridKey(GridX, GridY, GridZ, (StructureFace)FaceType);

        public void SetKey(GridKey key)
        {
            GridX = key.X;
            GridY = key.Y;
            GridZ = key.Z;
            FaceType = (int)key.Face;
        }
    }
}
