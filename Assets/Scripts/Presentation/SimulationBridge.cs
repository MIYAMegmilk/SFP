using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class SimulationBridge : MonoBehaviour
    {
        public float SeaLevelY = 100f;
        public float TickRate = 30f;

        CompartmentGraph _graph;
        FlowSolver _solver;
        float _tickAccumulator;
        float _dt;

        readonly Dictionary<CompartmentDefinition, int> _defToId = new();
        readonly Dictionary<int, float> _prevWaterLevels = new();
        readonly Dictionary<int, float> _currWaterLevels = new();

        public CompartmentGraph Graph => _graph;
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
            _graph = new CompartmentGraph { SeaLevelY = SeaLevelY };
            _solver = new FlowSolver();

            var compartmentDefs = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var def in compartmentDefs)
            {
                var c = _graph.AddCompartment(def.FloorY, def.Height, def.FloorArea);
                _defToId[def] = c.Id;
                _prevWaterLevels[c.Id] = c.WaterLevelY;
                _currWaterLevels[c.Id] = c.WaterLevelY;
            }

            var openingDefs = FindObjectsByType<OpeningDefinition>(FindObjectsSortMode.None);
            foreach (var def in openingDefs)
            {
                int idA = def.CompartmentA != null ? _defToId[def.CompartmentA] : Opening.Sea;
                int idB = def.CompartmentB != null ? _defToId[def.CompartmentB] : Opening.Sea;
                var o = _graph.AddOpening(def.Kind, idA, idB, def.Area,
                    def.transform.position.y, def.Height, def.IsOpen);
                def.SimIndex = o.Id;
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
                _solver.Tick(_graph, _dt);
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

        public Opening AddBreachAtRuntime(CompartmentDefinition target, float area, float centerY, float height)
        {
            int id = GetCompartmentId(target);
            if (id < 0) return null;
            return _graph.AddOpening(OpeningKind.Breach, Opening.Sea, id, area, centerY, height);
        }
    }
}
