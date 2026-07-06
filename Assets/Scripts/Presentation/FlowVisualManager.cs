using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class FlowVisualManager : MonoBehaviour
    {
        void Start()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var defs = FindObjectsByType<OpeningDefinition>(FindObjectsSortMode.None);
            foreach (var def in defs)
            {
                if (def.SimIndex < 0) continue;
                var opening = bridge.Graph.Openings[def.SimIndex];

                Vector3 posDir = Vector3.right;
                if (def.CompartmentA != null && def.CompartmentB != null)
                {
                    posDir = def.CompartmentB.transform.position
                           - def.CompartmentA.transform.position;
                    if (opening.Kind != OpeningKind.Hatch)
                        posDir.y = 0f;
                }

                def.gameObject.AddComponent<PhysicsWaterEmitter>()
                    .Init(opening, def, posDir);
            }
        }
    }
}
