using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public float WalkSpeed = 4f;
        public float SprintMultiplier = 1.8f;
        public float SwimSpeed = 2.5f;
        public float JumpForce = 5f;
        public float Gravity = 9.81f;
        public float MouseSensitivity = 2f;
        public float EyeHeight = 1.6f;

        public float MaxOxygen = 30f;
        public float OxygenRecoveryRate = 10f;
        public float DivingSuitOxygen = 120f;
        public float DivingSuitSpeedPenalty = 0.7f;

        CharacterController _cc;
        Transform _cameraTransform;
        float _verticalVelocity;
        float _pitch;
        float _oxygen;
        bool _isSubmerged;

        public bool HasDivingSuit;
        public float Oxygen => _oxygen;
        public float OxygenFraction => _oxygen / EffectiveMaxOxygen;
        public bool IsSubmerged => _isSubmerged;
        public float EffectiveMaxOxygen => HasDivingSuit ? DivingSuitOxygen : MaxOxygen;

        void Start()
        {
            _cc = GetComponent<CharacterController>();
            _cameraTransform = GetComponentInChildren<Camera>().transform;
            _oxygen = MaxOxygen;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            HandleLook(mouse);

            float waterY = GetWaterLevelAtPosition();
            float headY = transform.position.y + EyeHeight;
            _isSubmerged = headY < waterY;

            if (_isSubmerged)
                UpdateSwimming(kb);
            else
                UpdateWalking(kb);

            UpdateOxygen();
        }

        void HandleLook(Mouse mouse)
        {
            Vector2 delta = mouse.delta.ReadValue() * MouseSensitivity * 0.1f;
            transform.Rotate(0f, delta.x, 0f);
            _pitch = Mathf.Clamp(_pitch - delta.y, -89f, 89f);
            _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void UpdateWalking(Keyboard kb)
        {
            Vector3 input = GetMoveInput(kb);
            bool sprint = kb.leftShiftKey.isPressed;
            float speed = sprint ? WalkSpeed * SprintMultiplier : WalkSpeed;
            if (HasDivingSuit) speed *= DivingSuitSpeedPenalty;

            Vector3 move = transform.TransformDirection(input) * speed;

            if (_cc.isGrounded)
            {
                _verticalVelocity = -0.5f;
                if (kb.spaceKey.wasPressedThisFrame)
                    _verticalVelocity = JumpForce;
            }
            else
            {
                _verticalVelocity -= Gravity * Time.deltaTime;
            }

            move.y = _verticalVelocity;
            _cc.Move(move * Time.deltaTime);
        }

        void UpdateSwimming(Keyboard kb)
        {
            Vector3 input = GetMoveInput(kb);

            float vertical = 0f;
            if (kb.spaceKey.isPressed) vertical += 1f;
            if (kb.leftCtrlKey.isPressed) vertical -= 1f;

            Vector3 swimDir = _cameraTransform.TransformDirection(input);
            swimDir.y += vertical;

            if (swimDir.sqrMagnitude > 1f)
                swimDir.Normalize();

            float swimSpd = HasDivingSuit ? SwimSpeed * DivingSuitSpeedPenalty : SwimSpeed;
            _verticalVelocity = 0f;
            _cc.Move(swimDir * swimSpd * Time.deltaTime);
        }

        void UpdateOxygen()
        {
            if (_isSubmerged)
            {
                _oxygen = Mathf.Max(0f, _oxygen - Time.deltaTime);
            }
            else
            {
                float roomO2 = GetRoomOxygenLevel();
                float recoveryMultiplier = roomO2 > 0.2f ? roomO2 : 0f;
                if (roomO2 < 0.15f)
                    _oxygen = Mathf.Max(0f, _oxygen - (1f - roomO2 * 5f) * Time.deltaTime);
                else
                    _oxygen = Mathf.Min(EffectiveMaxOxygen, _oxygen + OxygenRecoveryRate * recoveryMultiplier * Time.deltaTime);
            }
        }

        float GetRoomOxygenLevel()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.Atmosphere == null) return 1f;

            var comps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in comps)
            {
                if (!IsInsideCompartmentXZ(transform.position, comp)) continue;
                int id = bridge.GetCompartmentId(comp);
                if (id < 0) continue;
                bridge.Atmosphere.CrewCompartmentId = id;
                return bridge.Atmosphere.GetOxygenLevel(id);
            }
            return 1f;
        }

        Vector3 GetMoveInput(Keyboard kb)
        {
            Vector3 input = Vector3.zero;
            if (kb.wKey.isPressed) input.z += 1f;
            if (kb.sKey.isPressed) input.z -= 1f;
            if (kb.aKey.isPressed) input.x -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (input.sqrMagnitude > 1f) input.Normalize();
            return input;
        }

        float GetWaterLevelAtPosition()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return -1000f;

            var comps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in comps)
            {
                if (!IsInsideCompartmentXZ(transform.position, comp)) continue;
                int id = bridge.GetCompartmentId(comp);
                if (id < 0) continue;
                return bridge.GetInterpolatedWaterLevelY(id);
            }
            return -1000f;
        }

        bool IsInsideCompartmentXZ(Vector3 pos, CompartmentDefinition comp)
        {
            float cx = comp.transform.position.x;
            float cz = comp.transform.position.z;
            float halfX = comp.LengthX * 0.5f;
            float halfZ = comp.WidthZ * 0.5f;
            return pos.x >= cx - halfX && pos.x <= cx + halfX
                && pos.z >= cz - halfZ && pos.z <= cz + halfZ;
        }
    }
}
