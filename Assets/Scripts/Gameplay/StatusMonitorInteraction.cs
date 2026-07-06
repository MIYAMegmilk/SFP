using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class StatusMonitorInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isViewing;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isViewing)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                    _isViewing = false;
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            if (hit.collider.GetComponentInParent<StatusMonitorDefinition>() == null) return;
            _isViewing = true;
        }

        void OnGUI()
        {
            if (!_isViewing) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.Graph == null) return;

            float cx = Screen.width * 0.5f;
            float top = Screen.height * 0.15f;
            float panelW = 400f;
            float panelH = 400f;

            GUI.Box(new Rect(cx - panelW * 0.5f, top, panelW, panelH), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + 5, panelW, 25), "STATUS MONITOR", titleStyle);

            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            float lx = cx - panelW * 0.5f + 10;
            float y = top + 35;

            GUI.Label(new Rect(lx, y, 120, 18), "COMPARTMENT", headerStyle);
            GUI.Label(new Rect(lx + 130, y, 60, 18), "FLOOD", headerStyle);
            GUI.Label(new Rect(lx + 200, y, 50, 18), "O2", headerStyle);
            y += 20f;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            var compDefs = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);

            foreach (var comp in compDefs)
            {
                int id = bridge.GetCompartmentId(comp);
                if (id < 0) continue;
                var c = bridge.Graph.GetCompartment(id);
                if (c == null) continue;

                float flood = c.WaterFraction;
                float o2 = bridge.Atmosphere?.GetOxygenLevel(id) ?? 1f;

                Color nameColor = flood > 0.5f ? Color.red : (flood > 0.1f ? Color.yellow : Color.green);
                style.normal.textColor = nameColor;
                GUI.Label(new Rect(lx, y, 120, 18), comp.gameObject.name, style);

                DrawBar(lx + 130, y + 2, 60, 12, flood, flood > 0.5f ? Color.red : Color.blue);

                Color o2Color = o2 > 0.5f ? Color.green : (o2 > 0.2f ? Color.yellow : Color.red);
                DrawBar(lx + 200, y + 2, 50, 12, o2, o2Color);

                style.normal.textColor = Color.white;
                GUI.Label(new Rect(lx + 260, y, 100, 18),
                    $"{flood * 100f:F0}%  O2:{o2 * 100f:F0}%", style);
                y += 18f;
            }

            y += 10f;
            var power = bridge.PowerGrid;
            if (power != null)
            {
                style.normal.textColor = power.GridVoltage >= 0.8f ? Color.green : Color.yellow;
                GUI.Label(new Rect(lx, y, panelW, 20),
                    $"Power: {power.TotalProduction:F0}/{power.TotalConsumption:F0} kW  Grid: {power.GridVoltage * 100f:F0}%", style);
                y += 18f;

                for (int i = 0; i < power.Batteries.Count; i++)
                {
                    var b = power.Batteries[i];
                    style.normal.textColor = b.ChargeFraction > 0.3f ? Color.cyan : Color.red;
                    GUI.Label(new Rect(lx, y, panelW, 20),
                        $"  Battery {i}: {b.ChargeFraction * 100f:F0}%", style);
                    y += 16f;
                }
            }

            style.normal.textColor = Color.white;
            var sub = bridge.SubState;
            if (sub != null)
            {
                y += 5f;
                GUI.Label(new Rect(lx, y, panelW, 20),
                    $"Depth: {sub.Depth:F1}m  Speed: {sub.HorizontalSpeed:F1}m/s  Heading: {sub.Heading:F0}°", style);
            }

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + panelH - 22, panelW, 20),
                "E / Esc: Close", hintStyle);
        }

        void DrawBar(float x, float y, float w, float h, float fraction, Color color)
        {
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            float fill = fraction > 1f ? 1f : (fraction < 0f ? 0f : fraction);
            GUI.DrawTexture(new Rect(x, y, w * fill, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
