using UnityEngine;
using UnityEngine.InputSystem;

namespace SFP.Gameplay
{
    public class Ladder : MonoBehaviour
    {
        public float ClimbSpeed = 3f;
        public float DeckHeight = 2.5f;
        public float InteractRadius = 1.5f;

        bool _isClimbing;
        PlayerController _climber;
        float _climbProgress;
        float _startY;
        float _targetY;

        void Update()
        {
            if (_isClimbing)
            {
                UpdateClimbing();
                return;
            }

            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;

            var player = FindClosestPlayer();
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
            _isClimbing = true;
            _climbProgress = 0f;
            _startY = player.transform.position.y;

            bool playerIsBelow = player.transform.position.y < transform.position.y + 0.5f;
            _targetY = playerIsBelow ? _startY + DeckHeight : _startY - DeckHeight;
        }

        void UpdateClimbing()
        {
            if (_climber == null)
            {
                _isClimbing = false;
                return;
            }

            _climbProgress += ClimbSpeed * Time.deltaTime / DeckHeight;
            if (_climbProgress >= 1f)
            {
                _climbProgress = 1f;
                _isClimbing = false;
            }

            float y = Mathf.Lerp(_startY, _targetY, _climbProgress);
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

            if (!_isClimbing)
                _climber = null;
        }

        PlayerController FindClosestPlayer()
        {
            return FindFirstObjectByType<PlayerController>();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * DeckHeight);
        }
    }
}
