using System;

namespace SFP.Simulation
{
    public static class AirPressureMath
    {
        public const float AtmPa = 101325f;
        public const float WaterDensity = 1000f;
        public const float Gravity = 9.81f;
        public const float MetersPerAtm = AtmPa / (WaterDensity * Gravity);
        public const float MinAirFraction = 0.005f;
        public const float MaxPressureAtm = 30f;

        public static float PressureHeadMeters(float pAtmHigh, float pAtmLow)
            => (pAtmHigh - pAtmLow) * MetersPerAtm;
    }
}
