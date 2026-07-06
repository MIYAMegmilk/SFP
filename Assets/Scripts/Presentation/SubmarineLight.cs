using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class SubmarineLight : MonoBehaviour
    {
        public float PowerConsumption = 10f;
        public CompartmentDefinition Compartment;

        Light _light;
        float _baseIntensity;
        int _powerNodeId = -1;
        float _flickerTimer;

        void Start()
        {
            _light = GetComponent<Light>();
            if (_light != null) _baseIntensity = _light.intensity;

            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid != null)
            {
                var node = bridge.PowerGrid.AddNode(0f, PowerConsumption);
                _powerNodeId = node.Id;
            }
        }

        void Update()
        {
            if (_light == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid == null)
            {
                _light.intensity = _baseIntensity;
                return;
            }

            float voltage = bridge.PowerGrid.GridVoltage;
            if (voltage < 0.2f)
            {
                _light.intensity = 0f;
                return;
            }

            if (voltage < 0.6f)
            {
                _flickerTimer += Time.deltaTime * (1f - voltage) * 20f;
                float flicker = 0.3f + 0.7f * (Mathf.PerlinNoise(_flickerTimer, _powerNodeId * 13.7f));
                _light.intensity = _baseIntensity * flicker * voltage;
            }
            else
            {
                float clamped = voltage > 1f ? 1f : voltage;
                _light.intensity = _baseIntensity * clamped;
            }
        }
    }
}
