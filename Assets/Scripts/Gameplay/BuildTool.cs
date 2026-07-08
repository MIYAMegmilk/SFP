using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Simulation;
using SFP.Presentation;

namespace SFP.Gameplay
{
    public class BuildTool : MonoBehaviour
    {
        public float MaxBuildDistance = 20f;

        bool _active;
        int _pieceMode; // 0=wall, 1=floor, 2=door, 3=hatch, 4=monitor
        bool _isSpectator;

        GameObject _ghost;
        MeshRenderer _ghostRenderer;
        Material _ghostValidMat;
        Material _ghostInvalidMat;
        GridKey _currentKey;
        bool _hasTarget;

        MonitorDefinition _linkingMonitor;
        static Material s_monitorBezelMat;

        const int GuidePoolSize = 64;
        const int GuideRadius = 2;
        readonly List<GameObject> _guidePool = new();
        Material _guideMat;

        BuiltStructureManager _manager;

        public bool IsActive => _active;

        static readonly string[] PieceNames = { "WALL", "FLOOR", "DOOR", "HATCH", "MONITOR" };

        void Start()
        {
            _manager = BuiltStructureManager.Instance;
            _isSpectator = TryGetComponent<FlyCamera>(out _);
            CreateGhost();
            CreateGhostMaterials();
            CreateGuidePool();
        }

        void OnDisable()
        {
            if (_active) ExitBuildMode();
        }

        void CreateGhost()
        {
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghost.name = "BuildGhost";
            Object.Destroy(_ghost.GetComponent<Collider>());
            _ghostRenderer = _ghost.GetComponent<MeshRenderer>();
            _ghost.SetActive(false);
        }

        void CreateGhostMaterials()
        {
            _ghostValidMat = CreateTransparentMat(new Color(0.2f, 0.9f, 0.3f, 0.35f));
            _ghostInvalidMat = CreateTransparentMat(new Color(0.9f, 0.2f, 0.2f, 0.35f));
            _guideMat = CreateTransparentMat(new Color(0.3f, 0.6f, 0.9f, 0.10f));
        }

        void CreateGuidePool()
        {
            for (int i = 0; i < GuidePoolSize; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "BuildGuide";
                Object.Destroy(go.GetComponent<Collider>());
                go.GetComponent<MeshRenderer>().sharedMaterial = _guideMat;
                go.SetActive(false);
                _guidePool.Add(go);
            }
        }

        static Material CreateTransparentMat(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.color = color;
            return mat;
        }

        void EnterBuildMode()
        {
            _active = true;
            SetBreachToolEnabled(false);
        }

        void ExitBuildMode()
        {
            _active = false;
            _ghost.SetActive(false);
            _hasTarget = false;
            HideGuides(0);
            SetBreachToolEnabled(true);
            if (_manager != null && _manager.IsDeckFilterActive)
                _manager.ShowAllDecks();
        }

        void SetBreachToolEnabled(bool enabled)
        {
            if (TryGetComponent<BreachTool>(out var bt))
                bt.enabled = enabled;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // V key: link monitors to devices (works outside build mode)
            if (!ConsoleFocus.IsLocked && kb.vKey.wasPressedThisFrame)
            {
                var cam0 = Camera.main;
                if (cam0 != null)
                {
                    bool vLocked = Cursor.lockState == CursorLockMode.Locked;
                    var vRay = vLocked
                        ? new Ray(cam0.transform.position, cam0.transform.forward)
                        : cam0.ScreenPointToRay(mouse.position.ReadValue());
                    if (Physics.Raycast(vRay, out var vHit, MaxBuildDistance))
                        HandleLinking(vHit.collider);
                    else if (_linkingMonitor != null)
                        _linkingMonitor = null;
                }
            }
            if (kb.escapeKey.wasPressedThisFrame && _linkingMonitor != null)
                _linkingMonitor = null;

            if (kb.bKey.wasPressedThisFrame)
            {
                if (_active) ExitBuildMode();
                else EnterBuildMode();
                return;
            }

            if (!_active || _manager == null) return;

            // Piece mode selection
            if (kb.digit1Key.wasPressedThisFrame) _pieceMode = 0;
            if (kb.digit2Key.wasPressedThisFrame) _pieceMode = 1;
            if (kb.digit3Key.wasPressedThisFrame) _pieceMode = 2;
            if (kb.digit4Key.wasPressedThisFrame) _pieceMode = 3;
            if (kb.digit5Key.wasPressedThisFrame) _pieceMode = 4;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0f) _pieceMode = (_pieceMode + 1) % 5;
            else if (scroll < 0f) _pieceMode = (_pieceMode + 4) % 5;

