using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class DebugOverlay : MonoBehaviour
    {
        bool _visible = true;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
                _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible) return;
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };

            float x = 10f, y = 10f;
            GUI.Label(new Rect(x, y, 300, 20), $"Tick: {bridge.TickDt * 1000f:F1}ms interval | Last: {bridge.LastTickMs:F3}ms", style);
            y += 20f;

            var graph = bridge.Graph;
            if (graph == null) return;

            foreach (var c in graph.Compartments)
            {
                float pct = c.WaterFraction * 100f;
                GUI.Label(new Rect(x, y, 400, 20),
                    $"C{c.Id}: {pct:F1}% ({c.WaterVolume:F2}/{c.Volume:F1}m³) Y={c.WaterLevelY:F2}m", style);
                y += 18f;
            }

            y += 5f;
            foreach (var o in graph.Openings)
            {
                string state = o.IsOpen ? "OPEN" : "CLOSED";
                GUI.Label(new Rect(x, y, 400, 20),
                    $"O{o.Id}({o.Kind}): {o.CompartmentA}->{o.CompartmentB} {state} A={o.Area:F3}m²", style);
                y += 18f;
            }
        }
    }
}
