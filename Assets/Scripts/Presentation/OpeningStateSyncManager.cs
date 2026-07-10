using UnityEngine;

namespace SFP.Presentation
{
    public class OpeningStateSyncManager : MonoBehaviour
    {
        OpeningDefinition[] _defs;
        int _lastCount;

        void LateUpdate()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;
            var graph = bridge.Graph;
            if (graph == null) return;

            if (_defs == null || graph.Openings.Count != _lastCount)
            {
                _defs = FindObjectsByType<OpeningDefinition>(FindObjectsSortMode.None);
                _lastCount = graph.Openings.Count;
            }

            for (int i = 0; i < _defs.Length; i++)
            {
                var def = _defs[i];
                if (def.SimIndex < 0 || def.SimIndex >= graph.Openings.Count) continue;
                def.IsOpen = graph.Openings[def.SimIndex].IsOpen;
            }
        }
    }
}
