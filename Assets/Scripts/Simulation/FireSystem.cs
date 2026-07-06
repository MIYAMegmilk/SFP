using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class FireSystem
    {
        readonly CompartmentGraph _graph;
        readonly Dictionary<int, float> _fireIntensity = new Dictionary<int, float>();
        float _spreadRate = 0.02f;
        float _oxygenConsumptionRate = 0.1f;

        public FireSystem(CompartmentGraph graph)
        {
            _graph = graph;
            foreach (var c in graph.Compartments)
                _fireIntensity[c.Id] = 0f;
        }

        public float GetFireIntensity(int compartmentId)
        {
            return _fireIntensity.TryGetValue(compartmentId, out float v) ? v : 0f;
        }

        public void StartFire(int compartmentId, float intensity = 0.3f)
        {
            if (_fireIntensity.ContainsKey(compartmentId))
            {
                float current = _fireIntensity[compartmentId];
                _fireIntensity[compartmentId] = current + intensity;
                if (_fireIntensity[compartmentId] > 1f) _fireIntensity[compartmentId] = 1f;
            }
        }

        public void Extinguish(int compartmentId, float amount)
        {
            if (_fireIntensity.ContainsKey(compartmentId))
            {
                _fireIntensity[compartmentId] -= amount;
                if (_fireIntensity[compartmentId] < 0f) _fireIntensity[compartmentId] = 0f;
            }
        }

        public void Tick(float dt, AtmosphereSystem atmosphere)
        {
            var ids = new List<int>(_fireIntensity.Keys);

            foreach (int id in ids)
            {
                float intensity = _fireIntensity[id];
                if (intensity <= 0f) continue;

                var comp = _graph.GetCompartment(id);
                if (comp == null) continue;

                float o2 = atmosphere?.GetOxygenLevel(id) ?? 1f;
                if (o2 < 0.1f)
                {
                    _fireIntensity[id] = intensity * (1f - 2f * dt);
                    if (_fireIntensity[id] < 0.01f) _fireIntensity[id] = 0f;
                    continue;
                }

                if (comp.WaterFraction > 0.5f)
                {
                    _fireIntensity[id] = intensity * (1f - comp.WaterFraction * 3f * dt);
                    if (_fireIntensity[id] < 0.01f) _fireIntensity[id] = 0f;
                    continue;
                }

                atmosphere?.AddOxygen(id, -intensity * _oxygenConsumptionRate * dt);
            }

            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) continue;

                float a = GetFireIntensity(o.CompartmentA);
                float b = GetFireIntensity(o.CompartmentB);
                if (a > 0.3f && b < a)
                    _fireIntensity[o.CompartmentB] = b + (a - b) * _spreadRate * dt;
                if (b > 0.3f && a < b)
                    _fireIntensity[o.CompartmentA] = a + (b - a) * _spreadRate * dt;
            }
        }
    }
}
