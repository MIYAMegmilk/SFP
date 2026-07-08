using System.Collections.Generic;
using UnityEngine;

namespace SFP.Presentation
{
    public class OpeningVisual : MonoBehaviour
    {
        public Transform PanelL;
        public Transform PanelR;
        public Vector3 ClosedOffsetL;
        public Vector3 ClosedOffsetR;
        public Vector3 OpenOffsetL;
        public Vector3 OpenOffsetR;
        public float AnimSpeed = 5f;
        public bool SkipWallCutting;

        OpeningDefinition _def;
        bool _lastOpen;

        struct WallRef
        {
            public GameObject Original;
            public Vector3 Pos;
            public Quaternion Rot;
            public Vector3 Scale;
            public Vector3 LocalHoleCenter;
            public int Thin, AxA, AxB;
            public Material Mat;
        }

        readonly List<WallRef> _wallRefs = new();
        readonly List<GameObject> _segments = new();
        float _holeA, _holeB;

        static Material _edgeMat;
        static Material _holeMat;

        readonly List<Material> _doorInstMats = new();

        struct BoundsRef
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public Collider Collider;
            public Bounds Bounds;
            public Material Material;
            public Material[] OriginalMats;
        }

        BoundsRef _floorRef;
        bool _hasFloorRef;
        float _hatchHalfExtent;

        static Shader _cutoutShader;
        readonly List<Material> _cutoutMats = new();

        BoundsRef _doorWallRef;
        bool _hasDoorWallRef;
        readonly List<GameObject> _doorWallSegments = new();

        void Awake()
        {
            _def = GetComponent<OpeningDefinition>();
            if (_def != null)
            {
                if (_def.Kind == SFP.Simulation.OpeningKind.Hatch)
                    FindFloorForHatch();
                else if (_def.Kind == SFP.Simulation.OpeningKind.Door)
                    FindWallForDoor();
            }
        }

        void Start()
        {
            if (_def == null)
                _def = GetComponent<OpeningDefinition>();
            _lastOpen = _def.IsOpen;
            PanelL.localPosition = _def.IsOpen ? OpenOffsetL : ClosedOffsetL;
            PanelR.localPosition = _def.IsOpen ? OpenOffsetR : ClosedOffsetR;

            EnsureInteractionCollider();

            if (!SkipWallCutting && _def.Kind != SFP.Simulation.OpeningKind.Hatch)
                FindWalls();

            if (_hasDoorWallRef)
                CutWallBounds();

            if (_def.IsOpen)
                CutWalls();
        }

        // The hull rides under ShipRoot, which ShipRootDriver moves/rotates every frame. All cut
        // geometry is therefore computed in ship-local (authored) coordinates and only converted
        // to world pose at spawn time — a world-space snapshot would be left behind in the ocean
        // the moment the ship moves.
        static Quaternion ShipRotation
        {
            get
            {
                var b = SimulationBridge.Instance;
                return b != null && b.ShipRoot != null ? b.ShipRoot.rotation : Quaternion.identity;
            }
        }

        static Vector3 ToShip(Vector3 world)
        {
            var b = SimulationBridge.Instance;
            return b != null && b.ShipRoot != null ? b.WorldToShip(world) : world;
        }

        static Vector3 ToWorld(Vector3 ship)
        {
            var b = SimulationBridge.Instance;
            return b != null && b.ShipRoot != null ? b.ShipToWorld(ship) : ship;
        }

