using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class CrushDepthDamage : MonoBehaviour
    {
        public float BaseInterval = 8f;
        public float MinInterval = 2f;
        public float BreachArea = 0.03f;
        public float MaxBreachArea = 0.2f;
        public float GrowthRate = 0.015f;

        float _timer;

        public bool Enabled = false;

        void Update()
        {
            if (!Enabled) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.SubState == null) return;

            var sub = bridge.SubState;
            if (!sub.IsBelowCrushDepth)
            {
                _timer = 0f;
                return;
            }

            float excessRatio = (sub.Depth - sub.CrushDepth) / sub.CrushDepth;
            float interval = Mathf.Lerp(BaseInterval, MinInterval, Mathf.Clamp01(excessRatio));

            _timer += Time.deltaTime;
            if (_timer < interval) return;
            _timer = 0f;

            CreateRandomBreach(bridge);
        }

        void CreateRandomBreach(SimulationBridge bridge)
        {
            var comps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            if (comps.Length == 0) return;

            var target = comps[Random.Range(0, comps.Length)];
            Vector3 center = target.transform.position;

            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            Vector3 dir = directions[Random.Range(0, directions.Length)];

            if (!Physics.Raycast(center, dir, out var hit, target.LengthX)) return;

            var hitComp = hit.collider.GetComponentInParent<CompartmentDefinition>();
            if (hitComp != target) return;

            var opening = bridge.AddBreachAtRuntime(target, BreachArea,
                hit.point.y, 0.3f, hit.point.x, hit.point.z);
            if (opening == null) return;

            var vfxGo = new GameObject($"CrushBreach_{opening.Id}");
            vfxGo.transform.position = hit.point;
            vfxGo.transform.rotation = Quaternion.LookRotation(hit.normal);

            vfxGo.AddComponent<PhysicsWaterEmitter>()
                .Init(opening, null, hit.normal);
            vfxGo.AddComponent<BreachGrower>()
                .Init(opening, MaxBreachArea, GrowthRate);

            var visual = vfxGo.AddComponent<BreachVisual>();
            visual.Init(opening, hit);
        }
    }
}
