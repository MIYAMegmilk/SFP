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

        OpeningDefinition _def;
        bool _lastOpen;

        // Wall cutting
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
        float _holeA, _holeB; // hole half-extents in wall local space

        static Material _edgeMat;

        void Start()
        {
            _def = GetComponent<OpeningDefinition>();
            _lastOpen = _def.IsOpen;
            PanelL.localPosition = _def.IsOpen ? OpenOffsetL : ClosedOffsetL;
            PanelR.localPosition = _def.IsOpen ? OpenOffsetR : ClosedOffsetR;

            FindWalls();

            if (_def.IsOpen)
                CutWalls();
        }

        void FindWalls()
        {
            if (_edgeMat == null)
            {
                _edgeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _edgeMat.color = new Color(0.35f, 0.12f, 0.03f, 1f);
                _edgeMat.SetFloat("_Metallic", 0.7f);
            }

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

            var overlaps = Physics.OverlapBox(transform.position, Vector3.one * 0.3f);
            foreach (var col in overlaps)
            {
                var go = col.gameObject;
                // Only cut actual walls/floors, not opening panels
                if (go.transform.parent == transform) continue;
                if (go.name.Contains("Panel") || go.name.Contains("Frame") ||
                    go.name.Contains("Hole") || go.name.Contains("Edge") ||
                    go.name.Contains("Seg")) continue;

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
                    Pos = wt.position,
                    Rot = wt.rotation,
                    Scale = scale,
                    LocalHoleCenter = localCenter,
                    Thin = thin,
                    AxA = axA,
                    AxB = axB,
                    Mat = go.GetComponent<MeshRenderer>().sharedMaterial
                });

                // Compute hole extents in this wall's local space
                // For doors (thin on X): axA=Y, axB=Z → holeA=height, holeB=width
                // For hatches (thin on Y): axA=Z, axB=X → holeA=sideZ, holeB=sideX
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
        }

        void RestoreWalls()
        {
            foreach (var go in _segments)
                if (go) Destroy(go);
            _segments.Clear();

            foreach (var w in _wallRefs)
            {
                w.Original.GetComponent<MeshRenderer>().enabled = true;
                var col = w.Original.GetComponent<Collider>();
                if (col) col.enabled = true;
            }
        }

        void OnDestroy()
        {
            foreach (var go in _segments)
                if (go) Destroy(go);
        }

        void Seg(WallRef w, float aMin, float aMax, float bMin, float bMax)
        {
            if (aMax - aMin < 0.001f || bMax - bMin < 0.001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DoorWallSeg";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = w.Mat;
            Place(go, w, aMin, aMax, bMin, bMax, 1f);
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

            go.transform.position = w.Pos + w.Rot * Vector3.Scale(lc, w.Scale);
            go.transform.rotation = w.Rot;
            go.transform.localScale = Vector3.Scale(ls, w.Scale);
        }
    }
}
