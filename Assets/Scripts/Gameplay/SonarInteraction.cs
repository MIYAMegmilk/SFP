using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class SonarInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isUsing;
        SonarDefinition _activeSonar;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isUsing)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                {
                    _isUsing = false;
                    _activeSonar = null;
                }
                else if (kb.tabKey.wasPressedThisFrame)
                {
                    var state = GetSonarState();
                    if (state != null) state.IsPassive = !state.IsPassive;
                }
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var sonar = hit.collider.GetComponentInParent<SonarDefinition>();
            if (sonar == null) return;

            _activeSonar = sonar;
            _isUsing = true;

            var s = GetSonarState();
            if (s != null) s.IsActive = true;
        }

        SonarState GetSonarState()
        {
            if (_activeSonar == null || _activeSonar.SonarIndex < 0) return null;
            return SimulationBridge.Instance?.GetSonar(_activeSonar.SonarIndex);
        }

        void OnGUI()
        {
            if (!_isUsing) return;
            var state = GetSonarState();
            if (state == null) return;

            var sub = SimulationBridge.Instance?.SubState;
            if (sub == null) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float radius = 120f;

            GUI.Box(new Rect(cx - radius - 10, cy - radius - 30, radius * 2 + 20, radius * 2 + 60), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.green }
            };
            string mode = state.IsPassive ? "PASSIVE" : "ACTIVE";
            GUI.Label(new Rect(cx - 60, cy - radius - 25, 120, 20), $"SONAR [{mode}]", titleStyle);

            // Draw sonar circle
            GUI.color = new Color(0f, 0.3f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(cx - radius, cy - radius, radius * 2, radius * 2), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Draw sweep line
            float sweepAngle = state.PingProgress * 360f;
            float rad = sweepAngle * Mathf.Deg2Rad;
            float lx = cx + Mathf.Sin(rad) * radius;
            float ly = cy - Mathf.Cos(rad) * radius;
            DrawLine(cx, cy, lx, ly, new Color(0f, 1f, 0f, 0.4f));

            // Draw contacts as dots
            GUI.color = Color.green;
            foreach (var c in state.Contacts)
            {
                float crad = c.Bearing * Mathf.Deg2Rad;
                float dist = c.Distance / state.Range;
                if (dist > 1f) continue;
                float dotX = cx + Mathf.Sin(crad) * radius * dist;
                float dotY = cy - Mathf.Cos(crad) * radius * dist;
                GUI.DrawTexture(new Rect(dotX - 3, dotY - 3, 6, 6), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            // Crosshair
            GUI.color = new Color(0f, 0.6f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 1, cy - radius, 2, radius * 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - radius, cy - 1, radius * 2, 2), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Range rings
            var infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0f, 0.8f, 0f, 0.6f) }
            };
            GUI.Label(new Rect(cx + 3, cy - radius + 2, 60, 15), $"{state.Range:F0}m", infoStyle);
            GUI.Label(new Rect(cx + 3, cy - radius * 0.5f + 2, 60, 15), $"{state.Range * 0.5f:F0}m", infoStyle);

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - 100, cy + radius + 5, 200, 20),
                "Tab: Toggle Active/Passive | E/Esc: Close", hintStyle);
        }

        void DrawLine(float x1, float y1, float x2, float y2, Color color)
        {
            GUI.color = color;
            float dx = x2 - x1, dy = y2 - y1;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            int steps = (int)(len / 2f);
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0f;
                float px = x1 + dx * t;
                float py = y1 + dy * t;
                GUI.DrawTexture(new Rect(px - 1, py - 1, 2, 2), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
