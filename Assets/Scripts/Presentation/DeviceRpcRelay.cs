using Unity.Netcode;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    // Wire payload for every player-issued device command. Fixed-size (1 + 4 + 4 + 4 = 13 bytes)
    // so a single struct covers helm/reactor/ballast/pump/HVAC/turret/crew/door commands without
    // per-device RPC overloads.
    public struct DeviceCommand : INetworkSerializable
    {
        public DeviceCommandKind Kind;
        public int IntVal;
        public int IntVal2;
        public float FloatVal;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            byte kind = (byte)Kind;
            serializer.SerializeValue(ref kind);
            Kind = (DeviceCommandKind)kind;
            serializer.SerializeValue(ref IntVal);
            serializer.SerializeValue(ref IntVal2);
            serializer.SerializeValue(ref FloatVal);
        }
    }

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

        // Unified entry point: every *Interaction calls this instead of touching Simulation
        // state directly, so the same call works whether we're the host (applied immediately)
        // or a client (round-tripped through the server for authority).
        public void RequestCommand(DeviceCommand cmd)
        {
            if (NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsServer)
                ExecuteCommand(cmd);
            else
                DeviceCommandServerRpc(cmd);
        }

        [ServerRpc(RequireOwnership = false)]
        void DeviceCommandServerRpc(DeviceCommand cmd, ServerRpcParams rpcParams = default)
        {
            ExecuteCommand(cmd);
        }

        void ExecuteCommand(DeviceCommand cmd)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            switch (cmd.Kind)
            {
                case DeviceCommandKind.SetThrottle:
                    if (bridge.Engine != null)
                        bridge.Engine.ThrottleSetting = Mathf.Clamp(cmd.FloatVal, -1f, 1f);
                    break;

                case DeviceCommandKind.SetRudder:
                    if (bridge.SubState != null)
                        bridge.SubState.RudderAngle = Mathf.Clamp(cmd.FloatVal, -1f, 1f);
                    break;

                case DeviceCommandKind.SetDesiredDepth:
                    if (bridge.Navigation != null)
                    {
                        bridge.Navigation.DesiredDepth = Mathf.Max(0f, cmd.FloatVal);
                        // A fresh depth target at the helm re-arms depth hold (see NavigationState).
                        bridge.Navigation.DepthHoldEnabled = true;
                    }
                    break;

                case DeviceCommandKind.SetDesiredHeading:
                    if (bridge.Navigation != null)
                        bridge.Navigation.DesiredHeading = cmd.FloatVal;
                    break;

                case DeviceCommandKind.SetDesiredSpeed:
                    if (bridge.Navigation != null)
                        bridge.Navigation.DesiredSpeed = cmd.FloatVal;
                    break;

                case DeviceCommandKind.ToggleAutoPilot:
                    if (bridge.Navigation != null)
                        bridge.Navigation.AutoPilotEnabled = !bridge.Navigation.AutoPilotEnabled;
                    break;

                case DeviceCommandKind.ToggleDepthHold:
                    if (bridge.Navigation != null)
                        bridge.Navigation.DepthHoldEnabled = !bridge.Navigation.DepthHoldEnabled;
                    break;

                case DeviceCommandKind.SetReactorFission:
                    {
                        var reactors = bridge.PowerGrid?.Reactors;
                        if (reactors != null && cmd.IntVal >= 0 && cmd.IntVal < reactors.Count)
                            reactors[cmd.IntVal].FissionRate = Mathf.Clamp(cmd.FloatVal, 0f, 100f);
                    }
                    break;

                case DeviceCommandKind.SetReactorTurbine:
                    {
                        var reactors = bridge.PowerGrid?.Reactors;
                        if (reactors != null && cmd.IntVal >= 0 && cmd.IntVal < reactors.Count)
                            reactors[cmd.IntVal].TurbineOutput = Mathf.Clamp(cmd.FloatVal, 0f, 100f);
                    }
                    break;

                case DeviceCommandKind.SetBallastTarget:
                    {
                        var ballasts = bridge.Ballasts;
                        if (ballasts != null && cmd.IntVal >= 0 && cmd.IntVal < ballasts.Length)
                        {
                            ballasts[cmd.IntVal].TargetFillLevel = Mathf.Clamp01(cmd.FloatVal);
                            // Manual ballast input takes over depth control from the autopilot;
                            // re-armed by the next SetDesiredDepth (see NavigationState).
                            if (bridge.Navigation != null)
                                bridge.Navigation.DepthHoldEnabled = false;
                        }
                    }
                    break;

                case DeviceCommandKind.TogglePump:
                    {
                        var pumps = bridge.BilgePumps;
                        if (cmd.IntVal >= 0 && cmd.IntVal < pumps.Count)
                            pumps[cmd.IntVal].IsActive = !pumps[cmd.IntVal].IsActive;
                    }
                    break;

                case DeviceCommandKind.AirlockFlood:
                    bridge.GetAirlock(cmd.IntVal)?.RequestFlood(bridge.Graph);
                    break;

                case DeviceCommandKind.AirlockDrain:
                    bridge.GetAirlock(cmd.IntVal)?.RequestDrain();
                    break;

                case DeviceCommandKind.ToggleO2Generator:
                    {
                        var gen = bridge.GetOxygenGenerator(cmd.IntVal);
                        if (gen != null) gen.IsEnabled = !gen.IsEnabled;
                    }
                    break;

                case DeviceCommandKind.ToggleCO2Scrubber:
                    {
                        var scrubber = bridge.GetScrubber(cmd.IntVal);
                        if (scrubber != null) scrubber.IsEnabled = !scrubber.IsEnabled;
                    }
                    break;

                case DeviceCommandKind.ToggleVent:
                    {
                        var vent = bridge.GetVent(cmd.IntVal);
                        if (vent != null) vent.IsEnabled = !vent.IsEnabled;
                    }
                    break;

                case DeviceCommandKind.Extinguish:
                    if (bridge.FireSystem != null && bridge.Graph != null
                        && cmd.IntVal >= 0 && cmd.IntVal < bridge.Graph.Compartments.Count)
                        bridge.FireSystem.Extinguish(cmd.IntVal, Mathf.Max(0f, cmd.FloatVal));
                    break;

                case DeviceCommandKind.ToggleSuppression:
                    {
                        var supp = bridge.GetSuppression(cmd.IntVal);
                        if (supp != null) supp.IsActive = !supp.IsActive;
                    }
                    break;

                case DeviceCommandKind.ToggleSonarActive:
                    {
                        var sonar = bridge.GetSonar(cmd.IntVal);
                        if (sonar != null) sonar.IsActive = !sonar.IsActive;
                    }
                    break;

                case DeviceCommandKind.ToggleSonarPassive:
                    {
                        var sonar = bridge.GetSonar(cmd.IntVal);
                        if (sonar != null) sonar.IsPassive = !sonar.IsPassive;
                    }
                    break;

                case DeviceCommandKind.SetTurretRotation:
                    {
                        var turret = bridge.GetTurret(cmd.IntVal);
                        if (turret != null)
                            turret.Rotation = ((cmd.FloatVal % 360f) + 360f) % 360f;
                    }
                    break;

                case DeviceCommandKind.SetTurretElevation:
                    {
                        var turret = bridge.GetTurret(cmd.IntVal);
                        if (turret != null)
                            turret.Elevation = Mathf.Clamp(cmd.FloatVal, -90f, 90f);
                    }
                    break;

                case DeviceCommandKind.FireTurret:
                    {
                        var turret = bridge.GetTurret(cmd.IntVal);
                        turret?.TryFire(turret.Rotation, bridge.SubState, bridge.Creatures,
                            out _, out _);
                    }
                    break;

                case DeviceCommandKind.IssueCrewOrder:
                    // DeviceCommand has no room for a world point (13-byte budget: Kind+2 ints+1
                    // float), so MoveTo-style orders that need x/y/z stay client-authoritative
                    // until M14 extends the wire format. Repair/pump/reactor/fire orders only
                    // need targetId, which fits in FloatVal.
                    bridge.CrewSystem?.IssueOrder(cmd.IntVal, (CrewOrderKind)cmd.IntVal2,
                        (int)cmd.FloatVal, 0f, 0f, 0f);
                    break;

                case DeviceCommandKind.CancelCrewOrder:
                    bridge.CrewSystem?.CancelOrder(cmd.IntVal);
                    break;

                case DeviceCommandKind.ToggleDoor:
                    ExecuteToggleDoor(cmd.IntVal, bridge);
                    break;

                case DeviceCommandKind.StartCraft:
                    {
                        var fab = bridge.GetFabricator(cmd.IntVal);
                        if (fab != null)
                        {
                            var recipes = fab.IsMedical
                                ? ItemDatabase.MedicalRecipes
                                : ItemDatabase.FabricatorRecipes;
                            if (cmd.IntVal2 >= 0 && cmd.IntVal2 < recipes.Length)
                                fab.StartCraft(recipes[cmd.IntVal2]);
                        }
                    }
                    break;

                case DeviceCommandKind.TakeSuit:
                case DeviceCommandKind.ReturnSuit:
                    // Locker suit count and PlayerController.HasDivingSuit are both Gameplay/
                    // Presentation-local player state with no Simulation-side equivalent yet
                    // (and Presentation may not reference Gameplay types) — stays client-local
                    // until M14 wires suit ownership through the simulation layer.
                    break;
            }
        }

        void ExecuteToggleDoor(int openingIndex, SimulationBridge bridge)
        {
            if (bridge.Graph == null) return;

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
