using System;
using System.Text;
using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class DebugRemoteControl : MonoBehaviour
    {
        public string Command;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<DebugRemoteControl>() != null) return;
            if (SimulationBridge.Instance == null && FindFirstObjectByType<SimulationBridge>() == null) return;
            var go = new GameObject("DebugRemoteControl");
            go.AddComponent<DebugRemoteControl>();
        }

        void Update()
        {
            if (string.IsNullOrEmpty(Command)) return;
            var cmd = Command.Trim();
            Command = "";
            ProcessCommand(cmd);
        }

        public void Execute(string cmd) => ProcessCommand(cmd);

        void ProcessCommand(string cmd)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) { Debug.Log("[RC] SimulationBridge not available"); return; }

            var sub = bridge.SubState;
            var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            switch (parts[0].ToLower())
            {
                case "status":
                    LogStatus(bridge, sub);
                    break;
                case "mission":
                    LogMission(sub);
                    break;
                case "damage":
                    LogDamage(bridge);
                    break;
                case "power":
                    LogPower(bridge);
                    break;
                case "flood":
                    LogFlood(bridge);
                    break;
                case "throttle":
                    if (TryFloat(parts, 1, out float t))
                    {
                        var eng = bridge.Engine;
                        if (eng != null) { eng.ThrottleSetting = Mathf.Clamp(t, -1f, 1f); Debug.Log($"[RC] Throttle → {eng.ThrottleSetting:F2}"); }
                    }
                    break;
                case "rudder":
                    if (TryFloat(parts, 1, out float r))
                    {
                        if (sub != null) { sub.RudderAngle = Mathf.Clamp(r, -1f, 1f); Debug.Log($"[RC] Rudder → {sub.RudderAngle:F2}"); }
                    }
                    break;
                case "heading":
                    if (TryFloat(parts, 1, out float h))
                    {
                        var nav = bridge.Navigation;
                        if (nav != null) { nav.DesiredHeading = h % 360f; nav.AutoPilotEnabled = true; Debug.Log($"[RC] Heading → {nav.DesiredHeading:F0}° (autopilot on)"); }
                    }
                    break;
                case "depth":
                    if (TryFloat(parts, 1, out float d))
                    {
                        var nav = bridge.Navigation;
                        if (nav != null) { nav.DesiredDepth = Mathf.Max(0f, d); nav.DepthHoldEnabled = true; Debug.Log($"[RC] Depth → {nav.DesiredDepth:F0}m (depth hold on)"); }
                    }
                    break;
                case "speed":
                    if (TryFloat(parts, 1, out float spd))
                    {
                        var nav = bridge.Navigation;
                        if (nav != null) { nav.DesiredSpeed = Mathf.Max(0f, spd); nav.AutoPilotEnabled = true; Debug.Log($"[RC] Speed → {nav.DesiredSpeed:F1}m/s (autopilot on)"); }
                    }
                    break;
                case "autopilot":
                    if (parts.Length >= 2)
                    {
                        var nav = bridge.Navigation;
                        if (nav != null) { nav.AutoPilotEnabled = parts[1].ToLower() == "on"; Debug.Log($"[RC] AutoPilot → {(nav.AutoPilotEnabled ? "ON" : "OFF")}"); }
                    }
                    break;
                case "depthhold":
                    if (parts.Length >= 2)
                    {
                        var nav = bridge.Navigation;
                        if (nav != null) { nav.DepthHoldEnabled = parts[1].ToLower() == "on"; Debug.Log($"[RC] DepthHold → {(nav.DepthHoldEnabled ? "ON" : "OFF")}"); }
                    }
                    break;
                case "stop":
                    if (bridge.Engine != null) bridge.Engine.ThrottleSetting = 0f;
                    if (sub != null) sub.RudderAngle = 0f;
                    if (bridge.Navigation != null) bridge.Navigation.AutoPilotEnabled = false;
                    Debug.Log("[RC] All stop — throttle 0, rudder 0, autopilot off");
                    break;
                case "surface":
                    if (bridge.Navigation != null) { bridge.Navigation.DesiredDepth = 0f; bridge.Navigation.DepthHoldEnabled = true; }
                    Debug.Log("[RC] Emergency surface — depth target 0m");
                    break;
                case "warp":
                    if (sub != null && TryFloat(parts, 1, out float wx) && TryFloat(parts, 2, out float wz))
                    {
                        sub.PositionX = wx;
                        sub.PositionZ = wz;
                        Debug.Log($"[RC] Warped to ({wx:F0}, {wz:F0})");
                    }
                    break;
                case "navigate":
                    NavigateToMission(bridge, sub);
                    break;
                case "help":
                    LogHelp();
                    break;
                default:
                    Debug.Log($"[RC] Unknown command: {parts[0]}. Use 'help' for list.");
                    break;
            }
        }

        void LogStatus(SimulationBridge bridge, SubmarineState sub)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== [RC] SHIP STATUS ===");

            if (sub != null)
            {
                sb.AppendLine($"Position: ({sub.PositionX:F1}, {sub.PositionZ:F1})");
                sb.AppendLine($"Depth: {sub.Depth:F1}m  (crush: {sub.CrushDepth:F0}m, below: {sub.IsBelowCrushDepth})");
                sb.AppendLine($"Heading: {sub.Heading:F1}°  TurnRate: {sub.TurnRate:F2}°/s");
                sb.AppendLine($"Speed: {sub.HorizontalSpeed:F2}m/s  Velocity(vert): {sub.Velocity:F2}m/s");
                sb.AppendLine($"Rudder: {sub.RudderAngle:F2}  Noise: {sub.NoiseLevel:F1}");
                sb.AppendLine($"TotalWater: {sub.TotalWaterVolume:F1}m³  NetForce: {sub.NetForce:F0}N");
            }

            var eng = bridge.Engine;
            if (eng != null)
                sb.AppendLine($"Engine: throttle={eng.ThrottleSetting:F2} thrust={eng.CurrentThrust:F0}N condition={eng.Condition:F0}%");

            var nav = bridge.Navigation;
            if (nav != null)
            {
                sb.AppendLine($"AutoPilot: {(nav.AutoPilotEnabled ? "ON" : "OFF")} heading={nav.DesiredHeading:F0}° speed={nav.DesiredSpeed:F1}m/s");
                sb.AppendLine($"DepthHold: {(nav.DepthHoldEnabled ? "ON" : "OFF")} target={nav.DesiredDepth:F0}m");
            }

            var pg = bridge.PowerGrid;
            if (pg != null)
                sb.AppendLine($"Power: prod={pg.TotalProduction:F0}W cons={pg.TotalConsumption:F0}W voltage={pg.GridVoltage:F2}");

            sb.Append("========================");
            Debug.Log(sb.ToString());
        }

        void LogMission(SubmarineState sub)
        {
            var mm = FindFirstObjectByType<MissionManager>();
            var m = mm?.Missions;
            if (m == null) { Debug.Log("[RC] MissionSystem not available"); return; }

            var sb = new StringBuilder();
            sb.AppendLine("=== [RC] MISSION STATUS ===");
            sb.AppendLine($"Round: {m.Round}  Phase: {m.Phase}  Progress: {m.CompletedCount}/{m.TotalCount}");

            var cur = m.Current;
            if (cur != null)
            {
                sb.AppendLine($"Objective: {cur.Label}");
                sb.AppendLine($"Target: ({cur.TargetX:F0}, {cur.TargetZ:F0})  Radius: {cur.Radius:F0}m");
                if (sub != null)
                {
                    sb.AppendLine($"Distance: {m.DistanceToTarget(sub):F0}m  Bearing: {m.BearingToTarget(sub):F0}°");
                    float delta = Mathf.DeltaAngle(sub.Heading, m.BearingToTarget(sub));
                    string turn = delta < -10f ? "TURN PORT" : delta > 10f ? "TURN STBD" : "ON COURSE";
                    sb.AppendLine($"Heading delta: {delta:F1}° → {turn}");
                }
                if (cur.Kind == MissionKind.HoldPosition)
                    sb.AppendLine($"Hold: {cur.HoldProgress:F1}/{cur.HoldSeconds:F0}s ({(cur.HoldSeconds > 0f ? cur.HoldProgress / cur.HoldSeconds * 100f : 0f):F0}%)");
            }
            else
            {
                sb.AppendLine("No active objective (standing by)");
            }
            sb.Append("===========================");
            Debug.Log(sb.ToString());
        }

        void LogDamage(SimulationBridge bridge)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== [RC] DAMAGE REPORT ===");

            var sub = bridge.SubState;
            if (sub != null)
                sb.AppendLine($"CrushDepth: {sub.CrushDepth:F0}m  BelowCrush: {sub.IsBelowCrushDepth}  TotalWater: {sub.TotalWaterVolume:F1}m³");

            var eng = bridge.Engine;
            if (eng != null)
                sb.AppendLine($"Engine condition: {eng.Condition:F0}%");

            var pg = bridge.PowerGrid;
            if (pg != null)
            {
                for (int i = 0; i < pg.Reactors.Count; i++)
                {
                    var r = pg.Reactors[i];
                    sb.AppendLine($"Reactor[{i}]: temp={r.Temperature:F1}° fuel={r.FuelRemaining:F1}% cond={r.Condition:F0}% melting={r.IsMeltingDown} exploded={r.HasExploded}");
                }
                for (int i = 0; i < pg.Junctions.Count; i++)
                {
                    var j = pg.Junctions[i];
                    sb.AppendLine($"Junction[{i}]: load={j.CurrentLoad:F0}/{j.MaxLoad:F0}W cond={j.Condition:F0}% overload={j.IsOverloaded} shorted={j.IsShortedByWater}");
                }
            }

            sb.Append("==========================");
            Debug.Log(sb.ToString());
        }

        void LogPower(SimulationBridge bridge)
        {
            var pg = bridge.PowerGrid;
            if (pg == null) { Debug.Log("[RC] PowerGrid not available"); return; }

            var sb = new StringBuilder();
            sb.AppendLine("=== [RC] POWER STATUS ===");
            sb.AppendLine($"Grid: prod={pg.TotalProduction:F0}W cons={pg.TotalConsumption:F0}W voltage={pg.GridVoltage:F2} unlimited={pg.UnlimitedPower}");

            for (int i = 0; i < pg.Reactors.Count; i++)
            {
                var r = pg.Reactors[i];
                sb.AppendLine($"Reactor[{i}]: output={r.CurrentPowerOutput:F0}W fission={r.FissionRate:F2} temp={r.Temperature:F1}° fuel={r.FuelRemaining:F1}%");
            }
            for (int i = 0; i < pg.Batteries.Count; i++)
            {
                var b = pg.Batteries[i];
                sb.AppendLine($"Battery[{i}]: {b.Charge:F0}/{b.MaxCharge:F0}Wh ({b.ChargeFraction * 100f:F0}%)");
            }

            sb.Append("=========================");
            Debug.Log(sb.ToString());
        }

        void LogFlood(SimulationBridge bridge)
        {
            var graph = bridge.Graph;
            if (graph == null) { Debug.Log("[RC] CompartmentGraph not available"); return; }

            var sb = new StringBuilder();
            sb.AppendLine("=== [RC] FLOOD STATUS ===");

            var comps = graph.Compartments;
            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                if (c.WaterVolume > 0.01f || c.AirPressureAtm > 1.05f)
                    sb.AppendLine($"Comp[{i}]: water={c.WaterVolume:F1}/{c.Volume:F1}m³ ({c.WaterFraction * 100f:F0}%) pressure={c.AirPressureAtm:F2}atm sealed={c.IsAirSealed}");
            }

            if (sb.Length < 30)
                sb.AppendLine("All compartments dry and nominal.");

            sb.Append("=========================");
            Debug.Log(sb.ToString());
        }

        void NavigateToMission(SimulationBridge bridge, SubmarineState sub)
        {
            var mm = FindFirstObjectByType<MissionManager>();
            var missions = mm?.Missions;
            var nav = bridge.Navigation;
            if (missions?.Current == null || nav == null || sub == null)
            {
                Debug.Log("[RC] Cannot navigate — no active mission or navigation unavailable");
                return;
            }

            float bearing = missions.BearingToTarget(sub);
            nav.DesiredHeading = bearing;
            nav.DesiredSpeed = 4f;
            nav.AutoPilotEnabled = true;
            Debug.Log($"[RC] Navigating to {missions.Current.Label} — heading {bearing:F0}°, speed 4m/s, autopilot on");
        }

        void LogHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== [RC] COMMANDS ===");
            sb.AppendLine("status        — ship state (position, speed, nav, power)");
            sb.AppendLine("mission       — current objective, distance, bearing");
            sb.AppendLine("damage        — hull, reactor, junction condition");
            sb.AppendLine("power         — reactor/battery/grid details");
            sb.AppendLine("flood         — compartment water levels");
            sb.AppendLine("throttle <v>  — set throttle (-1..1)");
            sb.AppendLine("rudder <v>    — set rudder (-1..1)");
            sb.AppendLine("heading <deg> — set heading + autopilot on");
            sb.AppendLine("depth <m>     — set depth target + depth hold on");
            sb.AppendLine("speed <m/s>   — set speed + autopilot on");
            sb.AppendLine("autopilot on|off");
            sb.AppendLine("depthhold on|off");
            sb.AppendLine("stop          — all stop (throttle 0, rudder 0, AP off)");
            sb.AppendLine("surface       — emergency surface (depth 0)");
            sb.AppendLine("warp <x> <z>  — teleport submarine");
            sb.AppendLine("navigate      — autopilot to current mission target");
            sb.Append("=====================");
            Debug.Log(sb.ToString());
        }

        static bool TryFloat(string[] parts, int index, out float value)
        {
            value = 0f;
            return index < parts.Length && float.TryParse(parts[index], out value);
        }
    }
}
