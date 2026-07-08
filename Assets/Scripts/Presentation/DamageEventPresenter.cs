using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class DamageEventPresenter : MonoBehaviour
    {
        public float BreachHeight = 0.3f;

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.DamageSystem == null) return;

            while (bridge.DamageSystem.TryDequeueEvent(out var evt))
            {
                switch (evt.Kind)
                {
                    case DamageEventKind.BreachCreated:
                        SpawnBreach(bridge, evt);
                        break;
                    case DamageEventKind.Explosion:
                        PlayExplosion(bridge, evt);
                        break;
                    case DamageEventKind.Collision:
                        PlayImpact(evt);
                        break;
                    case DamageEventKind.ElectricalShort:
                        PlaySparks(bridge, evt);
                        break;
                    case DamageEventKind.MineExplosion:
                        PlayMineExplosion(evt);
                        break;
                    case DamageEventKind.CreatureAttack:
                        PlayCreatureAttack(evt);
                        break;
                }
            }
        }

        void SpawnBreach(SimulationBridge bridge, DamageEvent evt)
        {
            var def = bridge.GetCompartmentDef(evt.CompartmentId);
            if (def == null)
            {
                ClearBreachPending(bridge, evt.SectionId);
                return;
            }

            if (!TryGetFaceHit(bridge, def, evt.Face, out var hit))
            {
                ClearBreachPending(bridge, evt.SectionId);
                return;
            }

            Vector3 localHit = bridge.WorldToShip(hit.point);
            var opening = bridge.AddBreachAtRuntime(def, evt.Magnitude,
                localHit.y, BreachHeight, localHit.x, localHit.z);
            if (opening == null)
            {
                ClearBreachPending(bridge, evt.SectionId);
                return;
            }

            var go = new GameObject($"AutoBreach_{opening.Id}");
            go.transform.SetPositionAndRotation(hit.point, Quaternion.LookRotation(hit.normal));
            go.transform.SetParent(def.transform, true);
            go.AddComponent<PhysicsWaterEmitter>().Init(opening, null, hit.normal);
            go.AddComponent<BreachVisual>().Init(opening, hit);

            float maxArea = Mathf.Max(evt.Magnitude * 6f, 0.25f);
            bridge.DamageSystem.RegisterBreach(opening.Id, maxArea, evt.SectionId);
        }

        void ClearBreachPending(SimulationBridge bridge, int sectionId)
        {
            if (sectionId < 0) return;
            var section = bridge.DamageSystem.GetSection(sectionId);
            if (section != null) section.BreachPending = false;
        }

        void PlayExplosion(SimulationBridge bridge, DamageEvent evt)
        {
            var def = bridge.GetCompartmentDef(evt.CompartmentId);
            if (def == null) return;
            Debug.Log($"[DamageSystem] EXPLOSION in compartment {evt.CompartmentId}, magnitude={evt.Magnitude:F2}");
        }

        void PlayImpact(DamageEvent evt)
        {
            Debug.Log($"[DamageSystem] COLLISION impact speed={evt.Magnitude:F1}m/s");
        }

        void PlaySparks(SimulationBridge bridge, DamageEvent evt)
        {
            Debug.Log($"[DamageSystem] ELECTRICAL SHORT in compartment {evt.CompartmentId}, water={evt.Magnitude:F0}%");
        }

        void PlayMineExplosion(DamageEvent evt)
        {
            Debug.Log($"[DamageSystem] MINE HIT! magnitude={evt.Magnitude:F2}");
        }

        void PlayCreatureAttack(DamageEvent evt)
        {
            Debug.Log($"[DamageSystem] CREATURE ATTACK! magnitude={evt.Magnitude:F2}");
        }

        public static bool TryGetFaceHit(SimulationBridge bridge, CompartmentDefinition def, HullFace face, out RaycastHit hit)
        {
            Vector3 center = def.transform.position;
            Vector3 localDir = FaceDirection(face);
            Vector3 dir = bridge != null && bridge.ShipRoot != null
                ? bridge.ShipRoot.rotation * localDir : localDir;
            float dist = Mathf.Max(Mathf.Max(def.LengthX, def.WidthZ), def.Height);
            if (Physics.Raycast(center, dir, out hit, dist))
                return hit.collider.GetComponentInParent<CompartmentDefinition>() == def;
            return false;
        }

        public static Vector3 FaceDirection(HullFace face)
        {
            switch (face)
            {
                case HullFace.North:   return Vector3.forward;
                case HullFace.South:   return Vector3.back;
                case HullFace.East:    return Vector3.right;
                case HullFace.West:    return Vector3.left;
                case HullFace.Floor:   return Vector3.down;
                case HullFace.Ceiling: return Vector3.up;
                default:               return Vector3.forward;
            }
        }

        public static HullFace FaceFromNormal(Vector3 n)
        {
            Vector3 inward = -n;
            float ax = Mathf.Abs(inward.x);
            float ay = Mathf.Abs(inward.y);
            float az = Mathf.Abs(inward.z);

            if (ax >= ay && ax >= az)
                return inward.x > 0f ? HullFace.East : HullFace.West;
            if (ay >= ax && ay >= az)
                return inward.y > 0f ? HullFace.Ceiling : HullFace.Floor;
            return inward.z > 0f ? HullFace.North : HullFace.South;
        }
    }
}
