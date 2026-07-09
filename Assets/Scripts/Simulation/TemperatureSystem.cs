using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class TemperatureSystem
    {
        readonly CompartmentGraph _graph;
        readonly Dictionary<int, float> _exteriorAreas = new Dictionary<int, float>();

        // J/(m³·K) — effective thermal mass of air + structure + equipment
        public float HeatCapacityPerVolume = 40000f;
        // W per unit fire intensity (~2 MW at intensity 1)
        public float FirePowerPerIntensity = 2e6f;
        // W/(m²·K) — steel hull with light insulation
        public float HullU = 10f;
        // W/(m²·K) — convective mixing through open doorways
        public float OpenDoorExchangeCoeff = 200f;
        // W/(m²·K) — heat conduction through closed bulkheads
        public float ClosedWallConductance = 5f;
        // W/(m²·K) — water surface quenching of fire/hot rooms
        public float WaterQuenchCoeff = 200f;

        public TemperatureSystem(CompartmentGraph graph)
        {
            _graph = graph;
        }

        public void SetExteriorArea(int compartmentId, float areaM2)
        {
            _exteriorAreas[compartmentId] = areaM2;
        }

        public float GetTemperatureK(int id)
        {
            if (id < 0 || id >= _graph.Compartments.Count) return GasFlowMath.T0;
            return _graph.GetCompartment(id).TemperatureK;
        }

        public float GetTemperatureC(int id)
        {
            return GetTemperatureK(id) - 273.15f;
        }

        public void Tick(float dt, FireSystem fire)
        {
            var comps = _graph.Compartments;
            float seaDepth = _graph.SeaLevelY;
            float tSea = GasFlowMath.SeaTemperatureK(seaDepth);

            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                float capacity = c.Volume * HeatCapacityPerVolume;
                if (capacity <= 0f) continue;

                float qNet = 0f;

                // Fire heating
                if (fire != null)
                {
                    float intensity = fire.GetFireIntensity(c.Id);
                    if (intensity > 0f)
                        qNet += FirePowerPerIntensity * intensity;
                }

                // Hull cooling (exterior surfaces)
                if (_exteriorAreas.TryGetValue(c.Id, out float extArea) && extArea > 0f)
                    qNet -= HullU * extArea * (c.TemperatureK - tSea);

                // Water quench (flooded floor area cools the room)
                if (c.WaterFraction > 0f)
                    qNet -= WaterQuenchCoeff * c.FloorArea * c.WaterFraction * (c.TemperatureK - tSea);

                c.TemperatureK += (qNet / capacity) * dt;
            }

            // Inter-compartment heat exchange through openings
            foreach (var o in _graph.Openings)
            {
                if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) continue;

                var compA = _graph.GetCompartment(o.CompartmentA);
                var compB = _graph.GetCompartment(o.CompartmentB);
                float deltaT = compA.TemperatureK - compB.TemperatureK;
                if (Math.Abs(deltaT) < 0.01f) continue;

                float coeff = o.IsOpen ? OpenDoorExchangeCoeff : ClosedWallConductance;
                float qFlow = coeff * o.Area * deltaT;

                float capA = compA.Volume * HeatCapacityPerVolume;
                float capB = compB.Volume * HeatCapacityPerVolume;
                if (capA <= 0f || capB <= 0f) continue;

                // Limit heat transfer to prevent overshoot
                float maxDelta = Math.Abs(deltaT) * 0.5f;
                float dtA = (qFlow / capA) * dt;
                float dtB = (qFlow / capB) * dt;
                if (Math.Abs(dtA) > maxDelta) { float scale = maxDelta / Math.Abs(dtA); dtA *= scale; dtB *= scale; }
                if (Math.Abs(dtB) > maxDelta) { float scale = maxDelta / Math.Abs(dtB); dtA *= scale; dtB *= scale; }

                compA.TemperatureK -= dtA;
                compB.TemperatureK += dtB;
            }

            // Clamp temperatures
            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                float tSeaLocal = GasFlowMath.SeaTemperatureK(seaDepth);
                if (c.TemperatureK < tSeaLocal - 5f) c.TemperatureK = tSeaLocal - 5f;
                if (c.TemperatureK > 1273f) c.TemperatureK = 1273f;
            }
        }
    }
}
