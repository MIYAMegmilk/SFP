using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class Ladder : MonoBehaviour
    {
        public float ClimbSpeed = 3f;
        public float DeckHeight = 2.5f;
        public float InteractRadius = 1.5f;
        public OpeningDefinition Hatch;

        bool _isClimbing;
        PlayerController _climber;
        float _floorY;
        float _ceilingY;

        void Update()
        {
            if (_isClimbing)
            {
                UpdateClimbing();
                return;
            }

            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;
            if (ConsoleFocus.IsLocked) return;

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            float dist = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(player.transform.position.x, 0f, player.transform.position.z));
            if (dist > InteractRadius) return;

            StartClimb(player);
        }

        void StartClimb(PlayerController player)
        {
            _climber = player;
            _climber.IsClimbing = true;
            _isClimbing = true;

            float hatchY = transform.position.y;
            _floorY = hatchY - DeckHeight;
            _ceilingY = hatchY + DeckHeight;

            var cc = _climber.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                var pos = _climber.transform.position;
                pos.x = transform.position.x;
                pos.z = transform.position.z;
                _climber.transform.position = pos;
                cc.enabled = true;
            }

            ConsoleFocus.Acquire(this);
        }

        void UpdateClimbing()
        {
            if (_climber == null)
            {
                StopClimb();
                return;
            }

            var kb = Keyboard.current;
            if (kb == null)
            {
                StopClimb();
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame
                || kb.sKey.wasPressedThisFrame)
            {
                StopClimb();
                return;
            }

            if (!kb.wKey.isPressed) return;
            var cam = Camera.main;
            float input = (cam != null && cam.transform.forward.y >= 0f) ? 1f : -1f;

            float hatchY = transform.position.y;
            float playerY = _climber.transform.position.y;
            float newY = playerY + input * ClimbSpeed * Time.deltaTime;

            if (input > 0f)
            {
                bool hatchOpen = IsHatchOpen();
                if (!hatchOpen)
                {
                    // 1.6 = eye height; head hits ceiling at hatch level
                    float headLimit = hatchY - 1.6f;
                    if (newY > headLimit) newY = headLimit;
                }
                else
                {
                    if (newY > _ceilingY) newY = _ceilingY;
                }

                // arrived on upper deck
                if (hatchOpen && newY >= hatchY + 0.1f)
                {
                    newY = hatchY + 0.1f;
                    ApplyPosition(newY);
                    StopClimb();
                    return;
                }
            }
            else
            {
                if (newY < _floorY)
                    newY = _floorY;

                // arrived on lower deck
                if (newY <= _floorY + 0.1f)
                {
                    newY = _floorY + 0.1f;
                    ApplyPosition(newY);
                    StopClimb();
                    return;
                }
            }

            ApplyPosition(newY);
        }

        bool IsHatchOpen()
        {
            if (Hatch == null) return false;

            var bridge = SimulationBridge.Instance;
            if (bridge != null && Hatch.SimIndex >= 0)
                return bridge.Graph.Openings[Hatch.SimIndex].IsOpen;

            return Hatch.IsOpen;
        }

        void ApplyPosition(float y)
        {
            var cc = _climber.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                var pos = _climber.transform.position;
                pos.x = transform.position.x;
                pos.y = y;
                pos.z = transform.position.z;
                _climber.transform.position = pos;
                cc.enabled = true;
            }
        }

        void StopClimb()
        {
            _isClimbing = false;
            if (_climber != null)
                _climber.IsClimbing = false;
            ConsoleFocus.Release(this);
            _climber = null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * DeckHeight);
        }
    }
}
