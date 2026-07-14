using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class AirlockInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;
        public float ReentryRange = 8f;

        float _messageUntil = -1f;
        string _message;
        Color _messageColor = Color.white;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            if (player.IsEVA)
                UpdateReentry(player, kb);
            else
                UpdateConsole(player, kb);
        }

        void UpdateConsole(PlayerController player, Keyboard kb)
        {
            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            var ray = cursorLocked
                ? new Ray(cam.transform.position, cam.transform.forward)
                : cam.ScreenPointToRay(Mouse.current?.position.ReadValue() ?? Vector2.zero);
            if (!Physics.Raycast(ray, out var hit, MaxDistance)) return;

            var airlockDef = hit.collider.GetComponentInParent<AirlockDefinition>();
            if (airlockDef == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var state = bridge.GetAirlock(airlockDef.AirlockIndex);
            if (state == null) return;

            switch (state.Phase)
            {
                case AirlockPhase.Dry:
                    {
                        var relay = DeviceRpcRelay.Instance;
                        if (relay != null)
                        {
                            relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.AirlockFlood, IntVal = airlockDef.AirlockIndex });
                            ShowMessage("FLOODING", Color.yellow);
                        }
                        else
                        {
                            if (state.RequestFlood(bridge.Graph))
                                ShowMessage("FLOODING", Color.yellow);
                            else
                                ShowMessage("CLOSE INNER DOOR FIRST", Color.red);
                        }
                    }
                    break;

                case AirlockPhase.Flooded:
                    if (!player.HasDivingSuit)
                    {
                        ShowMessage("DIVING SUIT REQUIRED", Color.red);
                        return;
                    }
                    ExitToSea(player, bridge);
                    break;

                case AirlockPhase.Flooding:
                    ShowMessage("FLOODING — PLEASE WAIT", Color.yellow);
                    break;

                case AirlockPhase.Draining:
                    ShowMessage("DRAINING — PLEASE WAIT", Color.yellow);
                    break;
            }
        }

        void ExitToSea(PlayerController player, SimulationBridge bridge)
        {
            if (bridge.Terrain == null || bridge.SubState == null) return;

            Vector3 hatchShipLocal = new Vector3(23f, 18.5f, 4.5f);
            Vector3 exteriorPos = bridge.ShipToWorld(hatchShipLocal) + Vector3.down * 3f;

            float floorY = -bridge.Terrain.GetFloorDepthAt(exteriorPos.x, exteriorPos.z);
            if (exteriorPos.y < floorY + 2f)
                exteriorPos.y = floorY + 2f;
            if (exteriorPos.y > -1f)
                exteriorPos.y = -1f;

            player.EnterEVA(exteriorPos);
        }

        void UpdateReentry(PlayerController player, Keyboard kb)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.SubState == null) return;

            Vector3 hatchShipLocal = new Vector3(23f, 18.5f, 4.5f);
            Vector3 hatchWorld = bridge.ShipToWorld(hatchShipLocal);
            float dist = Vector3.Distance(player.transform.position, hatchWorld);

            if (dist < ReentryRange && kb.eKey.wasPressedThisFrame)
            {
                var state = bridge.Airlocks.Count > 0 ? bridge.Airlocks[0] : null;
                if (state == null) return;

                switch (state.Phase)
                {
                    case AirlockPhase.Dry:
                        {
                            var relay = DeviceRpcRelay.Instance;
                            if (relay != null)
                            {
                                relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.AirlockFlood, IntVal = 0 });
                                ShowMessage("FLOODING FROM OUTSIDE", Color.yellow);
                            }
                            else
                            {
                                if (state.RequestFlood(bridge.Graph))
                                    ShowMessage("FLOODING FROM OUTSIDE", Color.yellow);
                                else
                                    ShowMessage("INNER DOOR OPEN — CANNOT FLOOD", Color.red);
                            }
                        }
                        break;

                    case AirlockPhase.Flooded:
                        var outerHatch = bridge.Graph.Openings[state.OuterHatchOpeningId];
                        if (outerHatch.IsOpen)
                        {
                            player.ExitEVA(new Vector3(21f, 18.6f, 3f));
                            var relay = DeviceRpcRelay.Instance;
                            if (relay != null)
                                relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.AirlockDrain, IntVal = 0 });
                            else
                                state.RequestDrain();
                            ShowMessage("RE-ENTERED — DRAINING", Color.yellow);
                        }
                        else
                        {
                            ShowMessage("OUTER HATCH CLOSED", Color.red);
                        }
                        break;

                    case AirlockPhase.Flooding:
                        ShowMessage("FLOODING — PLEASE WAIT", Color.yellow);
                        break;

                    case AirlockPhase.Draining:
                        ShowMessage("DRAINING — PLEASE WAIT", Color.yellow);
                        break;
                }
            }
        }

        void ShowMessage(string msg, Color color)
        {
            _message = msg;
            _messageColor = color;
            _messageUntil = Time.time + 2f;
        }

        void OnGUI()
        {
            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            float sw = Screen.width;
            float sh = Screen.height;

            if (Time.time < _messageUntil)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = _messageColor }
                };
                GUI.Label(new Rect(0, sh * 0.45f, sw, 30), _message, style);
            }

            var bridge = SimulationBridge.Instance;
            if (bridge == null || bridge.Airlocks.Count == 0) return;
            var state = bridge.Airlocks[0];

            var phaseStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = PhaseColor(state.Phase) }
            };

            string phaseText = state.Phase switch
            {
                AirlockPhase.Dry => "AIRLOCK: DRY",
                AirlockPhase.Flooding => "AIRLOCK: FLOODING",
                AirlockPhase.Flooded => "AIRLOCK: FLOODED",
                AirlockPhase.Draining => "AIRLOCK: DRAINING",
                _ => "AIRLOCK: ---"
            };

            var comp = bridge.Graph.GetCompartment(state.CompartmentId);
            float waterPct = comp.WaterFraction * 100f;
            string detail = $"{phaseText}  Water: {waterPct:F0}%  P: {comp.AirPressureAtm:F2} atm";
            GUI.Label(new Rect(10, sh - 55, 400, 20), detail, phaseStyle);

            if (player.IsEVA)
            {
                float depth = -player.transform.position.y;
                var infoStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = depth > player.SuitCrushDepth ? Color.red : Color.white }
                };

                float o2pct = player.OxygenFraction * 100f;
                Color o2col = o2pct > 25f ? Color.white : (o2pct > 10f ? Color.yellow : Color.red);
                string lampStr = player.HeadlampOn ? "ON" : "OFF";
                string tetherStr = player.TetherAttached
                    ? $"Tether: {player.TetherLength:F0}/{player.TetherMaxLength:F0}m"
                    : "Tether: SEVERED";
                Color tetherCol = player.TetherAttached
                    ? (player.TetherLength > player.TetherMaxLength * 0.8f ? Color.yellow : Color.white)
                    : Color.red;
                string topLine = $"EVA  Depth {depth:F0}m   O2 {o2pct:F0}%   Lamp: {lampStr} [F]";
                infoStyle.normal.textColor = depth > player.SuitCrushDepth ? Color.red : Color.white;
                GUI.Label(new Rect(0, sh - 145, sw, 20), topLine, infoStyle);
                infoStyle.normal.textColor = tetherCol;
                GUI.Label(new Rect(0, sh - 128, sw, 20), tetherStr, infoStyle);

                // O2 bar
                infoStyle.normal.textColor = o2col;
                float barW = 200f;
                float barX = (sw - barW) * 0.5f;
                GUI.Box(new Rect(barX, sh - 108, barW, 8), GUIContent.none);
                var barTex = Texture2D.whiteTexture;
                GUI.color = o2col;
                GUI.DrawTexture(new Rect(barX + 1, sh - 107, (barW - 2) * player.OxygenFraction, 6), barTex);
                GUI.color = Color.white;

                Vector3 hatchWorld = bridge.ShipToWorld(new Vector3(23f, 18.5f, 4.5f));
                float dist = Vector3.Distance(player.transform.position, hatchWorld);
                var hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
                };

                Vector3 toHatch = hatchWorld - player.transform.position;
                float bearing = Mathf.Atan2(toHatch.x, toHatch.z) * Mathf.Rad2Deg;
                if (bearing < 0f) bearing += 360f;
                string hatchHint;
                if (dist >= ReentryRange)
                {
                    hatchHint = $"Hatch: {dist:F0}m  bearing {bearing:F0}°";
                }
                else
                {
                    var airlockPhase = state.Phase;
                    hatchHint = airlockPhase switch
                    {
                        AirlockPhase.Dry => "[E] Flood airlock",
                        AirlockPhase.Flooding => "Flooding...",
                        AirlockPhase.Flooded => "[E] Re-enter airlock",
                        AirlockPhase.Draining => "Draining...",
                        _ => ""
                    };
                }
                string hatchInfo = hatchHint;
                GUI.Label(new Rect(0, sh - 90, sw, 20), hatchInfo, hintStyle);
            }
        }

        static Color PhaseColor(AirlockPhase phase)
        {
            return phase switch
            {
                AirlockPhase.Dry => Color.green,
                AirlockPhase.Flooding => Color.yellow,
                AirlockPhase.Flooded => new Color(0.3f, 0.7f, 1f),
                AirlockPhase.Draining => Color.yellow,
                _ => Color.white
            };
        }
    }
}
