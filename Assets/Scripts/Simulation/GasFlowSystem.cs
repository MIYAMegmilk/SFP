using System;
using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class GasFlowSystem
    {
        readonly CompartmentGraph _graph;
        readonly AtmosphereSystem _atmosphere;

        public SubmarineState Submarine;
        public int SubSteps = 2;
        public float TotalAirLostStd { get; private set; }

        int[] _componentIds;
        bool[] _componentVented;

        public GasFlowSystem(CompartmentGraph graph, AtmosphereSystem atmosphere)
        {
            _graph = graph;
            _atmosphere = atmosphere;
        }

        public bool IsVented(int compartmentId)
        {
            if (_componentVented == null || compartmentId < 0 ||
                compartmentId >= _componentVented.Length) return false;
            return _componentVented[Find(compartmentId)];
        }

        public void Tick(float dt)
        {
            var comps = _graph.Compartments;
            int n = comps.Count;
            if (n == 0) return;

            // Recompute pressure from ideal gas law: P = (N/V)*(T/T0)
            for (int i = 0; i < n; i++)
            {
                var c = comps[i];
                c.AirPressureAtm = GasFlowMath.PressureAtm(c.AirAmount, c.AirVolume, c.TemperatureK);
            }

            float subDt = dt / SubSteps;
            for (int step = 0; step < SubSteps; step++)
            {
                ProcessOpenings(subDt);
                ProcessSeaOpenings(subDt);

                // Recompute pressure after each substep
                for (int i = 0; i < n; i++)
                {
                    var c = comps[i];
                    c.AirPressureAtm = GasFlowMath.PressureAtm(c.AirAmount, c.AirVolume, c.TemperatureK);
                }
            }

            // Union-Find for IsAirSealed and vented detection
            UpdateSealedStatus();
        }

        void ProcessOpenings(float dt)
        {
            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) continue;

                var compA = _graph.GetCompartment(o.CompartmentA);
                var compB = _graph.GetCompartment(o.CompartmentB);

                // Check if air can pass (opening top above water on both sides)
                float openingTop = o.CenterY + o.Height * 0.5f;
                if (openingTop <= compA.WaterLevelY || openingTop <= compB.WaterLevelY) continue;

                float pA = compA.AirPressureAtm;
                float pB = compB.AirPressureAtm;
                if (Math.Abs(pA - pB) < 1e-4f) continue;

                // Determine upstream/downstream
                Compartment upComp, downComp;
                float pUp, pDown;
                int signDir; // +1 means flow A→B, -1 means B→A
                if (pA > pB)
                {
                    upComp = compA; downComp = compB;
                    pUp = pA; pDown = pB; signDir = 1;
                }
                else
                {
                    upComp = compB; downComp = compA;
                    pUp = pB; pDown = pA; signDir = -1;
                }

                float mdot = GasFlowMath.MassFlowKgS(o.EffectiveArea, pUp, pDown, upComp.TemperatureK);
                float dStd = mdot / GasFlowMath.Rho0 * dt;

                // Clamp to equilibrium to prevent overshoot
                float maxTransfer = GasFlowMath.EqualizeTransferStd(
                    upComp.AirAmount, upComp.AirVolume, upComp.TemperatureK,
                    downComp.AirAmount, downComp.AirVolume, downComp.TemperatureK);
                if (dStd > maxTransfer) dStd = maxTransfer;
                if (dStd <= 0f) continue;

                // Transfer air amount
                upComp.AirAmount -= dStd;
                downComp.AirAmount += dStd;

                // Advect gas composition (O2, CO2) — bulk flow carries upstream composition
                AdvectComposition(upComp, downComp, dStd);

                // Advect temperature (mass-weighted mixing)
                AdvectTemperature(upComp, downComp, dStd);

                // Blowout impulse: F = ṁ * v_exit, along opening normal
                if (Submarine != null && (o.NormalX != 0f || o.NormalY != 0f || o.NormalZ != 0f))
                {
                    float vExit = GasFlowMath.ExitVelocity(pUp, pDown, upComp.TemperatureK);
                    float force = mdot * vExit;
                    // Force direction: gas exits in −normal direction (outward from high-pressure side)
                    float dirSign = signDir == 1 ? -1f : 1f;
                    Submarine.GasForceLocalX += dirSign * o.NormalX * force;
                    Submarine.GasForceLocalY += dirSign * o.NormalY * force;
                    Submarine.GasForceLocalZ += dirSign * o.NormalZ * force;
                }
            }
        }

        void ProcessSeaOpenings(float dt)
        {
            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                if (o.IsGasSealed) continue;
                int compId;
                if (o.CompartmentA == Opening.Sea && o.CompartmentB != Opening.Sea)
                    compId = o.CompartmentB;
                else if (o.CompartmentB == Opening.Sea && o.CompartmentA != Opening.Sea)
                    compId = o.CompartmentA;
                else
                    continue;

                var comp = _graph.GetCompartment(compId);
                float openingTop = o.CenterY + o.Height * 0.5f;

                // Air can escape/enter only if the opening top is above the compartment water level
                if (openingTop <= comp.WaterLevelY) continue;

                float pExt = GasFlowMath.ExternalPressureAtm(_graph.SeaLevelY, o.CenterY);
                float pComp = comp.AirPressureAtm;

                if (Math.Abs(pComp - pExt) < 1e-4f) continue;

                if (pComp > pExt)
                {
                    // Air escapes to sea
                    float mdot = GasFlowMath.MassFlowKgS(o.EffectiveArea, pComp, pExt, comp.TemperatureK);
                    float dStd = mdot / GasFlowMath.Rho0 * dt;

                    // Clamp: don't let pressure drop below external
                    float maxLoss = comp.AirAmount - pExt * comp.AirVolume * GasFlowMath.T0 / comp.TemperatureK;
                    if (maxLoss < 0f) maxLoss = 0f;
                    if (dStd > maxLoss) dStd = maxLoss;

                    comp.AirAmount -= dStd;
                    TotalAirLostStd += dStd;

                    // Impulse from escaping air
                    if (Submarine != null && (o.NormalX != 0f || o.NormalY != 0f || o.NormalZ != 0f))
                    {
                        float vExit = GasFlowMath.ExitVelocity(pComp, pExt, comp.TemperatureK);
                        float force = mdot * vExit;
                        Submarine.GasForceLocalX -= o.NormalX * force;
                        Submarine.GasForceLocalY -= o.NormalY * force;
                        Submarine.GasForceLocalZ -= o.NormalZ * force;
                    }
                }
                else
                {
                    // Air enters from surface (above sea level) — repressurize
                    if (o.CenterY > _graph.SeaLevelY)
                    {
                        float mdot = GasFlowMath.MassFlowKgS(o.EffectiveArea, pExt, pComp, GasFlowMath.T0);
                        float dStd = mdot / GasFlowMath.Rho0 * dt;
                        float maxGain = pExt * comp.AirVolume * GasFlowMath.T0 / comp.TemperatureK - comp.AirAmount;
                        if (maxGain < 0f) maxGain = 0f;
                        if (dStd > maxGain) dStd = maxGain;
                        comp.AirAmount += dStd;

                        // Incoming fresh air dilutes CO2 toward ambient
                        if (_atmosphere != null && dStd > 0f && comp.AirAmount > 0f)
                        {
                            float co2 = _atmosphere.GetCo2Level(comp.Id);
                            float fraction = dStd / comp.AirAmount;
                            if (fraction > 1f) fraction = 1f;
                            _atmosphere.SetCo2Level(comp.Id, co2 * (1f - fraction) + 0.0004f * fraction);

                            float o2 = _atmosphere.GetOxygenLevel(comp.Id);
                            _atmosphere.SetOxygenLevel(comp.Id, o2 * (1f - fraction) + 1f * fraction);
                        }
                    }
                }
            }
        }

        void AdvectComposition(Compartment from, Compartment to, float transferStd)
        {
            if (_atmosphere == null || transferStd <= 0f) return;

            float fromO2 = _atmosphere.GetOxygenLevel(from.Id);
            float toO2 = _atmosphere.GetOxygenLevel(to.Id);
            float fromCo2 = _atmosphere.GetCo2Level(from.Id);
            float toCo2 = _atmosphere.GetCo2Level(to.Id);

            float totalTo = to.AirAmount;
            if (totalTo <= 0f) return;

            float frac = transferStd / totalTo;
            if (frac > 1f) frac = 1f;

            // Mix: new = old*(1-frac) + source*frac
            _atmosphere.SetOxygenLevel(to.Id, toO2 * (1f - frac) + fromO2 * frac);
            _atmosphere.SetCo2Level(to.Id, toCo2 * (1f - frac) + fromCo2 * frac);
        }

        void AdvectTemperature(Compartment from, Compartment to, float transferStd)
        {
            if (transferStd <= 0f) return;
            float totalTo = to.AirAmount;
            if (totalTo <= 0f) return;
            float frac = transferStd / totalTo;
            if (frac > 1f) frac = 1f;
            to.TemperatureK = to.TemperatureK * (1f - frac) + from.TemperatureK * frac;
        }

        void UpdateSealedStatus()
        {
            var comps = _graph.Compartments;
            int n = comps.Count;
            if (n == 0) return;

            if (_componentIds == null || _componentIds.Length < n)
            {
                _componentIds = new int[n];
                _componentVented = new bool[n];
            }

            for (int i = 0; i < n; i++)
            {
                _componentIds[i] = i;
                _componentVented[i] = false;
            }

            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                if (o.CompartmentA == Opening.Sea || o.CompartmentB == Opening.Sea) continue;
                float openingTop = o.CenterY + o.Height * 0.5f;
                var cA = _graph.GetCompartment(o.CompartmentA);
                var cB = _graph.GetCompartment(o.CompartmentB);
                if (openingTop > cA.WaterLevelY && openingTop > cB.WaterLevelY)
                    Union(o.CompartmentA, o.CompartmentB);
            }

            foreach (var o in _graph.Openings)
            {
                if (!o.IsOpen) continue;
                int seaSide = -1;
                if (o.CompartmentA == Opening.Sea && o.CompartmentB != Opening.Sea)
                    seaSide = o.CompartmentB;
                else if (o.CompartmentB == Opening.Sea && o.CompartmentA != Opening.Sea)
                    seaSide = o.CompartmentA;
                if (seaSide < 0) continue;

                float openingTop = o.CenterY + o.Height * 0.5f;
                var comp = _graph.GetCompartment(seaSide);
                if (openingTop > _graph.SeaLevelY && openingTop > comp.WaterLevelY)
                    _componentVented[Find(seaSide)] = true;
            }

            for (int i = 0; i < n; i++)
            {
                var c = comps[i];
                c.IsAirSealed = !_componentVented[Find(i)];
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
