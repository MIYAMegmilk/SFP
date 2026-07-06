using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class ReactorInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isControlling;
        ReactorDefinition _activeReactor;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isControlling)
            {
                UpdateControl(kb);
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var reactor = hit.collider.GetComponentInParent<ReactorDefinition>();
            if (reactor == null) return;

            _activeReactor = reactor;
            _isControlling = true;
        }

        void UpdateControl(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
            {
                _isControlling = false;
                _activeReactor = null;
                return;
            }

            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid == null || _activeReactor == null ||
                _activeReactor.ReactorIndex < 0) return;

            var reactor = bridge.PowerGrid.Reactors[_activeReactor.ReactorIndex];

            float adjustSpeed = 20f * Time.deltaTime;
            if (kb.upArrowKey.isPressed)
                reactor.FissionRate = Mathf.Clamp(reactor.FissionRate + adjustSpeed, 0f, 100f);
            if (kb.downArrowKey.isPressed)
                reactor.FissionRate = Mathf.Clamp(reactor.FissionRate - adjustSpeed, 0f, 100f);
            if (kb.rightArrowKey.isPressed)
                reactor.TurbineOutput = Mathf.Clamp(reactor.TurbineOutput + adjustSpeed, 0f, 100f);
            if (kb.leftArrowKey.isPressed)
                reactor.TurbineOutput = Mathf.Clamp(reactor.TurbineOutput - adjustSpeed, 0f, 100f);
        }

        void OnGUI()
        {
            if (!_isControlling || _activeReactor == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid == null || _activeReactor.ReactorIndex < 0) return;

            var reactor = bridge.PowerGrid.Reactors[_activeReactor.ReactorIndex];

            float cx = Screen.width * 0.5f;
            float top = Screen.height * 0.3f;
            float panelW = 280f;
            float panelH = 200f;

            GUI.Box(new Rect(cx - panelW * 0.5f, top, panelW, panelH), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + 5, panelW, 25), "REACTOR", titleStyle);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
            float y = top + 35;
            float lx = cx - panelW * 0.5f + 10;

            GUI.Label(new Rect(lx, y, panelW, 20),
                $"Fission: {reactor.FissionRate:F0}%  [Up/Down]", style);
            DrawBar(lx, y + 18, panelW - 20, 10, reactor.FissionRate / 100f, Color.red);
            y += 35;

            GUI.Label(new Rect(lx, y, panelW, 20),
                $"Turbine: {reactor.TurbineOutput:F0}%  [Left/Right]", style);
            DrawBar(lx, y + 18, panelW - 20, 10, reactor.TurbineOutput / 100f, Color.cyan);
            y += 35;

            GUI.Label(new Rect(lx, y, panelW, 20),
                $"Temp: {reactor.Temperature:F1}°C", style);
            Color tempColor = reactor.Temperature > reactor.OptimalTemperature * 1.5f
                ? Color.red : Color.green;
            DrawBar(lx, y + 18, panelW - 20, 10,
                reactor.Temperature / reactor.MeltdownTemperature, tempColor);
            y += 35;

            GUI.Label(new Rect(lx, y, panelW, 20),
                $"Output: {reactor.CurrentPowerOutput:F0} kW | Fuel: {reactor.FuelRemaining:F1}%", style);
            y += 22;
            GUI.Label(new Rect(lx, y, panelW, 20),
                $"Efficiency: {reactor.TemperatureEfficiency * 100f:F0}%", style);

            if (reactor.IsMeltingDown)
            {
                var warnStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.red }
                };
                GUI.Label(new Rect(cx - 100, top + panelH + 5, 200, 30),
                    "!! MELTDOWN !!", warnStyle);
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
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            float fill = fraction > 1f ? 1f : (fraction < 0f ? 0f : fraction);
            GUI.DrawTexture(new Rect(x, y, w * fill, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
