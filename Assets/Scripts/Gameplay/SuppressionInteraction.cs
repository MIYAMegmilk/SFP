using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class SuppressionInteraction : MonoBehaviour
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

            var suppression = hit.collider.GetComponentInParent<SuppressionSystemDefinition>();
            if (suppression == null || suppression.SuppressionIndex < 0) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var state = bridge.GetSuppression(suppression.SuppressionIndex);
            if (state == null) return;

            var relay = DeviceRpcRelay.Instance;
            if (relay != null)
                relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.ToggleSuppression, IntVal = suppression.SuppressionIndex });
            else
                state.IsActive = !state.IsActive;

            var mr = hit.collider.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = state.IsActive
                    ? new Color(0.3f, 0.7f, 1f)
                    : new Color(0.4f, 0.4f, 0.4f);
        }
    }
}
