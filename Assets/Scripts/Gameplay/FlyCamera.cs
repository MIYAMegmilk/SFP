using UnityEngine;
using UnityEngine.InputSystem;

namespace SFP.Gameplay
{
    public class FlyCamera : MonoBehaviour
    {
        public float MoveSpeed = 5f;
        public float LookSpeed = 0.15f;
        public float SprintMultiplier = 3f;

        float _rotX, _rotY;

        void Start()
        {
            ResetRotation();
        }

        public void ResetRotation()
        {
            _rotX = transform.eulerAngles.y;
            _rotY = -transform.eulerAngles.x;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null) return;

            if (mouse.rightButton.isPressed)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                var delta = mouse.delta.ReadValue();
                _rotX += delta.x * LookSpeed;
                _rotY += delta.y * LookSpeed;
                _rotY = Mathf.Clamp(_rotY, -89f, 89f);
                transform.rotation = Quaternion.Euler(-_rotY, _rotX, 0f);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            float speed = MoveSpeed * (kb.leftShiftKey.isPressed ? SprintMultiplier : 1f);
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += transform.forward;
            if (kb.sKey.isPressed) move -= transform.forward;
            if (kb.aKey.isPressed) move -= transform.right;
            if (kb.dKey.isPressed) move += transform.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;
            transform.position += move.normalized * speed * Time.deltaTime;
        }
    }
}
