using UnityEngine;
using UnityEngine.InputSystem;

namespace SFP.Gameplay
{
    public class PumpInteraction : MonoBehaviour
    {
        public float MaxDistance = 20f;

        static readonly Color ActiveColor = new(0.1f, 0.9f, 0.3f, 1f);
        static readonly Color InactiveColor = new(0.9f, 0.3f, 0.1f, 1f);

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (!kb.eKey.wasPressedThisFrame) return;

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var pump = hit.collider.GetComponentInParent<Pump>();
            if (pump == null) return;

            pump.IsActive = !pump.IsActive;
            UpdatePumpVisual(pump);
        }

        static void UpdatePumpVisual(Pump pump)
        {
            var renderers = pump.GetComponentsInChildren<MeshRenderer>();
            var color = pump.IsActive ? ActiveColor : InactiveColor;
            foreach (var r in renderers)
            {
                var mat = r.material;
                mat.color = color;
            }
        }
    }
}
