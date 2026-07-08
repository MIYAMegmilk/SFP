using System.Collections.Generic;

namespace SFP.Simulation
{
    public sealed class PowerNode
    {
        public int Id;
        public float Production;
        public float Consumption;
        public bool IsActive = true;
        public bool IsEnabled = true;
    }

    public sealed class ReactorState
    {
        public int PowerNodeId = -1;
        public int CompartmentId = -1;
        public float FissionRate;
        public float TurbineOutput;
        public float Temperature;
        public float FuelRemaining = 100f;
        public float FuelConsumptionRate = 0.005f;
        public float MaxPowerOutput = 2000f;
        public float OptimalTemperature = 50f;
        public float MeltdownTemperature = 100f;
        public float CoolingRate = 5f;
        public float HeatRate = 8f;

        public float Condition = 100f;
        public float MeltdownProgress;
        public float MeltdownFuseSeconds = 15f;
        public bool HasExploded;

        public bool IsMeltingDown => Temperature >= MeltdownTemperature;
        public float CurrentPowerOutput { get; private set; }
        public float TemperatureEfficiency { get; private set; }

        public void Tick(float dt)
        {
            if (HasExploded || FuelRemaining <= 0f)
            {
                FissionRate = 0f;
                FuelRemaining = 0f;
                if (HasExploded) { CurrentPowerOutput = 0f; return; }
            }

            float effectiveHeatRate = HeatRate * (1f + (100f - Condition) * 0.005f);
            float heatGeneration = FissionRate * 0.01f * effectiveHeatRate;
            float turbineCooling = TurbineOutput * 0.01f * CoolingRate;
            Temperature += (heatGeneration - turbineCooling) * dt;
            if (Temperature < 0f) Temperature = 0f;

            float tempDelta = System.Math.Abs(Temperature - OptimalTemperature);
            float halfRange = (MeltdownTemperature - OptimalTemperature) * 0.8f;
            TemperatureEfficiency = 1f - (tempDelta / halfRange);
            if (TemperatureEfficiency < 0f) TemperatureEfficiency = 0f;
            if (TemperatureEfficiency > 1f) TemperatureEfficiency = 1f;

            CurrentPowerOutput = MaxPowerOutput * (TurbineOutput * 0.01f) * TemperatureEfficiency * (Condition * 0.01f);

            FuelRemaining -= FissionRate * 0.01f * FuelConsumptionRate * dt;
        }
    }

    public sealed class BatteryState
    {
        public int PowerNodeId = -1;
        public int CompartmentId = -1;
        public float Charge;
        public float MaxCharge = 1000f;
        public float MaxChargeRate = 100f;
        public float MaxDischargeRate = 200f;

        public float ChargeFraction => MaxCharge > 0f ? Charge / MaxCharge : 0f;

        public float Tick(float dt, float gridSurplus)
        {
            if (gridSurplus > 0f)
            {
                float chargeAmount = System.Math.Min(gridSurplus, MaxChargeRate) * dt;
                float space = MaxCharge - Charge;
                if (chargeAmount > space) chargeAmount = space;
                Charge += chargeAmount;
                return -chargeAmount / dt;
            }
            else if (gridSurplus < 0f)
            {
                float needed = -gridSurplus;
                float canDischarge = System.Math.Min(needed, MaxDischargeRate);
                float available = Charge / dt;
                if (canDischarge > available) canDischarge = available;
                Charge -= canDischarge * dt;
                if (Charge < 0f) Charge = 0f;
                return canDischarge;
            }
            return 0f;
        }
    }

    public sealed class JunctionBoxState
    {
        public int PowerNodeId = -1;
        public int CompartmentId = -1;
        public float MaxLoad = 500f;
        public float CurrentLoad;
        public float Condition = 100f;
        public float OverloadDamageRate = 5f;

        public bool IsOverloaded => CurrentLoad > MaxLoad;
        public bool IsFunctional => Condition > 0f;
        public bool IsShortedByWater;

        public bool FireTriggered { get; private set; }

