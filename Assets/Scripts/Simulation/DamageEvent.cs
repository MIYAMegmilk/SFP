namespace SFP.Simulation
{
    public enum DamageEventKind
    {
        BreachCreated,
        Collision,
        Explosion,
        ElectricalShort,
        MineExplosion,
        CreatureAttack,
    }

    public struct DamageEvent
    {
        public DamageEventKind Kind;
        public int CompartmentId;
        public HullFace Face;
        public float Magnitude;
        public int SectionId;
    }
}
