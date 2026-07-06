using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class TurretInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isManning;
        TurretDefinition _activeTurret;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (_isManning)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                {
                    _isManning = false;
                    _activeTurret = null;
                    return;
                }

                UpdateTurretControl(kb, mouse);
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var turret = hit.collider.GetComponentInParent<TurretDefinition>();
            if (turret == null) return;

            _activeTurret = turret;
            _isManning = true;
        }

        void UpdateTurretControl(Keyboard kb, Mouse mouse)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || _activeTurret == null) return;

            var state = bridge.GetTurret(_activeTurret.TurretIndex);
            if (state == null) return;

            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                state.Rotation += delta.x * 0.5f;
                state.Elevation = Mathf.Clamp(state.Elevation - delta.y * 0.5f, -30f, 60f);
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                state.TryFire();
        }

        void OnGUI()
        {
            if (!_isManning || _activeTurret == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;
            var state = bridge.GetTurret(_activeTurret.TurretIndex);
            if (state == null) return;

            float cx = Screen.width * 0.5f;
            float bottom = Screen.height - 80f;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = Color.green }
            };

            GUI.Label(new Rect(cx - 150, bottom, 300, 20),
                $"[{state.Type}]  Ammo: {state.AmmoCount}  |  Rot: {state.Rotation:F0}°  Elev: {state.Elevation:F0}°", style);

            // Reload bar
            if (!state.IsReady && state.AmmoCount > 0)
            {
                float barW = 100f;
                GUI.color = new Color(0.2f, 0.2f, 0.2f);
                GUI.DrawTexture(new Rect(cx - barW * 0.5f, bottom + 22, barW, 8), Texture2D.whiteTexture);
                GUI.color = Color.yellow;
                GUI.DrawTexture(new Rect(cx - barW * 0.5f, bottom + 22, barW * state.ReloadFraction, 8), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            // Crosshair
            GUI.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            GUI.DrawTexture(new Rect(cx - 15, Screen.height * 0.5f - 1, 30, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1, Screen.height * 0.5f - 15, 2, 30), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - 100, bottom + 35, 200, 20),
                "Mouse: Aim | Click: Fire | E/Esc: Leave", hintStyle);
        }
    }
}
