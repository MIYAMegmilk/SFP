using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    // Renders the circular sonar picture (surface-echo outlines + rotating phosphor sweep)
    // into a Texture2D. Shared by the fullscreen sonar UI and wall monitors so the display
    // logic lives in one place.
    public sealed class SonarScreen
    {
        public const int Res = 96;
        public const float RebuildInterval = 0.5f;
        // Phosphor afterglow: a spot stays lit this long after the sweep passes it, then
        // goes fully dark until the next pass.
        public const float SweepFadeSeconds = 1f;

        // Monitors have no IMGUI overlay, so mine contacts and the boat marker are baked
        // into the texture itself when this is set.
        public bool BakeContacts;

        Texture2D _tex;
        Color[] _base;
        Color[] _display;
        float[] _bearingDeg;
        float _rebuildTimer;

        public Texture2D Texture => _tex;

        public void Tick(float dt, SonarState state)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || bridge.Map == null || bridge.SubState == null || state == null) return;

            _rebuildTimer -= dt;
            if (_tex == null || _rebuildTimer <= 0f)
            {
                _rebuildTimer = RebuildInterval;
                RebuildBase(bridge.Map, bridge.SubState, state.Range);
            }

            ApplySweepGlow(state);
        }

        void RebuildBase(MapData map, SubmarineState sub, float range)
        {
            if (_tex == null)
            {
                _tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var openColor = new Color(0f, 0.12f, 0.03f, 0.85f);
            var clearColor = new Color(0f, 0f, 0f, 0f);
            var wallColor = new Color(0.9f, 0.25f, 0.12f, 0.95f);
            var ascendColor = new Color(0.15f, 0.45f, 0.95f, 0.9f);
            var diveColor = new Color(0.9f, 0.75f, 0.15f, 0.9f);

            const float margin = 8f;
            const float minGap = 15f;

            var pixels = new Color[Res * Res];
            // 0 = outside the circle, 1 = open water, 2 = rock. Used by the edge pass below:
            // sonar echoes come from the rock FACE, so only rock pixels touching open water
            // keep their color — the interior goes dark.
            var cell = new byte[Res * Res];
            for (int j = 0; j < Res; j++)
            {
                // j maps to world +Z (north) upward.
                float v = (j / (float)Res - 0.5f) * 2f;
                for (int i = 0; i < Res; i++)
                {
                    float u = (i / (float)Res - 0.5f) * 2f;
                    int idx = j * Res + i;
                    if (u * u + v * v > 1f)
                    {
                        pixels[idx] = clearColor;
                        continue;
                    }

                    float worldX = sub.PositionX + u * range;
                    float worldZ = sub.PositionZ + v * range;
                    float floor = map.GetFloorDepthAt(worldX, worldZ);
                    float ceiling = map.GetCeilingDepthAt(worldX, worldZ);
                    bool blocked = floor <= sub.Depth + margin || (ceiling > 0f && ceiling >= sub.Depth - margin);
                    if (!blocked)
                    {
                        pixels[idx] = openColor;
                        cell[idx] = 1;
                        continue;
                    }
                    cell[idx] = 2;

                    // Blocked at the sub's current depth. Check whether the vertical gap at this
                    // XZ point (between the overhead rock and the sea floor) is wide enough to
                    // pass through at some other depth, and if so, which way to go.
                    bool passableAnywhere = floor > 0f && (floor - ceiling) >= minGap;
                    if (!passableAnywhere)
                    {
                        pixels[idx] = wallColor;
                    }
                    else if (sub.Depth > floor)
                    {
                        // Sub is below the passable band (near/in the floor) -> go up to clear it.
                        pixels[idx] = ascendColor;
                    }
                    else if (sub.Depth < ceiling)
                    {
                        // Sub is above the passable band (near/in the overhead rock) -> go down.
                        pixels[idx] = diveColor;
                    }
                    else
                    {
                        // Edge case: within the band but the margin still flags it as blocked.
                        // Break the tie by which half of the band the sub sits in.
                        float bandMid = (ceiling + floor) * 0.5f;
                        pixels[idx] = sub.Depth < bandMid ? ascendColor : diveColor;
                    }
                }
            }

            // Surface-only pass: keep a rock pixel lit only where it touches open water
            // (the echo face); rock interior reads as dark, like a real return.
            for (int j = 0; j < Res; j++)
            {
                for (int i = 0; i < Res; i++)
                {
                    int idx = j * Res + i;
                    if (cell[idx] != 2) continue;
                    bool onSurface =
                        (i > 0 && cell[idx - 1] == 1) ||
                        (i < Res - 1 && cell[idx + 1] == 1) ||
                        (j > 0 && cell[idx - Res] == 1) ||
                        (j < Res - 1 && cell[idx + Res] == 1);
                    if (!onSurface) pixels[idx] = openColor;
                }
            }

            // Cache terrain colors; ApplySweepGlow uploads them with per-frame brightness.
            _base = pixels;
        }

        void ApplySweepGlow(SonarState state)
        {
            if (_tex == null || _base == null) return;

            if (_bearingDeg == null)
            {
                _bearingDeg = new float[Res * Res];
                for (int j = 0; j < Res; j++)
                {
                    float v = (j / (float)Res - 0.5f) * 2f;
                    for (int i = 0; i < Res; i++)
                    {
                        float u = (i / (float)Res - 0.5f) * 2f;
                        float deg = Mathf.Atan2(u, v) * Mathf.Rad2Deg;
                        if (deg < 0f) deg += 360f;
                        _bearingDeg[j * Res + i] = deg;
                    }
                }
            }

            if (_display == null) _display = new Color[Res * Res];

            float sweepDeg = state.PingProgress * 360f;
            // Afterglow duration expressed as the angular width trailing the sweep.
            float fadeDeg = 360f * SweepFadeSeconds / Mathf.Max(0.5f, state.PingInterval);

            for (int idx = 0; idx < _base.Length; idx++)
            {
                var c = _base[idx];
                if (c.a <= 0f)
                {
                    _display[idx] = c;
                    continue;
                }
                float lag = sweepDeg - _bearingDeg[idx];
                if (lag < 0f) lag += 360f;
                // 1.25 at the sweep head (slightly overbright) fading linearly to full dark.
                float glow = 1.25f * (1f - lag / fadeDeg);
                float b = glow > 0f ? glow : 0f;
                _display[idx] = new Color(c.r * b, c.g * b, c.b * b, c.a);
            }

            if (BakeContacts)
            {
                BakeContactDots(state, sweepDeg, fadeDeg);

                // Constant boat marker at the center so the screen reads at a glance.
                int ci = (Res / 2) * Res + Res / 2;
                _display[ci] = new Color(0.2f, 1f, 1f, 1f);
                _display[ci - 1] = _display[ci + 1] = new Color(0.2f, 1f, 1f, 0.8f);
            }

            _tex.SetPixels(_display);
            _tex.Apply(false);
        }

        void BakeContactDots(SonarState state, float sweepDeg, float fadeDeg)
        {
            var mineColor = new Color(1f, 0.2f, 0.15f, 1f);
            foreach (var c in state.Contacts)
            {
                if (c.Type != ContactType.Structure) continue;
                float dist = c.Distance / state.Range;
                if (dist > 1f) continue;

                float lag = Mathf.Repeat(sweepDeg - c.Bearing, 360f);
                float glow = 1f - lag / fadeDeg;
                if (glow <= 0f) continue;

                float rad = c.Bearing * Mathf.Deg2Rad;
                int px = Mathf.RoundToInt((Mathf.Sin(rad) * dist * 0.5f + 0.5f) * (Res - 1));
                int py = Mathf.RoundToInt((Mathf.Cos(rad) * dist * 0.5f + 0.5f) * (Res - 1));
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = px + dx, y = py + dy;
                        if (x < 0 || x >= Res || y < 0 || y >= Res) continue;
                        int di = y * Res + x;
                        _display[di] = Color.Lerp(_display[di], mineColor, glow);
                    }
                }
            }
        }
    }
}
