using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class SonarInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        const float MapRebuildInterval = 0.5f;
        // Matches the circle display (SonarScreen) so both panels fade in sync.
        const float SweepFadeSeconds = SonarScreen.SweepFadeSeconds;

        const int ProfileResX = 96;
        const int ProfileResY = 64;
        const float ProfileMaxDepth = 650f;
        const float ProfileBehindFrac = 0.2f;
        const float ProfileAheadFrac = 0.8f;
        // Panel pixel size (matches OnGUI: width 260, height radius*2) — the rotating sweep's
        // angles are computed in screen aspect so the drawn line and lit pixels agree.
        const float ProfilePanelW = 260f;
        const float ProfilePanelH = 240f;

        bool _isUsing;
        SonarDefinition _activeSonar;

        public bool IsUsing => _isUsing;

        // Circle display shared with wall monitors (mines drawn via IMGUI here, not baked).
        readonly SonarScreen _mapScreen = new SonarScreen();
        float _mapRebuildTimer;

        Color[] _profileBase;
        Color[] _profileDisplay;

        Texture2D _profileTex;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isUsing)
            {
                TickMapRebuild();

                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                {
                    _isUsing = false;
                    ConsoleFocus.Release(this);
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
            ConsoleFocus.Acquire(this);

            var s = GetSonarState();
            if (s != null) s.IsActive = true;
        }

        SonarState GetSonarState()
        {
            if (_activeSonar == null || _activeSonar.SonarIndex < 0) return null;
            return SimulationBridge.Instance?.GetSonar(_activeSonar.SonarIndex);
        }

        void TickMapRebuild()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || bridge.Map == null || bridge.SubState == null) return;

            var state = GetSonarState();
            if (state == null) return;

            _mapRebuildTimer -= Time.deltaTime;
            if (_profileTex == null || _mapRebuildTimer <= 0f)
            {
                _mapRebuildTimer = MapRebuildInterval;
                RebuildProfileTexture(bridge.Map, bridge.SubState, state.Range);
            }

            // The circle display is handled by the shared SonarScreen; the profile's terrain
            // colors are cached in _profileBase and only the sweep glow repaints per frame.
            _mapScreen.Tick(Time.deltaTime, state);
            ApplyProfileSweep(state, bridge.SubState);
        }

        // The profile sweep mirrors the circle display: a line rotating about the boat's spot
        // on the panel (20% from the left, at current depth), same phase as the circle sweep.
        // Pixels light as the line passes and fade to full dark over SweepFadeSeconds.
        void ApplyProfileSweep(SonarState state, SubmarineState sub)
        {
            if (_profileTex == null || _profileBase == null) return;
            if (_profileDisplay == null) _profileDisplay = new Color[ProfileResX * ProfileResY];

            float sweepDeg = state.PingProgress * 360f;
            float fadeDeg = 360f * SweepFadeSeconds / Mathf.Max(0.5f, state.PingInterval);

            float pivotX = ProfileBehindFrac * ProfilePanelW;
            float pivotY = Mathf.Clamp01(sub.Depth / ProfileMaxDepth) * ProfilePanelH;

            for (int j = 0; j < ProfileResY; j++)
            {
                // Texture row j = depth td drawn at panel-y (y-down), matching GUI space.
                float py = j / (float)(ProfileResY - 1) * ProfilePanelH;
                for (int i = 0; i < ProfileResX; i++)
                {
                    int idx = j * ProfileResX + i;
                    var c = _profileBase[idx];
                    float px = i / (float)(ProfileResX - 1) * ProfilePanelW;
                    float deg = Mathf.Atan2(px - pivotX, -(py - pivotY)) * Mathf.Rad2Deg;
                    if (deg < 0f) deg += 360f;
                    float lag = sweepDeg - deg;
                    if (lag < 0f) lag += 360f;
                    float glow = 1.25f * (1f - lag / fadeDeg);
                    float b = glow > 0f ? glow : 0f;
                    _profileDisplay[idx] = new Color(c.r * b, c.g * b, c.b * b, c.a);
                }
            }

            _profileTex.SetPixels(_profileDisplay);
            _profileTex.Apply(false);
        }

        void RebuildProfileTexture(ProceduralMapData map, SubmarineState sub, float range)
        {
            if (_profileTex == null)
            {
                _profileTex = new Texture2D(ProfileResX, ProfileResY, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var blockedColor = new Color(0f, 0.9f, 0.2f, 0.9f);
            var openColor = new Color(0f, 0.12f, 0.03f, 0.85f);

            float headingRad = sub.Heading * Mathf.Deg2Rad;
            float dirX = Mathf.Sin(headingRad);
            float dirZ = Mathf.Cos(headingRad);

            // Row 0 = depth 0 (surface); row ProfileResY-1 = ProfileMaxDepth. SetPixels lays rows
            // out bottom-to-top, so OnGUI draws this with a V-flip (Rect(0,1,1,-1)) to put row 0
            // (the surface) at the top of the panel.
            var pixels = new Color[ProfileResX * ProfileResY];
            // Echoes come from the rock surface, not its interior: draw only a thin line where
            // a column crosses the sea floor (or the underside of overhead rock).
            float rowStep = ProfileMaxDepth / (ProfileResY - 1);
            float surfaceThickness = rowStep * 1.2f;
            for (int i = 0; i < ProfileResX; i++)
            {
                float d = Mathf.Lerp(-ProfileBehindFrac * range, ProfileAheadFrac * range,
                    i / (float)(ProfileResX - 1));
                float worldX = sub.PositionX + dirX * d;
                float worldZ = sub.PositionZ + dirZ * d;
                float floor = map.GetFloorDepthAt(worldX, worldZ);
                float ceiling = map.GetCeilingDepthAt(worldX, worldZ);
                // Rock breaking the surface (an island wall): its echo face sits at depth 0.
                float floorSurface = Mathf.Max(floor, 0f);

                for (int j = 0; j < ProfileResY; j++)
                {
                    float td = j / (float)(ProfileResY - 1) * ProfileMaxDepth;
                    bool surface = Mathf.Abs(td - floorSurface) <= surfaceThickness
                        || (ceiling > 0f && Mathf.Abs(td - ceiling) <= surfaceThickness);
                    pixels[j * ProfileResX + i] = surface ? blockedColor : openColor;
                }
            }

            // Cache terrain colors; ApplyProfileSweep uploads them with scan-line brightness.
            _profileBase = pixels;
        }

        void OnGUI()
        {
            if (!_isUsing) return;
            var state = GetSonarState();
            if (state == null) return;

            var bridge = SimulationBridge.Instance;
            var sub = bridge?.SubState;
            if (sub == null) return;

            var map = bridge.Map;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float radius = 120f;

            float boxLeft = cx - radius - 10;
            float boxRight = map != null ? cx + radius + 14 + 260 + 10 : cx + radius + 10;
            GUI.Box(new Rect(boxLeft, cy - radius - 45, boxRight - boxLeft, radius * 2 + 90), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.green }
            };
            string mode = state.IsPassive ? "PASSIVE" : "ACTIVE";
            GUI.Label(new Rect(cx - 60, cy - radius - 40, 120, 20), $"SONAR [{mode}]", titleStyle);

            // Depth / floor / ceiling readout
            var depthStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.6f, 1f, 0.6f, 0.8f) }
            };
            string depthLine;
            if (map != null && bridge.Terrain != null)
            {
                float floorHere = bridge.Terrain.GetFloorDepthAt(sub.PositionX, sub.PositionZ);
                float ceilingHere = bridge.Terrain.GetCeilingDepthAt(sub.PositionX, sub.PositionZ);
                depthLine = ceilingHere > 0f
                    ? $"Depth {sub.Depth:F0}m | Floor {floorHere:F0}m | Ceiling {ceilingHere:F0}m"
                    : $"Depth {sub.Depth:F0}m | Floor {floorHere:F0}m";
            }
            else
            {
                depthLine = $"Depth {sub.Depth:F0}m";
            }
            GUI.Label(new Rect(cx - 130, cy - radius - 18, 260, 15), depthLine, depthStyle);

            var circleRect = new Rect(cx - radius, cy - radius, radius * 2, radius * 2);

            if (map != null && _mapScreen.Texture != null)
            {
                // North-up terrain slice. SetPixels lays rows out bottom-to-top (row 0 = v=0) and
                // GUI.DrawTexture renders v=1 at the visual top, so high-j texels (north) already
                // land at the top of the circle — no flip needed.
                GUI.color = Color.white;
                GUI.DrawTexture(circleRect, _mapScreen.Texture);
            }
            else
            {
                GUI.color = new Color(0f, 0.3f, 0f, 0.5f);
                GUI.DrawTexture(circleRect, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            // Sweep line
            float sweepAngle = state.PingProgress * 360f;
            float rad = sweepAngle * Mathf.Deg2Rad;
            float lx = cx + Mathf.Sin(rad) * radius;
            float ly = cy - Mathf.Cos(rad) * radius;
            DrawLine(cx, cy, lx, ly, new Color(0.3f, 1f, 0.4f, 0.8f));

            // Heading indicator (0 = up/north = +Z, matching the sweep/bearing math above)
            float headingRad = sub.Heading * Mathf.Deg2Rad;
            float hx = cx + Mathf.Sin(headingRad) * radius;
            float hy = cy - Mathf.Cos(headingRad) * radius;
            DrawLine(cx, cy, hx, hy, new Color(0.4f, 1f, 0.4f, 0.85f));

            // Contacts
            if (map != null)
            {
                // Mine contacts light up as the sweep passes their bearing and fade out over
                // SweepFadeSeconds (phosphor afterglow); a faint floor keeps them from
                // disappearing completely between sweeps.
                float sweepDegC = state.PingProgress * 360f;
                float fadeDegC = 360f * SweepFadeSeconds / Mathf.Max(0.5f, state.PingInterval);
                foreach (var c in state.Contacts)
                {
                    if (c.Type != ContactType.Structure) continue;
                    float crad = c.Bearing * Mathf.Deg2Rad;
                    float dist = c.Distance / state.Range;
                    if (dist > 1f) continue;

                    float lag = Mathf.Repeat(sweepDegC - c.Bearing, 360f);
                    float glow = 1f - lag / fadeDegC;
                    if (glow <= 0f) continue;
                    GUI.color = new Color(1f, 0.2f, 0.15f, glow);

                    float dotX = cx + Mathf.Sin(crad) * radius * dist;
                    float dotY = cy - Mathf.Cos(crad) * radius * dist;
                    GUI.DrawTexture(new Rect(dotX - 2.5f, dotY - 2.5f, 5, 5), Texture2D.whiteTexture);

                    if (c.Depth < sub.Depth - 15f)
                        DrawMineAboveMarker(dotX, dotY);
                    else if (c.Depth > sub.Depth + 15f)
                        DrawMineBelowMarker(dotX, dotY);
                }
                GUI.color = Color.white;
            }
            else
            {
                // Fallback: old fake-dot display for all contact types.
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
            }

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

            // Vertical profile panel (YZ slice along the sub's heading), to the right of the circle.
            if (map != null && _profileTex != null)
            {
                var panelRect = new Rect(cx + radius + 14, cy - radius, 260, radius * 2);
                float spanMin = -ProfileBehindFrac * state.Range;
                float spanMax = ProfileAheadFrac * state.Range;

                var profileTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.green }
                };
                GUI.Label(new Rect(panelRect.x, panelRect.y - 18, panelRect.width, 16), "PROFILE", profileTitleStyle);

                // V-flip so texel row 0 (the surface) lands at the top of the panel.
                GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(panelRect, _profileTex, new Rect(0f, 1f, 1f, -1f));

                // Along-track position line (sub sits at 20% from the panel's left edge).
                float subPanelX = panelRect.x + Mathf.InverseLerp(spanMin, spanMax, 0f) * panelRect.width;
                GUI.color = new Color(0.4f, 1f, 0.4f, 0.6f);
                GUI.DrawTexture(new Rect(subPanelX - 0.5f, panelRect.y, 1f, panelRect.height), Texture2D.whiteTexture);

                // Rotating sweep line, same phase as the circle, pivoting on the boat's spot;
                // clipped to the panel edges.
                float pivotPanelY = panelRect.y + Mathf.Clamp01(sub.Depth / ProfileMaxDepth) * panelRect.height;
                float profRad = state.PingProgress * 360f * Mathf.Deg2Rad;
                float sDirX = Mathf.Sin(profRad);
                float sDirY = -Mathf.Cos(profRad);
                float tMax = float.MaxValue;
                if (sDirX > 1e-4f) tMax = Mathf.Min(tMax, (panelRect.xMax - subPanelX) / sDirX);
                else if (sDirX < -1e-4f) tMax = Mathf.Min(tMax, (panelRect.xMin - subPanelX) / sDirX);
                if (sDirY > 1e-4f) tMax = Mathf.Min(tMax, (panelRect.yMax - pivotPanelY) / sDirY);
                else if (sDirY < -1e-4f) tMax = Mathf.Min(tMax, (panelRect.yMin - pivotPanelY) / sDirY);
                if (tMax < float.MaxValue)
                    DrawLine(subPanelX, pivotPanelY, subPanelX + sDirX * tMax, pivotPanelY + sDirY * tMax,
                        new Color(0.3f, 1f, 0.4f, 0.8f));

                // Crush-depth line.
                float crushPanelY = panelRect.y + Mathf.Clamp01(sub.CrushDepth / ProfileMaxDepth) * panelRect.height;
                GUI.color = new Color(1f, 0.2f, 0.2f, 0.6f);
                GUI.DrawTexture(new Rect(panelRect.x, crushPanelY - 0.5f, panelRect.width, 1.5f), Texture2D.whiteTexture);
                GUI.color = Color.white;

                // Depth tick labels along the left edge. 100m ticks are cramped on this panel
                // height, so step by 200m.
                var profileTickStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    normal = { textColor = new Color(0f, 0.8f, 0f, 0.5f) }
                };
                for (float dTick = 200f; dTick < ProfileMaxDepth; dTick += 200f)
                {
                    float tickPanelY = panelRect.y + (dTick / ProfileMaxDepth) * panelRect.height;
                    GUI.Label(new Rect(panelRect.x + 2, tickPanelY - 7, 60, 14), $"{dTick:F0}m", profileTickStyle);
                }

                // Sub marker.
                float subPanelY = panelRect.y + Mathf.Clamp01(sub.Depth / ProfileMaxDepth) * panelRect.height;
                GUI.color = Color.cyan;
                GUI.DrawTexture(new Rect(subPanelX - 3f, subPanelY - 3f, 6f, 6f), Texture2D.whiteTexture);
                GUI.color = Color.white;

                // Mine contacts light up as the rotating sweep passes them and fade out,
                // matching the terrain pixels.
                float profFadeDeg = 360f * SweepFadeSeconds / Mathf.Max(0.5f, state.PingInterval);
                float profSweepDeg = state.PingProgress * 360f;
                foreach (var c in state.Contacts)
                {
                    if (c.Type != ContactType.Structure) continue;
                    float bearingRel = (c.Bearing - sub.Heading) * Mathf.Deg2Rad;
                    float along = c.Distance * Mathf.Cos(bearingRel);
                    float cross = c.Distance * Mathf.Sin(bearingRel);
                    if (Mathf.Abs(cross) > 40f) continue;
                    if (along < spanMin || along > spanMax) continue;

                    float contactPanelX = panelRect.x + Mathf.InverseLerp(spanMin, spanMax, along) * panelRect.width;
                    float contactPanelY = panelRect.y + Mathf.Clamp01(c.Depth / ProfileMaxDepth) * panelRect.height;

                    float aDeg = Mathf.Atan2(contactPanelX - subPanelX, -(contactPanelY - subPanelY)) * Mathf.Rad2Deg;
                    if (aDeg < 0f) aDeg += 360f;
                    float glow = 1f - Mathf.Repeat(profSweepDeg - aDeg, 360f) / profFadeDeg;
                    if (glow <= 0f) continue;
                    GUI.color = new Color(1f, 0.2f, 0.15f, glow);
                    GUI.DrawTexture(new Rect(contactPanelX - 2.5f, contactPanelY - 2.5f, 5, 5), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - 100, cy + radius + 5, 200, 20),
                "Tab: Toggle Active/Passive | E/Esc: Close", hintStyle);

            var legendStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.45f) }
            };
            GUI.Label(new Rect(cx - 130, cy + radius + 23, 260, 16),
                "BLUE ascend to pass | AMBER dive to pass | RED wall", legendStyle);
        }

        // Small widening 3-row up-triangle marker drawn just above a mine dot, indicating the
        // contact sits shallower than the sub. Rows are 2px tall, widths 2/4/6px stacked upward.
        void DrawMineAboveMarker(float dotX, float dotY)
        {
            const float rowHeight = 2f;
            float dotTop = dotY - 2.5f;
            float[] widths = { 2f, 4f, 6f };
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                float y = dotTop - (i + 1) * rowHeight;
                GUI.DrawTexture(new Rect(dotX - w * 0.5f, y, w, rowHeight), Texture2D.whiteTexture);
            }
        }

        // Mirrored down-triangle marker drawn just below a mine dot, for contacts deeper than the sub.
        void DrawMineBelowMarker(float dotX, float dotY)
        {
            const float rowHeight = 2f;
            float dotBottom = dotY + 2.5f;
            float[] widths = { 2f, 4f, 6f };
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                float y = dotBottom + i * rowHeight;
                GUI.DrawTexture(new Rect(dotX - w * 0.5f, y, w, rowHeight), Texture2D.whiteTexture);
            }
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
