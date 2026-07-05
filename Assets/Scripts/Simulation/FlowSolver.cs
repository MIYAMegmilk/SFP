using System;

namespace SFP.Simulation
{
    public sealed class FlowSolver
    {
        public float Cd = 0.6f;
        public float Gravity = 9.81f;
        public float DoorFlowMultiplier = 8f;
        public float MinDoorFlow = 2f; // m³/s minimum when any water exists

        public void Tick(CompartmentGraph graph, float dt)
        {
            var openings = graph.Openings;
            for (int i = 0; i < openings.Count; i++)
            {
                var o = openings[i];
                if (!o.IsOpen) continue;

                float waterYA = GetWaterLevelY(graph, o.CompartmentA);
                float waterYB = GetWaterLevelY(graph, o.CompartmentB);

                float effectiveArea = ComputeEffectiveArea(o, waterYA, waterYB);

                float deltaH = waterYA - waterYB;

                bool isDoorOrHatch = o.Kind != OpeningKind.Breach;

                // For doors/hatches: if either side has water, guarantee minimum flow
                if (isDoorOrHatch && effectiveArea <= 0f)
                {
                    float waterVolA = GetWaterVolume(graph, o.CompartmentA);
                    float waterVolB = GetWaterVolume(graph, o.CompartmentB);
                    if (waterVolA > 0.001f || waterVolB > 0.001f)
                    {
                        effectiveArea = o.Area * 0.6f;
                        if (Math.Abs(deltaH) < 0.05f)
                            deltaH = waterVolA > waterVolB ? 0.2f : -0.2f;
                    }
                }

                if (effectiveArea <= 0f) continue;
                if (Math.Abs(deltaH) < 1e-6f) continue;

                float absDeltaH = Math.Abs(deltaH);
                float q = Cd * effectiveArea * (float)Math.Sqrt(2f * Gravity * absDeltaH);

                if (isDoorOrHatch)
                    q = Math.Max(q * DoorFlowMultiplier, MinDoorFlow * (effectiveArea / o.Area));

                float transfer = q * dt;

                int fromId = deltaH > 0f ? o.CompartmentA : o.CompartmentB;
                int toId = deltaH > 0f ? o.CompartmentB : o.CompartmentA;

                transfer = ClampTransfer(graph, fromId, toId, transfer);
                ApplyTransfer(graph, fromId, -transfer);
                ApplyTransfer(graph, toId, transfer);
            }
        }

        float GetWaterLevelY(CompartmentGraph graph, int compartmentId)
        {
            if (compartmentId == Opening.Sea) return graph.SeaLevelY;
            return graph.GetCompartment(compartmentId).WaterLevelY;
        }

        float GetWaterVolume(CompartmentGraph graph, int compartmentId)
        {
            if (compartmentId == Opening.Sea) return float.MaxValue;
            return graph.GetCompartment(compartmentId).WaterVolume;
        }

        float ComputeEffectiveArea(Opening o, float waterYA, float waterYB)
        {
            float higherWater = Math.Max(waterYA, waterYB);
            float openingBottom = o.CenterY - o.Height * 0.5f;
            float openingTop = o.CenterY + o.Height * 0.5f;

            if (higherWater <= openingBottom) return 0f;
            if (higherWater >= openingTop) return o.Area;

            float submergedFraction = (higherWater - openingBottom) / o.Height;
            return o.Area * submergedFraction;
        }

        float ClampTransfer(CompartmentGraph graph, int fromId, int toId, float transfer)
        {
            if (fromId != Opening.Sea)
            {
                var from = graph.GetCompartment(fromId);
                transfer = Math.Min(transfer, from.WaterVolume);
            }
            if (toId != Opening.Sea)
            {
                var to = graph.GetCompartment(toId);
                float remaining = to.Volume - to.WaterVolume;
                transfer = Math.Min(transfer, remaining);
            }
            return Math.Max(transfer, 0f);
        }

        void ApplyTransfer(CompartmentGraph graph, int compartmentId, float amount)
        {
            if (compartmentId == Opening.Sea) return;
            var c = graph.GetCompartment(compartmentId);
            c.WaterVolume = Math.Clamp(c.WaterVolume + amount, 0f, c.Volume);
        }
    }
}
