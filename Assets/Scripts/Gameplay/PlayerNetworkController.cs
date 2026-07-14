using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Netcode.Components;

namespace SFP.Gameplay
{
    // Owner-authoritative player networking. Movement/oxygen/swimming logic stays in
    // PlayerController; this class only gates ownership, reads input, and ships it to the server.
    // NetworkTransform (owner-authoritative) must be present on this GameObject to replicate position.
    [RequireComponent(typeof(NetworkTransform))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerNetworkController : NetworkBehaviour
    {
        PlayerController _playerController;
        CharacterController _cc;

        public bool IsLocalPlayer => IsOwner;

        void Awake()
        {
            _playerController = GetComponent<PlayerController>();
            _cc = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            var camera = GetComponentInChildren<Camera>(true);
            var listener = GetComponentInChildren<AudioListener>(true);

            if (IsOwner)
            {
                if (camera != null) camera.enabled = true;
                if (listener != null) listener.enabled = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                if (camera != null) camera.enabled = false;
                if (listener != null) listener.enabled = false;
            }
        }

        void Update()
        {
            if (!IsOwner) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            var cmd = BuildInputCommand(kb, mouse);
            SendInputServerRpc(cmd);

            // Client-side prediction: PlayerController already applies this frame's input locally
            // (it reads Keyboard.current/Mouse.current directly), so no extra local apply is needed here.
        }

        PlayerInputCommand BuildInputCommand(Keyboard kb, Mouse mouse)
        {
            var cmd = new PlayerInputCommand();

            if (!ConsoleFocus.IsLocked)
            {
                Vector2 move = Vector2.zero;
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (move.sqrMagnitude > 1f) move.Normalize();
                cmd.MoveX = move.x;
                cmd.MoveZ = move.y;

                cmd.Jump = kb.spaceKey.isPressed;
                cmd.Sprint = kb.leftShiftKey.isPressed;
                cmd.ClimbUp = kb.spaceKey.isPressed;
                cmd.ClimbDown = kb.leftCtrlKey.isPressed;
            }

            cmd.Interact = kb.eKey.wasPressedThisFrame;

            Vector2 look = mouse.delta.ReadValue();
            cmd.LookDeltaX = look.x;
            cmd.LookDeltaY = look.y;

            return cmd;
        }

        [ServerRpc]
        void SendInputServerRpc(PlayerInputCommand cmd)
        {
            // M12 foundation: server trusts the owning client's CharacterController position
            // (replicated via NetworkTransform); no server-side reconciliation yet.
            if (_cc == null) return;
        }
    }
}
