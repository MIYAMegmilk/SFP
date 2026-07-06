using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class AtmosphereSystem
    {
        readonly CompartmentGraph _graph;
        readonly Dictionary<int, float> _oxygenLevels = new Dictionary<int, float>();
        float _crewConsumptionRate = 0.002f;
        float _diffusionRate = 0.05f;

        public int CrewCompartmentId { get; set; } = -1;

        public AtmosphereSystem(CompartmentGraph graph)
        {
            _graph = graph;
            foreach (var c in graph.Compartments)
                _oxygenLevels[c.Id] = 1f;
        }

        public float GetOxygenLevel(int compartmentId)
        {
            return _oxygenLevels.TryGetValue(compartmentId, out float v) ? v : 0f;
        }

        public void SetOxygenLevel(int compartmentId, float level)
        {
            if (_oxygenLevels.ContainsKey(compartmentId))
            {
                if (level > 1f) level = 1f;
                if (level < 0f) level = 0f;
                _oxygenLevels[compartmentId] = level;
            }
        }

        public void AddOxygen(int compartmentId, float amount)
        {
            if (_oxygenLevels.TryGetValue(compartmentId, out float current))
            {
                float next = current + amount;
                if (next > 1f) next = 1f;
                if (next < 0f) next = 0f;
                _oxygenLevels[compartmentId] = next;
            }
        }

        public void Tick(float dt)
        {
            foreach (var c in _graph.Compartments)
            {
                float o2 = _oxygenLevels[c.Id];
                if (c.Id == CrewCompartmentId)
                    o2 -= _crewConsumptionRate * dt;

                float airFraction = 1f - c.WaterFraction;
                if (airFraction < 0.05f) airFraction = 0.05f;
                o2 *= airFraction / (airFraction + (1f - airFraction) * 0.1f * dt);

                if (o2 < 0f) o2 = 0f;
                _oxygenLevels[c.Id] = o2;
            }

            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) continue;
                if (!_oxygenLevels.ContainsKey(o.CompartmentA) ||
                    !_oxygenLevels.ContainsKey(o.CompartmentB)) continue;

                float a = _oxygenLevels[o.CompartmentA];
                float b = _oxygenLevels[o.CompartmentB];
                float diff = (a - b) * _diffusionRate * dt;
                _oxygenLevels[o.CompartmentA] = a - diff;
                _oxygenLevels[o.CompartmentB] = b + diff;
            }
        }
    }
}
