namespace SFP.Simulation
{
    public enum HullFace { North, South, East, West, Floor, Ceiling }

    public sealed class HullSection
    {
        public int Id;
        public int CompartmentId;
        public HullFace Face;
        public float Integrity = 100f;
        public float MaxIntegrity = 100f;
        public int ActiveOpeningId = -1;
        public bool IsExterior;
        public float WeaknessFactor = 1f;
        public bool BreachPending;

        public bool IsWeakened => Integrity < 60f;
        public bool HasBreach => ActiveOpeningId >= 0;
        public float IntegrityFraction => MaxIntegrity > 0f ? Integrity / MaxIntegrity : 0f;
    }
}
