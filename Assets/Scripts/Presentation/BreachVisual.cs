using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class BreachVisual : MonoBehaviour
    {
        Opening _opening;
        readonly List<WallInfo> _walls = new();
        readonly List<GameObject> _managed = new();
        float _lastArea = -1f;

        static Material _edgeMat;

        struct WallInfo
        {
            public GameObject Original;
            public Vector3 Pos;
            public Quaternion Rot;
            public Vector3 Scale;
            public Vector3 LocalHit;
            public int Thin, AxA, AxB;
            public Material Mat;
        }

        public void Init(Opening opening, RaycastHit hit)
        {
            _opening = opening;

            if (_edgeMat == null)
            {
                _edgeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _edgeMat.color = new Color(0.35f, 0.12f, 0.03f, 1f);
                _edgeMat.SetFloat("_Metallic", 0.7f);
            }

            // Find ALL overlapping walls at the hit position
            var hitWall = hit.collider.gameObject;
            var wallT = hitWall.transform;
            var center = wallT.position;
            var halfExt = wallT.lossyScale * 0.5f + Vector3.one * 0.05f;

            var overlaps = Physics.OverlapBox(center, halfExt, wallT.rotation);
            foreach (var col in overlaps)
            {
                var go = col.gameObject;
                if (go == hitWall || IsWallAtSamePosition(go, hitWall))
                {
                    var info = BuildWallInfo(go, hit.point);
                    _walls.Add(info);

                    go.GetComponent<MeshRenderer>().enabled = false;
                    col.enabled = false;
                }
            }

            // If somehow only hit wall was found (OverlapBox missed it because we disabled it)
            if (_walls.Count == 0)
            {
                var info = BuildWallInfo(hitWall, hit.point);
                _walls.Add(info);
                hitWall.GetComponent<MeshRenderer>().enabled = false;
                var c = hitWall.GetComponent<Collider>();
                if (c) c.enabled = false;
            }

            Rebuild();
        }

        bool IsWallAtSamePosition(GameObject a, GameObject b)
        {
            float dist = Vector3.Distance(a.transform.position, b.transform.position);
            return dist < 0.15f;
        }

        WallInfo BuildWallInfo(GameObject wall, Vector3 hitWorld)
        {
            var wt = wall.transform;
            var scale = wt.lossyScale;
            int thin = 0;
            if (scale.y < scale[thin]) thin = 1;
            if (scale.z < scale[thin]) thin = 2;

            return new WallInfo
            {
                Original = wall,
                Pos = wt.position,
                Rot = wt.rotation,
                Scale = scale,
                LocalHit = wt.InverseTransformPoint(hitWorld),
                Thin = thin,
                AxA = (thin + 1) % 3,
                AxB = (thin + 2) % 3,
                Mat = wall.GetComponent<MeshRenderer>().sharedMaterial
            };
        }

        void Update()
        {
            if (_opening == null) return;
            if (!Mathf.Approximately(_opening.Area, _lastArea))
                Rebuild();
        }

        public bool HasOpening => _opening != null;

        void OnDestroy()
        {
            foreach (var go in _managed)
                if (go) Destroy(go);
        }

        void Rebuild()
        {
            foreach (var go in _managed)
                if (go) Destroy(go);
            _managed.Clear();
            _lastArea = _opening != null ? _opening.Area : 0.3f;

            foreach (var w in _walls)
                RebuildWall(w);
        }

        void RebuildWall(WallInfo w)
        {
            float holeSide = Mathf.Sqrt(_lastArea);
            float hHalfA = (holeSide * 0.5f) / w.Scale[w.AxA];
            float hHalfB = (holeSide * 0.5f) / w.Scale[w.AxB];

            float hitA = w.LocalHit[w.AxA];
            float hitB = w.LocalHit[w.AxB];

            float minA = Mathf.Max(hitA - hHalfA, -0.5f);
            float maxA = Mathf.Min(hitA + hHalfA,  0.5f);
            float minB = Mathf.Max(hitB - hHalfB, -0.5f);
            float maxB = Mathf.Min(hitB + hHalfB,  0.5f);

            // 4 wall segments around the hole
            Seg(w, -0.5f, 0.5f, maxB, 0.5f);   // top
            Seg(w, -0.5f, 0.5f, -0.5f, minB);  // bottom
            Seg(w, -0.5f, minA, minB, maxB);    // left
            Seg(w, maxA,  0.5f, minB, maxB);    // right

            // Edge frame (only on the first wall to avoid doubling)
            if (_walls.Count == 0 || _walls[0].Original == w.Original)
            {
                float e = 0.05f / Mathf.Max(w.Scale[w.AxA], w.Scale[w.AxB]);
                Edge(w, minA - e, maxA + e, maxB - e, maxB + e);
                Edge(w, minA - e, maxA + e, minB - e, minB + e);
                Edge(w, minA - e, minA + e, minB, maxB);
                Edge(w, maxA - e, maxA + e, minB, maxB);
            }
        }

        void Seg(WallInfo w, float aMin, float aMax, float bMin, float bMax)
        {
            if (aMax - aMin < 0.001f || bMax - bMin < 0.001f) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "WallSeg";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = w.Mat;
            PlaceCube(go, w, aMin, aMax, bMin, bMax, 1f);
            _managed.Add(go);
        }

        void Edge(WallInfo w, float aMin, float aMax, float bMin, float bMax)
        {
            if (aMax - aMin < 0.001f || bMax - bMin < 0.001f) return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "HoleEdge";
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = _edgeMat;
            PlaceCube(go, w, aMin, aMax, bMin, bMax, 1.3f);
            _managed.Add(go);
        }

        void PlaceCube(GameObject go, WallInfo w,
            float aMin, float aMax, float bMin, float bMax, float thinMult)
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
