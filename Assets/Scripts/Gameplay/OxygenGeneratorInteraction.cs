using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class OxygenGeneratorInteraction : MonoBehaviour
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

            var gen = hit.collider.GetComponentInParent<OxygenGeneratorDefinition>();
            if (gen == null || gen.GeneratorIndex < 0) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var state = bridge.GetOxygenGenerator(gen.GeneratorIndex);
            if (state == null) return;

            state.IsEnabled = !state.IsEnabled;

            var mr = hit.collider.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = state.IsEnabled
                    ? new Color(0.1f, 0.7f, 0.3f)
                    : new Color(0.3f, 0.3f, 0.3f);
        }
    }
}
