using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class CompartmentGraph
    {
        readonly List<Compartment> _compartments = new();
        readonly List<Opening> _openings = new();

        public IReadOnlyList<Compartment> Compartments => _compartments;
        public IReadOnlyList<Opening> Openings => _openings;

        public float SeaLevelY { get; set; } = 100f;

        public Compartment AddCompartment(float floorY, float height, float floorArea)
        {
            var c = new Compartment(_compartments.Count, floorY, height, floorArea);
            _compartments.Add(c);
            return c;
        }

        public Opening AddOpening(OpeningKind kind, int compartmentA, int compartmentB,
                                  float area, float centerY, float height, bool isOpen = true)
        {
            var o = new Opening(_openings.Count, kind, compartmentA, compartmentB,
                                area, centerY, height, isOpen);
            _openings.Add(o);
            return o;
        }

        public Compartment GetCompartment(int id) => _compartments[id];
    }
}
