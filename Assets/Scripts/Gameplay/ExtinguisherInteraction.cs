using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class ExtinguisherInteraction : MonoBehaviour
    {
        public float MaxCharge = 100f;
        public float ExtinguishRate = 0.8f;
        public float RechargeRange = 4f;

        float _charge;
        bool _isActive;

        void Start() => _charge = MaxCharge;

        public void Recharge() => _charge = MaxCharge;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.fKey.wasPressedThisFrame)
                _isActive = !_isActive;

            if (_charge < MaxCharge * 0.1f && kb.rKey.wasPressedThisFrame)
                TryRechargeFromFabricator();

            if (!_isActive || _charge <= 0f) return;

            var bridge = SimulationBridge.Instance;
            if (bridge?.FireSystem == null) return;

            int compId = FindCurrentCompartment(bridge);
            if (compId < 0) return;

            float intensity = bridge.FireSystem.GetFireIntensity(compId);
            if (intensity <= 0f) return;

            float suppress = ExtinguishRate * Time.deltaTime;
            var relay = DeviceRpcRelay.Instance;
            if (relay != null)
                relay.RequestCommand(new DeviceCommand { Kind = DeviceCommandKind.Extinguish, IntVal = compId, FloatVal = suppress });
            else
                bridge.FireSystem.Extinguish(compId, suppress);
            _charge = Mathf.Max(0f, _charge - suppress * 2f);
        }

        void TryRechargeFromFabricator()
        {
            var fabs = FindObjectsByType<FabricatorDefinition>(FindObjectsSortMode.None);
            foreach (var fab in fabs)
            {
                if (Vector3.Distance(transform.position, fab.transform.position) <= RechargeRange)
                {
                    Recharge();
                    return;
                }
            }
        }

        // shipLocalPoint conversion follows BreachTool.IsPointInsideCompartment: compartment
        // world position is converted to ship-local before bounds comparison (ShipRoot can yaw).
        int FindCurrentCompartment(SimulationBridge bridge)
        {
            Vector3 localPos = bridge.WorldToShip(transform.position);
            var comps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in comps)
            {
                Vector3 c = bridge.WorldToShip(comp.transform.position);
                float hx = comp.LengthX * 0.5f, hz = comp.WidthZ * 0.5f;
                if (localPos.x >= c.x - hx && localPos.x <= c.x + hx
                    && localPos.z >= c.z - hz && localPos.z <= c.z + hz
                    && localPos.y >= comp.FloorY && localPos.y <= comp.FloorY + comp.Height)
                    return bridge.GetCompartmentId(comp);
            }
            return -1;
        }

        void OnGUI()
        {
            float pct = MaxCharge > 0f ? _charge / MaxCharge : 0f;

            if (_isActive)
            {
                int bars = Mathf.RoundToInt(pct * 10f);
                string barStr = new string('#', bars) + new string('-', 10 - bars);

                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = _charge <= 0f ? Color.red : Color.white }
                };

                float w = 260f;
                float h = 26f;
                float x = Screen.width * 0.5f - w * 0.5f;
                float y = Screen.height - 80f;

                string label = _charge <= 0f
                    ? "EXTINGUISHER EMPTY"
                    : $"EXTINGUISHER [{barStr}] {pct * 100f:F0}%";
                GUI.Label(new Rect(x, y, w, h), label, style);
            }

            if (pct < 0.1f)
            {
                var hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.yellow }
                };
                float w = 320f;
                float x = Screen.width * 0.5f - w * 0.5f;
                float y = Screen.height - 52f;
                GUI.Label(new Rect(x, y, w, 20f), "Press R near fabricator to recharge", hintStyle);
            }
        }
    }
}
