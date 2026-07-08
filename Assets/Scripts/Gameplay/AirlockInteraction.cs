using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    // Lives on the ExteriorHatch console GameObject (interior, Airlock compartment), not on
    // the player. Both directions of travel are driven from here: interior->exterior via the
    // usual raycast-E pattern (works regardless of which GameObject hosts this component), and
    // exterior->interior via a distance check against the ship's hull while the player is
    // in EVA. There is only one hatch in the scene, so this effectively behaves like a
    // singleton and doesn't need proximity gating to run its per-frame checks.
    //
    // The ship no longer has a separate proxy object (M6 Phase 1: the hull physically lives in
    // the world under SimulationBridge.ShipRoot). "Ship position" here means the world position
    // of the authored ship center, which is exactly (sub.PositionX, -sub.Depth, sub.PositionZ)
    // regardless of heading (see ShipRootDriver / THE INVARIANT).
    public class AirlockInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;
        public float ReentryRange = 8f;

        float _suitWarningUntil = -1f;

        static Vector3 ShipCenterWorld(SimulationBridge bridge) =>
            new Vector3(bridge.SubState.PositionX, -bridge.SubState.Depth, bridge.SubState.PositionZ);

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            if (player.IsEVA)
                UpdateReentry(player, kb);
            else
                UpdateExit(player, kb);
        }

        void UpdateExit(PlayerController player, Keyboard kb)
        {
            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, MaxDistance)) return;
            if (hit.collider.GetComponentInParent<AirlockInteraction>() == null) return;

            if (!player.HasDivingSuit)
            {
                _suitWarningUntil = Time.time + 2f;
                return;
            }

            var bridge = SimulationBridge.Instance;
            if (bridge?.Terrain == null || bridge.SubState == null) return;

            Vector3 exteriorPos = ShipCenterWorld(bridge) + Vector3.down * 6f;
            float floorY = -bridge.Terrain.GetFloorDepthAt(exteriorPos.x, exteriorPos.z);
            if (exteriorPos.y < floorY + 2f)
                exteriorPos.y = floorY + 2f;
            if (exteriorPos.y > -1f)
                exteriorPos.y = -1f;

            player.EnterEVA(exteriorPos);
        }

        void UpdateReentry(PlayerController player, Keyboard kb)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.SubState == null) return;

            float dist = Vector3.Distance(player.transform.position, ShipCenterWorld(bridge));
            if (dist < ReentryRange && kb.eKey.wasPressedThisFrame)
                player.ExitEVA(new Vector3(21f, 0.6f, 3f));
        }

        void OnGUI()
        {
            if (Time.time < _suitWarningUntil)
            {
                var warnStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.red }
                };
                GUI.Label(new Rect(0, Screen.height * 0.5f - 40, Screen.width, 30),
                    "DIVING SUIT REQUIRED", warnStyle);
            }

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null || !player.IsEVA) return;

            float sw = Screen.width;
            float sh = Screen.height;

            float depth = -player.transform.position.y;
            var depthStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = depth > player.SuitCrushDepth ? Color.red : Color.white }
            };
            GUI.Label(new Rect(0, sh - 110, sw, 20), $"EVA depth {depth:F0}m", depthStyle);

            var bridge = SimulationBridge.Instance;
            if (bridge?.SubState == null) return;

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
            };

            float dist = Vector3.Distance(player.transform.position, ShipCenterWorld(bridge));
            string label = dist < ReentryRange ? "E: Re-enter airlock" : $"Sub: {dist:F0}m";
            GUI.Label(new Rect(0, sh - 90, sw, 20), label, hintStyle);
        }
    }
}
