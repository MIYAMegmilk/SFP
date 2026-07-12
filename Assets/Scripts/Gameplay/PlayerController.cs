using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public CrewJobKind Job = CrewJobKind.Captain;

        public float WalkSpeed = 4f;
        public float SprintMultiplier = 1.8f;
        public float SwimSpeed = 2.5f;
        public float JumpForce = 5f;
        public float Gravity = 9.81f;
        public float MouseSensitivity = 2f;
        public float EyeHeight = 1.6f;

        public float MaxOxygen = 30f;
        public float OxygenRecoveryRate = 10f;
        public float DivingSuitOxygen = 120f;
        public float DivingSuitSpeedPenalty = 0.7f;

        public float FlowPushScale = 1.2f;
        public float MaxFlowPush = 6f;

        CharacterController _cc;
        Transform _cameraTransform;
        Transform _shipRoot;
        float _verticalVelocity;
        float _pitch;
        float _oxygen;
        bool _isSubmerged;
        int _currentCompartmentId = -1;
        Vector3 _evaCurrentPush;

        // Ship-local (authored) XZ center of each compartment, cached from the first time it is
        // resolved. Ship-local == authored coordinates, so this stays constant for the lifetime of
        // the compartment regardless of ShipRoot's world position/heading.
        readonly Dictionary<CompartmentDefinition, Vector3> _compShipLocalCenter = new();

        public float NarcosisPressure = 2f;
        public float ToxicPressure = 4f;
        public float LethalPressure = 6f;

        public bool HasDivingSuit;
        public bool IsClimbing;
        public float Oxygen => _oxygen;
        public float OxygenFraction => _oxygen / EffectiveMaxOxygen;
        public bool IsSubmerged => _isSubmerged;
        public float EffectiveMaxOxygen => HasDivingSuit ? DivingSuitOxygen : MaxOxygen;
        public float RoomPressureAtm { get; private set; } = 1f;
        public int CurrentCompartmentId => _currentCompartmentId;

        public float SuitCrushDepth = 300f;
        public float EVABuoyancy = 0.4f;
        public float EVADepthDragScale = 200f;
        public float TetherMaxLength = 30f;
        public bool IsEVA { get; private set; }
        public bool TetherAttached { get; private set; }
        public float TetherLength { get; private set; }
        Transform _evaReturnParent;
        Vector3 _tetherAnchorWorld;
        LineRenderer _tetherLine;

        void Start()
        {
            _cc = GetComponent<CharacterController>();
            _cameraTransform = GetComponentInChildren<Camera>().transform;
            _oxygen = MaxOxygen;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            // ShipRoot moves in LateUpdate; re-sync CC's internal position to the
            // transform that was moved by the parent hierarchy since last frame.
            if (!IsEVA)
            {
                _cc.enabled = false;
                _cc.enabled = true;
            }

            HandleLook(mouse);

            if (IsClimbing)
            {
                _currentCompartmentId = GetCurrentCompartmentId();
                UpdateOxygen();
                return;
            }

            if (IsEVA)
            {
                _currentCompartmentId = -1;
                _isSubmerged = true;

                var evaBridge = SimulationBridge.Instance;
                _evaCurrentPush = Vector3.zero;
                if (evaBridge?.OceanCurrents != null)
                {
                    evaBridge.OceanCurrents.Sample(transform.position.x, transform.position.z, -transform.position.y,
                        out float cx, out float cz);
                    _evaCurrentPush = new Vector3(cx, 0f, cz);
                }

                if (evaBridge?.Creatures != null)
                {
                    var creatures = evaBridge.Creatures;
                    creatures.HasEVATarget = true;
                    creatures.EVAX = transform.position.x;
                    creatures.EVAZ = transform.position.z;
                    creatures.EVADepth = -transform.position.y;
                    if (creatures.EVADamageAccumulated > 0f)
                    {
                        ApplyEVADamage(creatures.EVADamageAccumulated);
                        creatures.EVADamageAccumulated = 0f;
                    }
                }

                UpdateSwimming(kb);
                UpdateTether();

                if (transform.position.y > -0.5f)
                {
                    Vector3 pos = transform.position;
                    pos.y = -0.5f;
                    transform.position = pos;
                }
            }
            else
            {
                _evaCurrentPush = Vector3.zero;
                var clearBridge = SimulationBridge.Instance;
                if (clearBridge?.Creatures != null)
                    clearBridge.Creatures.HasEVATarget = false;
                _currentCompartmentId = GetCurrentCompartmentId();
                float waterY = GetWaterLevelAtPosition(_currentCompartmentId);
                // waterY is ship-local; convert the player's head position to ship-local before comparing.
                var bridge = SimulationBridge.Instance;
                Vector3 headWorldPos = transform.position + Vector3.up * EyeHeight;
                float headY = bridge != null ? bridge.WorldToShip(headWorldPos).y : headWorldPos.y;
                _isSubmerged = headY < waterY;

                if (_isSubmerged)
                    UpdateSwimming(kb);
                else
                    UpdateWalking(kb);
            }

            UpdateOxygen();
            UpdateFireDamage();
            UpdateHeadlamp(kb);
        }

        public void EnterEVA(Vector3 exteriorWorldPos)
        {
            _cc.enabled = false;
            _evaReturnParent = transform.parent;
            transform.SetParent(null, true);
            transform.position = exteriorWorldPos;
            _cc.enabled = true;
            IsEVA = true;
            _tetherAnchorWorld = exteriorWorldPos;
            TetherAttached = true;
            CreateHeadlamp();
            CreateTetherLine();
        }

        public void ExitEVA(Vector3 interiorShipLocal)
        {
            _cc.enabled = false;
            transform.SetParent(_evaReturnParent, true);
            var bridge = SimulationBridge.Instance;
            if (bridge != null)
                transform.position = bridge.ShipToWorld(interiorShipLocal);
            else
                transform.localPosition = interiorShipLocal;
            _cc.enabled = true;
            IsEVA = false;
            TetherAttached = false;
            DestroyHeadlamp();
            DestroyTetherLine();
        }

        public void ApplyEVADamage(float damage)
        {
            _oxygen = Mathf.Max(0f, _oxygen - damage);
            if (TetherAttached && damage >= 10f)
            {
                TetherAttached = false;
            }
        }

        void HandleLook(Mouse mouse)
        {
            Vector2 delta = mouse.delta.ReadValue() * MouseSensitivity * 0.1f;
            transform.Rotate(0f, delta.x, 0f);
            _pitch = Mathf.Clamp(_pitch - delta.y, -89f, 89f);
            _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void UpdateWalking(Keyboard kb)
        {
            Vector3 input = GetMoveInput(kb);
            bool sprint = kb.leftShiftKey.isPressed;
            float speed = sprint ? WalkSpeed * SprintMultiplier : WalkSpeed;
            if (HasDivingSuit) speed *= DivingSuitSpeedPenalty;

            Vector3 move = transform.TransformDirection(input) * speed;

            if (_cc.isGrounded)
            {
                _verticalVelocity = -0.5f;
                if (kb.spaceKey.wasPressedThisFrame && !ConsoleFocus.IsLocked)
                    _verticalVelocity = JumpForce;
            }
            else
            {
                _verticalVelocity -= Gravity * Time.deltaTime;
            }

            move.y = _verticalVelocity;
            move += SampleWaterFlow();
            _cc.Move(move * Time.deltaTime);
        }

        void UpdateSwimming(Keyboard kb)
        {
            Vector3 input = GetMoveInput(kb);

            float vertical = 0f;
            if (!ConsoleFocus.IsLocked)
            {
                if (kb.spaceKey.isPressed) vertical += 1f;
                if (kb.leftCtrlKey.isPressed) vertical -= 1f;
            }

            Vector3 swimDir = _cameraTransform.TransformDirection(input);
            swimDir.y += vertical;

            if (IsEVA && vertical == 0f && input.sqrMagnitude < 0.01f)
                swimDir.y += EVABuoyancy;

            if (swimDir.sqrMagnitude > 1f)
                swimDir.Normalize();

            float swimSpd = HasDivingSuit ? SwimSpeed * DivingSuitSpeedPenalty : SwimSpeed;
            if (IsEVA)
            {
                // Pressure drag: speed halves every EVADepthDragScale metres
                float depth = -transform.position.y;
                swimSpd /= (1f + depth / EVADepthDragScale);
            }
            _verticalVelocity = 0f;
            Vector3 flow = SampleWaterFlow();
            _cc.Move((swimDir * swimSpd + flow + _evaCurrentPush) * Time.deltaTime);
        }

        Vector3 SampleWaterFlow()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.WaterSystem == null) return Vector3.zero;

            int id = _currentCompartmentId;
            if (id < 0 || id >= bridge.WaterSystem.Grids.Count) return Vector3.zero;

            var grid = bridge.WaterSystem.GetGrid(id);
            if (grid == null) return Vector3.zero;

            // Grid coordinates and water level are ship-local; sample using the player's ship-local position.
            Vector3 shipLocalPos = bridge.WorldToShip(transform.position);
            grid.SampleFlow(shipLocalPos.x, shipLocalPos.z,
                out float vx, out float vz, out float h);
            if (h <= 0.05f) return Vector3.zero;

            float waterY = bridge.GetInterpolatedWaterLevelY(id);
            float immersion = Mathf.Clamp01((waterY - shipLocalPos.y) / 1.7f);
            if (immersion <= 0f) return Vector3.zero;

            Vector3 flowLocal = new Vector3(vx, 0f, vz) * FlowPushScale * immersion;
            if (flowLocal.magnitude > MaxFlowPush)
                flowLocal = flowLocal.normalized * MaxFlowPush;

            // The sampled flow is a ship-local direction; rotate it into world space before it is
            // applied to the (world-space) CharacterController movement.
            return GetShipRoot() != null ? GetShipRoot().rotation * flowLocal : flowLocal;
        }

        Transform GetShipRoot()
        {
            if (_shipRoot == null)
            {
                var bridge = SimulationBridge.Instance;
                if (bridge != null) _shipRoot = bridge.ShipRoot;
            }
            return _shipRoot;
        }


        void UpdateOxygen()
        {
            UpdateRoomPressure();

            if (_isSubmerged)
            {
                float drain = Time.deltaTime;
                if (IsEVA && -transform.position.y > SuitCrushDepth)
                    drain *= 5f;
                _oxygen = Mathf.Max(0f, _oxygen - drain);
            }
            else
            {
                float roomO2 = GetRoomOxygenLevel();
                float roomCo2 = GetRoomCo2Level();
                float recoveryMultiplier = roomO2 > 0.2f ? roomO2 : 0f;
                if (roomO2 < 0.15f)
                {
                    _oxygen = Mathf.Max(0f, _oxygen - (1f - roomO2 * 5f) * Time.deltaTime);
                }
                else
                {
                    float recovery = OxygenRecoveryRate * recoveryMultiplier;
                    if (RoomPressureAtm > NarcosisPressure)
                        recovery *= 0.5f;
                    // CO2 drowsiness halves recovery
                    if (roomCo2 > 0.05f)
                        recovery *= 0.5f;
                    _oxygen = Mathf.Min(EffectiveMaxOxygen, _oxygen + recovery * Time.deltaTime);
                }

                if (RoomPressureAtm > ToxicPressure)
                    _oxygen = Mathf.Max(0f, _oxygen - (RoomPressureAtm - ToxicPressure) * 0.5f * Time.deltaTime);
                if (RoomPressureAtm > LethalPressure)
                    _oxygen = Mathf.Max(0f, _oxygen - (2f + (RoomPressureAtm - LethalPressure)) * Time.deltaTime);

                // CO2 effects: >10% unconsciousness drain, >15% lethal drain
                if (roomCo2 > 0.10f)
                    _oxygen = Mathf.Max(0f, _oxygen - Time.deltaTime);
                if (roomCo2 > 0.15f)
                    _oxygen = Mathf.Max(0f, _oxygen - 4f * Time.deltaTime);
            }
        }

        void UpdateFireDamage()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            int id = _currentCompartmentId;
            if (id < 0) return;

            if (bridge.FireSystem != null)
            {
                float heatRate = bridge.FireSystem.GetHeatDamageRate(id);
                if (heatRate > 0f)
                    _oxygen = Mathf.Max(0f, _oxygen - heatRate * Time.deltaTime);
            }

            if (bridge.Temperature != null)
            {
                float tempK = bridge.Temperature.GetTemperatureK(id);
                if (tempK > 318f)
                    _oxygen = Mathf.Max(0f, _oxygen - (tempK - 318f) / 15f * Time.deltaTime);
            }
        }

        void UpdateRoomPressure()
        {
            if (IsEVA) { RoomPressureAtm = 1f; return; }

            var bridge = SimulationBridge.Instance;
            if (bridge?.Graph == null) { RoomPressureAtm = 1f; return; }

            int id = GetCurrentCompartmentId();
            if (id >= 0)
                RoomPressureAtm = bridge.Graph.GetCompartment(id).AirPressureAtm;
            else
                RoomPressureAtm = 1f;
        }

        float GetRoomOxygenLevel()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.Atmosphere == null) return 1f;

            int id = GetCurrentCompartmentId();
            if (id < 0) return 1f;

            bridge.Atmosphere.CrewCompartmentId = id;
            return bridge.Atmosphere.GetOxygenLevel(id);
        }

        float GetRoomCo2Level()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.Atmosphere == null) return 0f;

            int id = GetCurrentCompartmentId();
            if (id < 0) return 0f;

            return bridge.Atmosphere.GetCo2Level(id);
        }

        int GetCurrentCompartmentId()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return -1;

            Vector3 shipLocalPos = bridge.WorldToShip(transform.position);
            var comps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in comps)
            {
                if (!IsInsideCompartment(shipLocalPos, comp)) continue;
                int id = bridge.GetCompartmentId(comp);
                if (id >= 0) return id;
            }
            return -1;
        }

        Vector3 GetMoveInput(Keyboard kb)
        {
            Vector3 input = Vector3.zero;
            // A console UI owns the keyboard: stand still (gravity/water push still apply).
            if (ConsoleFocus.IsLocked) return input;
            if (kb.wKey.isPressed) input.z += 1f;
            if (kb.sKey.isPressed) input.z -= 1f;
            if (kb.aKey.isPressed) input.x -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (input.sqrMagnitude > 1f) input.Normalize();
            return input;
        }

        float GetWaterLevelAtPosition(int compartmentId)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null || compartmentId < 0) return -1000f;
            return bridge.GetInterpolatedWaterLevelY(compartmentId);
        }

        // pos must already be in ship-local space (see bridge.WorldToShip). CompartmentDefinition.FloorY
        // and Height are authored (ship-local absolute) heights, so they compare directly against pos.y.
        bool IsInsideCompartment(Vector3 pos, CompartmentDefinition comp)
        {
            Vector3 center = GetShipLocalCenter(comp);
            float halfX = comp.LengthX * 0.5f;
            float halfZ = comp.WidthZ * 0.5f;
            return pos.x >= center.x - halfX && pos.x <= center.x + halfX
                && pos.z >= center.z - halfZ && pos.z <= center.z + halfZ
                && pos.y >= comp.FloorY && pos.y <= comp.FloorY + comp.Height;
        }

        // comp.transform sits under ShipRoot and never moves relative to it, so its ship-local
        // center only needs to be resolved once and can be cached for the object's lifetime.
        Vector3 GetShipLocalCenter(CompartmentDefinition comp)
        {
            if (_compShipLocalCenter.TryGetValue(comp, out var center))
                return center;

            var shipRoot = GetShipRoot();
            if (shipRoot == null) return comp.transform.position; // ShipRoot not ready yet; don't cache

            center = shipRoot.InverseTransformPoint(comp.transform.position);
            _compShipLocalCenter[comp] = center;
            return center;
        }

        // --- Headlamp ---

        Light _headlamp;
        public bool HeadlampOn { get; private set; }

        void UpdateHeadlamp(Keyboard kb)
        {
            if (!IsEVA || _headlamp == null) return;
            if (kb.fKey.wasPressedThisFrame)
            {
                HeadlampOn = !HeadlampOn;
                _headlamp.enabled = HeadlampOn;
            }
        }

        void CreateHeadlamp()
        {
            if (_headlamp != null) return;
            var lampGo = new GameObject("Headlamp");
            lampGo.transform.SetParent(_cameraTransform, false);
            lampGo.transform.localPosition = Vector3.zero;
            lampGo.transform.localRotation = Quaternion.identity;
            _headlamp = lampGo.AddComponent<Light>();
            _headlamp.type = LightType.Spot;
            _headlamp.spotAngle = 60f;
            _headlamp.innerSpotAngle = 30f;
            _headlamp.range = 40f;
            _headlamp.intensity = 800f;
            _headlamp.color = new Color(0.85f, 0.95f, 1f);
            _headlamp.shadows = LightShadows.Soft;
            _headlamp.enabled = true;
            HeadlampOn = true;
            SFP.Presentation.BackscatterLightManager.RegisterLight(_headlamp);
        }

        void DestroyHeadlamp()
        {
            if (_headlamp != null)
            {
                SFP.Presentation.BackscatterLightManager.UnregisterLight(_headlamp);
                Destroy(_headlamp.gameObject);
                _headlamp = null;
            }
            HeadlampOn = false;
        }

        // --- Tether ---

        void UpdateTether()
        {
            if (!TetherAttached)
            {
                TetherLength = 0f;
                UpdateTetherVisual();
                return;
            }

            // Anchor tracks the hatch world position (sub may have moved)
            var bridge = SimulationBridge.Instance;
            if (bridge != null)
                _tetherAnchorWorld = bridge.ShipToWorld(new Vector3(21f, 0.5f, 4.5f));

            TetherLength = Vector3.Distance(transform.position, _tetherAnchorWorld);

            if (TetherLength > TetherMaxLength)
            {
                // Pull player back toward anchor
                Vector3 dir = (_tetherAnchorWorld - transform.position).normalized;
                float excess = TetherLength - TetherMaxLength;
                _cc.Move(dir * excess * 0.5f);
            }

            UpdateTetherVisual();
        }

        void CreateTetherLine()
        {
            if (_tetherLine != null) return;
            var go = new GameObject("TetherLine");
            go.transform.SetParent(null);
            _tetherLine = go.AddComponent<LineRenderer>();
            _tetherLine.positionCount = 2;
            _tetherLine.startWidth = 0.03f;
            _tetherLine.endWidth = 0.03f;
            _tetherLine.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _tetherLine.material.color = new Color(0.9f, 0.8f, 0.2f);
            _tetherLine.useWorldSpace = true;
        }

        void DestroyTetherLine()
        {
            if (_tetherLine != null)
            {
                Destroy(_tetherLine.gameObject);
                _tetherLine = null;
            }
        }

        void UpdateTetherVisual()
        {
            if (_tetherLine == null) return;
            if (!TetherAttached)
            {
                _tetherLine.enabled = false;
                return;
            }
            _tetherLine.enabled = true;
            _tetherLine.SetPosition(0, _tetherAnchorWorld);
            _tetherLine.SetPosition(1, transform.position + Vector3.up * 0.5f);
        }
    }
}
