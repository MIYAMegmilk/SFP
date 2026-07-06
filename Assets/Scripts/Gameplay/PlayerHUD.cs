using UnityEngine;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class PlayerHUD : MonoBehaviour
    {
        PlayerController _player;

        void OnGUI()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerController>();
            if (_player == null || !_player.enabled) return;

            float sw = Screen.width;
            float sh = Screen.height;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };

            // Power status (top-right)
            var bridge = SimulationBridge.Instance;
            if (bridge?.PowerGrid != null)
            {
                var pg = bridge.PowerGrid;
                string powerText = $"Power: {pg.GridVoltage * 100f:F0}%";
                Color powerColor = pg.GridVoltage >= 0.8f ? Color.green
                    : pg.GridVoltage >= 0.4f ? Color.yellow : Color.red;
                var powerStyle = new GUIStyle(style) { alignment = TextAnchor.UpperRight, normal = { textColor = powerColor } };
                GUI.Label(new Rect(sw - 210, 10, 200, 20), powerText, powerStyle);
            }

            // Oxygen bar (bottom center)
            if (_player.IsSubmerged)
            {
                float barW = 200f, barH = 16f;
                float barX = (sw - barW) * 0.5f;
                float barY = sh - 60f;

                GUI.Box(new Rect(barX - 2, barY - 2, barW + 4, barH + 4), "");
                GUI.color = Color.Lerp(Color.red, Color.cyan, _player.OxygenFraction);
                GUI.DrawTexture(new Rect(barX, barY, barW * _player.OxygenFraction, barH), Texture2D.whiteTexture);
                GUI.color = Color.white;

                var oxyStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    normal = { textColor = Color.white }
                };
                GUI.Label(new Rect(barX, barY, barW, barH), $"O2: {_player.Oxygen:F0}s", oxyStyle);
            }

            // Crosshair
            float size = 2f;
            float cx = sw * 0.5f, cy = sh * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(cx - 8, cy - size * 0.5f, 16, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - 8, size, 16), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Controls hint (bottom-left)
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(10, sh - 100, 350, 80),
                "WASD: Move | T: Repair | F: Door | E: Interact\nClick: Breach | Tab: Spectator | F1: Debug", hintStyle);
        }
    }
}
