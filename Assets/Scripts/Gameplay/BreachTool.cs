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
        public float MaxDistance = 50f;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (mouse.rightButton.isPressed) return;
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.isPressed) return;

            var cam = Camera.main;
            if (cam == null) return;
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            var ray = cursorLocked
                ? new Ray(cam.transform.position, cam.transform.forward)
                : cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, MaxDistance, ~0, QueryTriggerInteraction.Ignore)) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            // Simulation space == ship-local space; the raycast hit is world-space and must be
            // converted before it can be compared against authored compartment bounds or fed
            // into any sim call.
            Vector3 localHit = bridge.WorldToShip(hit.point);

            var compDef = hit.collider.GetComponentInParent<CompartmentDefinition>();
            if (compDef == null)
                compDef = FindCompartmentAt(bridge, bridge.WorldToShip(hit.point + hit.normal * 0.3f));
            if (compDef == null) return;

            int thisId = bridge.GetCompartmentId(compDef);
            if (thisId < 0) return;

            // hit.normal.y is frame-invariant under the ship's yaw-only rotation (rotation about Y
            // never mixes Y with X/Z), so no conversion is needed here.
            bool isFloorOrCeiling = Mathf.Abs(hit.normal.y) > 0.7f;

            int otherId = FindAdjacentCompartment(bridge, hit.point, hit.normal, compDef);

            bool behindInsideSame = IsPointInsideCompartment(bridge,
                bridge.WorldToShip(hit.point - hit.normal * 0.5f), compDef);
            bool isHullSurface = otherId < 0 && !behindInsideSame;

            Opening opening = null;
            if (otherId >= 0)
            {
                opening = bridge.Graph.AddOpening(OpeningKind.Breach, thisId, otherId,
                    InitialArea, localHit.y, 0.3f);

                if (bridge.WaterSystem != null)
                {
                    float sillY = localHit.y - 0.15f;
                    float w = Mathf.Sqrt(InitialArea);
                    bridge.WaterSystem.AddConnection(opening, thisId, otherId,
                        localHit.x, localHit.z, w, sillY, isFloorOrCeiling);
                }
            }
            else if (isHullSurface)
            {
                // bridge.AddBreachAtRuntime takes ship-local X/Z (localX/localZ) going forward.
                opening = bridge.AddBreachAtRuntime(compDef, InitialArea, localHit.y, 0.3f,
                    localHit.x, localHit.z);
            }

            var vfxGo = new GameObject($"Breach_{(opening != null ? opening.Id : -1)}");
            // Parent under the hit compartment so the breach visual/emitter rides with the ship.
            vfxGo.transform.SetParent(compDef.transform);
            vfxGo.transform.position = hit.point;
            vfxGo.transform.rotation = Quaternion.LookRotation(hit.normal);

            if (opening != null)
            {
                Vector3 posDir = opening.CompartmentA == Opening.Sea
                    ? hit.normal : -hit.normal;
                vfxGo.AddComponent<PhysicsWaterEmitter>()
                    .Init(opening, null, posDir);
                Vector3 localNormal = bridge.ShipRoot != null
                    ? bridge.ShipRoot.InverseTransformDirection(hit.normal) : hit.normal;
                var face = DamageEventPresenter.FaceFromNormal(localNormal);
                var section = bridge.DamageSystem.FindSection(thisId, face);
                bridge.DamageSystem.RegisterBreach(opening.Id, MaxArea, section?.Id ?? -1);
            }

            var visual = vfxGo.AddComponent<BreachVisual>();
            visual.Init(opening, hit);
        }

        // shipLocalPoint must already be in ship-local space (see bridge.WorldToShip).
        CompartmentDefinition FindCompartmentAt(SimulationBridge bridge, Vector3 shipLocalPoint)
        {
            var allComps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in allComps)
                if (IsPointInsideCompartment(bridge, shipLocalPoint, comp))
                    return comp;
            return null;
        }

        int FindAdjacentCompartment(SimulationBridge bridge, Vector3 hitPoint, Vector3 hitNormal, CompartmentDefinition exclude)
        {
            // Probe a short distance past the wall in the normal direction (toward the camera)
            // and in the opposite direction (away from camera). hitPoint/hitNormal are world-space;
            // convert the resulting probe point to ship-local before testing compartment bounds.
            Vector3 probeDir = -hitNormal;
            Vector3 probePointLocal = bridge.WorldToShip(hitPoint + probeDir * 0.5f);

            var allComps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in allComps)
            {
                if (comp == exclude) continue;
                if (IsPointInsideCompartment(bridge, probePointLocal, comp))
                    return bridge.GetCompartmentId(comp);
            }
            return -1;
        }

        // shipLocalPoint must already be in ship-local space. The compartment's world position is
        // converted to ship-local too, since ShipRoot can now rotate (world-axis-aligned math would
        // break once the ship yaws).
        bool IsPointInsideCompartment(SimulationBridge bridge, Vector3 shipLocalPoint, CompartmentDefinition comp)
        {
            Vector3 c = bridge.WorldToShip(comp.transform.position);
            float halfX = comp.LengthX * 0.5f;
            float halfZ = comp.WidthZ * 0.5f;

            return shipLocalPoint.x >= c.x - halfX && shipLocalPoint.x <= c.x + halfX
                && shipLocalPoint.z >= c.z - halfZ && shipLocalPoint.z <= c.z + halfZ
                && shipLocalPoint.y >= comp.FloorY && shipLocalPoint.y <= comp.FloorY + comp.Height;
        }
    }

}
