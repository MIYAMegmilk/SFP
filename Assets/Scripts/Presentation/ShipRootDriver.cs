using UnityEngine;

namespace SFP.Presentation
{
    // Drives ShipRoot's world transform from simulation state every frame. This is what makes
    // the submarine physically travel through the ocean world instead of being represented by a
    // separate remote proxy (M6 Phase 1).
    //
    // THE INVARIANT: simulation space == ship-local space == the authored build coordinates
    // (x 0..24, y 0..24, z 0..6). The authored ship center c = (12, 9, 3) must land at world
    // position p = (sub.PositionX, -sub.Depth, sub.PositionZ) every frame.
    //
    // Heading axis: SubmarineState.Tick advances PositionX/Z by (sin H, 0, cos H) * speed, so
    // heading 0 moves the ship along world +Z. The authored hull's length axis is ship-local
    // +X (the hull runs x=0..24). Quaternion.Euler(0, H, 0) sends local +Z to (sin H, 0, cos H);
    // to send local +X there instead we need one extra -90 degree yaw first (Euler(0, H-90, 0)
    // sends local +X to (cos(H-90), 0, -sin(H-90)) = (sin H, 0, cos H)). Hence the rotation below.
    [DefaultExecutionOrder(-10)]
    public class ShipRootDriver : MonoBehaviour
    {
        static readonly Vector3 AuthoredCenter = new(12f, 9f, 3f);
        static readonly int ShipWorldToLocalId = Shader.PropertyToID("_SFPShipWorldToLocal");

        void OnEnable()
        {
            Shader.SetGlobalMatrix(ShipWorldToLocalId, transform.worldToLocalMatrix);
        }

        void LateUpdate()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || bridge.SubState == null) return;

            var sub = bridge.SubState;
            Quaternion r = Quaternion.Euler(0f, sub.Heading - 90f, 0f);
            Vector3 p = new(sub.PositionX, -sub.Depth, sub.PositionZ);

            // p must equal transform.position + r * AuthoredCenter (the authored center mapped
            // through this frame's rotation+translation), so solve for transform.position.
            // No smoothing: the sim ticks at 30Hz, direct set is fine for this phase.
            transform.SetPositionAndRotation(p - r * AuthoredCenter, r);

            // Keep the SFP/LitCutout hole test in ship-local space in sync with this pose.
            Shader.SetGlobalMatrix(ShipWorldToLocalId, transform.worldToLocalMatrix);
        }
    }
}
