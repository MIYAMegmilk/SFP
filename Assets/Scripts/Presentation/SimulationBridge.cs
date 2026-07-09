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
        public float SeaFloorDepth = 600f;
        public int MapSeed = 12345;

        // Debug: devices never brown out (grid pinned at 100%). Toggleable live in the inspector.
        public bool DebugUnlimitedPower;

        // Assigned by FloodTestShipBuilder: the transform ShipRootDriver moves every frame to
        // carry the whole interior (Hull + Player + ladders + devices) through the ocean world.
        // THE INVARIANT: simulation space == ship-local space == the authored build coordinates
        // (x 0..24, y 0..18, z 0..6). BuildGraph (Awake) runs while this is still at identity, so
        // any transform reads there are already ship-local; everything read at RUNTIME must be
        // converted via WorldToShip/ShipToWorld before it is compared against or fed into sim state.
        public Transform ShipRootRef;
        public Transform ShipRoot => ShipRootRef;

        public Vector3 WorldToShip(Vector3 worldPos) => ShipRootRef.InverseTransformPoint(worldPos);
        public Vector3 ShipToWorld(Vector3 shipLocalPos) => ShipRootRef.TransformPoint(shipLocalPos);

        CompartmentGraph _graph;
        ShallowWaterSystem _waterSystem;
        SubmarineState _subState;
        PowerGrid _powerGrid;
        AtmosphereSystem _atmosphere;
        DamageSystem _damageSystem;
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
        readonly Dictionary<int, CompartmentDefinition> _idToDef = new();
        readonly Dictionary<int, float> _prevWaterLevels = new();
        readonly Dictionary<int, float> _currWaterLevels = new();

        public CompartmentGraph Graph => _graph;
        public ShallowWaterSystem WaterSystem => _waterSystem;
        public SubmarineState SubState => _subState;
        public PowerGrid PowerGrid => _powerGrid;
        public AtmosphereSystem Atmosphere => _atmosphere;
        public DamageSystem DamageSystem => _damageSystem;
        public ProceduralMapData Map { get; private set; }
        public TerrainModel Terrain { get; private set; }
        public MineSystem MineSystem { get; private set; }
        public CreatureSystem Creatures { get; private set; }
        public OceanCurrentField OceanCurrents { get; private set; }
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
                _idToDef[c.Id] = def;

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
                    Capacity = btd.Capacity,
                    PumpRate = btd.PumpRate,
                    PowerConsumption = btd.PowerConsumption,
                    CurrentFillLevel = btd.InitialFillLevel,
                    TargetFillLevel = btd.InitialFillLevel,
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
                var sonarAudio = sd.GetComponent<SonarAudio>();
                if (sonarAudio != null)
                    sonarAudio.Init(sonar);
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

            Map = new ProceduralMapData(MapSeed);
            Terrain = new TerrainModel { SeaFloorDepth = SeaFloorDepth, Map = Map };
            _subState.PositionX = Map.SpawnX;
            _subState.PositionZ = Map.SpawnZ;

            MineSystem = new MineSystem(MapSeed, Map);
            Creatures = new CreatureSystem(MapSeed, Map);
            OceanCurrents = new OceanCurrentField(MapSeed);

            _damageSystem = new DamageSystem(_graph, _subState);
            _damageSystem.Terrain = Terrain;
            if (_engine != null) _damageSystem.Engine = _engine;

            // Register 6 hull faces per compartment
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float minFloorY = float.MaxValue, maxCeilY = float.MinValue;
            foreach (var def in compartmentDefs)
            {
                float cx = def.transform.position.x;
                float cz = def.transform.position.z;
                float hx = def.LengthX * 0.5f;
                float hz = def.WidthZ * 0.5f;
                if (cx - hx < minX) minX = cx - hx;
                if (cx + hx > maxX) maxX = cx + hx;
                if (cz - hz < minZ) minZ = cz - hz;
                if (cz + hz > maxZ) maxZ = cz + hz;
                if (def.FloorY < minFloorY) minFloorY = def.FloorY;
                float ceil = def.FloorY + def.Height;
                if (ceil > maxCeilY) maxCeilY = ceil;
            }
            foreach (var def in compartmentDefs)
            {
                int cid = _defToId[def];
                float cx = def.transform.position.x;
                float cz = def.transform.position.z;
                float hx = def.LengthX * 0.5f;
                float hz = def.WidthZ * 0.5f;
                float ceil = def.FloorY + def.Height;
                _damageSystem.RegisterHullSection(cid, HullFace.East,    Mathf.Abs(cx + hx - maxX) < 0.1f);
                _damageSystem.RegisterHullSection(cid, HullFace.West,    Mathf.Abs(cx - hx - minX) < 0.1f);
                _damageSystem.RegisterHullSection(cid, HullFace.North,   Mathf.Abs(cz + hz - maxZ) < 0.1f);
                _damageSystem.RegisterHullSection(cid, HullFace.South,   Mathf.Abs(cz - hz - minZ) < 0.1f);
                _damageSystem.RegisterHullSection(cid, HullFace.Floor,   Mathf.Abs(def.FloorY - minFloorY) < 0.1f);
                _damageSystem.RegisterHullSection(cid, HullFace.Ceiling, Mathf.Abs(ceil - maxCeilY) < 0.1f);
            }

            _fireSystem = new FireSystem(_graph);
            _damageSystem.Fire = _fireSystem;

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
                _powerGrid.UnlimitedPower = DebugUnlimitedPower;
                _powerGrid.Tick(_dt);
                _damageSystem.Tick(_dt, _fireSystem, _powerGrid);
                MineSystem.Tick(_dt, _subState, _damageSystem);
                bool activeSonarPinging = false;
                for (int i = 0; i < _sonars.Count; i++)
                {
                    var s = _sonars[i];
                    if (s.IsActive && !s.IsPassive && s.HasPower) activeSonarPinging = true;
                }
                Creatures?.Tick(_dt, _subState, _damageSystem, Terrain, activeSonarPinging, OceanCurrents);
                _waterSystem.Tick(_dt);
                _engine?.Tick(_dt, _powerGrid);
                float ballastWater = 0f;
                for (int i = 0; i < _ballasts.Length; i++)
                {
                    _ballasts[i].Tick(_dt, _powerGrid);
                    ballastWater += _ballasts[i].WaterVolume;
                }
                _subState.ExternalBallastVolume = ballastWater;
                _navigation?.Tick(_dt, _subState, _engine, _ballasts);
                float thrust = _engine?.CurrentThrust ?? 0f;
                float curX = 0f, curZ = 0f;
                OceanCurrents?.Sample(_subState.PositionX, _subState.PositionZ, _subState.Depth,
                    out curX, out curZ);
                _subState.Tick(_dt, _graph, thrust, curX, curZ);
                _atmosphere.Tick(_dt);
                for (int i = 0; i < _oxygenGenerators.Count; i++)
                    _oxygenGenerators[i].Tick(_dt, _atmosphere, _powerGrid);
                for (int i = 0; i < _sonars.Count; i++)
                    _sonars[i].Tick(_dt, _powerGrid, _subState, Terrain, MineSystem, Creatures);
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
                    _fireSystem.Tick(_dt, _atmosphere, _powerGrid);
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

        public CompartmentDefinition GetCompartmentDef(int id)
        {
            return _idToDef.TryGetValue(id, out var def) ? def : null;
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
            // target.transform.position is WORLD space at RUNTIME (compartments are rigid
            // children of the moving ShipRoot); convert back to ship-local before delegating,
            // since the water grid is authored in ship-local space (see THE INVARIANT above).
            Vector3 shipLocal = WorldToShip(target.transform.position);
            return AddBreachAtRuntime(target, area, centerY, height,
                shipLocal.x, shipLocal.z);
        }

        // localX/localZ are SHIP-LOCAL coordinates (see THE INVARIANT above), matching the
        // domain of the water grid. Callers (e.g. BreachTool) must convert world-space hit
        // points via WorldToShip before calling this overload; nothing is converted here.
        public Opening AddBreachAtRuntime(CompartmentDefinition target, float area,
            float centerY, float height, float localX, float localZ)
        {
            int id = GetCompartmentId(target);
            if (id < 0) return null;
            var opening = _graph.AddOpening(OpeningKind.Breach, Opening.Sea, id,
                area, centerY, height);

            _waterSystem.AddBreachSource(opening, id, localX, localZ, centerY);

            return opening;
        }

        public Opening RegisterOpeningAtRuntime(OpeningDefinition def)
        {
            int idA = def.CompartmentA != null ? GetCompartmentId(def.CompartmentA) : Opening.Sea;
            int idB = def.CompartmentB != null ? GetCompartmentId(def.CompartmentB) : Opening.Sea;
            if (idA == Opening.Sea || idB == Opening.Sea) return null;

            // def.transform.position is WORLD space at RUNTIME (openings built via
            // BuiltStructureManager live under the moving ShipRoot); convert to ship-local
            // before feeding the sim/water grid, which are authored in ship-local space.
            Vector3 shipLocal = WorldToShip(def.transform.position);

            var o = _graph.AddOpening(def.Kind, idA, idB, def.Area,
                shipLocal.y, def.Height, def.IsOpen);
            def.SimIndex = o.Id;

            bool isVertical = def.Kind == OpeningKind.Hatch;
            float sillY = shipLocal.y - def.Height * 0.5f;
            float openingWidth = isVertical
                ? Mathf.Sqrt(def.Area)
                : def.Area / def.Height;

            _waterSystem.AddConnection(o, idA, idB,
                shipLocal.x, shipLocal.z,
                openingWidth, sillY, isVertical);
            return o;
        }
    }
}
