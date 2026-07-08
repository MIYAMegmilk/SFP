using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class SteeringInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;
        public float WaypointArrivalDistance = 80f;

        bool _isSteering;
        int _targetWaypointIndex = 1;

        public bool IsSteering => _isSteering;

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
            ConsoleFocus.Acquire(this);
        }

        void UpdateSteering(Keyboard kb)
        {
            var bridge = SimulationBridge.Instance;
            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
            {
                _isSteering = false;
                ConsoleFocus.Release(this);
                if (bridge?.SubState != null) bridge.SubState.RudderAngle = 0f;
                return;
            }

            if (bridge == null) return;

            var engine = bridge.Engine;
            var nav = bridge.Navigation;
            var sub = bridge.SubState;
            if (sub == null) return;

            float throttleSpeed = 0.5f * Time.deltaTime;
            float depthSpeed = 10f * Time.deltaTime;

            if (engine != null)
            {
                if (kb.wKey.isPressed)
                    engine.ThrottleSetting = Mathf.Clamp(engine.ThrottleSetting + throttleSpeed, -1f, 1f);
                if (kb.sKey.isPressed)
                    engine.ThrottleSetting = Mathf.Clamp(engine.ThrottleSetting - throttleSpeed, -1f, 1f);
            }

            float rudder = 0f;
            if (kb.aKey.isPressed) rudder -= 1f;
            if (kb.dKey.isPressed) rudder += 1f;
            sub.RudderAngle = rudder;

            if (nav != null)
            {
                if (kb.tabKey.wasPressedThisFrame)
                    nav.AutoPilotEnabled = !nav.AutoPilotEnabled;

                if (kb.upArrowKey.isPressed)
                    nav.DesiredDepth = Mathf.Max(0f, nav.DesiredDepth - depthSpeed);
                if (kb.downArrowKey.isPressed)
                    nav.DesiredDepth += depthSpeed;
                if (kb.upArrowKey.isPressed || kb.downArrowKey.isPressed)
                    nav.DepthHoldEnabled = true;

                // M: set autopilot heading/speed toward current mission objective
                if (kb.mKey.wasPressedThisFrame)
                {
                    var mm = FindFirstObjectByType<MissionManager>();
                    var missions = mm?.Missions;
                    if (missions?.Current != null)
                    {
                        float bearing = missions.BearingToTarget(sub);
                        nav.DesiredHeading = bearing;
                        nav.DesiredSpeed = 4f;
                        nav.AutoPilotEnabled = true;
                    }
                }
            }

            var waypoints = bridge.Map?.ChannelWaypoints;
            if (waypoints != null && waypoints.Count > 0)
            {
                if (_targetWaypointIndex >= waypoints.Count) _targetWaypointIndex = 0;

                if (kb.nKey.wasPressedThisFrame)
                    _targetWaypointIndex = (_targetWaypointIndex + 1) % waypoints.Count;

                var wp = waypoints[_targetWaypointIndex];
                float wdx = wp.X - sub.PositionX;
                float wdz = wp.Z - sub.PositionZ;
                float wdist = Mathf.Sqrt(wdx * wdx + wdz * wdz);
                if (wdist < WaypointArrivalDistance)
                    _targetWaypointIndex = (_targetWaypointIndex + 1) % waypoints.Count;
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
            var waypoints = bridge.Map?.ChannelWaypoints;
            bool showWaypoint = waypoints != null && waypoints.Count > 0;

            float cx = Screen.width * 0.5f;
            float top = Screen.height * 0.25f;
            float panelW = 300f;
            float panelH = 240f;
            if (showWaypoint) panelH += 40f;

            // Fused console (Tier 2+): the sonar UI is open alongside on the same console —
            // dock the helm panel to the left of the sonar circle so the two don't overlap.
            if (TryGetComponent<SonarInteraction>(out var fusedSonar) && fusedSonar.IsUsing)
            {
                cx = Screen.width * 0.5f - 130f - 24f - panelW * 0.5f;
                top = Screen.height * 0.5f - panelH * 0.5f;
            }

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

            string rudderLabel = sub.RudderAngle < -0.1f ? "◄ PORT" : sub.RudderAngle > 0.1f ? "STBD ►" : "CENTER";
            GUI.Label(new Rect(lx, y, panelW, 20), $"Rudder: {rudderLabel}  [A/D]", style);
            y += 20f;
            GUI.Label(new Rect(lx, y, panelW, 20), $"Depth Target: Up/Down", style);
            y += 22f;

            if (nav != null)
            {
                string dhStatus = nav.DepthHoldEnabled ? "AUTO" : "MANUAL";
                Color dhColor = nav.DepthHoldEnabled ? Color.green : new Color(1f, 0.7f, 0.2f);
                var dhStyle = new GUIStyle(style) { normal = { textColor = dhColor } };
                GUI.Label(new Rect(lx, y, panelW, 20),
                    $"Depth Hold: {dhStatus}  Target: {nav.DesiredDepth:F0}m", dhStyle);
                y += 22f;

                string apStatus = nav.AutoPilotEnabled ? "ON" : "OFF";
                Color apColor = nav.AutoPilotEnabled ? Color.green : Color.gray;
                var apStyle = new GUIStyle(style) { normal = { textColor = apColor } };
                GUI.Label(new Rect(lx, y, panelW, 20),
                    $"AutoPilot (speed/heading): {apStatus} [Tab]", apStyle);
                y += 22f;
            }

            if (showWaypoint)
            {
                int idx = _targetWaypointIndex >= waypoints.Count ? 0 : _targetWaypointIndex;
                var wp = waypoints[idx];
                float wdx = wp.X - sub.PositionX;
                float wdz = wp.Z - sub.PositionZ;
                float wdist = Mathf.Sqrt(wdx * wdx + wdz * wdz);
                float bearing = Mathf.Atan2(wdx, wdz) * Mathf.Rad2Deg;
                if (bearing < 0f) bearing += 360f;
                float delta = Mathf.DeltaAngle(sub.Heading, bearing);

                string turnHint = delta < -10f ? "<<" : delta > 10f ? ">>" : "ON COURSE";
                bool onCourse = delta >= -10f && delta <= 10f;
                Color wpColor = onCourse ? new Color(0.4f, 1f, 1f) : Color.white;
                var wpStyle = new GUIStyle(style) { normal = { textColor = wpColor } };

                GUI.Label(new Rect(lx, y, panelW, 20),
                    $"WP {idx}/{waypoints.Count}: {wdist:F0}m brg {bearing:F0}°  {turnHint}", wpStyle);
                y += 20f;
                GUI.Label(new Rect(lx, y, panelW, 20), "(N: next  M: autopilot to mission)", style);
                y += 22f;
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
