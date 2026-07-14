using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class DoorInteraction : MonoBehaviour
    {
        public float MaxDistance = 20f;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (!kb.fKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            var ray = cursorLocked
                ? new Ray(cam.transform.position, cam.transform.forward)
                : cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance, ~0, QueryTriggerInteraction.Collide)) return;

            var openingDef = hit.collider.GetComponentInParent<OpeningDefinition>();
            if (openingDef == null) return;
            if (openingDef.Kind == SFP.Simulation.OpeningKind.Breach) return;
            if (openingDef.SimIndex < 0) return;

            var relay = DeviceRpcRelay.Instance;
            if (relay != null)
            {
                relay.RequestToggleDoor(openingDef.SimIndex);
            }
            else
            {
                // Standalone / no network: apply directly
                var bridge = SimulationBridge.Instance;
                if (bridge?.Graph != null)
                {
                    var opening = bridge.Graph.Openings[openingDef.SimIndex];
                    if (opening.IsLocked) return;
                    opening.IsOpen = !opening.IsOpen;
                    openingDef.IsOpen = opening.IsOpen;
                }
            }
        }
    }
}
