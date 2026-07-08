using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class BuiltStructureManager : MonoBehaviour
    {
        public Vector3 GridOrigin = new(0f, 0f, 0f);
        public float CellSize = 6f;
        public float CellHeight = 6f;
        public float SlabThickness = 0.3f;

        public GameObject WallPrefab;
        public GameObject FloorPrefab;

        public float DoorHeight = 3f;
        public float DoorArea = 6f;
        public float HatchArea = 0.8f;
        public float HatchHeight = 0.5f;

        public static BuiltStructureManager Instance { get; private set; }
        public BuildingGrid Grid { get; private set; }

        readonly Dictionary<GridKey, GameObject> _visuals = new();
        Transform _parent;
        Material _fallbackMat;
        Material _doorMat;
        Material _hatchMat;
        string _savePath;
        float _wallNativeHeight = 9f;
        int _visibleMaxDeck = int.MaxValue;

        public int VisibleMaxDeck => _visibleMaxDeck;
        public bool IsDeckFilterActive => _visibleMaxDeck != int.MaxValue;

        void Awake()
        {
            Instance = this;
            Grid = new BuildingGrid();
            _parent = new GameObject("BuiltStructures").transform;
            _savePath = Path.Combine(Application.persistentDataPath, "built_structures.json");

            _fallbackMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _fallbackMat.color = new Color(0.45f, 0.55f, 0.65f, 1f);

            _doorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _doorMat.color = new Color(0.2f, 0.7f, 0.3f, 1f);

            _hatchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _hatchMat.color = new Color(0.8f, 0.7f, 0.2f, 1f);

            if (WallPrefab != null)
            {
                var bc = WallPrefab.GetComponent<BoxCollider>();
                if (bc != null) _wallNativeHeight = bc.size.y;
            }
        }

        void Start()
        {
            // Parent BuiltStructures under ShipRoot so built pieces ride with the ship. Face
            // center/scale are ship-local (see GetFaceCenter); with _parent at identity under
            // ShipRoot, child localPosition/localRotation values equal those ship-local values directly.
            var bridge = SimulationBridge.Instance;
            if (bridge != null && bridge.ShipRoot != null)
                _parent.SetParent(bridge.ShipRoot, false);

            Load();
        }

        public bool CanPlace(GridKey key)
        {
            if (Grid.Has(key)) return false;
            if (StructureFaceUtil.IsOpening(key.Face))
            {
                var support = new GridKey(key.X, key.Y, key.Z,
                    StructureFaceUtil.SupportOf(key.Face));
                return Grid.Has(support) || HasPhysicalSupport(support);
            }
            return true;
        }

        bool HasPhysicalSupport(GridKey supportKey)
        {
            // GetFaceCenter is ship-local; Physics.CheckBox always queries in world space, so the
            // center and orientation must be converted to world/ship-rotation explicitly.
            var center = GetFaceCenter(supportKey);
            var halfExt = GetFaceScale(supportKey) * 0.45f;
            var bridge = SimulationBridge.Instance;
            if (bridge != null && bridge.ShipRoot != null)
                return Physics.CheckBox(bridge.ShipToWorld(center), halfExt, bridge.ShipRoot.rotation,
                    ~0, QueryTriggerInteraction.Ignore);
            return Physics.CheckBox(center, halfExt, Quaternion.identity,
                ~0, QueryTriggerInteraction.Ignore);
        }

        public bool TryPlace(GridKey key)
        {
            if (!CanPlace(key)) return false;
            if (!Grid.Place(key)) return false;

            if (StructureFaceUtil.IsOpening(key.Face))
                SpawnOpening(key);
            else
                SpawnVisual(key);

            Save();
            return true;
        }

        public bool TryRemove(GridKey key)
        {
            if (!Grid.Has(key)) return false;

            if (!StructureFaceUtil.IsOpening(key.Face))
            {
                var openingFace = StructureFaceUtil.OpeningOf(key.Face);
                var openingKey = new GridKey(key.X, key.Y, key.Z, openingFace);
                if (Grid.Has(openingKey))
                    RemoveSingle(openingKey);
            }

            RemoveSingle(key);
            Save();
            return true;
        }

        void RemoveSingle(GridKey key)
        {
            if (!Grid.Remove(key)) return;

            if (_visuals.TryGetValue(key, out var go))
            {
                if (StructureFaceUtil.IsOpening(key.Face))
                {
                    NeutralizeOpening(go);
                    SetSupportWallColliders(key, true);
                }

                Destroy(go);
                _visuals.Remove(key);
            }
        }

        void NeutralizeOpening(GameObject go)
        {
            var def = go.GetComponent<OpeningDefinition>();
            if (def == null) return;

            if (def.SimIndex >= 0 && SimulationBridge.Instance != null)
            {
                var opening = SimulationBridge.Instance.Graph.Openings[def.SimIndex];
                opening.IsOpen = false;
                opening.Area = 0f;
            }
            def.IsOpen = false;
        }

        // --- Deck visibility ---

        public int MaxBuiltDeck()
        {
            int max = 0;
            foreach (var key in Grid.Pieces)
                if (key.Y > max) max = key.Y;
            return max;
        }

        public void ShowAllDecks()
        {
            _visibleMaxDeck = int.MaxValue;
            ApplyDeckVisibility();
        }

        public void SetVisibleMaxDeck(int deck)
        {
            _visibleMaxDeck = Mathf.Max(0, deck);
            ApplyDeckVisibility();
        }

        void ApplyDeckVisibility()
        {
            foreach (var kv in _visuals)
                kv.Value.SetActive(kv.Key.Y <= _visibleMaxDeck);
        }

        // --- Face geometry for ghost/guide ---

        public Vector3 GetFaceCenter(GridKey key)
        {
            return key.Face switch
            {
                StructureFace.WallX => GridOrigin + new Vector3(
                    key.X * CellSize,
                    (key.Y + 0.5f) * CellHeight,
                    (key.Z + 0.5f) * CellSize),
                StructureFace.WallZ => GridOrigin + new Vector3(
                    (key.X + 0.5f) * CellSize,
                    (key.Y + 0.5f) * CellHeight,
                    key.Z * CellSize),
                StructureFace.Floor => GridOrigin + new Vector3(
                    (key.X + 0.5f) * CellSize,
                    key.Y * CellHeight,
                    (key.Z + 0.5f) * CellSize),
                StructureFace.DoorX => GridOrigin + new Vector3(
                    key.X * CellSize,
                    key.Y * CellHeight + DoorHeight * 0.5f,
                    (key.Z + 0.5f) * CellSize),
                StructureFace.DoorZ => GridOrigin + new Vector3(
                    (key.X + 0.5f) * CellSize,
                    key.Y * CellHeight + DoorHeight * 0.5f,
                    key.Z * CellSize),
                StructureFace.Hatch => GridOrigin + new Vector3(
                    (key.X + 0.5f) * CellSize,
                    key.Y * CellHeight,
                    (key.Z + 0.5f) * CellSize),
                _ => Vector3.zero
            };
        }

        public Vector3 GetFaceScale(GridKey key)
        {
            float t = SlabThickness;
            float doorW = DoorArea / DoorHeight;
            float hatchSide = Mathf.Sqrt(HatchArea);
            return key.Face switch
            {
                StructureFace.WallX => new Vector3(t, CellHeight, CellSize),
                StructureFace.WallZ => new Vector3(CellSize, CellHeight, t),
                StructureFace.Floor => new Vector3(CellSize, t, CellSize),
                StructureFace.DoorX => new Vector3(t, DoorHeight, doorW),
                StructureFace.DoorZ => new Vector3(doorW, DoorHeight, t),
                StructureFace.Hatch => new Vector3(hatchSide, t, hatchSide),
                _ => Vector3.one
            };
        }

        // --- Wall/Floor spawning ---

        void SpawnVisual(GridKey key)
        {
            bool isFloor = key.Face == StructureFace.Floor;
            var prefab = isFloor ? FloorPrefab : WallPrefab;

            if (prefab != null)
                SpawnPrefab(key, prefab, isFloor);
            else
                SpawnPrimitive(key);
        }

        void SpawnPrefab(GridKey key, GameObject prefab, bool isFloor)
        {
            var cornerPos = GridOrigin + new Vector3(
                key.X * CellSize, key.Y * CellHeight, key.Z * CellSize);

            Quaternion rotation;
            Vector3 scale;

            if (isFloor)
            {
                rotation = Quaternion.identity;
                scale = Vector3.one;
            }
            else
            {
                rotation = key.Face == StructureFace.WallX
                    ? Quaternion.identity
                    : Quaternion.Euler(0f, 90f, 0f);
                scale = new Vector3(1f, CellHeight / _wallNativeHeight, 1f);
            }

            var go = Instantiate(prefab, _parent);
            go.name = $"Built_{key.Face}_{key.X}_{key.Y}_{key.Z}";
            // cornerPos/rotation are ship-local; _parent sits at identity under ShipRoot, so setting
            // local values here places the piece correctly regardless of the ship's world transform.
            go.transform.localPosition = cornerPos;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;

            var bs = go.AddComponent<BuiltStructure>();
            bs.SetKey(key);
            _visuals[key] = go;

            go.SetActive(key.Y <= _visibleMaxDeck);
        }

        void SpawnPrimitive(GridKey key)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Built_{key.Face}_{key.X}_{key.Y}_{key.Z}";
            go.transform.SetParent(_parent, false);
            go.transform.localPosition = GetFaceCenter(key);
            go.transform.localScale = GetFaceScale(key);
            go.GetComponent<MeshRenderer>().sharedMaterial = _fallbackMat;

            var bs = go.AddComponent<BuiltStructure>();
            bs.SetKey(key);
            _visuals[key] = go;

            go.SetActive(key.Y <= _visibleMaxDeck);
        }

        // --- Door/Hatch spawning ---

        void SpawnOpening(GridKey key)
        {
            bool isDoor = key.Face == StructureFace.DoorX || key.Face == StructureFace.DoorZ;
            Vector3 center = GetFaceCenter(key);

            var go = new GameObject($"Built_{key.Face}_{key.X}_{key.Y}_{key.Z}");
            go.transform.SetParent(_parent, false);
            // center is ship-local; _parent sits at identity under ShipRoot, so localPosition here
            // equals the ship-local coordinate directly.
            go.transform.localPosition = center;

            if (key.Face == StructureFace.DoorZ)
                go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var def = go.AddComponent<OpeningDefinition>();
            def.Kind = isDoor ? OpeningKind.Door : OpeningKind.Hatch;
            def.Area = isDoor ? DoorArea : HatchArea;
            def.Height = isDoor ? DoorHeight : HatchHeight;
            def.IsOpen = false;

            ResolveCompartments(def, key);

            var visual = go.AddComponent<OpeningVisual>();
            visual.AnimSpeed = 5f;

            if (isDoor)
                BuildDoorPanels(go.transform, visual);
            else
                BuildHatchPanels(go.transform, visual);

            SetSupportWallColliders(key, false);

            var bs = go.AddComponent<BuiltStructure>();
            bs.SetKey(key);
            _visuals[key] = go;

            go.SetActive(key.Y <= _visibleMaxDeck);

            if (SimulationBridge.Instance != null)
                SimulationBridge.Instance.RegisterOpeningAtRuntime(def);
        }

        void SetSupportWallColliders(GridKey openingKey, bool enabled)
        {
            var supportKey = new GridKey(openingKey.X, openingKey.Y, openingKey.Z,
                StructureFaceUtil.SupportOf(openingKey.Face));
            if (!_visuals.TryGetValue(supportKey, out var wallGo)) return;
            foreach (var col in wallGo.GetComponentsInChildren<Collider>())
                col.enabled = enabled;
        }

        void ResolveCompartments(OpeningDefinition def, GridKey key)
        {
            bool isDoor = key.Face == StructureFace.DoorX || key.Face == StructureFace.DoorZ;
            Vector3 center = def.transform.position;
            Vector3 probeDir;

            if (isDoor)
                probeDir = key.Face == StructureFace.DoorX ? Vector3.right : Vector3.forward;
            else
                probeDir = Vector3.up;

            float probeOffset = 0.75f;
            var compDefs = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);

            def.CompartmentA = FindCompartmentAt(compDefs, center - probeDir * probeOffset);
            def.CompartmentB = FindCompartmentAt(compDefs, center + probeDir * probeOffset);
        }

        static CompartmentDefinition FindCompartmentAt(CompartmentDefinition[] defs, Vector3 point)
        {
            foreach (var cd in defs)
            {
                var t = cd.transform;
                Vector3 local = t.InverseTransformPoint(point);
                Vector3 halfExt = new Vector3(cd.LengthX, cd.Height, cd.WidthZ) * 0.5f;
                if (Mathf.Abs(local.x) <= halfExt.x &&
                    local.y >= -0.1f && local.y <= cd.Height + 0.1f &&
                    Mathf.Abs(local.z) <= halfExt.z)
                    return cd;
            }
            return null;
        }

        void BuildDoorPanels(Transform parent, OpeningVisual visual)
        {
            float doorW = DoorArea / DoorHeight;
            float halfW = doorW * 0.5f;

            var panelL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panelL.name = "DoorPanel_L";
            panelL.transform.SetParent(parent);
            panelL.transform.localPosition = new Vector3(0f, 0f, -halfW * 0.5f);
            panelL.transform.localScale = new Vector3(0.15f, DoorHeight, halfW);
            panelL.GetComponent<MeshRenderer>().sharedMaterial = _doorMat;

            var panelR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panelR.name = "DoorPanel_R";
            panelR.transform.SetParent(parent);
            panelR.transform.localPosition = new Vector3(0f, 0f, halfW * 0.5f);
            panelR.transform.localScale = new Vector3(0.15f, DoorHeight, halfW);
            panelR.GetComponent<MeshRenderer>().sharedMaterial = _doorMat;

            visual.PanelL = panelL.transform;
            visual.PanelR = panelR.transform;
            visual.ClosedOffsetL = new Vector3(0f, 0f, -halfW * 0.5f);
            visual.ClosedOffsetR = new Vector3(0f, 0f, halfW * 0.5f);
            visual.OpenOffsetL = new Vector3(0f, 0f, -halfW * 1.5f);
            visual.OpenOffsetR = new Vector3(0f, 0f, halfW * 1.5f);
        }

        void BuildHatchPanels(Transform parent, OpeningVisual visual)
        {
            float hatchSide = Mathf.Sqrt(HatchArea);
            float halfSide = hatchSide * 0.5f;

            var panelL = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panelL.name = "HatchPanel_L";
            panelL.transform.SetParent(parent);
            panelL.transform.localPosition = new Vector3(-halfSide * 0.5f, 0f, 0f);
            panelL.transform.localScale = new Vector3(halfSide, 0.15f, hatchSide);
            panelL.GetComponent<MeshRenderer>().sharedMaterial = _hatchMat;

            var panelR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panelR.name = "HatchPanel_R";
            panelR.transform.SetParent(parent);
            panelR.transform.localPosition = new Vector3(halfSide * 0.5f, 0f, 0f);
            panelR.transform.localScale = new Vector3(halfSide, 0.15f, hatchSide);
            panelR.GetComponent<MeshRenderer>().sharedMaterial = _hatchMat;

            visual.PanelL = panelL.transform;
            visual.PanelR = panelR.transform;
            visual.ClosedOffsetL = new Vector3(-halfSide * 0.5f, 0f, 0f);
            visual.ClosedOffsetR = new Vector3(halfSide * 0.5f, 0f, 0f);
            visual.OpenOffsetL = new Vector3(-halfSide * 1.5f, 0f, 0f);
            visual.OpenOffsetR = new Vector3(halfSide * 1.5f, 0f, 0f);
        }

        // --- Persistence ---

        [System.Serializable]
        class SaveData
        {
            public List<PieceEntry> pieces = new();
        }

        [System.Serializable]
        class PieceEntry
        {
            public int x, y, z, face;
        }

        void Save()
        {
            try
            {
                var data = new SaveData();
                foreach (var key in Grid.Pieces)
                    data.pieces.Add(new PieceEntry
                    {
                        x = key.X, y = key.Y, z = key.Z, face = (int)key.Face
                    });
                File.WriteAllText(_savePath, JsonUtility.ToJson(data, true));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to save structures: {e.Message}");
            }
        }

        void Load()
        {
            if (!File.Exists(_savePath)) return;
            try
            {
                var json = File.ReadAllText(_savePath);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data?.pieces == null) return;

                // Pass 1: supports (walls/floors)
                foreach (var p in data.pieces)
                {
                    if (p.face > 2) continue;
                    var key = new GridKey(p.x, p.y, p.z, (StructureFace)p.face);
                    if (Grid.Place(key))
                        SpawnVisual(key);
                }

                Physics.SyncTransforms();

                // Pass 2: openings (doors/hatches)
                foreach (var p in data.pieces)
                {
                    if (p.face <= 2) continue;
                    var key = new GridKey(p.x, p.y, p.z, (StructureFace)p.face);
                    if (CanPlace(key) && Grid.Place(key))
                        SpawnOpening(key);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to load structures: {e.Message}");
            }
        }
    }
}
