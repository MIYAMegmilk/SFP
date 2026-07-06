using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class SteeringInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isSteering;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isSteering)
            {
                UpdateSteering(kb);
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            if (hit.collider.GetComponentInParent<NavigationTerminalDefinition>() == null) return;

            _isSteering = true;
        }

        void UpdateSteering(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
            {
                _isSteering = false;
                return;
            }

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var engine = bridge.Engine;
            var nav = bridge.Navigation;
            var sub = bridge.SubState;
            if (sub == null) return;

            float throttleSpeed = 0.5f * Time.deltaTime;
            float rudderSpeed = 30f * Time.deltaTime;
            float depthSpeed = 10f * Time.deltaTime;

            if (engine != null)
            {
                if (kb.wKey.isPressed)
                    engine.ThrottleSetting = Mathf.Clamp(engine.ThrottleSetting + throttleSpeed, -1f, 1f);
                if (kb.sKey.isPressed)
                    engine.ThrottleSetting = Mathf.Clamp(engine.ThrottleSetting - throttleSpeed, -1f, 1f);
            }

            if (kb.aKey.isPressed) sub.Heading -= rudderSpeed;
            if (kb.dKey.isPressed) sub.Heading += rudderSpeed;

            if (nav != null)
            {
                if (kb.tabKey.wasPressedThisFrame)
                    nav.AutoPilotEnabled = !nav.AutoPilotEnabled;

                if (kb.upArrowKey.isPressed)
                    nav.DesiredDepth = Mathf.Max(0f, nav.DesiredDepth - depthSpeed);
                if (kb.downArrowKey.isPressed)
                    nav.DesiredDepth += depthSpeed;
            }
        }

        void OnGUI()
        {
            if (!_isSteering) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.SubState == null) return;

            var sub = bridge.SubState;
            var engine = bridge.Engine;
            var nav = bridge.Navigation;

            float cx = Screen.width * 0.5f;
            float top = Screen.height * 0.25f;
            float panelW = 300f;
            float panelH = 220f;

            GUI.Box(new Rect(cx - panelW * 0.5f, top, panelW, panelH), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + 5, panelW, 25), "HELM", titleStyle);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
            float y = top + 35;
            float lx = cx - panelW * 0.5f + 10;

            GUI.Label(new Rect(lx, y, panelW, 20), $"Depth: {sub.Depth:F1}m  |  Velocity: {sub.Velocity:F2}m/s", style);
            y += 20f;
            GUI.Label(new Rect(lx, y, panelW, 20), $"Heading: {sub.Heading:F0}°  |  Speed: {sub.HorizontalSpeed:F1}m/s", style);
            y += 20f;
            GUI.Label(new Rect(lx, y, panelW, 20), $"Position: ({sub.PositionX:F0}, {sub.PositionZ:F0})", style);
            y += 25f;

            float throttle = engine?.ThrottleSetting ?? 0f;
            GUI.Label(new Rect(lx, y, panelW, 20), $"Throttle: {throttle * 100f:F0}%  [W/S]", style);
            DrawBar(lx, y + 18, panelW - 20, 10, (throttle + 1f) * 0.5f,
                throttle >= 0 ? Color.green : Color.red);
            y += 35f;

            GUI.Label(new Rect(lx, y, panelW, 20), $"Heading: A/D  |  Depth Target: Up/Down", style);
            y += 22f;

            if (nav != null)
            {
                string apStatus = nav.AutoPilotEnabled ? "ON" : "OFF";
                Color apColor = nav.AutoPilotEnabled ? Color.green : Color.gray;
                var apStyle = new GUIStyle(style) { normal = { textColor = apColor } };
                GUI.Label(new Rect(lx, y, panelW, 20),
                    $"AutoPilot: {apStatus} [Tab]  Target: {nav.DesiredDepth:F0}m", apStyle);
            }

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + panelH - 22, panelW, 20),
                "E / Esc: Leave Helm", hintStyle);
        }

        void DrawBar(float x, float y, float w, float h, float fraction, Color color)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            float fill = Mathf.Clamp01(fraction);
            GUI.DrawTexture(new Rect(x, y, w * fill, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
