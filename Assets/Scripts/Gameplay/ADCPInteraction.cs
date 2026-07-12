using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class ADCPInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isUsing;
        ADCPDefinition _activeADCP;

        public bool IsUsing => _isUsing;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isUsing)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                {
                    _isUsing = false;
                    ConsoleFocus.Release(this);
                    _activeADCP = null;
                }
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var adcp = hit.collider.GetComponentInParent<ADCPDefinition>();
            if (adcp == null) return;

            _activeADCP = adcp;
            _isUsing = true;
            ConsoleFocus.Acquire(this);
        }

        ADCPState GetState()
        {
            if (_activeADCP == null || _activeADCP.ADCPIndex < 0) return null;
            return SimulationBridge.Instance?.GetADCP(_activeADCP.ADCPIndex);
        }

        void OnGUI()
        {
            if (!_isUsing) return;
            var state = GetState();
            if (state == null) return;

            var bridge = SimulationBridge.Instance;
            var sub = bridge?.SubState;
            if (sub == null) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float panelW = 420f;
            float panelH = 480f;

            float panelX = cx - panelW * 0.5f;
            float panelY = cy - panelH * 0.5f;

            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };
            GUI.Label(new Rect(cx - 80, panelY + 8, 160, 20), "ADCP", titleStyle);

            var dataStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.7f, 1f, 1f, 0.9f) }
            };

            float y = panelY + 35;
            float left = panelX + 12;

            string statusText = state.IsActive ? "ACTIVE" : "STANDBY";
            GUI.Label(new Rect(left, y, panelW - 24, 18), $"Status: {statusText}    Ship Depth: {sub.Depth:F0}m", dataStyle);
            y += 20;

            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.5f, 0.9f, 1f, 0.85f) }
            };
            GUI.Label(new Rect(left, y, panelW - 24, 18), "Current at Depth:", headerStyle);
            y += 18;
            GUI.Label(new Rect(left, y, panelW - 24, 18),
                $"  Speed: {state.MeasuredSpeed:F2} m/s  Bearing: {state.MeasuredBearing:F0}°",
                dataStyle);
            y += 18;
            GUI.Label(new Rect(left, y, panelW - 24, 18),
                $"  Vx: {state.MeasuredVelX:F2}  Vz: {state.MeasuredVelZ:F2} m/s",
                dataStyle);
            y += 22;

            // Depth profile
            GUI.Label(new Rect(left, y, panelW - 24, 18), "Depth Profile:", headerStyle);
            y += 18;

            var binStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.6f, 0.95f, 0.95f, 0.8f) }
            };

            for (int i = 0; i < ADCPState.BinCount; i++)
            {
                float barWidth = Mathf.Clamp01(state.BinSpeed[i] / 2.9f) * 100f;
                GUI.Label(new Rect(left, y, panelW - 24, 16),
                    $"{state.BinDepth[i]:F0}m  {state.BinSpeed[i]:F2} m/s",
                    binStyle);

                GUI.color = new Color(0f, 0.8f, 1f, 0.6f);
                GUI.DrawTexture(new Rect(left + 150, y + 3, barWidth, 10), Texture2D.whiteTexture);
                GUI.color = Color.white;

                float binBearing = Mathf.Atan2(state.BinVelX[i], state.BinVelZ[i]) * Mathf.Rad2Deg;
                if (binBearing < 0f) binBearing += 360f;
                GUI.Label(new Rect(left + 260, y, 50, 16), $"{binBearing:F0}°", binStyle);

                // Direction dot relative to ship heading
                float relRad = (binBearing - sub.Heading) * Mathf.Deg2Rad;
                float dotCx = left + 320;
                float dotCy = y + 8;
                float dotR = 6f;
                GUI.color = new Color(0f, 0.9f, 1f, 0.8f);
                GUI.DrawTexture(new Rect(dotCx + Mathf.Sin(relRad) * dotR - 2,
                    dotCy - Mathf.Cos(relRad) * dotR - 2, 4, 4), Texture2D.whiteTexture);
                GUI.color = Color.white;

                y += 16;
            }

            y += 10;

            // History graph (tidal pattern)
            GUI.Label(new Rect(left, y, panelW - 24, 18), "History (speed):", headerStyle);
            y += 18;

            float graphX = left;
            float graphW = panelW - 24;
            float graphH = 50f;

            // Graph background
            GUI.color = new Color(0.05f, 0.1f, 0.15f, 0.8f);
            GUI.DrawTexture(new Rect(graphX, y, graphW, graphH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (state.HistoryCount > 1)
            {
                // Find max speed in history for scaling
                float maxHist = 0.5f;
                for (int i = 0; i < state.HistoryCount; i++)
                {
                    int idx = (state.HistoryHead - state.HistoryCount + i + ADCPState.HistorySize) % ADCPState.HistorySize;
                    if (state.HistorySpeed[idx] > maxHist) maxHist = state.HistorySpeed[idx];
                }

                // Draw speed line
                GUI.color = new Color(0f, 0.9f, 1f, 0.9f);
                for (int i = 0; i < state.HistoryCount - 1; i++)
                {
                    int idx0 = (state.HistoryHead - state.HistoryCount + i + ADCPState.HistorySize) % ADCPState.HistorySize;
                    int idx1 = (idx0 + 1) % ADCPState.HistorySize;
                    float x0 = graphX + (float)i / (state.HistoryCount - 1) * graphW;
                    float x1 = graphX + (float)(i + 1) / (state.HistoryCount - 1) * graphW;
                    float y0 = y + graphH - (state.HistorySpeed[idx0] / maxHist) * graphH;
                    float y1 = y + graphH - (state.HistorySpeed[idx1] / maxHist) * graphH;

                    // Simple line as small rects
                    float segLen = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    int steps = Mathf.Max(1, (int)(segLen / 3f));
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = (float)s / steps;
                        float px = Mathf.Lerp(x0, x1, t);
                        float py = Mathf.Lerp(y0, y1, t);
                        GUI.DrawTexture(new Rect(px - 1, py - 1, 2, 2), Texture2D.whiteTexture);
                    }
                }
                GUI.color = Color.white;

                // Scale label
                var scaleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    normal = { textColor = new Color(0.5f, 0.8f, 0.8f, 0.6f) }
                };
                GUI.Label(new Rect(graphX + graphW - 50, y, 50, 12), $"{maxHist:F1} m/s", scaleStyle);

                // Time span label
                float totalTimeSec = state.HistoryCount * 6f;
                string timeLabel = totalTimeSec >= 60f ? $"{totalTimeSec / 60f:F0} min" : $"{totalTimeSec:F0}s";
                GUI.Label(new Rect(graphX, y + graphH, 60, 12), timeLabel, scaleStyle);
            }

            y += graphH + 16;

            // Tidal prediction
            if (state.EstimatedTidalPeriod > 0f)
            {
                var tidalStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    normal = { textColor = new Color(0.9f, 0.9f, 0.4f, 0.9f) }
                };
                float periodMin = state.EstimatedTidalPeriod / 60f;
                float nextReversal = state.EstimatedTidalPeriod * 0.5f - state.TimeSinceLastReversal;
                if (nextReversal < 0f) nextReversal = 0f;
                GUI.Label(new Rect(left, y, panelW - 24, 16),
                    $"Tidal period: ~{periodMin:F1} min  Next reversal: ~{nextReversal:F0}s", tidalStyle);
                y += 18;
            }

            y += 4;
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - 80, y, 160, 20), "E/Esc: Close", hintStyle);
        }
    }
}