        // Walls/floors are axis-aligned boxes in ship space but may be yawed relative to the
        // world, so a world AABB is inflated once the ship turns. Rebuild the exact box along
        // ship axes instead.
        static Bounds ShipLocalBounds(Collider col)
        {
            Vector3 centerWorld, size;
            Quaternion frameRot;
            if (col is BoxCollider bc)
            {
                centerWorld = col.transform.TransformPoint(bc.center);
                var s = col.transform.lossyScale;
                size = Vector3.Scale(bc.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                frameRot = col.transform.rotation;
            }
            else
            {
                centerWorld = col.bounds.center;
                size = col.bounds.size;
                frameRot = Quaternion.identity;
            }
            var rel = Quaternion.Inverse(ShipRotation) * frameRot;
            Vector3 rs = rel * size;
            size = new Vector3(Mathf.Abs(rs.x), Mathf.Abs(rs.y), Mathf.Abs(rs.z));
            return new Bounds(ToShip(centerWorld), size);
        }

        void EnsureInteractionCollider()
        {
            if (GetComponent<Collider>() != null) return;
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            if (_def.Kind == SFP.Simulation.OpeningKind.Door)
            {
                float doorW = _def.Area / _def.Height;
                bc.size = new Vector3(0.4f, _def.Height, doorW);
            }
            else
            {
                float side = Mathf.Sqrt(_def.Area);
                bc.size = new Vector3(side, 0.4f, side);
            }
        }

        void FindFloorForHatch()
        {
            float side = Mathf.Sqrt(_def.Area);
            _hatchHalfExtent = side * 0.5f;

            var overlaps = Physics.OverlapBox(
                transform.position,
                new Vector3(0.3f, 0.4f, 0.3f),
                ShipRotation, ~0, QueryTriggerInteraction.Ignore);

            foreach (var col in overlaps)
            {
                var go = col.gameObject;
                if (go.transform.IsChildOf(transform)) continue;
                if (go.name.Contains("Panel") || go.name.Contains("Hatch")) continue;

                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var bounds = ShipLocalBounds(col);
                if (bounds.size.y > 0.5f) continue;

                _floorRef = new BoundsRef
                {
                    Go = go,
                    Renderer = mr,
                    Collider = col,
                    Bounds = bounds,
                    Material = mr.sharedMaterial,
                    OriginalMats = mr.sharedMaterials
                };
                _hasFloorRef = true;
                break;
            }
        }

        void FindWallForDoor()
        {
            var overlaps = Physics.OverlapBox(
                transform.position,
                new Vector3(0.5f, _def.Height * 0.5f + 0.1f, 0.5f),
                ShipRotation, ~0, QueryTriggerInteraction.Ignore);

            foreach (var col in overlaps)
            {
                var go = col.gameObject;
                if (go.transform.IsChildOf(transform)) continue;
                if (go.name.Contains("Panel") || go.name.Contains("Seg") || go.name.Contains("Edge")) continue;

                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var bounds = ShipLocalBounds(col);
                if (bounds.size.y < 1f) continue;
                float thinAxis = Mathf.Min(bounds.size.x, bounds.size.z);
                if (thinAxis > 0.6f) continue;

                _doorWallRef = new BoundsRef
                {
                    Go = go,
                    Renderer = mr,
                    Collider = col,
                    Bounds = bounds,
                    Material = mr.sharedMaterial
                };
                _hasDoorWallRef = true;
                break;
            }
        }

        void FindWalls()
        {
            EnsureEdgeMat();

            bool isDoor = _def.Kind == SFP.Simulation.OpeningKind.Door;
            float holeWorldA, holeWorldB;

            if (isDoor)
            {
                float doorW = _def.Area / _def.Height;
                holeWorldA = _def.Height;
                holeWorldB = doorW;
            }
            else
            {
                float side = Mathf.Sqrt(_def.Area);
                holeWorldA = side;
                holeWorldB = side;
            }

            var overlaps = Physics.OverlapBox(transform.position, Vector3.one * 0.3f,
                ShipRotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in overlaps)
            {
                var go = col.gameObject;
                if (go.transform.parent == transform) continue;
                if (go.name.Contains("Panel") || go.name.Contains("Frame") ||
                    go.name.Contains("Hole") || go.name.Contains("Edge") ||
                    go.name.Contains("Seg")) continue;
                if (go.GetComponentInParent<BuiltStructure>() != null) continue;

                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var wt = go.transform;
                var scale = wt.lossyScale;

                int thin = 0;
                if (scale.y < scale[thin]) thin = 1;
                if (scale.z < scale[thin]) thin = 2;
                int axA = (thin + 1) % 3;
                int axB = (thin + 2) % 3;

                var localCenter = wt.InverseTransformPoint(transform.position);

                _wallRefs.Add(new WallRef
                {
                    Original = go,
                    Pos = ToShip(wt.position),
                    Rot = Quaternion.Inverse(ShipRotation) * wt.rotation,
                    Scale = scale,
                    LocalHoleCenter = localCenter,
                    Thin = thin,
                    AxA = axA,
                    AxB = axB,
                    Mat = mr.sharedMaterial
                });

                _holeA = (holeWorldA * 0.5f) / scale[axA];
                _holeB = (holeWorldB * 0.5f) / scale[axB];
            }
        }

        void Update()
        {
            if (_def == null) return;
            bool open = _def.IsOpen;
            float t = AnimSpeed * Time.deltaTime;
            PanelL.localPosition = Vector3.Lerp(PanelL.localPosition, open ? OpenOffsetL : ClosedOffsetL, t);
            PanelR.localPosition = Vector3.Lerp(PanelR.localPosition, open ? OpenOffsetR : ClosedOffsetR, t);

            if (open != _lastOpen)
            {
                _lastOpen = open;
                if (open)
                    CutWalls();
                else
                    RestoreWalls();
            }
        }

        void CutWalls()
        {
            foreach (var w in _wallRefs)
            {
                if (w.Original == null) continue;
                w.Original.GetComponent<MeshRenderer>().enabled = false;
                var col = w.Original.GetComponent<Collider>();
                if (col) col.enabled = false;

                float hitA = w.LocalHoleCenter[w.AxA];
                float hitB = w.LocalHoleCenter[w.AxB];

                float minA = Mathf.Max(hitA - _holeA, -0.5f);
                float maxA = Mathf.Min(hitA + _holeA,  0.5f);
                float minB = Mathf.Max(hitB - _holeB, -0.5f);
                float maxB = Mathf.Min(hitB + _holeB,  0.5f);

                Seg(w, -0.5f, 0.5f, maxB, 0.5f);
                Seg(w, -0.5f, 0.5f, -0.5f, minB);
                Seg(w, -0.5f, minA, minB, maxB);
                Seg(w, maxA,  0.5f, minB, maxB);

                float e = 0.04f / Mathf.Max(w.Scale[w.AxA], w.Scale[w.AxB]);
                Edge(w, minA - e, maxA + e, maxB - e, maxB + e);
                Edge(w, minA - e, maxA + e, minB - e, minB + e);
                Edge(w, minA - e, minA + e, minB, maxB);
                Edge(w, maxA - e, maxA + e, minB, maxB);
            }

            if (_hasFloorRef && _floorRef.Go != null)
                CutFloor();
        }

        void CutFloor()
        {
            EnsureEdgeMat();
            // _floorRef.Bounds is ship-local (captured via ShipLocalBounds); stay in that frame.
            var b = _floorRef.Bounds;
            if (_floorRef.Collider) _floorRef.Collider.enabled = false;
            var hatchShip = ToShip(transform.position);
            float hx = hatchShip.x;
            float hz = hatchShip.z;
            float he = _hatchHalfExtent;

            float hMinX = Mathf.Max(hx - he, b.min.x);
            float hMaxX = Mathf.Min(hx + he, b.max.x);
            float hMinZ = Mathf.Max(hz - he, b.min.z);
            float hMaxZ = Mathf.Min(hz + he, b.max.z);

            ColliderSeg(b.min.x, b.max.x, b.min.y, b.max.y, hMaxZ, b.max.z);
            ColliderSeg(b.min.x, b.max.x, b.min.y, b.max.y, b.min.z, hMinZ);
            ColliderSeg(b.min.x, hMinX, b.min.y, b.max.y, hMinZ, hMaxZ);
            ColliderSeg(hMaxX, b.max.x, b.min.y, b.max.y, hMinZ, hMaxZ);

            if (_cutoutShader == null) _cutoutShader = Shader.Find("SFP/LitCutout");
            if (_cutoutShader != null)
            {
                var mats = new Material[_floorRef.OriginalMats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = _floorRef.OriginalMats[i];
                    var m = src != null ? new Material(src) : new Material(_cutoutShader);
                    m.shader = _cutoutShader;
                    _cutoutMats.Add(m);
                    mats[i] = m;
                }
                _floorRef.Renderer.sharedMaterials = mats;
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_HoleMin", new Vector4(hMinX, 0f, hMinZ, 0f));
                mpb.SetVector("_HoleMax", new Vector4(hMaxX, 0f, hMaxZ, 0f));
                _floorRef.Renderer.SetPropertyBlock(mpb);
            }

            float wy0 = b.min.y - 0.05f;
            float wy1 = b.max.y + 0.005f;
            const float wt = 0.03f;
            ShaftWall(hMinX - wt, hMaxX + wt, wy0, wy1, hMaxZ, hMaxZ + wt);
            ShaftWall(hMinX - wt, hMaxX + wt, wy0, wy1, hMinZ - wt, hMinZ);
            ShaftWall(hMaxX, hMaxX + wt, wy0, wy1, hMinZ, hMaxZ);
            ShaftWall(hMinX - wt, hMinX, wy0, wy1, hMinZ, hMaxZ);

            float et = 0.04f;
            BoundsEdge(_segments, hMinX - et, hMaxX + et, b.max.y - 0.01f, b.max.y + 0.03f, hMaxZ - et, hMaxZ + et);
            BoundsEdge(_segments, hMinX - et, hMaxX + et, b.max.y - 0.01f, b.max.y + 0.03f, hMinZ - et, hMinZ + et);
            BoundsEdge(_segments, hMinX - et, hMinX + et, b.max.y - 0.01f, b.max.y + 0.03f, hMinZ, hMaxZ);
            BoundsEdge(_segments, hMaxX - et, hMaxX + et, b.max.y - 0.01f, b.max.y + 0.03f, hMinZ, hMaxZ);
        }

        void ColliderSeg(float x0, float x1, float y0, float y1, float z0, float z1)
        {
            float sx = x1 - x0, sz = z1 - z0;
            if (sx < 0.001f || sz < 0.001f) return;
            var go = new GameObject("HatchFloorCol");
            go.transform.position = ToWorld(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
            go.transform.rotation = ShipRotation;
            go.transform.localScale = new Vector3(sx, y1 - y0, sz);
            go.AddComponent<BoxCollider>();
            go.transform.SetParent(transform, true);
            _segments.Add(go);
        }

        void CutWallBounds()
        {
            EnsureEdgeMat();
            // _doorWallRef.Bounds is ship-local (captured via ShipLocalBounds); stay in that frame.
            var b = _doorWallRef.Bounds;
            _doorWallRef.Renderer.enabled = false;
            if (_doorWallRef.Collider) _doorWallRef.Collider.enabled = false;
            float doorW = _def.Area / _def.Height;
            float halfW = doorW * 0.5f;
            float halfH = _def.Height * 0.5f;
            var doorShip = ToShip(transform.position);
            float cy = doorShip.y;

            bool thinX = b.size.x < b.size.z;

            float vMin = Mathf.Max(cy - halfH, b.min.y);
            float vMax = Mathf.Min(cy + halfH, b.max.y);

            if (thinX)
            {
                float cz = doorShip.z;
                float hMin = Mathf.Max(cz - halfW, b.min.z);
                float hMax = Mathf.Min(cz + halfW, b.max.z);

                BoundsSeg(_doorWallSegments, b.min.x, b.max.x, vMax, b.max.y, b.min.z, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, b.min.z, b.max.z, vMax, b.max.y, 2, 1));
                BoundsSeg(_doorWallSegments, b.min.x, b.max.x, b.min.y, vMin, b.min.z, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, b.min.z, b.max.z, b.min.y, vMin, 2, 1));
                BoundsSeg(_doorWallSegments, b.min.x, b.max.x, vMin, vMax, b.min.z, hMin,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, b.min.z, hMin, vMin, vMax, 2, 1));
                BoundsSeg(_doorWallSegments, b.min.x, b.max.x, vMin, vMax, hMax, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, hMax, b.max.z, vMin, vMax, 2, 1));

