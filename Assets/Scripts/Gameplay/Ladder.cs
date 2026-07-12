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
        float _lowerFloorY;
        float _hatchY;

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

            _hatchY = transform.position.y;
            _lowerFloorY = _hatchY - DeckHeight;

            // Snap player XZ to hatch position so they pass through the opening
            Vector3 snapXZ = Hatch != null
                ? new Vector3(Hatch.transform.position.x, 0f, Hatch.transform.position.z)
                : new Vector3(transform.position.x, 0f, transform.position.z);

            var cc = _climber.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                var pos = _climber.transform.position;
                pos.x = snapXZ.x;
                pos.z = snapXZ.z;
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
                || kb.spaceKey.wasPressedThisFrame)
            {
                StopClimb();
                return;
            }

            float input = 0f;
            if (kb.wKey.isPressed) input += 1f;
            if (kb.sKey.isPressed || kb.leftCtrlKey.isPressed) input -= 1f;
            if (input == 0f) return;

            float playerY = _climber.transform.position.y;
            float newY = playerY + input * ClimbSpeed * Time.deltaTime;
            bool hatchOpen = IsHatchOpen();

            if (input > 0f)
            {
                if (!hatchOpen && playerY < _hatchY)
                {
                    float headLimit = _hatchY - 1.6f;
                    if (newY > headLimit) newY = headLimit;
                }
                else if (hatchOpen && newY >= _hatchY + 0.1f)
                {
                    ApplyPosition(_hatchY + 0.1f);
                    StopClimb();
                    return;
                }

                float ceiling = _hatchY + DeckHeight;
                if (newY > ceiling) newY = ceiling;
            }
            else
            {
                if (!hatchOpen && playerY >= _hatchY)
                {
                    float feetLimit = _hatchY + 0.1f;
                    if (newY < feetLimit) newY = feetLimit;
                }
                else if (newY <= _lowerFloorY + 0.1f)
                {
                    ApplyPosition(_lowerFloorY + 0.1f);
                    StopClimb();
                    return;
                }

                if (newY < _lowerFloorY) newY = _lowerFloorY;
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
                pos.y = y;
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
            Gizmos.DrawLine(
                transform.position + Vector3.down * DeckHeight,
                transform.position + Vector3.up * DeckHeight);
        }
    }
}
