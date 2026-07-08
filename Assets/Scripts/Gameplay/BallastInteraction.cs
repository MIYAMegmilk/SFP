using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class BallastInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isUsing;
        BallastTankDefinition _activeBallast;
        int _activeIndex = -1;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isUsing)
            {
                UpdateBallast(kb);
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var ballast = hit.collider.GetComponentInParent<BallastTankDefinition>();
            if (ballast == null || ballast.BallastIndex < 0) return;

            _activeBallast = ballast;
            _activeIndex = ballast.BallastIndex;
            _isUsing = true;
            ConsoleFocus.Acquire(this);
        }

        void UpdateBallast(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
            {
                _isUsing = false;
                ConsoleFocus.Release(this);
                _activeBallast = null;
                _activeIndex = -1;
                return;
            }

            var bridge = SimulationBridge.Instance;
            if (bridge == null || _activeIndex < 0 || _activeIndex >= bridge.Ballasts.Length) return;

            var ballast = bridge.Ballasts[_activeIndex];

            float adjustSpeed = 0.5f * Time.deltaTime;
            bool adjusted = false;
            if (kb.wKey.isPressed)
            {
                ballast.TargetFillLevel = Mathf.Clamp01(ballast.TargetFillLevel + adjustSpeed);
                adjusted = true;
            }
            if (kb.sKey.isPressed)
            {
                ballast.TargetFillLevel = Mathf.Clamp01(ballast.TargetFillLevel - adjustSpeed);
                adjusted = true;
            }
            // Manual pump input takes over depth control; setting a new depth target at the
            // helm re-arms the automatic depth hold.
            if (adjusted && bridge.Navigation != null)
                bridge.Navigation.DepthHoldEnabled = false;
        }

        void OnGUI()
        {
            if (!_isUsing || _activeIndex < 0) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null || _activeIndex >= bridge.Ballasts.Length) return;

            var ballast = bridge.Ballasts[_activeIndex];

            float cx = Screen.width * 0.5f;
            float top = Screen.height * 0.3f;
            float panelW = 280f;
            float panelH = 210f;

            GUI.Box(new Rect(cx - panelW * 0.5f, top, panelW, panelH), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + 5, panelW, 25), "BALLAST CONTROL", titleStyle);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
            float y = top + 35;
            float lx = cx - panelW * 0.5f + 10;
            float barW = panelW - 20;

            GUI.Label(new Rect(lx, y, panelW, 20), $"FILL: {ballast.CurrentFillLevel * 100f:F0}%", style);
            y += 20f;
            DrawBarWithMarker(lx, y, barW, 14, ballast.CurrentFillLevel, ballast.TargetFillLevel, new Color(0.3f, 0.6f, 0.95f));
            y += 26f;

            GUI.Label(new Rect(lx, y, panelW, 20), $"TARGET: {ballast.TargetFillLevel * 100f:F0}%  [W/S]", style);
            y += 25f;

            float diff = ballast.TargetFillLevel - ballast.CurrentFillLevel;
            string pumpDir = diff > 0.01f ? "▲ FILLING" : diff < -0.01f ? "▼ DRAINING" : "— IDLE";
            Color pumpColor = diff > 0.01f ? Color.cyan : diff < -0.01f ? new Color(1f, 0.7f, 0.2f) : Color.gray;
            var pumpStyle = new GUIStyle(style) { normal = { textColor = pumpColor } };
            GUI.Label(new Rect(lx, y, panelW, 20), pumpDir, pumpStyle);
            y += 25f;

            var sub = bridge.SubState;
            if (sub != null)
                GUI.Label(new Rect(lx, y, panelW, 20), $"Depth: {sub.Depth:F1}m", style);
            y += 20f;

            var nav = bridge.Navigation;
            if (nav != null)
            {
                var dhStyle = new GUIStyle(style)
                {
                    normal = { textColor = nav.DepthHoldEnabled ? Color.green : new Color(1f, 0.7f, 0.2f) }
                };
                GUI.Label(new Rect(lx, y, panelW, 20),
                    nav.DepthHoldEnabled ? "DEPTH HOLD: AUTO" : "DEPTH HOLD: MANUAL", dhStyle);
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

        void DrawBarWithMarker(float x, float y, float w, float h, float fraction, float targetFraction, Color color)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            float fill = Mathf.Clamp01(fraction);
            GUI.DrawTexture(new Rect(x, y, w * fill, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float markerX = x + w * Mathf.Clamp01(targetFraction);
            GUI.DrawTexture(new Rect(markerX - 1f, y - 2f, 2f, h + 4f), Texture2D.whiteTexture);
        }
    }
}
