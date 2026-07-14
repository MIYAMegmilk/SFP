using System;

namespace SFP.Simulation
{
    public enum AirlockPhase { Dry, Flooding, Flooded, Draining }

    public sealed class AirlockState
    {
        public int CompartmentId;
        public int InnerDoorOpeningId;
        public int OuterHatchOpeningId;
        public int FloodValveOpeningId;
        public int FloorHatchOpeningId = -1;
        public int PowerNodeId = -1;

        // 250 kW pump — sized so 216 m³ × 95% drains in ~25 s at 200 m
        // Q = η·P/(ρ·g·H) → 0.7×250000/(1000×9.81×200) ≈ 0.089 m³/s → 216×0.95/0.089 ≈ 23 s
        public float PumpPowerW = 250_000f;
        public float PumpEfficiency = 0.7f;
        public float PowerConsumption = 200f;

        // 0.1 atm ≈ 10 kPa; over 0.8 m² hatch → 8.1 kN — mechanical dogging limit
        public float EqualizeToleranceAtm = 0.1f;

        public AirlockPhase Phase;

        public void Tick(float dt, CompartmentGraph graph, ShallowWaterSystem water,
                         SubmarineState sub, PowerGrid power)
        {
            var comp = graph.GetCompartment(CompartmentId);
            var innerDoor = graph.Openings[InnerDoorOpeningId];
            var outerHatch = graph.Openings[OuterHatchOpeningId];
            var floodValve = graph.Openings[FloodValveOpeningId];
            Opening floorHatch = FloorHatchOpeningId >= 0 ? graph.Openings[FloorHatchOpeningId] : null;

            float hatchY = outerHatch.CenterY;
            float ambientAtm = GasFlowMath.ExternalPressureAtm(graph.SeaLevelY, hatchY);

            switch (Phase)
            {
                case AirlockPhase.Dry:
                    TickDry(innerDoor, outerHatch, floodValve, floorHatch);
                    break;
                case AirlockPhase.Flooding:
                    TickFlooding(dt, comp, innerDoor, outerHatch, floodValve, floorHatch, ambientAtm);
                    break;
                case AirlockPhase.Flooded:
                    TickFlooded(innerDoor, outerHatch, floodValve, floorHatch, ambientAtm);
                    break;
                case AirlockPhase.Draining:
                    TickDraining(dt, comp, innerDoor, outerHatch, floodValve, floorHatch, water, sub, power);
                    break;
            }
        }

        public bool RequestFlood(CompartmentGraph graph)
        {
            if (Phase != AirlockPhase.Dry) return false;
            var innerDoor = graph.Openings[InnerDoorOpeningId];
            if (innerDoor.IsOpen) return false;
            if (FloorHatchOpeningId >= 0 && graph.Openings[FloorHatchOpeningId].IsOpen) return false;
            Phase = AirlockPhase.Flooding;
            return true;
        }

        public bool RequestDrain()
        {
            if (Phase != AirlockPhase.Flooded) return false;
            Phase = AirlockPhase.Draining;
            return true;
        }

        void LockFloorHatch(Opening floorHatch)
        {
            if (floorHatch == null) return;
            floorHatch.IsOpen = false;
            floorHatch.IsLocked = true;
        }

        void UnlockFloorHatch(Opening floorHatch)
        {
            if (floorHatch == null) return;
            floorHatch.IsLocked = false;
        }

        void TickDry(Opening innerDoor, Opening outerHatch, Opening floodValve, Opening floorHatch)
        {
            innerDoor.IsLocked = false;
            UnlockFloorHatch(floorHatch);
            outerHatch.IsOpen = false;
            outerHatch.IsLocked = true;
            floodValve.IsOpen = false;
            floodValve.IsLocked = true;
        }

        void TickFlooding(float dt, Compartment comp, Opening innerDoor,
                          Opening outerHatch, Opening floodValve, Opening floorHatch, float ambientAtm)
        {
            innerDoor.IsOpen = false;
            innerDoor.IsLocked = true;
            LockFloorHatch(floorHatch);
            outerHatch.IsOpen = false;
            outerHatch.IsLocked = true;

            floodValve.IsLocked = false;
            floodValve.IsOpen = true;

            // Boyle equilibrium: air compresses until P_air ≈ P_ambient
            // f_eq = 1 − 1/P_amb (fraction of room flooded at equilibrium)
            float deltaP = Math.Abs(comp.AirPressureAtm - ambientAtm);
            float feq = ambientAtm > 1f ? 1f - 1f / ambientAtm : 0f;
            bool pressureEqualized = deltaP < EqualizeToleranceAtm;
            bool waterEqualized = comp.WaterFraction >= feq - 0.02f;

            if (pressureEqualized || waterEqualized)
            {
                floodValve.IsOpen = false;
                floodValve.IsLocked = true;
                Phase = AirlockPhase.Flooded;
            }
        }

        void TickFlooded(Opening innerDoor, Opening outerHatch, Opening floodValve,
                         Opening floorHatch, float ambientAtm)
        {
            innerDoor.IsOpen = false;
            innerDoor.IsLocked = true;
            LockFloorHatch(floorHatch);
            floodValve.IsOpen = false;
            floodValve.IsLocked = true;

            outerHatch.IsLocked = false;
            outerHatch.IsOpen = true;
        }

        void TickDraining(float dt, Compartment comp, Opening innerDoor,
                          Opening outerHatch, Opening floodValve, Opening floorHatch,
                          ShallowWaterSystem water, SubmarineState sub, PowerGrid power)
        {
            outerHatch.IsOpen = false;
            outerHatch.IsLocked = true;
            innerDoor.IsOpen = false;
            innerDoor.IsLocked = true;
            LockFloorHatch(floorHatch);
            floodValve.IsOpen = false;
            floodValve.IsLocked = true;

            if (!HasPower(power)) return;

            // Q_drain = η·P_pump / (ρ·g·H), H = max(depth, 1)
            float depth = sub != null ? sub.Depth : 0f;
            float head = depth > 1f ? depth : 1f;
            float drainRate = PumpEfficiency * PumpPowerW
                            / (AirPressureMath.WaterDensity * AirPressureMath.Gravity * head);
            float voltageScale = GetVoltageScale(power);
            drainRate *= voltageScale;

            var grid = water?.GetGrid(CompartmentId);
            if (grid != null)
            {
                float total = grid.TotalVolume();
                float remove = drainRate * dt;
                if (remove > total) remove = total;
                if (total > 0.001f)
                    grid.AddWaterUniform(-remove);
            }
            else
            {
                float next = comp.WaterVolume - drainRate * dt;
                comp.WaterVolume = next > 0f ? next : 0f;
            }

            // Done when nearly dry and pressure near 1 atm
            if (comp.WaterVolume < 0.5f && comp.AirPressureAtm > 0.9f && comp.AirPressureAtm < 1.1f)
            {
                Phase = AirlockPhase.Dry;
            }
        }

        bool HasPower(PowerGrid power)
        {
            if (power == null || PowerNodeId < 0) return true;
            var node = power.GetNode(PowerNodeId);
            if (node == null) return true;
            node.IsEnabled = true;
            node.Consumption = PowerConsumption;
            return node.IsActive;
        }

        float GetVoltageScale(PowerGrid power)
        {
            if (power == null) return 1f;
            float v = power.GridVoltage;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