            // Deck visibility
            if (kb.leftBracketKey.wasPressedThisFrame)
            {
                int target = _manager.IsDeckFilterActive
                    ? _manager.VisibleMaxDeck - 1
                    : _manager.MaxBuiltDeck() - 1;
                if (target >= 0)
                    _manager.SetVisibleMaxDeck(target);
            }
            if (kb.rightBracketKey.wasPressedThisFrame)
            {
                if (_manager.IsDeckFilterActive)
                {
                    int next = _manager.VisibleMaxDeck + 1;
                    if (next > _manager.MaxBuiltDeck())
                        _manager.ShowAllDecks();
                    else
                        _manager.SetVisibleMaxDeck(next);
                }
            }

            var cam = Camera.main;
            if (cam == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            // Dual-mode aiming: center ray when cursor locked, mouse ray when free
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            var ray = cursorLocked
                ? new Ray(cam.transform.position, cam.transform.forward)
                : cam.ScreenPointToRay(mouse.position.ReadValue());

            Vector3 aimPointWorld;
            bool hasHit = Physics.Raycast(ray, out var hit, MaxBuildDistance);
            if (hasHit)
                aimPointWorld = hit.point + hit.normal * 0.05f;
            else
                aimPointWorld = ray.GetPoint(10f);

            // Grid cells are ship-local; convert the world-space aim point before deriving a GridKey.
            Vector3 aimPointLocal = bridge.WorldToShip(aimPointWorld);

            if (_pieceMode == 4)
            {
                UpdateMonitorGhost(bridge, hasHit, hit);
                if (mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed && hasHit)
                    PlaceMonitor(bridge, hit);
                if (kb.xKey.wasPressedThisFrame && hasHit)
                {
                    var md = hit.collider.GetComponentInParent<MonitorDefinition>();
                    if (md != null) Destroy(md.gameObject);
                }
                return;
            }

            _currentKey = CalculateGridKey(aimPointLocal);
            _hasTarget = true;
            UpdateGhost(bridge);
            UpdateGuides(bridge, aimPointLocal);

            // Place: LMB (skip if RMB held for camera look)
            if (mouse.leftButton.wasPressedThisFrame
                && !mouse.rightButton.isPressed
                && _hasTarget
                && _manager.CanPlace(_currentKey))
            {
                _manager.TryPlace(_currentKey);
            }

            // Remove: X key (both modes), or RMB in player mode only
            bool removePressed = kb.xKey.wasPressedThisFrame
                || (!_isSpectator && cursorLocked && mouse.rightButton.wasPressedThisFrame);

            if (removePressed && hasHit)
            {
                var bs = hit.collider.GetComponentInParent<BuiltStructure>();
                if (bs != null)
                    _manager.TryRemove(bs.GetKey());
            }
        }

        // point must already be ship-local (see bridge.WorldToShip).
        GridKey CalculateGridKey(Vector3 point)
        {
            float cs = _manager.CellSize;
            float ch = _manager.CellHeight;
            Vector3 origin = _manager.GridOrigin;

            float gx = (point.x - origin.x) / cs;
            float gy = (point.y - origin.y) / ch;
            float gz = (point.z - origin.z) / cs;

            // Floor / Hatch
            if (_pieceMode == 1 || _pieceMode == 3)
            {
                var face = _pieceMode == 1 ? StructureFace.Floor : StructureFace.Hatch;
                return new GridKey(
                    Mathf.FloorToInt(gx),
                    Mathf.RoundToInt(gy),
                    Mathf.FloorToInt(gz),
                    face);
            }

            // Wall / Door
            float fracX = gx - Mathf.Floor(gx);
            float fracZ = gz - Mathf.Floor(gz);
            float distToX = Mathf.Min(fracX, 1f - fracX);
            float distToZ = Mathf.Min(fracZ, 1f - fracZ);

            int iy = Mathf.FloorToInt(gy);

            var keyX = new GridKey(Mathf.RoundToInt(gx), iy, Mathf.FloorToInt(gz),
                _pieceMode == 0 ? StructureFace.WallX : StructureFace.DoorX);
            var keyZ = new GridKey(Mathf.FloorToInt(gx), iy, Mathf.RoundToInt(gz),
                _pieceMode == 0 ? StructureFace.WallZ : StructureFace.DoorZ);

            if (_pieceMode == 2)
            {
                bool canX = _manager.CanPlace(keyX);
                bool canZ = _manager.CanPlace(keyZ);
                if (canX && !canZ) return keyX;
                if (canZ && !canX) return keyZ;
            }

            return distToX <= distToZ ? keyX : keyZ;
        }

