namespace SFP.Simulation
{
    public sealed class Compartment
    {
        public int Id;
        public float FloorY;
        public float Height;
        public float FloorArea;
        public float Volume;
        public float WaterVolume;

        public float WaterFraction => Volume > 0f ? WaterVolume / Volume : 0f;
        public float WaterLevelY => FloorY + WaterFraction * Height;

        public Compartment(int id, float floorY, float height, float floorArea)
        {
            Id = id;
            FloorY = floorY;
            Height = height;
            FloorArea = floorArea;
            Volume = height * floorArea;
        }
    }
}
