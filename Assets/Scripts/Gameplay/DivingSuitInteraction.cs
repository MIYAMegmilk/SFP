using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class DivingSuitInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var locker = hit.collider.GetComponentInParent<DivingSuitLockerDefinition>();
            if (locker == null || locker.SuitCount <= 0) return;

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            if (player.HasDivingSuit)
            {
                player.HasDivingSuit = false;
                locker.SuitCount++;
            }
            else
            {
                player.HasDivingSuit = true;
                locker.SuitCount--;
            }

            var mr = hit.collider.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = locker.SuitCount > 0
                    ? new Color(0.8f, 0.6f, 0.1f)
                    : new Color(0.3f, 0.3f, 0.3f);
        }
    }
}
