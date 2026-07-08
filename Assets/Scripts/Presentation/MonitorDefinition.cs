using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{ // force recompile
    public class MonitorDefinition : MonoBehaviour
    {
        public Component SourceDevice;
        public float PowerConsumption = 20f;
        public Vector2 ScreenSize = new Vector2(1.5f, 1.5f);
        public float MaxGUIDistance = 12f;

        SonarScreen _sonarScreen;
        StatusScreen _statusScreen;
        Material _mat;
        int _powerNodeId = -1;
        bool _powered;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly Color OffColor = new Color(0.01f, 0.02f, 0.015f);
        static readonly Color ScreenBg = new Color(0.02f, 0.06f, 0.04f);

        void Start()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Screen";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, -0.09f);
            quad.transform.localScale = new Vector3(ScreenSize.x, ScreenSize.y, 1f);

            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _mat.color = OffColor;
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid != null)
                _powerNodeId = bridge.PowerGrid.AddNode(0f, PowerConsumption).Id;
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || _mat == null) return;

            _powered = true;
            if (_powerNodeId >= 0)
            {
                var node = bridge.PowerGrid?.GetNode(_powerNodeId);
                _powered = node != null && node.IsActive;
            }

            Texture2D tex = null;
            if (_powered)
            {
                if (SourceDevice is SonarDefinition sonar)
                {
                    var state = sonar.SonarIndex >= 0 ? bridge.GetSonar(sonar.SonarIndex) : null;
                    if (state != null)
                    {
                        if (!state.IsActive) state.IsActive = true;
                        if (state.HasPower)
                        {
                            if (_sonarScreen == null) _sonarScreen = new SonarScreen { BakeContacts = true };
                            _sonarScreen.Tick(Time.deltaTime, state);
                            tex = _sonarScreen.Texture;
                        }
                    }
                }
                else if (SourceDevice == null || SourceDevice is StatusMonitorDefinition)
                {
                    if (_statusScreen == null) _statusScreen = new StatusScreen();
                    _statusScreen.Tick(Time.deltaTime, bridge);
                    tex = _statusScreen.Texture;
                }
            }

            if (tex != null)
            {
                _mat.SetTexture(BaseMapId, tex);
                _mat.color = Color.white;
            }
            else
            {
                _mat.SetTexture(BaseMapId, null);
                _mat.color = _powered && HasGUIFeed() ? ScreenBg : OffColor;
            }
        }

        bool HasGUIFeed()
        {
            return SourceDevice is ReactorDefinition || SourceDevice is EngineDefinition
                || SourceDevice is BatteryDefinition || SourceDevice is JunctionBoxDefinition
                || SourceDevice is NavigationTerminalDefinition;
        }

        void OnGUI()
        {
            if (!_powered || !HasGUIFeed()) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 screenPos = cam.WorldToScreenPoint(transform.position);
            if (screenPos.z <= 0f) return;

            float dist = screenPos.z;
            if (dist > MaxGUIDistance) return;

            // Check facing: monitor's -Z should face the camera
            Vector3 toCamera = cam.transform.position - transform.position;
            if (Vector3.Dot(transform.forward, toCamera) > 0f) return;

            float guiY = Screen.height - screenPos.y;
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            if (SourceDevice is ReactorDefinition rd)
                DrawReactorGUI(bridge, screenPos.x, guiY, rd);
            else if (SourceDevice is EngineDefinition)
                DrawEngineGUI(bridge, screenPos.x, guiY);
            else if (SourceDevice is BatteryDefinition bd)
                DrawBatteryGUI(bridge, screenPos.x, guiY, bd);
            else if (SourceDevice is JunctionBoxDefinition jd)
                DrawJunctionBoxGUI(bridge, screenPos.x, guiY, jd);
            else if (SourceDevice is NavigationTerminalDefinition)
                DrawNavGUI(bridge, screenPos.x, guiY);
        }

        void DrawReactorGUI(SimulationBridge bridge, float cx, float cy, ReactorDefinition rd)
        {
            var pg = bridge.PowerGrid;
            if (pg == null || rd.ReactorIndex < 0 || rd.ReactorIndex >= pg.Reactors.Count) return;
            var r = pg.Reactors[rd.ReactorIndex];

            float pw = 240f, ph = 170f;
            float left = cx - pw * 0.5f;
            float top = cy - ph * 0.5f;

            GUI.Box(new Rect(left, top, pw, ph), "");

            var title = TitleStyle();
            GUI.Label(new Rect(left, top + 3, pw, 22), "REACTOR", title);

            var s = LabelStyle();
            float y = top + 28;
            float lx = left + 8;
            float bw = pw - 16;

            s.normal.textColor = Color.white;
            GUI.Label(new Rect(lx, y, pw, 18), $"Fission: {r.FissionRate:F0}%", s);
            DrawBar(lx, y + 16, bw, 8, r.FissionRate / 100f, Color.red);
            y += 28;

            GUI.Label(new Rect(lx, y, pw, 18), $"Turbine: {r.TurbineOutput:F0}%", s);
            DrawBar(lx, y + 16, bw, 8, r.TurbineOutput / 100f, Color.cyan);
            y += 28;

            Color tc = r.Temperature > r.OptimalTemperature * 1.5f ? Color.red : Color.green;
            GUI.Label(new Rect(lx, y, pw, 18), $"Temp: {r.Temperature:F1}°C", s);
            DrawBar(lx, y + 16, bw, 8, r.Temperature / r.MeltdownTemperature, tc);
            y += 28;

            GUI.Label(new Rect(lx, y, pw, 18),
                $"Output: {r.CurrentPowerOutput:F0} kW  Fuel: {r.FuelRemaining:F1}%", s);
            y += 18;
            GUI.Label(new Rect(lx, y, pw, 18),
                $"Efficiency: {r.TemperatureEfficiency * 100f:F0}%", s);

            if (r.IsMeltingDown)
            {
                var w = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.red }
                };
                GUI.Label(new Rect(left, top + ph + 2, pw, 22), "!! MELTDOWN !!", w);
            }
        }

        void DrawEngineGUI(SimulationBridge bridge, float cx, float cy)
        {
            var engine = bridge.Engine;
            if (engine == null) return;

            float pw = 220f, ph = 100f;
            float left = cx - pw * 0.5f;
            float top = cy - ph * 0.5f;

            GUI.Box(new Rect(left, top, pw, ph), "");
            GUI.Label(new Rect(left, top + 3, pw, 22), "ENGINE", TitleStyle());

            var s = LabelStyle();
            float y = top + 28;
            float lx = left + 8;
            float bw = pw - 16;

            GUI.Label(new Rect(lx, y, pw, 18),
                $"Throttle: {engine.ThrottleSetting * 100f:F0}%", s);
            DrawBar(lx, y + 16, bw, 8, Mathf.Abs(engine.ThrottleSetting), Color.green);
            y += 28;

            GUI.Label(new Rect(lx, y, pw, 18),
                $"Thrust: {engine.CurrentThrust:F0} N  Cond: {engine.Condition:F0}%", s);
        }

        void DrawBatteryGUI(SimulationBridge bridge, float cx, float cy, BatteryDefinition bd)
        {
            var pg = bridge.PowerGrid;
            if (pg == null || bd.BatteryIndex < 0 || bd.BatteryIndex >= pg.Batteries.Count) return;
            var b = pg.Batteries[bd.BatteryIndex];

            float pw = 200f, ph = 70f;
            float left = cx - pw * 0.5f;
            float top = cy - ph * 0.5f;

            GUI.Box(new Rect(left, top, pw, ph), "");
            GUI.Label(new Rect(left, top + 3, pw, 22), "BATTERY", TitleStyle());

            var s = LabelStyle();
            Color cc = b.ChargeFraction > 0.3f ? Color.cyan : Color.red;
            s.normal.textColor = cc;
            GUI.Label(new Rect(left + 8, top + 28, pw, 18),
                $"Charge: {b.ChargeFraction * 100f:F0}%  ({b.Charge:F0}/{b.MaxCharge:F0} kWh)", s);
            DrawBar(left + 8, top + 48, pw - 16, 10, b.ChargeFraction, cc);
        }

        void DrawJunctionBoxGUI(SimulationBridge bridge, float cx, float cy, JunctionBoxDefinition jd)
        {
            var pg = bridge.PowerGrid;
            if (pg == null) return;

            float pw = 220f, ph = 90f;
            float left = cx - pw * 0.5f;
            float top = cy - ph * 0.5f;

            GUI.Box(new Rect(left, top, pw, ph), "");
            GUI.Label(new Rect(left, top + 3, pw, 22), "JUNCTION BOX", TitleStyle());

            var s = LabelStyle();
            float y = top + 28;
            float lx = left + 8;

            Color vc = pg.GridVoltage >= 0.8f ? Color.green : pg.GridVoltage >= 0.4f ? Color.yellow : Color.red;
            s.normal.textColor = vc;
            GUI.Label(new Rect(lx, y, pw, 18),
                $"Grid: {pg.GridVoltage * 100f:F0}%  ({pg.TotalProduction:F0}/{pg.TotalConsumption:F0} kW)", s);
            y += 20;

            if (jd.JunctionBoxIndex >= 0 && jd.JunctionBoxIndex < pg.Junctions.Count)
            {
                var jb = pg.Junctions[jd.JunctionBoxIndex];
                Color lc = jb.IsOverloaded ? Color.red : Color.white;
                s.normal.textColor = lc;
                GUI.Label(new Rect(lx, y, pw, 18),
                    $"Load: {jb.CurrentLoad:F0}/{jb.MaxLoad:F0} W  Cond: {jb.Condition:F0}%", s);
            }
        }

        void DrawNavGUI(SimulationBridge bridge, float cx, float cy)
        {
            var sub = bridge.SubState;
            if (sub == null) return;

            float pw = 240f, ph = 90f;
            float left = cx - pw * 0.5f;
            float top = cy - ph * 0.5f;

            GUI.Box(new Rect(left, top, pw, ph), "");
            GUI.Label(new Rect(left, top + 3, pw, 22), "NAVIGATION", TitleStyle());

            var s = LabelStyle();
            float y = top + 28;
            float lx = left + 8;

            GUI.Label(new Rect(lx, y, pw, 18),
                $"Depth: {sub.Depth:F1}m  Speed: {sub.HorizontalSpeed:F1} m/s", s);
            y += 18;
            GUI.Label(new Rect(lx, y, pw, 18),
                $"Heading: {sub.Heading:F0}°", s);
            y += 18;

            var nav = bridge.Navigation;
            if (nav != null)
            {
                string depthHold = nav.DepthHoldEnabled ? $"HOLD {nav.DesiredDepth:F0}m" : "MANUAL";
                GUI.Label(new Rect(lx, y, pw, 18), $"Depth: {depthHold}", s);
            }
        }

        static GUIStyle TitleStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13, fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };
        }

        static GUIStyle LabelStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = Color.white }
            };
        }

        static void DrawBar(float x, float y, float w, float h, float fraction, Color color)
        {
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(fraction), h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
