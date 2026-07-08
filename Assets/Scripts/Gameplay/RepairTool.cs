using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class RepairTool : MonoBehaviour
    {
        public float MaxDistance = 50f;
        public float RepairRadius = 2f;
        public float PatchRate = 0.5f;
        public float SealRate = 0.02f;

        BreachVisual _activeTarget;
        float _sealStartArea;

        public BreachVisual ActiveTarget => _activeTarget;
        public float DisplayProgress { get; private set; }
        public string StageLabel { get; private set; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.tKey.isPressed)
            {
                ResumeGrowth();
                _activeTarget = null;
                DisplayProgress = 0f;
                StageLabel = null;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            var breach = FindClosestBreachOnRay(ray);

            if (breach == null || !breach.HasOpening)
            {
                ResumeGrowth();
                _activeTarget = null;
                DisplayProgress = 0f;
                StageLabel = null;
                return;
            }

            if (_activeTarget != breach)
            {
                ResumeGrowth();
                _activeTarget = breach;
                _sealStartArea = breach.Opening.Area;
            }

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            bridge.DamageSystem.SetRepairing(breach.Opening.Id, true);
            var stage = bridge.DamageSystem.GetRepairStage(breach.Opening.Id);

            if (stage == BreachRepairStage.None)
            {
                bridge.DamageSystem.AddPatchProgress(breach.Opening.Id, PatchRate * Time.deltaTime);
                DisplayProgress = bridge.DamageSystem.GetPatchProgress(breach.Opening.Id);
                StageLabel = "PATCHING";
            }
            else
            {
                var opening = breach.Opening;
                opening.Area -= SealRate * Time.deltaTime;
                DisplayProgress = 1f - opening.Area / Mathf.Max(0.01f, _sealStartArea);
                StageLabel = "WELDING";

                if (opening.Area <= 0f)
                {
                    bridge.DamageSystem.UnregisterBreach(opening.Id);
                    breach.Repair();
                    Destroy(breach.gameObject);
                    _activeTarget = null;
                    DisplayProgress = 0f;
                    StageLabel = null;
                }
            }
        }

        void ResumeGrowth()
        {
            if (_activeTarget == null) return;
            var bridge = SimulationBridge.Instance;
            if (bridge != null && _activeTarget.HasOpening)
                bridge.DamageSystem.SetRepairing(_activeTarget.Opening.Id, false);
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
