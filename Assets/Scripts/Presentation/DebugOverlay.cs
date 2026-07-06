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

            var sub = bridge.SubState;
            if (sub != null)
            {
                string depthStatus = sub.IsBelowCrushDepth ? "  !! CRUSH DEPTH !!" : "";
                GUI.Label(new Rect(x, y, 600, 20),
                    $"Depth: {sub.Depth:F1}m | Vel: {sub.Velocity:F2}m/s | Water: {sub.TotalWaterVolume:F1}m³{depthStatus}", style);
                y += 18f;
                GUI.Label(new Rect(x, y, 500, 20),
                    $"Heading: {sub.Heading:F0}° | HSpeed: {sub.HorizontalSpeed:F1}m/s | Pos: ({sub.PositionX:F0}, {sub.PositionZ:F0})", style);
                y += 20f;
            }

            var power = bridge.PowerGrid;
            if (power != null)
            {
                GUI.Label(new Rect(x, y, 500, 20),
                    $"Power: {power.TotalProduction:F0}/{power.TotalConsumption:F0} kW | Grid: {power.GridVoltage * 100f:F0}%", style);
                y += 18f;
                for (int i = 0; i < power.Reactors.Count; i++)
                {
                    var r = power.Reactors[i];
                    GUI.Label(new Rect(x, y, 500, 20),
                        $"  Reactor{i}: F={r.FissionRate:F0}% T={r.TurbineOutput:F0}% Temp={r.Temperature:F1}°C Out={r.CurrentPowerOutput:F0}kW Fuel={r.FuelRemaining:F1}%", style);
                    y += 16f;
                }
                for (int i = 0; i < power.Batteries.Count; i++)
                {
                    var b = power.Batteries[i];
                    GUI.Label(new Rect(x, y, 400, 20),
                        $"  Battery{i}: {b.Charge:F0}/{b.MaxCharge:F0} ({b.ChargeFraction * 100f:F0}%)", style);
                    y += 16f;
                }
            }

            GUI.Label(new Rect(x, y, 300, 20), $"Tick: {bridge.TickDt * 1000f:F1}ms interval | Last: {bridge.LastTickMs:F3}ms", style);
            y += 20f;

            var graph = bridge.Graph;
            if (graph == null) return;

            foreach (var c in graph.Compartments)
            {
                float pct = c.WaterFraction * 100f;
                float o2 = bridge.Atmosphere?.GetOxygenLevel(c.Id) ?? 1f;
                float fire = bridge.FireSystem?.GetFireIntensity(c.Id) ?? 0f;
                string extras = "";
                if (o2 < 0.9f) extras += $" O2={o2 * 100f:F0}%";
                if (fire > 0f) extras += $" FIRE={fire * 100f:F0}%";
                GUI.Label(new Rect(x, y, 600, 20),
                    $"C{c.Id}: {pct:F1}% ({c.WaterVolume:F2}/{c.Volume:F1}m³) Y={c.WaterLevelY:F2}m{extras}", style);
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