                float et = 0.04f;
                BoundsEdge(_doorWallSegments, b.min.x, b.max.x, vMax - et, vMax + et, hMin - et, hMax + et);
                BoundsEdge(_doorWallSegments, b.min.x, b.max.x, vMin - et, vMin + et, hMin - et, hMax + et);
                BoundsEdge(_doorWallSegments, b.min.x, b.max.x, vMin, vMax, hMin - et, hMin + et);
                BoundsEdge(_doorWallSegments, b.min.x, b.max.x, vMin, vMax, hMax - et, hMax + et);
            }
            else
            {
                float cx = doorShip.x;
                float hMin = Mathf.Max(cx - halfW, b.min.x);
                float hMax = Mathf.Min(cx + halfW, b.max.x);

                BoundsSeg(_doorWallSegments, b.min.x, b.max.x, vMax, b.max.y, b.min.z, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, b.min.x, b.max.x, vMax, b.max.y, 0, 1));
                BoundsSeg(_doorWallSegments, b.min.x, b.max.x, b.min.y, vMin, b.min.z, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, b.min.x, b.max.x, b.min.y, vMin, 0, 1));
                BoundsSeg(_doorWallSegments, b.min.x, hMin, vMin, vMax, b.min.z, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, b.min.x, hMin, vMin, vMax, 0, 1));
                BoundsSeg(_doorWallSegments, hMax, b.max.x, vMin, vMax, b.min.z, b.max.z,
                    TiledMat(_doorInstMats, _doorWallRef.Material, b, hMax, b.max.x, vMin, vMax, 0, 1));

                float et = 0.04f;
                BoundsEdge(_doorWallSegments, hMin - et, hMax + et, vMax - et, vMax + et, b.min.z, b.max.z);
                BoundsEdge(_doorWallSegments, hMin - et, hMax + et, vMin - et, vMin + et, b.min.z, b.max.z);
                BoundsEdge(_doorWallSegments, hMin - et, hMin + et, vMin, vMax, b.min.z, b.max.z);
                BoundsEdge(_doorWallSegments, hMax - et, hMax + et, vMin, vMax, b.min.z, b.max.z);
            }
        }

        void BoundsSeg(List<GameObject> list, float x0, float x1, float y0, float y1, float z0, float z1, Material mat)
        {
            float sx = x1 - x0, sy = y1 - y0, sz = z1 - z0;
            if (sx < 0.001f || sy < 0.001f || sz < 0.001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BoundsSeg";
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.position = ToWorld(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
            go.transform.rotation = ShipRotation;
            go.transform.localScale = new Vector3(sx, sy, sz);
            go.transform.SetParent(transform, true);
            list.Add(go);
        }

        void BoundsEdge(List<GameObject> list, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            float sx = x1 - x0, sy = y1 - y0, sz = z1 - z0;
            if (sx < 0.001f || sy < 0.001f || sz < 0.001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BoundsEdge";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _edgeMat;
            go.transform.position = ToWorld(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
            go.transform.rotation = ShipRotation;
            go.transform.localScale = new Vector3(sx, sy, sz);
            go.transform.SetParent(transform, true);
            list.Add(go);
        }

        void ShaftWall(float x0, float x1, float y0, float y1, float z0, float z1)
        {
            float sx = x1 - x0, sy = y1 - y0, sz = z1 - z0;
            if (sx < 0.001f || sy < 0.001f || sz < 0.001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "HatchShaftWall";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _holeMat;
            go.transform.position = ToWorld(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
            go.transform.rotation = ShipRotation;
            go.transform.localScale = new Vector3(sx, sy, sz);
            go.transform.SetParent(transform, true);
            _segments.Add(go);
        }

        void RestoreWalls()
        {
            foreach (var go in _segments)
                if (go) Destroy(go);
            _segments.Clear();

            foreach (var w in _wallRefs)
            {
                if (w.Original == null) continue;
                w.Original.GetComponent<MeshRenderer>().enabled = true;
                var col = w.Original.GetComponent<Collider>();
                if (col) col.enabled = true;
            }

            if (_hasFloorRef && _floorRef.Go != null && _floorRef.Collider)
                _floorRef.Collider.enabled = true;

            if (_hasFloorRef && _floorRef.Go != null && _floorRef.Renderer != null)
            {
                _floorRef.Renderer.sharedMaterials = _floorRef.OriginalMats;
                _floorRef.Renderer.SetPropertyBlock(null);
            }

            foreach (var m in _cutoutMats)
                if (m) Destroy(m);
            _cutoutMats.Clear();
        }

        void OnDestroy()
        {
            foreach (var go in _segments)
                if (go) Destroy(go);
            _segments.Clear();

            foreach (var go in _doorWallSegments)
                if (go) Destroy(go);
            _doorWallSegments.Clear();

            foreach (var m in _doorInstMats)
                if (m) Destroy(m);
            _doorInstMats.Clear();

            foreach (var w in _wallRefs)
            {
                if (w.Original == null) continue;
                var mr = w.Original.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = true;
                var col = w.Original.GetComponent<Collider>();
                if (col != null) col.enabled = true;
            }

            if (_hasFloorRef && _floorRef.Go != null && _floorRef.Collider != null)
                _floorRef.Collider.enabled = true;

            if (_hasFloorRef && _floorRef.Go != null && _floorRef.Renderer != null)
            {
                _floorRef.Renderer.sharedMaterials = _floorRef.OriginalMats;
                _floorRef.Renderer.SetPropertyBlock(null);
            }

            foreach (var m in _cutoutMats)
                if (m) Destroy(m);
            _cutoutMats.Clear();

            if (_hasDoorWallRef && _doorWallRef.Go != null)
            {
                if (_doorWallRef.Renderer != null) _doorWallRef.Renderer.enabled = true;
                if (_doorWallRef.Collider != null) _doorWallRef.Collider.enabled = true;
            }
        }

        void Seg(WallRef w, float aMin, float aMax, float bMin, float bMax)
        {
            if (aMax - aMin < 0.001f || bMax - bMin < 0.001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DoorWallSeg";
            go.GetComponent<MeshRenderer>().sharedMaterial = w.Mat;
            Place(go, w, aMin, aMax, bMin, bMax, 1f);
            go.transform.SetParent(transform, true);
            _segments.Add(go);
        }

        void Edge(WallRef w, float aMin, float aMax, float bMin, float bMax)
        {
            if (aMax - aMin < 0.001f || bMax - bMin < 0.001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DoorEdge";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _edgeMat;
            Place(go, w, aMin, aMax, bMin, bMax, 1.3f);
            go.transform.SetParent(transform, true);
            _segments.Add(go);
        }

        void Place(GameObject go, WallRef w, float aMin, float aMax, float bMin, float bMax, float thinMult)
        {
            var lc = Vector3.zero;
            lc[w.AxA] = (aMin + aMax) * 0.5f;
            lc[w.AxB] = (bMin + bMax) * 0.5f;

            var ls = Vector3.one;
            ls[w.Thin] = thinMult;
            ls[w.AxA] = aMax - aMin;
            ls[w.AxB] = bMax - bMin;

            go.transform.position = ToWorld(w.Pos + w.Rot * Vector3.Scale(lc, w.Scale));
            go.transform.rotation = ShipRotation * w.Rot;
            go.transform.localScale = Vector3.Scale(ls, w.Scale);
        }

        static void EnsureEdgeMat()
        {
            if (_edgeMat != null) return;
            _edgeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _edgeMat.color = new Color(0.35f, 0.12f, 0.03f, 1f);
            _edgeMat.SetFloat("_Metallic", 0.7f);

            _holeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _holeMat.color = new Color(0.03f, 0.03f, 0.04f);
            _holeMat.SetFloat("_Metallic", 0f);
        }

        static Material TiledMat(List<Material> list, Material src, Bounds b, float u0, float u1, float v0, float v1, int uAx, int vAx)
        {
            if (src == null || src.mainTexture == null) return src;
            var m = new Material(src);
            float bu = b.size[uAx], bv = b.size[vAx];
            m.mainTextureScale = new Vector2((u1 - u0) / bu, (v1 - v0) / bv);
            m.mainTextureOffset = new Vector2((u0 - b.min[uAx]) / bu, (v0 - b.min[vAx]) / bv);
            list.Add(m);
            return m;
        }
    }
}