        public void Tick(float dt)
        {
            FireTriggered = false;
            if (IsOverloaded)
            {
                Condition -= OverloadDamageRate * (CurrentLoad / MaxLoad) * dt;
                if (Condition <= 50f && CurrentLoad > MaxLoad * 1.5f)
                    FireTriggered = true;
            }
            if (Condition < 0f) Condition = 0f;
        }
    }

    public sealed class PowerGrid
    {
        readonly List<PowerNode> _nodes = new List<PowerNode>();
        readonly List<ReactorState> _reactors = new List<ReactorState>();
        readonly List<BatteryState> _batteries = new List<BatteryState>();
        readonly List<JunctionBoxState> _junctions = new List<JunctionBoxState>();
        int _nextId;

        public IReadOnlyList<ReactorState> Reactors => _reactors;
        public IReadOnlyList<BatteryState> Batteries => _batteries;
        public IReadOnlyList<JunctionBoxState> Junctions => _junctions;
        public float TotalProduction { get; private set; }
        public float TotalConsumption { get; private set; }
        public float GridVoltage { get; private set; } = 1f;

        // Debug: pin the grid at nominal voltage so devices never brown out.
        public bool UnlimitedPower;

        public PowerNode AddNode(float production, float consumption)
        {
            var node = new PowerNode { Id = _nextId++, Production = production, Consumption = consumption };
            _nodes.Add(node);
            return node;
        }

        public PowerNode GetNode(int id)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i].Id == id) return _nodes[i];
            return null;
        }

        public ReactorState AddReactor(float maxPower)
        {
            var node = AddNode(maxPower, 0f);
            var reactor = new ReactorState { PowerNodeId = node.Id, MaxPowerOutput = maxPower };
            _reactors.Add(reactor);
            return reactor;
        }

        public BatteryState AddBattery(float maxCharge, float initialCharge)
        {
            var node = AddNode(0f, 0f);
            var battery = new BatteryState
            {
                PowerNodeId = node.Id,
                MaxCharge = maxCharge,
                Charge = initialCharge
            };
            _batteries.Add(battery);
            return battery;
        }

        public JunctionBoxState AddJunctionBox(float maxLoad)
        {
            var node = AddNode(0f, 0f);
            var junction = new JunctionBoxState { PowerNodeId = node.Id, MaxLoad = maxLoad };
            _junctions.Add(junction);
            return junction;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _reactors.Count; i++)
            {
                var r = _reactors[i];
                r.Tick(dt);
                var node = GetNode(r.PowerNodeId);
                if (node != null)
                    node.Production = node.IsEnabled ? r.CurrentPowerOutput : 0f;
            }

            float production = 0f;
            float consumption = 0f;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                if (!n.IsEnabled) continue;
                production += n.Production;
                consumption += n.Consumption;
            }

            float surplus = production - consumption;

            for (int i = 0; i < _batteries.Count; i++)
            {
                float delta = _batteries[i].Tick(dt, surplus);
                surplus += delta * dt;
                production += delta > 0f ? delta : 0f;
            }

            TotalProduction = production;
            TotalConsumption = consumption;
            GridVoltage = consumption > 0.01f ? production / consumption : (production > 0f ? 1.5f : 0f);
            if (GridVoltage > 1.5f) GridVoltage = 1.5f;

            float loadPerJunction = _junctions.Count > 0 ? consumption / _junctions.Count : 0f;
            float condSum = 0f;
            for (int i = 0; i < _junctions.Count; i++)
            {
                _junctions[i].CurrentLoad = loadPerJunction;
                _junctions[i].Tick(dt);
                condSum += _junctions[i].Condition;
            }
            if (_junctions.Count > 0)
            {
                float avgCond = condSum / (_junctions.Count * 100f);
                GridVoltage *= 0.3f + 0.7f * avgCond;
            }

            if (UnlimitedPower)
                GridVoltage = 1f;

            float minVoltage = 0.2f;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                if (n.Consumption > 0f)
                    n.IsActive = n.IsEnabled && GridVoltage >= minVoltage;
            }
        }
    }
}
