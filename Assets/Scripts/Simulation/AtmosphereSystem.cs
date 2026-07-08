using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class AtmosphereSystem
    {
        readonly CompartmentGraph _graph;
        readonly Dictionary<int, float> _oxygenLevels = new Dictionary<int, float>();
        float _crewConsumptionRate = 0.002f;
        float _diffusionRate = 0.05f;

        int[] _componentIds;
        bool[] _componentVented;
        int _componentCount;

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
            UpdateAirPressure();

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
                if (!CanPassAir(o)) continue;

                float a = _oxygenLevels.TryGetValue(o.CompartmentA, out float va) ? va : 0f;
                float b = _oxygenLevels.TryGetValue(o.CompartmentB, out float vb) ? vb : 0f;
                float diff = (a - b) * _diffusionRate * dt;
                if (_oxygenLevels.ContainsKey(o.CompartmentA))
                    _oxygenLevels[o.CompartmentA] = a - diff;
                if (_oxygenLevels.ContainsKey(o.CompartmentB))
                    _oxygenLevels[o.CompartmentB] = b + diff;
            }
        }

        bool CanPassAir(Opening o)
        {
            if (!o.IsOpen) return false;
            if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) return false;

            float openingTop = o.CenterY + o.Height * 0.5f;
            var compA = _graph.GetCompartment(o.CompartmentA);
            var compB = _graph.GetCompartment(o.CompartmentB);
            return openingTop > compA.WaterLevelY && openingTop > compB.WaterLevelY;
        }

        void UpdateAirPressure()
        {
            var comps = _graph.Compartments;
            int n = comps.Count;
            if (n == 0) return;

            if (_componentIds == null || _componentIds.Length < n)
            {
                _componentIds = new int[n];
                _componentVented = new bool[n];
            }

            // Union-Find initialization
            for (int i = 0; i < n; i++)
            {
                _componentIds[i] = i;
                _componentVented[i] = false;
            }

            // Merge compartments connected by air-passable openings
            foreach (var o in _graph.Openings)
            {
                if (!CanPassAir(o)) continue;
                Union(o.CompartmentA, o.CompartmentB);
            }

            // Detect vented components (sea opening above water and sea level)
            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                if (o.CompartmentA != Opening.Sea && o.CompartmentB != Opening.Sea) continue;

                int compId = o.CompartmentA == Opening.Sea ? o.CompartmentB : o.CompartmentA;
                if (compId < 0 || compId >= n) continue;

                float openingTop = o.CenterY + o.Height * 0.5f;
                var comp = _graph.GetCompartment(compId);
                if (openingTop > _graph.SeaLevelY && openingTop > comp.WaterLevelY)
                    _componentVented[Find(compId)] = true;
            }

            // Also vent any component where at least one compartment has
            // no water and no seal path — the default "open to atmosphere" case.
            // In a submarine submerged, all compartments are sealed unless
            // connected to a sea opening above sea level.
            // For gameplay: when not submerged, all compartments vent.
            // This is handled by the sea opening check above.

            // Compute pressure per component
            // Pass 1: accumulate totals per root
            float[] totalAirAmount = new float[n];
            float[] totalAirVolume = new float[n];

            for (int i = 0; i < n; i++)
            {
                var c = comps[i];
                int root = Find(i);
                totalAirAmount[root] += c.AirAmount;
                totalAirVolume[root] += c.AirVolume;
            }

            // Pass 2: compute pressure and distribute
            for (int i = 0; i < n; i++)
            {
                var c = comps[i];
                int root = Find(i);

                if (_componentVented[root])
                {
                    c.AirPressureAtm = 1f;
                    c.AirAmount = c.AirVolume;
                    c.IsAirSealed = false;
                }
                else
                {
                    float sumAmount = totalAirAmount[root];
                    float sumVolume = totalAirVolume[root];
                    float p = sumVolume > 0f ? sumAmount / sumVolume : 1f;
                    p = Math.Clamp(p, 0.25f, AirPressureMath.MaxPressureAtm);

                    c.AirPressureAtm = p;
                    c.AirAmount = p * c.AirVolume;
                    c.IsAirSealed = true;
                }
            }
        }

        int Find(int x)
        {
            while (_componentIds[x] != x)
            {
                _componentIds[x] = _componentIds[_componentIds[x]];
                x = _componentIds[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
            {
                _componentIds[rb] = ra;
                if (_componentVented[rb])
                    _componentVented[ra] = true;
            }
        }
    }
}
