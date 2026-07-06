using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class RepairTool : MonoBehaviour
    {
        public float MaxDistance = 50f;
        public float RepairRadius = 2f;
        public float RepairRate = 0.08f;

        BreachVisual _activeTarget;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.tKey.isPressed)
            {
                ResumeGrower();
                _activeTarget = null;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            var breach = FindClosestBreachOnRay(ray);

            if (breach == null || !breach.HasOpening)
            {
                ResumeGrower();
                _activeTarget = null;
                return;
            }

            if (_activeTarget != breach)
            {
                ResumeGrower();
                _activeTarget = breach;
            }

            var grower = breach.GetComponent<BreachGrower>();
            if (grower != null) grower.enabled = false;

            var opening = breach.Opening;
            opening.Area -= RepairRate * Time.deltaTime;

            if (opening.Area <= 0f)
            {
                breach.Repair();
                Destroy(breach.gameObject);
                _activeTarget = null;
            }
        }

        void ResumeGrower()
        {
            if (_activeTarget == null) return;
            var grower = _activeTarget.GetComponent<BreachGrower>();
            if (grower != null) grower.enabled = true;
        }

        BreachVisual FindClosestBreachOnRay(Ray ray)
        {
            var all = FindObjectsByType<BreachVisual>(FindObjectsSortMode.None);
            BreachVisual closest = null;
            float bestDist = RepairRadius;

            foreach (var bv in all)
            {
                if (!bv.HasOpening) continue;

                Vector3 toBreach = bv.transform.position - ray.origin;
                float along = Vector3.Dot(toBreach, ray.direction);
                if (along < 0f || along > MaxDistance) continue;

                Vector3 nearestOnRay = ray.origin + ray.direction * along;
                float perpDist = Vector3.Distance(nearestOnRay, bv.transform.position);
                if (perpDist < bestDist)
                {
                    bestDist = perpDist;
                    closest = bv;
                }
            }
            return closest;
        }
    }
}
