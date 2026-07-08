using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class MapData
    {
        public int CellsX, CellsZ;
        public float CellSize;
        public float OriginX, OriginZ;
        public float[] FloorDepth;
        public float[] CeilingDepth;
        public float SpawnX, SpawnZ;
        public int Seed;

        public List<(float X, float Z, float Depth)> MineSpots = new();
        public List<(float X, float Z)> ChannelWaypoints = new();

        public float WorldSizeX => CellsX * CellSize;
        public float WorldSizeZ => CellsZ * CellSize;

        public float GetFloorDepthAt(float x, float z)
        {
            float maxX = CellsX - 1;
            float maxZ = CellsZ - 1;
            float cx = (x - OriginX) / CellSize;
            float cz = (z - OriginZ) / CellSize;
            if (cx < 0f) cx = 0f;
            else if (cx > maxX) cx = maxX;
            if (cz < 0f) cz = 0f;
            else if (cz > maxZ) cz = maxZ;

            int x0 = (int)cx;
            int z0 = (int)cz;
            int x1 = x0 + 1 <= (int)maxX ? x0 + 1 : x0;
            int z1 = z0 + 1 <= (int)maxZ ? z0 + 1 : z0;
            float tx = cx - x0;
            float tz = cz - z0;

            float d00 = FloorDepth[z0 * CellsX + x0];
            float d10 = FloorDepth[z0 * CellsX + x1];
            float d01 = FloorDepth[z1 * CellsX + x0];
            float d11 = FloorDepth[z1 * CellsX + x1];

            float dx0 = d00 + (d10 - d00) * tx;
            float dx1 = d01 + (d11 - d01) * tx;
            return dx0 + (dx1 - dx0) * tz;
        }

        public float GetCeilingDepthAt(float x, float z)
        {
            float maxX = CellsX - 1;
            float maxZ = CellsZ - 1;
            float cx = (x - OriginX) / CellSize;
            float cz = (z - OriginZ) / CellSize;
            if (cx < 0f) cx = 0f;
            else if (cx > maxX) cx = maxX;
            if (cz < 0f) cz = 0f;
            else if (cz > maxZ) cz = maxZ;

            int x0 = (int)cx;
            int z0 = (int)cz;
            int x1 = x0 + 1 <= (int)maxX ? x0 + 1 : x0;
            int z1 = z0 + 1 <= (int)maxZ ? z0 + 1 : z0;
            float tx = cx - x0;
            float tz = cz - z0;

            float d00 = CeilingDepth[z0 * CellsX + x0];
            float d10 = CeilingDepth[z0 * CellsX + x1];
            float d01 = CeilingDepth[z1 * CellsX + x0];
            float d11 = CeilingDepth[z1 * CellsX + x1];

            float dx0 = d00 + (d10 - d00) * tx;
            float dx1 = d01 + (d11 - d01) * tx;
            return dx0 + (dx1 - dx0) * tz;
        }
    }
}
