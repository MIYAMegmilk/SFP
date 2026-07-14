using Unity.Netcode;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class DeviceRpcRelay : NetworkBehaviour
    {
        public static DeviceRpcRelay Instance { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public void RequestToggleDoor(int openingIndex)
        {
            ToggleDoorServerRpc(openingIndex);
        }

        [ServerRpc(RequireOwnership = false)]
        void ToggleDoorServerRpc(int openingIndex, ServerRpcParams rpcParams = default)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.Graph == null) return;

            var openings = bridge.Graph.Openings;
            if (openingIndex < 0 || openingIndex >= openings.Count) return;

            var opening = openings[openingIndex];
            if (opening.Kind == OpeningKind.Breach) return;
            if (opening.IsLocked) return;

            opening.IsOpen = !opening.IsOpen;

            var defs = UnityEngine.Object.FindObjectsByType<OpeningDefinition>(
                UnityEngine.FindObjectsSortMode.None);
            foreach (var def in defs)
            {
                if (def.SimIndex == openingIndex)
                {
                    def.IsOpen = opening.IsOpen;
                    break;
                }
            }
        }
    }
}
