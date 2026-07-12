using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class EVAWeaponController : MonoBehaviour
    {
        public float Range = 20f;
        public float Damage = 25f;
        public float Cooldown = 1.5f;
        public float AimConeDeg = 12f;

        float _cooldownTimer;
        float _flashTimer;
        bool _lastShotHit;
        int _targetIdInCone = -1;

        void Update()
        {
            if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
            if (_flashTimer > 0f) _flashTimer -= Time.deltaTime;

            var player = GetComponent<PlayerController>();
            if (player == null || !player.IsEVA) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.Creatures == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 origin = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            float cosThreshold = Mathf.Cos(AimConeDeg * Mathf.Deg2Rad);

            _targetIdInCone = FindBestTarget(bridge, origin, forward, cosThreshold);

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (ConsoleFocus.IsLocked) return;

            if (mouse.leftButton.wasPressedThisFrame && _cooldownTimer <= 0f)
                Fire(bridge, origin, forward, cosThreshold);
        }

        int FindBestTarget(SimulationBridge bridge, Vector3 origin, Vector3 forward, float cosThreshold)
        {
            float bestDist = float.MaxValue;
            int bestId = -1;

            foreach (var c in bridge.Creatures.Creatures)
            {
                if (c.IsDead) continue;
                Vector3 cWorld = new Vector3(c.X, -c.Depth, c.Z);
                Vector3 toCreature = cWorld - origin;
                float dist = toCreature.magnitude;
                if (dist > Range || dist < 0.5f) continue;

                float cosAngle = Vector3.Dot(forward, toCreature / dist);
                if (cosAngle < cosThreshold) continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestId = c.Id;
                }
            }

            return bestId;
        }

        void Fire(SimulationBridge bridge, Vector3 origin, Vector3 forward, float cosThreshold)
        {
            _cooldownTimer = Cooldown;
            _flashTimer = 0.6f;

            int hitId = FindBestTarget(bridge, origin, forward, cosThreshold);
            if (hitId >= 0)
            {
                bridge.Creatures.TakeDamage(hitId, Damage);
                _lastShotHit = true;
            }
            else
            {
                _lastShotHit = false;
            }
        }

        void OnGUI()
        {
            var player = GetComponent<PlayerController>();
            if (player == null || !player.IsEVA) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            bool ready = _cooldownTimer <= 0f;
            bool targeted = _targetIdInCone >= 0;

            Color crossColor = targeted
                ? new Color(1f, 0.3f, 0.3f, ready ? 1f : 0.5f)
                : new Color(1f, 1f, 1f, ready ? 0.8f : 0.3f);
            GUI.color = crossColor;

            // Crosshair
            GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 12, cy - 0.5f, 7, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 5, cy - 0.5f, 7, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy - 12, 1, 7), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy + 5, 1, 7), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Weapon label
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.9f, 1f, 0.7f) }
            };
            GUI.Label(new Rect(cx - 50, cy + 25, 100, 18), "HARPOON [LMB]", labelStyle);

            // Cooldown bar
            float barW = 50f;
            float barX = cx - barW * 0.5f;
            float barY = cy + 42;
            float frac = Cooldown > 0f ? 1f - Mathf.Clamp01(_cooldownTimer / Cooldown) : 1f;
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.5f);
            GUI.DrawTexture(new Rect(barX, barY, barW, 3), Texture2D.whiteTexture);
            GUI.color = frac >= 1f ? new Color(0.3f, 1f, 0.3f, 0.7f) : new Color(1f, 0.7f, 0.2f, 0.5f);
            GUI.DrawTexture(new Rect(barX, barY, barW * frac, 3), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // HIT/MISS flash
            if (_flashTimer > 0f)
            {
                var flashStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = _lastShotHit ? Color.green : new Color(1f, 0.4f, 0.4f) }
                };
                GUI.Label(new Rect(cx - 40, cy - 50, 80, 25),
                    _lastShotHit ? "HIT" : "MISS", flashStyle);
            }
        }
    }
}
