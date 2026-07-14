using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class PumpInteraction : MonoBehaviour
    {
        public float MaxDistance = 20f;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (!kb.eKey.wasPressedThisFrame) return;

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var pump = hit.collider.GetComponentInParent<Pump>();
            if (pump?.State == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            int pumpIndex = -1;
            for (int i = 0; i < bridge.BilgePumps.Count; i++)
            {
                if (bridge.BilgePumps[i] == pump.State)
                {
                    pumpIndex = i;
                    break;
                }
            }
            if (pumpIndex < 0) return;

            var relay = DeviceRpcRelay.Instance;
            if (relay != null)
                relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.TogglePump, IntVal = pumpIndex });
            else
                pump.State.IsActive = !pump.State.IsActive;
        }
    }
}
