using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class CrewCommandInteraction : MonoBehaviour
    {
        public float MaxDistance = 50f;

        int _selectedCrewId = -1;

        void Update()
        {
            if (ConsoleFocus.IsLocked) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.CrewSystem == null) return;

            if (kb.cKey.wasPressedThisFrame)
                SelectCrew(bridge, mouse);

            if (kb.vKey.wasPressedThisFrame && _selectedCrewId >= 0)
                IssueContextOrder(bridge, mouse);

            if (kb.xKey.wasPressedThisFrame && _selectedCrewId >= 0)
            {
                var relay = DeviceRpcRelay.Instance;
                if (relay != null)
                    relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.CancelCrewOrder, IntVal = _selectedCrewId });
                else
                    bridge.CrewSystem.CancelOrder(_selectedCrewId);
            }
        }

        void SelectCrew(SimulationBridge bridge, Mouse mouse)
        {
            var ray = GetRay(mouse);
            if (Physics.Raycast(ray, out var hit, MaxDistance))
            {
                var crewRef = hit.collider.GetComponentInParent<CrewMemberRef>();
                if (crewRef != null && crewRef.CrewId >= 0)
                {
                    _selectedCrewId = crewRef.CrewId;
                    SyncSelection();
                    return;
                }
            }

            // cycle to next living crew
            var crew = bridge.CrewSystem.Crew;
            if (crew.Count == 0) return;

            int start = _selectedCrewId + 1;
            for (int i = 0; i < crew.Count; i++)
            {
                int idx = (start + i) % crew.Count;
                if (!crew[idx].IsDead)
                {
                    _selectedCrewId = crew[idx].Id;
                    SyncSelection();
                    return;
                }
            }
        }

        void IssueContextOrder(SimulationBridge bridge, Mouse mouse)
        {
            var ray = GetRay(mouse);
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var shipLocal = bridge.WorldToShip(hit.point);

            // breach repair
            var breachVis = hit.collider.GetComponentInParent<BreachVisual>();
            if (breachVis != null && breachVis.Opening != null)
            {
                var relay = DeviceRpcRelay.Instance;
                if (relay != null)
                    relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.IssueCrewOrder, IntVal = _selectedCrewId, IntVal2 = (int)CrewOrderKind.RepairBreach, FloatVal = (float)breachVis.Opening.Id });
                else
                    bridge.CrewSystem.IssueOrder(_selectedCrewId, CrewOrderKind.RepairBreach,
                        breachVis.Opening.Id, shipLocal.x, shipLocal.y, shipLocal.z);
                return;
            }

            // pump
            var pump = hit.collider.GetComponentInParent<Pump>();
            if (pump != null)
            {
                int pumpIdx = FindPumpIndex(bridge, pump);
                if (pumpIdx >= 0)
                {
                    var relay = DeviceRpcRelay.Instance;
                    if (relay != null)
                        relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.IssueCrewOrder, IntVal = _selectedCrewId, IntVal2 = (int)CrewOrderKind.OperatePump, FloatVal = (float)pumpIdx });
                    else
                        bridge.CrewSystem.IssueOrder(_selectedCrewId, CrewOrderKind.OperatePump,
                            pumpIdx, shipLocal.x, shipLocal.y, shipLocal.z);
                    return;
                }
            }

            // reactor
            var reactorDef = hit.collider.GetComponentInParent<ReactorDefinition>();
            if (reactorDef != null)
            {
                int stationIdx = FindReactorStationIndex(bridge, reactorDef);
                if (stationIdx >= 0)
                {
                    var relay = DeviceRpcRelay.Instance;
                    if (relay != null)
                        relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.IssueCrewOrder, IntVal = _selectedCrewId, IntVal2 = (int)CrewOrderKind.OperateReactor, FloatVal = (float)stationIdx });
                    else
                        bridge.CrewSystem.IssueOrder(_selectedCrewId, CrewOrderKind.OperateReactor,
                            stationIdx, shipLocal.x, shipLocal.y, shipLocal.z);
                    return;
                }
            }

            // fight fire or move to
            int compId = bridge.CrewSystem.Nav.FindCompartmentAt(shipLocal.x, shipLocal.y, shipLocal.z);
            if (compId >= 0)
            {
                var fire = bridge.FireSystem;
                if (fire != null && fire.GetFireIntensity(compId) > 0.05f)
                {
                    var relay = DeviceRpcRelay.Instance;
                    if (relay != null)
                        relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.IssueCrewOrder, IntVal = _selectedCrewId, IntVal2 = (int)CrewOrderKind.FightFire, FloatVal = (float)compId });
                    else
                        bridge.CrewSystem.IssueOrder(_selectedCrewId, CrewOrderKind.FightFire,
                            compId, shipLocal.x, shipLocal.y, shipLocal.z);
                    return;
                }
            }

            {
                var relay = DeviceRpcRelay.Instance;
                if (relay != null)
                    relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.IssueCrewOrder, IntVal = _selectedCrewId, IntVal2 = (int)CrewOrderKind.MoveTo, FloatVal = -1f });
                else
                    bridge.CrewSystem.IssueOrder(_selectedCrewId, CrewOrderKind.MoveTo,
                        -1, shipLocal.x, shipLocal.y, shipLocal.z);
            }
        }

        int FindPumpIndex(SimulationBridge bridge, Pump pump)
        {
            if (pump.State == null) return -1;
            var crew = bridge.CrewSystem;
            for (int i = 0; i < bridge.BilgePumps.Count; i++)
            {
                if (bridge.BilgePumps[i] == pump.State) return i;
            }
            return -1;
        }

        int FindReactorStationIndex(SimulationBridge bridge, ReactorDefinition reactorDef)
        {
            return reactorDef.ReactorIndex >= 0 ? reactorDef.ReactorIndex : -1;
        }

        Ray GetRay(Mouse mouse)
        {
            var cam = Camera.main;
            if (cam == null) return new Ray();
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            return cursorLocked
                ? new Ray(cam.transform.position, cam.transform.forward)
                : cam.ScreenPointToRay(mouse.position.ReadValue());
        }

        void SyncSelection()
        {
            var vm = CrewVisualManager.Instance;
            if (vm != null) vm.SetSelected(_selectedCrewId);
        }

        void OnGUI()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.CrewSystem == null) return;

            var crew = bridge.CrewSystem.Crew;
            if (crew.Count == 0) return;

            float x = 10f;
            float y = Screen.height - 30f * crew.Count - 50f;

            GUI.Label(new Rect(x, y, 200f, 20f), "<b>Crew</b>  C:select V:order X:cancel");
            y += 22f;

            for (int i = 0; i < crew.Count; i++)
            {
                var c = crew[i];
                bool selected = c.Id == _selectedCrewId;
                string prefix = selected ? "> " : "  ";

                string jobTag = CrewJob.GetLabel(c.Job);
                string status = c.IsDead ? "<color=red>DEAD</color>" : TaskLabel(c.Task);
                string hp = c.IsDead ? "" : $"  HP:{c.Health:F0} O2:{c.Oxygen:F0}";

                string label = $"{prefix}{jobTag}{c.Id + 1}: {status}{hp}";
                if (selected)
                    label = $"<color=lime>{label}</color>";

                GUI.Label(new Rect(x, y, 400f, 22f), label);
                y += 22f;
            }
        }

        static string TaskLabel(CrewTaskKind task)
        {
            switch (task)
            {
                case CrewTaskKind.FightFire: return "<color=red>FIRE</color>";
                case CrewTaskKind.RepairBreach: return "<color=orange>REPAIR</color>";
                case CrewTaskKind.OperatePump: return "<color=cyan>PUMP</color>";
                case CrewTaskKind.OperateReactor: return "<color=cyan>REACTOR</color>";
                case CrewTaskKind.MoveTo: return "MOVE";
                case CrewTaskKind.Flee: return "<color=magenta>FLEE</color>";
                case CrewTaskKind.Idle: return "Idle";
                default: return "-";
            }
        }
    }
}