        void UpdateGhost(SimulationBridge bridge)
        {
            if (!_hasTarget || _manager == null)
            {
                _ghost.SetActive(false);
                return;
            }

            _ghost.SetActive(true);
            // GetFaceCenter/Scale are ship-local; the ghost is a free-standing world object, and the
            // grid is axis-aligned in ship-local space, so the ghost must also inherit the ship's
            // current heading (yaw-only) rotation.
            _ghost.transform.SetPositionAndRotation(
                bridge.ShipToWorld(_manager.GetFaceCenter(_currentKey)),
                bridge.ShipRoot.rotation);
            _ghost.transform.localScale = _manager.GetFaceScale(_currentKey);

            bool canPlace = _manager.CanPlace(_currentKey);
            _ghostRenderer.sharedMaterial = canPlace ? _ghostValidMat : _ghostInvalidMat;
        }

        // aimPoint must already be ship-local (see bridge.WorldToShip).
        void UpdateGuides(SimulationBridge bridge, Vector3 aimPoint)
        {
            if (!_hasTarget || _manager == null)
            {
                HideGuides(0);
                return;
            }

            float cs = _manager.CellSize;
            float ch = _manager.CellHeight;
            Vector3 origin = _manager.GridOrigin;

            int cx = Mathf.FloorToInt((aimPoint.x - origin.x) / cs);
            int cy = (_pieceMode == 1 || _pieceMode == 3)
                ? Mathf.RoundToInt((aimPoint.y - origin.y) / ch)
                : Mathf.FloorToInt((aimPoint.y - origin.y) / ch);
            int cz = Mathf.FloorToInt((aimPoint.z - origin.z) / cs);

            int idx = 0;

            if (_pieceMode == 0) // Wall
            {
                for (int ix = cx - GuideRadius; ix <= cx + GuideRadius + 1; ix++)
                    for (int iz = cz - GuideRadius; iz <= cz + GuideRadius; iz++)
                    {
                        if (idx >= GuidePoolSize) goto done;
                        var key = new GridKey(ix, cy, iz, StructureFace.WallX);
                        if (key.Equals(_currentKey) || !_manager.CanPlace(key)) continue;
                        PositionGuide(bridge, idx++, key);
                    }

                for (int ix = cx - GuideRadius; ix <= cx + GuideRadius; ix++)
                    for (int iz = cz - GuideRadius; iz <= cz + GuideRadius + 1; iz++)
                    {
                        if (idx >= GuidePoolSize) goto done;
                        var key = new GridKey(ix, cy, iz, StructureFace.WallZ);
                        if (key.Equals(_currentKey) || !_manager.CanPlace(key)) continue;
                        PositionGuide(bridge, idx++, key);
                    }
            }
            else if (_pieceMode == 1) // Floor
            {
                for (int ix = cx - GuideRadius; ix <= cx + GuideRadius; ix++)
                    for (int iz = cz - GuideRadius; iz <= cz + GuideRadius; iz++)
                    {
                        if (idx >= GuidePoolSize) goto done;
                        var key = new GridKey(ix, cy, iz, StructureFace.Floor);
                        if (key.Equals(_currentKey) || !_manager.CanPlace(key)) continue;
                        PositionGuide(bridge, idx++, key);
                    }
            }
            else if (_pieceMode == 2) // Door — show guides only where walls exist
            {
                for (int ix = cx - GuideRadius; ix <= cx + GuideRadius + 1; ix++)
                    for (int iz = cz - GuideRadius; iz <= cz + GuideRadius; iz++)
                    {
                        if (idx >= GuidePoolSize) goto done;
                        var key = new GridKey(ix, cy, iz, StructureFace.DoorX);
                        if (key.Equals(_currentKey) || !_manager.CanPlace(key)) continue;
                        PositionGuide(bridge, idx++, key);
                    }

                for (int ix = cx - GuideRadius; ix <= cx + GuideRadius; ix++)
                    for (int iz = cz - GuideRadius; iz <= cz + GuideRadius + 1; iz++)
                    {
                        if (idx >= GuidePoolSize) goto done;
                        var key = new GridKey(ix, cy, iz, StructureFace.DoorZ);
                        if (key.Equals(_currentKey) || !_manager.CanPlace(key)) continue;
                        PositionGuide(bridge, idx++, key);
                    }
            }
            else // Hatch — show guides only where floors exist
            {
                for (int ix = cx - GuideRadius; ix <= cx + GuideRadius; ix++)
                    for (int iz = cz - GuideRadius; iz <= cz + GuideRadius; iz++)
                    {
                        if (idx >= GuidePoolSize) goto done;
                        var key = new GridKey(ix, cy, iz, StructureFace.Hatch);
                        if (key.Equals(_currentKey) || !_manager.CanPlace(key)) continue;
                        PositionGuide(bridge, idx++, key);
                    }
            }

            done:
            HideGuides(idx);
        }

        void PositionGuide(SimulationBridge bridge, int idx, GridKey key)
        {
            var go = _guidePool[idx];
            go.SetActive(true);
            go.transform.SetPositionAndRotation(
                bridge.ShipToWorld(_manager.GetFaceCenter(key)),
                bridge.ShipRoot.rotation);
            go.transform.localScale = _manager.GetFaceScale(key) * 0.97f;
        }

