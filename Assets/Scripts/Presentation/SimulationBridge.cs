using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class SimulationBridge : MonoBehaviour
    {
        public float InitialDepth = 200f;
        public float TickRate = 30f;
        public float WaterCellSize = 0.25f;

        public float HullVolume = 1700f;
        public float SubmarineDryMass = 1742500f;

        CompartmentGraph _graph;
        ShallowWaterSystem _waterSystem;
        SubmarineState _subState;
        PowerGrid _powerGrid;
        AtmosphereSystem _atmosphere;
        readonly System.Collections.Generic.List<OxygenGeneratorState> _oxygenGenerators = new();
        EngineState _engine;
        NavigationState _navigation;
        BallastTankState[] _ballasts = System.Array.Empty<BallastTankState>();
        readonly System.Collections.Generic.List<SonarState> _sonars = new();
        readonly System.Collections.Generic.List<FabricatorState> _fabricators = new();
        FireSystem _fireSystem;
        readonly System.Collections.Generic.List<TurretState> _turrets = new();
        readonly System.Collections.Generic.List<SuppressionSystemState> _suppressions = new();
        float _tickAccumulator;
        float _dt;

        readonly Dictionary<CompartmentDefinition, int> _defToId = new();
        readonly Dictionary<int, float> _prevWaterLevels = new();
        readonly Dictionary<int, float> _currWaterLevels = new();

        public CompartmentGraph Graph => _graph;
        public ShallowWaterSystem WaterSystem => _waterSystem;
        public SubmarineState SubState => _subState;
        public PowerGrid PowerGrid => _powerGrid;
        public AtmosphereSystem Atmosphere => _atmosphere;
        public FireSystem FireSystem => _fireSystem;
        public EngineState Engine => _engine;
        public NavigationState Navigation => _navigation;
        public BallastTankState[] Ballasts => _ballasts;
        public float TickDt => _dt;
        public float LastTickMs { get; private set; }

        public static SimulationBridge Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            _dt = 1f / TickRate;
            BuildGraph();
        }

        void BuildGraph()
        {
            _graph = new CompartmentGraph { SeaLevelY = InitialDepth };
            _waterSystem = new ShallowWaterSystem(_graph, WaterCellSize);
            _subState = new SubmarineState
            {
                Depth = InitialDepth,
                HullVolume = HullVolume,
                DryMass = SubmarineDryMass,
            };

            var compartmentDefs = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var def in compartmentDefs)
            {
                var c = _graph.AddCompartment(def.FloorY, def.Height, def.FloorArea);
                _defToId[def] = c.Id;

                float originX = def.transform.position.x - def.LengthX * 0.5f;
                float originZ = def.transform.position.z - def.WidthZ * 0.5f;
                _waterSystem.AddGrid(c.Id, def.FloorY, def.Height,
                    originX, originZ, def.LengthX, def.WidthZ);

                _prevWaterLevels[c.Id] = c.WaterLevelY;
                _currWaterLevels[c.Id] = c.WaterLevelY;
            }

            _powerGrid = new PowerGrid();

            var reactorDefs = FindObjectsByType<ReactorDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < reactorDefs.Length; i++)
            {
                var rd = reactorDefs[i];
                var reactor = _powerGrid.AddReactor(rd.MaxPowerOutput);
                reactor.FissionRate = rd.InitialFissionRate;
                reactor.TurbineOutput = rd.InitialTurbineOutput;
                if (rd.Compartment != null)
                    reactor.CompartmentId = GetCompartmentId(rd.Compartment);
                rd.ReactorIndex = i;
            }

            var junctionDefs = FindObjectsByType<JunctionBoxDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < junctionDefs.Length; i++)
            {
                var jd = junctionDefs[i];
                var junction = _powerGrid.AddJunctionBox(jd.MaxLoad);
                if (jd.Compartment != null)
                    junction.CompartmentId = GetCompartmentId(jd.Compartment);
                jd.JunctionBoxIndex = i;
            }

            var batteryDefs = FindObjectsByType<BatteryDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < batteryDefs.Length; i++)
            {
                var bd = batteryDefs[i];
                var battery = _powerGrid.AddBattery(bd.MaxCharge, bd.InitialCharge);
                if (bd.Compartment != null)
                    battery.CompartmentId = GetCompartmentId(bd.Compartment);
                bd.BatteryIndex = i;
            }

            var openingDefs = FindObjectsByType<OpeningDefinition>(FindObjectsSortMode.None);
            foreach (var def in openingDefs)
            {
                int idA = def.CompartmentA != null ? _defToId[def.CompartmentA] : Opening.Sea;
                int idB = def.CompartmentB != null ? _defToId[def.CompartmentB] : Opening.Sea;
                var o = _graph.AddOpening(def.Kind, idA, idB, def.Area,
                    def.transform.position.y, def.Height, def.IsOpen);
                def.SimIndex = o.Id;

                if (idA != Opening.Sea && idB != Opening.Sea)
                {
                    bool isVertical = def.Kind == OpeningKind.Hatch;
                    float sillY = def.transform.position.y - def.Height * 0.5f;
                    float openingWidth = isVertical
                        ? Mathf.Sqrt(def.Area)
                        : def.Area / def.Height;

                    _waterSystem.AddConnection(o, idA, idB,
                        def.transform.position.x, def.transform.position.z,
                        openingWidth, sillY, isVertical);
                }
            }

            _atmosphere = new AtmosphereSystem(_graph);

            var oxygenDefs = FindObjectsByType<OxygenGeneratorDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < oxygenDefs.Length; i++)
            {
                var od = oxygenDefs[i];
                var node = _powerGrid.AddNode(0f, od.PowerConsumption);
                var gen = new OxygenGeneratorState
                {
                    PowerNodeId = node.Id,
                    ProductionRate = od.ProductionRate,
                    TargetCompartmentId = od.TargetCompartment != null
                        ? GetCompartmentId(od.TargetCompartment) : -1,
                };
                _oxygenGenerators.Add(gen);
                od.GeneratorIndex = i;
            }

            var engineDefs = FindObjectsByType<EngineDefinition>(FindObjectsSortMode.None);
            if (engineDefs.Length > 0)
            {
                var ed = engineDefs[0];
                var eNode = _powerGrid.AddNode(0f, ed.PowerConsumption);
                _engine = new EngineState
                {
                    PowerNodeId = eNode.Id,
                    MaxThrust = ed.MaxThrust,
                    PowerConsumption = ed.PowerConsumption,
                    CompartmentId = ed.Compartment != null ? GetCompartmentId(ed.Compartment) : -1,
                };
                ed.EngineIndex = 0;
            }

            _navigation = new NavigationState { DesiredDepth = InitialDepth };

            var ballastDefs = FindObjectsByType<BallastTankDefinition>(FindObjectsSortMode.None);
            var ballastList = new System.Collections.Generic.List<BallastTankState>();
            for (int i = 0; i < ballastDefs.Length; i++)
            {
                var btd = ballastDefs[i];
                var bNode = _powerGrid.AddNode(0f, btd.PowerConsumption);
                var bs = new BallastTankState
                {
                    PowerNodeId = bNode.Id,
                    CompartmentId = btd.BallastCompartment != null
                        ? GetCompartmentId(btd.BallastCompartment) : -1,
                    PumpRate = btd.PumpRate,
                    PowerConsumption = btd.PowerConsumption,
                };
                ballastList.Add(bs);
                btd.BallastIndex = i;
            }
            _ballasts = ballastList.ToArray();

            var sonarDefs = FindObjectsByType<SonarDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < sonarDefs.Length; i++)
            {
                var sd = sonarDefs[i];
                var sNode = _powerGrid.AddNode(0f, sd.PowerConsumption);
                var sonar = new SonarState
                {
                    PowerNodeId = sNode.Id,
                    ActiveRange = sd.ActiveRange,
                    PowerConsumption = sd.PowerConsumption,
                };
                _sonars.Add(sonar);
                sd.SonarIndex = i;
            }

            var fabDefs = FindObjectsByType<FabricatorDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < fabDefs.Length; i++)
            {
                var fd = fabDefs[i];
                var fNode = _powerGrid.AddNode(0f, fd.PowerConsumption);
                var fab = new FabricatorState
                {
                    PowerNodeId = fNode.Id,
                    PowerConsumption = fd.PowerConsumption,
                    IsMedical = fd.IsMedical,
                };
                // Pre-stock with some materials for testing
                fab.InputInventory.Add(ItemId.SteelBar, 5);
                fab.InputInventory.Add(ItemId.Copper, 3);
                fab.InputInventory.Add(ItemId.Rubber, 3);
                fab.InputInventory.Add(ItemId.Plastic, 3);
                fab.InputInventory.Add(ItemId.Silicon, 2);
                _fabricators.Add(fab);
                fd.FabricatorIndex = i;
            }

            _fireSystem = new FireSystem(_graph);

            var turretDefs = FindObjectsByType<TurretDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < turretDefs.Length; i++)
            {
                var td = turretDefs[i];
                var tNode = _powerGrid.AddNode(0f, td.PowerConsumption);
                var turret = new TurretState
                {
                    PowerNodeId = tNode.Id,
                    Type = td.Type,
                    PowerConsumption = td.PowerConsumption,
                    AmmoCount = td.InitialAmmo,
                };
                _turrets.Add(turret);
                td.TurretIndex = i;
            }

            var suppDefs = FindObjectsByType<SuppressionSystemDefinition>(FindObjectsSortMode.None);
            for (int i = 0; i < suppDefs.Length; i++)
            {
                var sd = suppDefs[i];
                var sNode = _powerGrid.AddNode(0f, sd.PowerConsumption);
                var supp = new SuppressionSystemState
                {
                    PowerNodeId = sNode.Id,
                    TargetCompartmentId = sd.TargetCompartment != null
                        ? GetCompartmentId(sd.TargetCompartment) : -1,
                    ExtinguishRate = sd.ExtinguishRate,
                    WaterReserve = sd.WaterReserve,
                    PowerConsumption = sd.PowerConsumption,
                };
                _suppressions.Add(supp);
                sd.SuppressionIndex = i;
            }
        }

        void Update()
        {
            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= _dt)
            {
                _tickAccumulator -= _dt;
                foreach (var c in _graph.Compartments)
                    _prevWaterLevels[c.Id] = c.WaterLevelY;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _powerGrid.Tick(_dt);
                _waterSystem.Tick(_dt);
                _engine?.Tick(_dt, _powerGrid);
                for (int i = 0; i < _ballasts.Length; i++)
                    _ballasts[i].Tick(_dt, _graph, _powerGrid);
                _navigation?.Tick(_dt, _subState, _engine, _ballasts);
                float thrust = _engine?.CurrentThrust ?? 0f;
                float rudder = 0f;
                _subState.Tick(_dt, _graph, thrust, rudder);
                _atmosphere.Tick(_dt);
                for (int i = 0; i < _oxygenGenerators.Count; i++)
                    _oxygenGenerators[i].Tick(_dt, _atmosphere, _powerGrid);
                for (int i = 0; i < _sonars.Count; i++)
                    _sonars[i].Tick(_dt, _powerGrid, _subState);
                for (int i = 0; i < _fabricators.Count; i++)
                    _fabricators[i].Tick(_dt, _powerGrid);
                if (_fireSystem != null)
                {
                    for (int i = 0; i < _powerGrid.Junctions.Count; i++)
                    {
                        var j = _powerGrid.Junctions[i];
                        if (j.FireTriggered && j.CompartmentId >= 0)
                            _fireSystem.StartFire(j.CompartmentId, 0.1f);
                    }
                    _fireSystem.Tick(_dt, _atmosphere);
                }
                for (int i = 0; i < _turrets.Count; i++)
                    _turrets[i].Tick(_dt, _powerGrid);
                for (int i = 0; i < _suppressions.Count; i++)
                    _suppressions[i].Tick(_dt, _fireSystem, _powerGrid);
                sw.Stop();
                LastTickMs = (float)sw.Elapsed.TotalMilliseconds;

                foreach (var c in _graph.Compartments)
                    _currWaterLevels[c.Id] = c.WaterLevelY;
            }
        }

        public float GetInterpolatedWaterLevelY(int compartmentId)
        {
            if (!_prevWaterLevels.ContainsKey(compartmentId)) return 0f;
            float t = _dt > 0f ? _tickAccumulator / _dt : 0f;
            return Mathf.Lerp(_prevWaterLevels[compartmentId], _currWaterLevels[compartmentId], t);
        }

        public int GetCompartmentId(CompartmentDefinition def)
        {
            return _defToId.TryGetValue(def, out int id) ? id : -1;
        }

        public OxygenGeneratorState GetOxygenGenerator(int index)
        {
            return index >= 0 && index < _oxygenGenerators.Count ? _oxygenGenerators[index] : null;
        }

        public SonarState GetSonar(int index)
        {
            return index >= 0 && index < _sonars.Count ? _sonars[index] : null;
        }

        public FabricatorState GetFabricator(int index)
        {
            return index >= 0 && index < _fabricators.Count ? _fabricators[index] : null;
        }

        public TurretState GetTurret(int index)
        {
            return index >= 0 && index < _turrets.Count ? _turrets[index] : null;
        }

        public SuppressionSystemState GetSuppression(int index)
        {
            return index >= 0 && index < _suppressions.Count ? _suppressions[index] : null;
        }

        public Opening AddBreachAtRuntime(CompartmentDefinition target, float area,
            float centerY, float height)
        {
            return AddBreachAtRuntime(target, area, centerY, height,
                target.transform.position.x, target.transform.position.z);
        }

        public Opening AddBreachAtRuntime(CompartmentDefinition target, float area,
            float centerY, float height, float worldX, float worldZ)
        {
            int id = GetCompartmentId(target);
            if (id < 0) return null;
            var opening = _graph.AddOpening(OpeningKind.Breach, Opening.Sea, id,
                area, centerY, height);

            _waterSystem.AddBreachSource(opening, id, worldX, worldZ, centerY);

            return opening;
        }
    }
}
