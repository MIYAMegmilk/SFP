using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class AtmosphereSystem
    {
        readonly CompartmentGraph _graph;
        readonly Dictionary<int, float> _oxygenLevels = new Dictionary<int, float>();
        readonly Dictionary<int, float> _co2Levels = new Dictionary<int, float>();
        float _crewConsumptionRate = 0.002f;
        float _diffusionRate = 0.05f;

        // Real CO2 production: ~4e-6 m³/s per person (~240 mL/min)
        public float Co2ProductionRate = 4e-6f;
        // Gameplay accelerator (real rate would take days to matter in large rooms)
        public float Co2GameplayScale = 6000f;

        public int CrewCompartmentId { get; set; } = -1;

        public AtmosphereSystem(CompartmentGraph graph)
        {
            _graph = graph;
            foreach (var c in graph.Compartments)
            {
                _oxygenLevels[c.Id] = 1f;
                _co2Levels[c.Id] = 0.0004f;
            }
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

        public float GetCo2Level(int compartmentId)
        {
            return _co2Levels.TryGetValue(compartmentId, out float v) ? v : 0f;
        }

        public void SetCo2Level(int compartmentId, float level)
        {
            if (_co2Levels.ContainsKey(compartmentId))
            {
                if (level > 1f) level = 1f;
                if (level < 0f) level = 0f;
                _co2Levels[compartmentId] = level;
            }
        }

        public void AddCo2(int compartmentId, float amount)
        {
            if (_co2Levels.TryGetValue(compartmentId, out float current))
            {
                float next = current + amount;
                if (next > 1f) next = 1f;
                if (next < 0f) next = 0f;
                _co2Levels[compartmentId] = next;
            }
        }

        public void Tick(float dt)
        {
            foreach (var c in _graph.Compartments)
            {
                float o2 = _oxygenLevels[c.Id];
                float co2 = _co2Levels[c.Id];

                if (c.Id == CrewCompartmentId)
                {
                    o2 -= _crewConsumptionRate * dt;

                    // CO2 production: scaled real rate / air volume
                    float airVol = c.AirVolume;
                    if (airVol > 0f)
                        co2 += Co2ProductionRate * Co2GameplayScale / airVol * dt;
                }

                // Water dilution (flooding washes out both gases)
                float airFraction = 1f - c.WaterFraction;
                if (airFraction < 0.05f) airFraction = 0.05f;
                float dilution = airFraction / (airFraction + (1f - airFraction) * 0.1f * dt);
                o2 *= dilution;
                co2 *= dilution;

                if (o2 < 0f) o2 = 0f;
                if (co2 < 0f) co2 = 0f;
                _oxygenLevels[c.Id] = o2;
                _co2Levels[c.Id] = co2;
            }

            // Diffusion through open doorways
            foreach (var o in _graph.Openings)
            {
                if (!CanPassAir(_graph, o)) continue;

                float oA = _oxygenLevels.TryGetValue(o.CompartmentA, out float va) ? va : 0f;
                float oB = _oxygenLevels.TryGetValue(o.CompartmentB, out float vb) ? vb : 0f;
                float diffO2 = (oA - oB) * _diffusionRate * dt;
                if (_oxygenLevels.ContainsKey(o.CompartmentA))
                    _oxygenLevels[o.CompartmentA] = oA - diffO2;
                if (_oxygenLevels.ContainsKey(o.CompartmentB))
                    _oxygenLevels[o.CompartmentB] = oB + diffO2;

                float cA = _co2Levels.TryGetValue(o.CompartmentA, out float ca) ? ca : 0f;
                float cB = _co2Levels.TryGetValue(o.CompartmentB, out float cb) ? cb : 0f;
                float diffCo2 = (cA - cB) * _diffusionRate * dt;
                if (_co2Levels.ContainsKey(o.CompartmentA))
                    _co2Levels[o.CompartmentA] = cA - diffCo2;
                if (_co2Levels.ContainsKey(o.CompartmentB))
                    _co2Levels[o.CompartmentB] = cB + diffCo2;
            }
        }

        public static bool CanPassAir(CompartmentGraph graph, Opening o)
        {
            if (!o.IsOpen) return false;
            if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) return false;

            float openingTop = o.CenterY + o.Height * 0.5f;
            var compA = graph.GetCompartment(o.CompartmentA);
            var compB = graph.GetCompartment(o.CompartmentB);
            return openingTop > compA.WaterLevelY && openingTop > compB.WaterLevelY;
        }
    }
}
