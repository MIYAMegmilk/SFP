using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class BreachTool : MonoBehaviour
    {
        public float InitialArea = 0.05f;
        public float MaxArea = 0.3f;
        public float GrowthRate = 0.02f;
        public float MaxDistance = 50f;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (mouse.rightButton.isPressed) return;
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.isPressed) return;

            var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var compDef = hit.collider.GetComponentInParent<CompartmentDefinition>();
            if (compDef == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            int thisId = bridge.GetCompartmentId(compDef);
            if (thisId < 0) return;

            bool isFloorOrCeiling = Mathf.Abs(hit.normal.y) > 0.7f;

            int otherId = FindAdjacentCompartment(bridge, hit.point, hit.normal, compDef);

            bool isHullSurface = isFloorOrCeiling
                ? IsHullFloor(compDef, hit.normal)
                : IsHullWall(hit.collider.gameObject, compDef);

            Opening opening = null;
            if (otherId >= 0)
            {
                opening = bridge.Graph.AddOpening(OpeningKind.Breach, thisId, otherId,
                    InitialArea, hit.point.y, 0.3f);

                if (bridge.WaterSystem != null)
                {
                    float sillY = hit.point.y - 0.15f;
                    float w = Mathf.Sqrt(InitialArea);
                    bridge.WaterSystem.AddConnection(opening, thisId, otherId,
                        hit.point.x, hit.point.z, w, sillY, isFloorOrCeiling);
                }
            }
            else if (isHullSurface)
            {
                opening = bridge.AddBreachAtRuntime(compDef, InitialArea, hit.point.y, 0.3f,
                    hit.point.x, hit.point.z);
            }

            var vfxGo = new GameObject($"Breach_{(opening != null ? opening.Id : -1)}");
            vfxGo.transform.position = hit.point;
            vfxGo.transform.rotation = Quaternion.LookRotation(hit.normal);

            if (opening != null)
            {
                Vector3 posDir = opening.CompartmentA == Opening.Sea
                    ? hit.normal : -hit.normal;
                vfxGo.AddComponent<PhysicsWaterEmitter>()
                    .Init(opening, null, posDir);
                var grower = vfxGo.AddComponent<BreachGrower>();
                grower.Init(opening, MaxArea, GrowthRate);
            }

            var visual = vfxGo.AddComponent<BreachVisual>();
            visual.Init(opening, hit);
        }

        bool IsHullFloor(CompartmentDefinition comp, Vector3 normal)
        {
            bool hitFromAbove = normal.y > 0f;
            var allComps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);

            if (hitFromAbove)
            {
                float minFloor = float.MaxValue;
                foreach (var c in allComps)
                    if (c.FloorY < minFloor) minFloor = c.FloorY;
                return Mathf.Abs(comp.FloorY - minFloor) < 0.1f;
            }
            else
            {
                float maxCeiling = float.MinValue;
                foreach (var c in allComps)
                {
                    float ceil = c.FloorY + c.Height;
                    if (ceil > maxCeiling) maxCeiling = ceil;
                }
                return Mathf.Abs((comp.FloorY + comp.Height) - maxCeiling) < 0.1f;
            }
        }

        bool IsHullWall(GameObject wallGo, CompartmentDefinition comp)
        {
            // Hull walls are the outermost walls of the ship
            // East wall of rightmost compartment, West wall of leftmost compartment,
            // and North wall (back wall) of any compartment
            var name = wallGo.name;
            if (name.Contains("WallN")) return true;

            // Check if this is the outermost east or west wall
            float wallX = wallGo.transform.position.x;
            var allComps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);

            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var c in allComps)
            {
                float left = c.transform.position.x - c.LengthX * 0.5f;
                float right = c.transform.position.x + c.LengthX * 0.5f;
                if (left < minX) minX = left;
                if (right > maxX) maxX = right;
            }

            if (name.Contains("WallW") && Mathf.Abs(wallX - minX) < 0.2f) return true;
            if (name.Contains("WallE") && Mathf.Abs(wallX - maxX) < 0.2f) return true;

            return false;
        }

        int FindAdjacentCompartment(SimulationBridge bridge, Vector3 hitPoint, Vector3 hitNormal, CompartmentDefinition exclude)
        {
            // Probe a short distance past the wall in the normal direction (toward the camera)
            // and in the opposite direction (away from camera)
            Vector3 probeDir = -hitNormal;
            Vector3 probePoint = hitPoint + probeDir * 0.5f;

            var allComps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in allComps)
            {
                if (comp == exclude) continue;
                if (IsPointInsideCompartment(probePoint, comp))
                    return bridge.GetCompartmentId(comp);
            }
            return -1;
        }

        bool IsPointInsideCompartment(Vector3 point, CompartmentDefinition comp)
        {
            float cx = comp.transform.position.x;
            float cz = comp.transform.position.z;
            float halfX = comp.LengthX * 0.5f;
            float halfZ = comp.WidthZ * 0.5f;

            return point.x >= cx - halfX && point.x <= cx + halfX
                && point.z >= cz - halfZ && point.z <= cz + halfZ
                && point.y >= comp.FloorY && point.y <= comp.FloorY + comp.Height;
        }
    }

    public class BreachGrower : MonoBehaviour
    {
        Opening _opening;
        float _maxArea;
        float _growthRate;

        public void Init(Opening opening, float maxArea, float growthRate)
        {
            _opening = opening;
            _maxArea = maxArea;
            _growthRate = growthRate;
        }

        void Update()
        {
            if (_opening == null) return;
            if (_opening.Area >= _maxArea) return;
            _opening.Area = Mathf.Min(_opening.Area + _growthRate * Time.deltaTime, _maxArea);
        }
    }
}
