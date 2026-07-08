using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class PlayerHUD : MonoBehaviour
    {
        public float MaxDistance = 3f;

        PlayerController _player;
        RepairTool _repairTool;
        string _targetLabel;

        void Update()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerController>();
            if (_player == null || !_player.enabled)
            {
                _targetLabel = null;
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                _targetLabel = null;
                return;
            }

            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, MaxDistance))
                _targetLabel = ResolveTargetLabel(hit.collider);
            else
                _targetLabel = null;
        }

        static string ResolveTargetLabel(Collider col)
        {
            var navDef = col.GetComponentInParent<NavigationTerminalDefinition>();
            var sonarDef = col.GetComponentInParent<SonarDefinition>();
            if (navDef != null && sonarDef != null) return $"NavSonar Console Mk{navDef.Tier} [E]";
            if (navDef != null) return "Navigation Terminal [E]";
            if (sonarDef != null) return "Sonar [E]";
            var monDef = col.GetComponentInParent<MonitorDefinition>();
            if (monDef != null)
            {
                string src = monDef.SourceDevice != null ? monDef.SourceDevice.GetType().Name.Replace("Definition", "") : "Status";
                return $"Monitor ({src}) [V]";
            }
            if (col.GetComponentInParent<ReactorDefinition>() != null) return "Reactor [E]";
            if (col.GetComponentInParent<StatusMonitorDefinition>() != null) return "Status Monitor [E]";

            var fab = col.GetComponentInParent<FabricatorDefinition>();
            if (fab != null) return fab.IsMedical ? "Medical Fabricator [E]" : "Fabricator [E]";

            if (col.GetComponentInParent<DivingSuitLockerDefinition>() != null) return "Diving Suit Locker [E]";
            if (col.GetComponentInParent<TurretDefinition>() != null) return "Turret [E]";
            if (col.GetComponentInParent<SuppressionSystemDefinition>() != null) return "Fire Suppression [E]";
            if (col.GetComponentInParent<OxygenGeneratorDefinition>() != null) return "Oxygen Generator [E]";
            if (col.GetComponentInParent<JunctionBoxDefinition>() != null) return "Junction Box";
            if (col.GetComponentInParent<BatteryDefinition>() != null) return "Battery";
            if (col.GetComponentInParent<EngineDefinition>() != null) return "Engine";

            // No player-driven interaction toggles ballast tanks (auto-controlled by
            // NavigationState autopilot), so this is an informational label with no key hint.
            if (col.GetComponentInParent<BallastTankDefinition>() != null) return "Ballast Pump";

            if (col.GetComponentInParent<Pump>() != null) return "Pump [E]";

            var opening = col.GetComponentInParent<OpeningDefinition>();
            if (opening != null)
            {
                switch (opening.Kind)
                {
                    case OpeningKind.Door: return "Door [F]";
                    case OpeningKind.Hatch: return "Hatch [F]";
                    case OpeningKind.Breach: return "Breach";
                }
            }

            if (col.GetComponentInParent<Ladder>() != null) return "Ladder";

            return null;
        }

        void OnGUI()
        {
            if (_player == null)
                _player = FindFirstObjectByType<PlayerController>();
            if (_player == null || !_player.enabled) return;

            float sw = Screen.width;
            float sh = Screen.height;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };

            var bridge = SimulationBridge.Instance;

            // Power status (top-right)
            if (bridge?.PowerGrid != null)
            {
                var pg = bridge.PowerGrid;
                string powerText = $"Power: {pg.GridVoltage * 100f:F0}%";
                Color powerColor = pg.GridVoltage >= 0.8f ? Color.green
                    : pg.GridVoltage >= 0.4f ? Color.yellow : Color.red;
                var powerStyle = new GUIStyle(style) { alignment = TextAnchor.UpperRight, normal = { textColor = powerColor } };
                GUI.Label(new Rect(sw - 210, 10, 200, 20), powerText, powerStyle);
            }

            // Damage panel (top-left)
            DrawDamagePanel(bridge, style);

            // Oxygen bar (bottom center)
            if (_player.IsSubmerged)
            {
                float barW = 200f, barH = 16f;
                float barX = (sw - barW) * 0.5f;
                float barY = sh - 60f;

                GUI.Box(new Rect(barX - 2, barY - 2, barW + 4, barH + 4), "");
                GUI.color = Color.Lerp(Color.red, Color.cyan, _player.OxygenFraction);
                GUI.DrawTexture(new Rect(barX, barY, barW * _player.OxygenFraction, barH), Texture2D.whiteTexture);
                GUI.color = Color.white;

                var oxyStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    normal = { textColor = Color.white }
                };
                GUI.Label(new Rect(barX, barY, barW, barH), $"O2: {_player.Oxygen:F0}s", oxyStyle);
            }

            // Pressure warning
            if (_player.RoomPressureAtm > 1.2f)
            {
                float p = _player.RoomPressureAtm;
                Color pColor = p > 4f ? Color.red : p > 2f ? Color.yellow : Color.white;
                var pressStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                    normal = { textColor = pColor }
                };
                GUI.Label(new Rect(0, 30, sw, 25), $"Pressure: {p:F1} atm", pressStyle);
            }

            // Reactor meltdown warning
            if (bridge?.PowerGrid != null)
            {
                foreach (var r in bridge.PowerGrid.Reactors)
                {
                    if (r.IsMeltingDown && !r.HasExploded)
                    {
                        float remaining = r.MeltdownFuseSeconds - r.MeltdownProgress;
                        bool flash = Mathf.Repeat(Time.time, 0.5f) < 0.25f;
                        if (flash)
                        {
                            var meltStyle = new GUIStyle(GUI.skin.label)
                            {
                                fontSize = 18, fontStyle = FontStyle.Bold,
                                alignment = TextAnchor.UpperCenter,
                                normal = { textColor = Color.red }
                            };
                            GUI.Label(new Rect(0, 55, sw, 25),
                                $"!! REACTOR MELTDOWN IN {remaining:F0}s !!", meltStyle);
                        }
                        break;
                    }
                }
            }

            // Crosshair
            float size = 2f;
            float cx = sw * 0.5f, cy = sh * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(cx - 8, cy - size * 0.5f, 16, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - 8, size, 16), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Target name label (above crosshair)
            if (!string.IsNullOrEmpty(_targetLabel))
            {
                var targetStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 1f, 1f, 0.9f) }
                };
                GUI.Label(new Rect(cx - 150, cy - 30 - 8, 300, 16), _targetLabel, targetStyle);
            }

            // Repair progress bar (below crosshair)
            DrawRepairProgress(sw, sh);

            // Controls hint (bottom-left)
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(10, sh - 100, 400, 80),
                "WASD: Move | T: Repair | F: Door | E: Interact\nClick: Breach | B: Build | X: Remove | V: Link | []: Deck\nTab: Spectator | F1: Debug", hintStyle);
        }

        void DrawDamagePanel(SimulationBridge bridge, GUIStyle baseStyle)
        {
            var ds = bridge?.DamageSystem;
            if (ds == null) return;

            float y = 10f;

            float hull = ds.GetHullIntegrityFraction();
            if (hull < 0.99f)
            {
                Color hullColor = hull > 0.7f ? Color.green : hull > 0.4f ? Color.yellow : Color.red;
                var hullStyle = new GUIStyle(baseStyle) { normal = { textColor = hullColor } };
                GUI.Label(new Rect(10, y, 220, 20), $"Hull: {hull * 100f:F0}%", hullStyle);
                y += 18f;
            }

            int breaches = ds.BreachCount;
            if (breaches > 0)
            {
                int patched = ds.PatchedBreachCount;
                bool flash = Mathf.Repeat(Time.time, 1f) < 0.5f && patched < breaches;
                Color bColor = flash ? Color.red : new Color(1f, 0.5f, 0.3f);
                var bStyle = new GUIStyle(baseStyle) { normal = { textColor = bColor } };
                GUI.Label(new Rect(10, y, 260, 20),
                    $"BREACHES: {breaches} ({patched} patched)", bStyle);
                y += 18f;
            }

            if (ds.HullStress > 0.05f)
            {
                var stressLbl = new GUIStyle(baseStyle) { normal = { textColor = Color.red }, fontSize = 12 };
                GUI.Label(new Rect(10, y, 200, 20), "HULL STRESS", stressLbl);
                y += 16f;
                GUI.Box(new Rect(10, y, 154, 12), "");
                GUI.color = Color.Lerp(Color.yellow, Color.red, ds.HullStress);
                GUI.DrawTexture(new Rect(12, y + 2, 150 * ds.HullStress, 8), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        void DrawRepairProgress(float sw, float sh)
        {
            if (_repairTool == null)
                _repairTool = FindFirstObjectByType<RepairTool>();
            if (_repairTool == null || _repairTool.ActiveTarget == null) return;

            float barW = 120f, barH = 10f;
            float cx = sw * 0.5f, cy = sh * 0.5f;
            float barX = cx - barW * 0.5f;
            float barY = cy + 24f;

            Color barColor = _repairTool.StageLabel == "PATCHING"
                ? new Color(1f, 0.9f, 0.2f) : Color.cyan;

            GUI.Box(new Rect(barX - 1, barY - 1, barW + 2, barH + 2), "");
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(barX, barY, barW * _repairTool.DisplayProgress, barH),
                Texture2D.whiteTexture);
            GUI.color = Color.white;

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10, alignment = TextAnchor.UpperCenter,
                normal = { textColor = barColor }
            };
            GUI.Label(new Rect(barX, barY + barH + 2, barW, 16), _repairTool.StageLabel, labelStyle);
        }
    }
}
