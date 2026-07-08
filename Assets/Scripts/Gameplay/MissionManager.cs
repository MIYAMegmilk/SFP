using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class MissionManager : MonoBehaviour
    {
        MissionSystem _missions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<MissionManager>() != null) return;
            if (SimulationBridge.Instance == null && FindFirstObjectByType<SimulationBridge>() == null) return;

            var go = new GameObject("MissionManager");
            go.AddComponent<MissionManager>();
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            if (_missions == null)
            {
                if (bridge.Map == null) return;
                _missions = new MissionSystem(bridge.MapSeed, bridge.Map);
            }

            if (bridge.SubState != null)
                _missions.Tick(Time.deltaTime, bridge.SubState);
        }

        void OnGUI()
        {
            if (_missions == null) return;

            var bridge = SimulationBridge.Instance;
            var sub = bridge?.SubState;

            float sw = Screen.width;
            var current = _missions.Current;

            string text;
            Color color;
            if (current == null)
            {
                text = $"ALL OBJECTIVES COMPLETE ({_missions.TotalCount}/{_missions.TotalCount})";
                color = Color.green;
            }
            else
            {
                color = new Color(1f, 0.85f, 0.4f);
                float dist = sub != null ? _missions.DistanceToTarget(sub) : 0f;
                string hint = "";
                if (sub != null)
                {
                    float dx = current.TargetX - sub.PositionX;
                    float dz = current.TargetZ - sub.PositionZ;
                    float bearing = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                    if (bearing < 0f) bearing += 360f;
                    float delta = Mathf.DeltaAngle(sub.Heading, bearing);
                    hint = delta < -10f ? " <<" : delta > 10f ? " >>" : " ^";
                }

                text = $"OBJECTIVE: {current.Label} — {dist:F0}m{hint}";
                if (current.Kind == MissionKind.HoldPosition)
                {
                    float pct = current.HoldSeconds > 0f
                        ? Mathf.Clamp01(current.HoldProgress / current.HoldSeconds) * 100f : 0f;
                    text += $" ({pct:F0}%)";
                }
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = color }
            };
            GUI.Label(new Rect(0, 80, sw, 20), text, style);
        }
    }
}
