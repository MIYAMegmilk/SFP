using UnityEngine;
using UnityEngine.InputSystem;

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

            pump.State.IsActive = !pump.State.IsActive;
        }
    }
}
