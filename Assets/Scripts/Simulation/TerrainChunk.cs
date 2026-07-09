namespace SFP.Simulation
{
    public sealed class TerrainChunk
    {
        public const int VertsPerSide = MapConstants.ChunkCells + 1;
        public ChunkCoord Coord;
        public float[] FloorDepth;
        public float[] CeilingDepth;
        public bool HasCeiling;
    }
}
