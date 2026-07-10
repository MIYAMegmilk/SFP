using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class Pump : MonoBehaviour
    {
        public CompartmentDefinition TargetCompartment;
        public float PumpRate = 1f;
        public bool StartActive = true;
        public float MaxPumpDepth = 800f;
        public float PowerConsumption = 50f;

        public BilgePumpState State { get; private set; }

        static readonly Color ActiveColor = new(0.1f, 0.9f, 0.3f, 1f);
        static readonly Color InactiveColor = new(0.9f, 0.3f, 0.1f, 1f);
        MeshRenderer[] _renderers;
        bool _lastVisualState;

        void Start()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            int compId = TargetCompartment != null ? bridge.GetCompartmentId(TargetCompartment) : -1;
            var node = bridge.PowerGrid?.AddNode(0f, PowerConsumption);

            State = new BilgePumpState
            {
                PowerNodeId = node?.Id ?? -1,
                CompartmentId = compId,
                IsActive = StartActive,
                PumpRate = PumpRate,
                MaxPumpDepth = MaxPumpDepth,
                PowerConsumption = PowerConsumption,
            };

            Vector3 shipLocal = bridge.WorldToShip(transform.position);
            bridge.RegisterBilgePump(State, shipLocal);

            _renderers = GetComponentsInChildren<MeshRenderer>();
            _lastVisualState = State.IsActive;
            UpdateVisual();
        }

        void Update()
        {
            if (State == null) return;

            if (TryGetComponent<DeviceDegradation>(out var deg))
                State.IsFunctional = deg.IsFunctional;

            if (State.IsActive != _lastVisualState)
            {
                _lastVisualState = State.IsActive;
                UpdateVisual();
            }
        }

        void UpdateVisual()
        {
            if (_renderers == null) return;
            var color = State.IsActive ? ActiveColor : InactiveColor;
            foreach (var r in _renderers)
                r.material.color = color;
        }
    }
}
