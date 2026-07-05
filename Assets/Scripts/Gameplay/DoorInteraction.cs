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

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var openingDef = hit.collider.GetComponentInParent<OpeningDefinition>();
            if (openingDef == null) return;
            if (openingDef.Kind == SFP.Simulation.OpeningKind.Breach) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;
            if (openingDef.SimIndex < 0) return;

            var opening = bridge.Graph.Openings[openingDef.SimIndex];
            opening.IsOpen = !opening.IsOpen;
            openingDef.IsOpen = opening.IsOpen;
        }
    }
}
