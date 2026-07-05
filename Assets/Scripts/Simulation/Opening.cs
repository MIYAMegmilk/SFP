namespace SFP.Simulation
{
    public enum OpeningKind { Door, Hatch, Breach }

    public sealed class Opening
    {
        public int Id;
        public OpeningKind Kind;
        public int CompartmentA;
        public int CompartmentB;
        public float Area;
        public float CenterY;
        public float Height;
        public bool IsOpen;

        public const int Sea = -1;

        public Opening(int id, OpeningKind kind, int compartmentA, int compartmentB,
                       float area, float centerY, float height, bool isOpen = true)
        {
            Id = id;
            Kind = kind;
            CompartmentA = compartmentA;
            CompartmentB = compartmentB;
            Area = area;
            CenterY = centerY;
            Height = height;
            IsOpen = isOpen;
        }
    }
}
