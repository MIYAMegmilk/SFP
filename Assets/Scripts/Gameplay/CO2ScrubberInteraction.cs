using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Simulation;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class CO2ScrubberInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var def = hit.collider.GetComponentInParent<CO2ScrubberDefinition>();
            if (def == null || def.ScrubberIndex < 0) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var state = bridge.GetScrubber(def.ScrubberIndex);
            if (state == null) return;

            bool newState = !state.IsEnabled;
            var relay = DeviceRpcRelay.Instance;
            if (relay != null)
            {
                relay.RequestCommand(new DeviceCommand
                {
                    Kind = DeviceCommandKind.ToggleCO2Scrubber,
                    IntVal = def.ScrubberIndex
                });
            }
            else
            {
                state.IsEnabled = newState;
            }

            var mr = hit.collider.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = newState
                    ? new Color(0.1f, 0.7f, 0.7f)
                    : new Color(0.3f, 0.3f, 0.3f);
        }
    }
}