        void HideGuides(int fromIdx)
        {
            for (int i = fromIdx; i < _guidePool.Count; i++)
            {
                if (_guidePool[i].activeSelf)
                    _guidePool[i].SetActive(false);
            }
        }

        void UpdateMonitorGhost(SimulationBridge bridge, bool hasHit, RaycastHit hit)
        {
            HideGuides(0);
            if (!hasHit)
            {
                _ghost.SetActive(false);
                return;
            }
            _ghost.SetActive(true);
            Vector3 localPos = bridge.WorldToShip(hit.point);
            Vector3 localNormal = bridge.ShipRoot.InverseTransformDirection(hit.normal);
            Vector3 fwd = -localNormal;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            _ghost.transform.SetPositionAndRotation(
                bridge.ShipToWorld(localPos + localNormal * 0.12f),
                bridge.ShipRoot.rotation * Quaternion.LookRotation(fwd, up));
            _ghost.transform.localScale = new Vector3(1.7f, 1.7f, 0.15f);
            _ghostRenderer.sharedMaterial = _ghostValidMat;
        }

        void PlaceMonitor(SimulationBridge bridge, RaycastHit hit)
        {
            Vector3 localHit = bridge.WorldToShip(hit.point);
            Vector3 localNormal = bridge.ShipRoot.InverseTransformDirection(hit.normal);
            Vector3 fwd = -localNormal;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;

            var go = new GameObject("PlacedMonitor");
            go.transform.SetParent(bridge.ShipRoot, false);
            go.transform.localPosition = localHit + localNormal * 0.12f;
            go.transform.localRotation = Quaternion.LookRotation(fwd, up);

            if (s_monitorBezelMat == null)
            {
                s_monitorBezelMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                s_monitorBezelMat.color = new Color(0.08f, 0.1f, 0.12f);
            }
            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bezel.name = "Console";
            bezel.transform.SetParent(go.transform, false);
            bezel.transform.localScale = new Vector3(1.7f, 1.7f, 0.15f);
            bezel.GetComponent<MeshRenderer>().sharedMaterial = s_monitorBezelMat;

            var md = go.AddComponent<MonitorDefinition>();
            md.ScreenSize = new Vector2(1.5f, 1.5f);
        }

        void HandleLinking(Collider col)
        {
            if (_linkingMonitor == null)
            {
                var md = col.GetComponentInParent<MonitorDefinition>();
                if (md != null) _linkingMonitor = md;
                return;
            }

            Component target = FindLinkableDevice(col);
            if (target != null)
            {
                _linkingMonitor.SourceDevice = target;
                _linkingMonitor = null;
                return;
            }

            var otherMd = col.GetComponentInParent<MonitorDefinition>();
            if (otherMd != null)
                _linkingMonitor = otherMd;
        }

        static Component FindLinkableDevice(Collider col)
        {
            Component c;
            if ((c = col.GetComponentInParent<SonarDefinition>()) != null) return c;
            if ((c = col.GetComponentInParent<ReactorDefinition>()) != null) return c;
            if ((c = col.GetComponentInParent<EngineDefinition>()) != null) return c;
            if ((c = col.GetComponentInParent<BatteryDefinition>()) != null) return c;
            if ((c = col.GetComponentInParent<JunctionBoxDefinition>()) != null) return c;
            if ((c = col.GetComponentInParent<NavigationTerminalDefinition>()) != null) return c;
            if ((c = col.GetComponentInParent<StatusMonitorDefinition>()) != null) return c;
            return null;
        }

        void OnGUI()
        {
            float sw = Screen.width;

            if (_linkingMonitor != null)
            {
                var linkStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                    normal = { textColor = Color.cyan }
                };
                GUI.Label(new Rect(0, 85, sw, 25),
                    "LINKING: Select target [V] | Cancel [Esc]", linkStyle);
            }

            if (!_active) return;

            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(0.3f, 1f, 0.4f) }
            };
            GUI.Label(new Rect(0, 10, sw, 30), "BUILD MODE", headerStyle);

            string pieceName = PieceNames[_pieceMode];
            var pieceStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(0, 35, sw, 25),
                $"[1] Wall  [2] Floor  [3] Door  [4] Hatch  [5] Monitor  |  Current: {pieceName}", pieceStyle);

            var controlStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.6f) }
            };

            string deckStatus = _manager != null && _manager.IsDeckFilterActive
                ? $"Deck: <={_manager.VisibleMaxDeck}"
                : "Deck: ALL";

            GUI.Label(new Rect(0, 58, sw, 20),
                $"LMB: Place  |  X: Remove  |  []: {deckStatus}  |  B: Exit", controlStyle);
        }
    }
}
