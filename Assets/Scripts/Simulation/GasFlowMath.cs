using System;

namespace SFP.Simulation
{
    public static class GasFlowMath
    {
        // Specific gas constant for dry air (J/(kg·K))
        public const float RAir = 287.05f;
        // Heat capacity ratio cp/cv for diatomic gas
        public const float Gamma = 1.4f;
        // Reference temperature for "standard m³" (20°C)
        public const float T0 = 293.15f;
        // Air density at 1 atm, T0 (kg/m³)
        public const float Rho0 = 1.204f;
        // Sharp-edged orifice discharge coefficient
        public const float OrificeCd = 0.61f;
        // Critical pressure ratio for choked flow: ((γ+1)/2)^(γ/(γ-1))
        public const float ChokedRatio = 1.8929f;
        // Specific heat at constant pressure (J/(kg·K))
        public const float CpAir = 1005f;

        // Air density from pressure (atm) and temperature (K)
        public static float AirDensity(float pAtm, float tempK)
        {
            return pAtm * AirPressureMath.AtmPa / (RAir * tempK);
        }

        // Ideal gas pressure: P = (N/V) * (T/T0), clamped
        // N = AirAmount in "standard m³", V = AirVolume in m³
        public static float PressureAtm(float airAmountStd, float airVolume, float tempK)
        {
            if (airVolume <= 0f) return AirPressureMath.MaxPressureAtm;
            float p = (airAmountStd / airVolume) * (tempK / T0);
            if (p < 0.25f) p = 0.25f;
            if (p > AirPressureMath.MaxPressureAtm) p = AirPressureMath.MaxPressureAtm;
            return p;
        }

        // Mass flow rate through an orifice (kg/s)
        // Handles both subsonic and choked regimes
        public static float MassFlowKgS(float area, float pUpAtm, float pDownAtm, float tempUpK)
        {
            if (pUpAtm <= pDownAtm || area <= 0f) return 0f;
            float pUpPa = pUpAtm * AirPressureMath.AtmPa;
            float pDownPa = pDownAtm * AirPressureMath.AtmPa;
            float rhoUp = pUpPa / (RAir * tempUpK);
            float ratio = pUpAtm / pDownAtm;

            float mdot;
            if (ratio >= ChokedRatio)
            {
                // Choked flow — mass flow independent of downstream pressure
                // ṁ = Cd * A * P_up * sqrt(γ/(R*T)) * (2/(γ+1))^((γ+1)/(2(γ-1)))
                float chokedCoeff = 0.5787f; // (2/(γ+1))^((γ+1)/(2(γ-1))) for γ=1.4
                mdot = OrificeCd * area * pUpPa * (float)Math.Sqrt(Gamma / (RAir * tempUpK)) * chokedCoeff;
            }
            else
            {
                // Subsonic: ṁ = Cd * A * sqrt(2 * ρ_up * ΔP)
                float deltaP = pUpPa - pDownPa;
                mdot = OrificeCd * area * (float)Math.Sqrt(2f * rhoUp * deltaP);
            }
            return mdot;
        }

        // Exit velocity of gas through the orifice (m/s)
        public static float ExitVelocity(float pUpAtm, float pDownAtm, float tempUpK)
        {
            if (pUpAtm <= pDownAtm) return 0f;
            float ratio = pUpAtm / pDownAtm;
            if (ratio >= ChokedRatio)
            {
                // Choked: throat velocity = speed of sound at throat temperature
                float tThroat = tempUpK * 2f / (Gamma + 1f);
                return (float)Math.Sqrt(Gamma * RAir * tThroat);
            }
            float pUpPa = pUpAtm * AirPressureMath.AtmPa;
            float pDownPa = pDownAtm * AirPressureMath.AtmPa;
            float rhoUp = pUpPa / (RAir * tempUpK);
            return (float)Math.Sqrt(2f * (pUpPa - pDownPa) / rhoUp);
        }

        // Equilibrium transfer amount to equalize pressure between two volumes (std m³)
        // Prevents overshoot in discrete timesteps
        public static float EqualizeTransferStd(float nA, float vA, float tA,
                                                 float nB, float vB, float tB)
        {
            // Pressure: pA = (nA/vA)*(tA/T0), pB = (nB/vB)*(tB/T0)
            // After transfer x from A to B:
            // pA' = ((nA-x)/vA)*(tA/T0) = pB' = ((nB+x)/vB)*(tB/T0)
            // Solve: x = (nA*tA*vB - nB*tB*vA) / (tA*vB + tB*vA)
            float denom = tA * vB + tB * vA;
            if (denom <= 0f) return 0f;
            float x = (nA * tA * vB - nB * tB * vA) / denom;
            if (x < 0f) x = 0f;
            if (x > nA * 0.5f) x = nA * 0.5f;
            return x;
        }

        // Sea temperature as a function of depth (K)
        // Surface ~20°C (293K), deep ocean ~4°C (277K), exponential thermocline
        public static float SeaTemperatureK(float depthM)
        {
            if (depthM < 0f) depthM = 0f;
            return 277.15f + 16f * (float)Math.Exp(-depthM / 150.0);
        }

        // External (hydrostatic + atmospheric) pressure at a given opening height
        // seaLevelY = submarine depth, openingY = opening's Y in ship-local coords
        // Submerged openings have openingY < seaLevelY → higher external pressure
        public static float ExternalPressureAtm(float seaLevelY, float openingY)
        {
            float waterHead = seaLevelY - openingY;
            if (waterHead < 0f) waterHead = 0f;
            return 1f + waterHead / AirPressureMath.MetersPerAtm;
        }
    }
}
