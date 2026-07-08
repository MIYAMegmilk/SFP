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

        public float GetHeatDamageRate(int compartmentId)
        {
            float intensity = GetFireIntensity(compartmentId);
            return intensity > 0.2f ? intensity * 3f : 0f;
        }

        public void TickDeviceDamage(float dt, PowerGrid power)
        {
            if (power == null) return;

            foreach (var kvp in _fireIntensity)
            {
                int id = kvp.Key;
                float intensity = kvp.Value;
                if (intensity <= 0.3f) continue;

                float damage = intensity * 2f * dt;

                for (int i = 0; i < power.Reactors.Count; i++)
                {
                    var reactor = power.Reactors[i];
                    if (reactor.CompartmentId != id) continue;
                    reactor.Condition -= damage;
                    if (reactor.Condition < 0f) reactor.Condition = 0f;
                }

                for (int i = 0; i < power.Junctions.Count; i++)
                {
                    var junction = power.Junctions[i];
                    if (junction.CompartmentId != id) continue;
                    junction.Condition -= damage;
                    if (junction.Condition < 0f) junction.Condition = 0f;
                }

                if (intensity > 0.5f)
                {
                    for (int i = 0; i < power.Batteries.Count; i++)
                    {
                        var battery = power.Batteries[i];
                        if (battery.CompartmentId != id) continue;
                        var node = power.GetNode(battery.PowerNodeId);
                        if (node != null) node.IsEnabled = false;
                    }
                }
            }
        }

        public void Tick(float dt, AtmosphereSystem atmosphere, PowerGrid power = null)
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

            if (power != null)
                TickDeviceDamage(dt, power);
        }
    }
}
