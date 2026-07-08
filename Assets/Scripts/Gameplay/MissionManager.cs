using UnityEngine;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class MissionManager : MonoBehaviour
    {
        MissionSystem _missions;
        float _roundCompleteTimer;
        int _lastRound;

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
                _lastRound = _missions.Round;
            }

            if (bridge.SubState != null)
                _missions.Tick(Time.deltaTime, bridge.SubState);

            if (_missions.Round != _lastRound)
            {
                _roundCompleteTimer = 3f;
                _lastRound = _missions.Round;
            }

            if (_roundCompleteTimer > 0f)
                _roundCompleteTimer -= Time.deltaTime;
        }

        public MissionSystem Missions => _missions;

        void OnGUI()
        {
            if (_missions == null) return;

            var bridge = SimulationBridge.Instance;
            var sub = bridge?.SubState;
            float sw = Screen.width;

            // Round complete banner
            if (_roundCompleteTimer > 0f)
            {
                float alpha = Mathf.Clamp01(_roundCompleteTimer);
                var bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.3f, 1f, 0.3f, alpha) }
                };
                GUI.Label(new Rect(0, Screen.height * 0.35f, sw, 30),
                    $"ROUND {_missions.Round - 1} COMPLETE — NEW ORDERS RECEIVED", bannerStyle);
            }

            var current = _missions.Current;

            // Phase / round header
            string phaseLabel = _missions.Phase == MissionPhase.Returning ? "RTB" : "PATROL";
            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };
            GUI.Label(new Rect(0, 68, sw, 16),
                $"ROUND {_missions.Round}  |  {phaseLabel}  |  {_missions.CompletedCount}/{_missions.TotalCount}", headerStyle);

            // Current objective
            string text;
            Color color;
            if (current == null)
            {
                text = "STANDING BY...";
                color = Color.green;
            }
            else
            {
                color = _missions.Phase == MissionPhase.Returning
                    ? new Color(0.4f, 0.8f, 1f)
                    : new Color(1f, 0.85f, 0.4f);

                float dist = sub != null ? _missions.DistanceToTarget(sub) : 0f;
                string hint = "";
                if (sub != null)
                {
                    float bearing = _missions.BearingToTarget(sub);
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
            GUI.Label(new Rect(0, 82, sw, 20), text, style);
        }
    }
}
