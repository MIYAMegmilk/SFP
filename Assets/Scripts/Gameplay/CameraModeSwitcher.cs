using UnityEngine;
using UnityEngine.InputSystem;

namespace SFP.Gameplay
{
    public class CameraModeSwitcher : MonoBehaviour
    {
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.tabKey.wasPressedThisFrame) return;

            var player = FindFirstObjectByType<PlayerController>();
            var flyCam = FindFirstObjectByType<FlyCamera>();
            if (player == null || flyCam == null) return;

            var playerCam = player.GetComponentInChildren<Camera>();
            var specCam = flyCam.GetComponent<Camera>();
            if (playerCam == null || specCam == null) return;

            bool switchToSpec = playerCam.enabled;

            playerCam.enabled = !switchToSpec;
            player.enabled = !switchToSpec;
            playerCam.gameObject.tag = switchToSpec ? "Untagged" : "MainCamera";

            specCam.enabled = switchToSpec;
            flyCam.enabled = switchToSpec;
            specCam.gameObject.tag = switchToSpec ? "MainCamera" : "Untagged";

            SetToolsActive(player.gameObject, !switchToSpec);
            SetToolsActive(flyCam.gameObject, switchToSpec);

            if (switchToSpec)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        static void SetToolsActive(GameObject go, bool active)
        {
            if (go.TryGetComponent<BreachTool>(out var bt)) bt.enabled = active;
            if (go.TryGetComponent<DoorInteraction>(out var di)) di.enabled = active;
            if (go.TryGetComponent<RepairTool>(out var rt)) rt.enabled = active;
            if (go.TryGetComponent<PumpInteraction>(out var pi)) pi.enabled = active;
            if (go.TryGetComponent<ReactorInteraction>(out var ri)) ri.enabled = active;
            if (go.TryGetComponent<SteeringInteraction>(out var si)) si.enabled = active;
            if (go.TryGetComponent<OxygenGeneratorInteraction>(out var oi)) oi.enabled = active;
            if (go.TryGetComponent<SonarInteraction>(out var sni)) sni.enabled = active;
            if (go.TryGetComponent<StatusMonitorInteraction>(out var smi)) smi.enabled = active;
            if (go.TryGetComponent<FabricatorInteraction>(out var fi)) fi.enabled = active;
            if (go.TryGetComponent<DivingSuitInteraction>(out var dsi)) dsi.enabled = active;
            if (go.TryGetComponent<TurretInteraction>(out var ti)) ti.enabled = active;
            if (go.TryGetComponent<SuppressionInteraction>(out var spi)) spi.enabled = active;
        }
    }
}
