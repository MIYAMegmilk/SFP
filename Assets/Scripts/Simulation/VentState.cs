using System;

namespace SFP.Simulation
{
    public sealed class VentState
    {
        public int PowerNodeId = -1;
        public int CompartmentA = -1;
        public int CompartmentB = -1;
        // m² duct cross-section
        public float DuctArea = 0.1f;
        // m³/s forced circulation when powered
        public float FanFlowRate = 1.5f;
        // Duct centerline Y — blocked when either side's water covers this height
        public float DuctY;
        public bool IsEnabled = true;
        // Device condition — inoperative at 0
        public float Condition = 100f;

        public void Tick(float dt, CompartmentGraph graph, AtmosphereSystem atmo, PowerGrid power)
        {
            if (!IsEnabled || Condition <= 0f) return;
            if (CompartmentA < 0 || CompartmentB < 0 || atmo == null) return;

            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
            }

            var compA = graph.GetCompartment(CompartmentA);
            var compB = graph.GetCompartment(CompartmentB);
            if (compA == null || compB == null) return;

            // Duct blocked if submerged on either side
            if (DuctY <= compA.WaterLevelY || DuctY <= compB.WaterLevelY) return;

            float airVolA = compA.AirVolume;
            float airVolB = compB.AirVolume;
            if (airVolA <= 0f || airVolB <= 0f) return;

            // Bidirectional mixing: exchange volume proportional to fan flow rate
            float exchangeVol = FanFlowRate * dt;
            float fracA = exchangeVol / airVolA;
            float fracB = exchangeVol / airVolB;
            if (fracA > 0.5f) fracA = 0.5f;
            if (fracB > 0.5f) fracB = 0.5f;

            // O2 mixing
            float o2A = atmo.GetOxygenLevel(CompartmentA);
            float o2B = atmo.GetOxygenLevel(CompartmentB);
            float newO2A = o2A + (o2B - o2A) * fracA;
            float newO2B = o2B + (o2A - o2B) * fracB;
            atmo.SetOxygenLevel(CompartmentA, newO2A);
            atmo.SetOxygenLevel(CompartmentB, newO2B);

            // CO2 mixing
            float co2A = atmo.GetCo2Level(CompartmentA);
            float co2B = atmo.GetCo2Level(CompartmentB);
            float newCo2A = co2A + (co2B - co2A) * fracA;
            float newCo2B = co2B + (co2A - co2B) * fracB;
            atmo.SetCo2Level(CompartmentA, newCo2A);
            atmo.SetCo2Level(CompartmentB, newCo2B);

            // Temperature mixing
            float tA = compA.TemperatureK;
            float tB = compB.TemperatureK;
            compA.TemperatureK = tA + (tB - tA) * fracA;
            compB.TemperatureK = tB + (tA - tB) * fracB;

            // Slow pressure equalization through duct area (passive, even without fan)
            float pA = compA.AirPressureAtm;
            float pB = compB.AirPressureAtm;
            if (Math.Abs(pA - pB) > 1e-4f)
            {
                float mdot = GasFlowMath.MassFlowKgS(DuctArea, Math.Max(pA, pB), Math.Min(pA, pB),
                    pA > pB ? compA.TemperatureK : compB.TemperatureK);
                float dStd = mdot / GasFlowMath.Rho0 * dt;
                float maxTransfer = GasFlowMath.EqualizeTransferStd(
                    pA > pB ? compA.AirAmount : compB.AirAmount,
                    pA > pB ? compA.AirVolume : compB.AirVolume,
                    pA > pB ? compA.TemperatureK : compB.TemperatureK,
                    pA > pB ? compB.AirAmount : compA.AirAmount,
                    pA > pB ? compB.AirVolume : compA.AirVolume,
                    pA > pB ? compB.TemperatureK : compA.TemperatureK);
                if (dStd > maxTransfer) dStd = maxTransfer;
                if (dStd > 0f)
                {
                    if (pA > pB)
                    {
                        compA.AirAmount -= dStd;
                        compB.AirAmount += dStd;
                    }
                    else
                    {
                        compB.AirAmount -= dStd;
                        compA.AirAmount += dStd;
                    }
                }
            }
        }
    }
}
