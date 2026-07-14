using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class TurretInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;
        public float TurnRateDegPerSec = 60f;

        bool _isManning;
        TurretDefinition _activeTurret;
        float _aimBearing;

        float _flashTimer;
        bool _lastShotHit;
        float _localCooldownTimer;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_flashTimer > 0f) _flashTimer -= Time.deltaTime;
            if (_localCooldownTimer > 0f) _localCooldownTimer -= Time.deltaTime;

            if (_isManning)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                {
                    _isManning = false;
                    _activeTurret = null;
                    ConsoleFocus.Release(this);
                    return;
                }

                UpdateTurretControl(kb);
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
            ConsoleFocus.Acquire(this);

            var sub = SimulationBridge.Instance?.SubState;
            _aimBearing = sub != null ? sub.Heading : 0f;
        }

        void UpdateTurretControl(Keyboard kb)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || _activeTurret == null) return;

            var state = bridge.GetTurret(_activeTurret.TurretIndex);
            if (state == null) return;

            float turn = 0f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turn -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn += 1f;
            _aimBearing += turn * TurnRateDegPerSec * Time.deltaTime;
            _aimBearing = ((_aimBearing % 360f) + 360f) % 360f;

            var relay = DeviceRpcRelay.Instance;
            if (relay != null)
                relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.SetTurretRotation, IntVal = _activeTurret.TurretIndex, FloatVal = _aimBearing });
            else
                state.Rotation = _aimBearing;

            if (kb.spaceKey.wasPressedThisFrame && bridge.Creatures != null)
            {
                if (relay != null)
                {
                    relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.FireTurret, IntVal = _activeTurret.TurretIndex });
                }
                else
                {
                    bool fired = state.TryFire(_aimBearing, bridge.SubState, bridge.Creatures,
                        out int hitCreatureId, out float hitDistance);
                    if (fired)
                    {
                        _flashTimer = 0.6f;
                        _lastShotHit = hitCreatureId >= 0;
                        _localCooldownTimer = state.FireCooldown;
                    }
                }
            }
        }

        void OnGUI()
        {
            if (!_isManning || _activeTurret == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;
            var state = bridge.GetTurret(_activeTurret.TurretIndex);
            if (state == null) return;
            var sub = bridge.SubState;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float radius = 110f;

            GUI.Box(new Rect(cx - radius - 10, cy - radius - 45, radius * 2 + 20, radius * 2 + 110), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.red }
            };
            GUI.Label(new Rect(cx - 60, cy - radius - 40, 120, 20), $"TURRET [{state.Type}]", titleStyle);

            var circleRect = new Rect(cx - radius, cy - radius, radius * 2, radius * 2);
            GUI.color = new Color(0.3f, 0f, 0f, 0.4f);
            GUI.DrawTexture(circleRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Aim line (bright, world-absolute bearing, 0 = north/+Z, matching sonar convention)
            float aimRad = _aimBearing * Mathf.Deg2Rad;
            float ax = cx + Mathf.Sin(aimRad) * radius;
            float ay = cy - Mathf.Cos(aimRad) * radius;
            DrawLine(cx, cy, ax, ay, new Color(1f, 0.9f, 0.2f, 0.9f));

            // Alive creature blips
            if (bridge.Creatures != null && sub != null)
            {
                GUI.color = Color.red;
                foreach (var creature in bridge.Creatures.Creatures)
                {
                    if (creature.IsDead) continue;

                    float dx = creature.X - sub.PositionX;
                    float dz = creature.Z - sub.PositionZ;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > state.FireRange) continue;

                    float bearing = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                    if (bearing < 0f) bearing += 360f;

                    float crad = bearing * Mathf.Deg2Rad;
                    float frac = dist / state.FireRange;
                    float dotX = cx + Mathf.Sin(crad) * radius * frac;
                    float dotY = cy - Mathf.Cos(crad) * radius * frac;
                    GUI.DrawTexture(new Rect(dotX - 3f, dotY - 3f, 6f, 6f), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            // Crosshair
            GUI.color = new Color(0.6f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 1, cy - radius, 2, radius * 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - radius, cy - 1, radius * 2, 2), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(1f, 0.4f, 0.4f, 0.7f) }
            };
            GUI.Label(new Rect(cx + 3, cy - radius + 2, 80, 15), $"{state.FireRange:F0}m", infoStyle);

            var statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = Color.green }
            };
            GUI.Label(new Rect(cx - 150, cy + radius + 10, 300, 20),
                $"Ammo: {state.AmmoCount}  |  Bearing: {_aimBearing:F0}°", statusStyle);

            // Cooldown bar (locally tracked: fills back up over FireCooldown seconds after a shot)
            float barW = 100f;
            float cooldownFrac = state.FireCooldown > 0f
                ? 1f - Mathf.Clamp01(_localCooldownTimer / state.FireCooldown)
                : 1f;
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(cx - barW * 0.5f, cy + radius + 32, barW, 8), Texture2D.whiteTexture);
            GUI.color = state.CanFire ? Color.green : Color.yellow;
            GUI.DrawTexture(new Rect(cx - barW * 0.5f, cy + radius + 32, barW * cooldownFrac, 8), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // HIT/MISS flash
            if (_flashTimer > 0f)
            {
                var flashStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = _lastShotHit ? Color.green : Color.red }
                };
                GUI.Label(new Rect(cx - 60, cy - 60, 120, 30), _lastShotHit ? "HIT" : "MISS", flashStyle);
            }

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - 100, cy + radius + 50, 200, 20),
                "A/D: Aim | Space: Fire | E/Esc: Leave", hintStyle);
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
