using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class BreachVisual : MonoBehaviour
    {
        Opening _opening;
        readonly List<GameObject> _managed = new();
        float _lastArea = -1f;

        static Material _edgeMat;
        static Material _holeMat;
        static Material _exteriorMarkerMat;
        GameObject _exteriorMarker;

        // Stored in this transform's local space (aligned with the wall face via
        // LookRotation(hit.normal)) so Rebuild positions survive ship movement/rotation.
        Bounds _wallBoundsLocal;
        int _thin, _axA, _axB;
        Vector3 _hitLocal;
        bool _valid;

        public void Init(Opening opening, RaycastHit hit)
        {
            _opening = opening;

            if (_edgeMat == null)
            {
                _edgeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _edgeMat.color = new Color(0.35f, 0.12f, 0.03f, 1f);
                _edgeMat.SetFloat("_Metallic", 0.7f);
            }

            if (_holeMat == null)
            {
                _holeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _holeMat.color = new Color(0.03f, 0.03f, 0.04f);
                _holeMat.SetFloat("_Metallic", 0f);
                _holeMat.SetFloat("_Smoothness", 0.2f);
            }

            var bounds = hit.collider.bounds;

            _hitLocal = transform.InverseTransformPoint(hit.point);
            Vector3 wMin = bounds.min, wMax = bounds.max;
            Vector3 lMin = Vector3.one * float.MaxValue;
            Vector3 lMax = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) != 0 ? wMax.x : wMin.x,
                    (i & 2) != 0 ? wMax.y : wMin.y,
                    (i & 4) != 0 ? wMax.z : wMin.z);
                Vector3 lc = transform.InverseTransformPoint(corner);
                lMin = Vector3.Min(lMin, lc);
                lMax = Vector3.Max(lMax, lc);
            }
            _wallBoundsLocal = new Bounds((lMin + lMax) * 0.5f, lMax - lMin);

            _thin = 0;
            if (_wallBoundsLocal.size.y < _wallBoundsLocal.size[_thin]) _thin = 1;
            if (_wallBoundsLocal.size.z < _wallBoundsLocal.size[_thin]) _thin = 2;

            if (_wallBoundsLocal.size[_thin] >= 0.8f)
            {
                _valid = false;
                return;
            }

            _axA = (_thin + 1) % 3;
            _axB = (_thin + 2) % 3;
            _valid = true;

            if (_exteriorMarkerMat == null)
            {
                _exteriorMarkerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _exteriorMarkerMat.color = new Color(1f, 0.2f, 0.1f, 1f);
            }

            Rebuild();
        }

        void Update()
        {
            if (_opening == null) return;
            if (!Mathf.Approximately(_opening.Area, _lastArea))
                Rebuild();
        }

        public bool HasOpening => _opening != null;
        public Opening Opening => _opening;

        public void Repair()
        {
            if (_opening != null)
            {
                _opening.IsOpen = false;
                _opening.Area = 0f;
                _opening.FlowQ = 0f;
                _opening.FlowVelocity = 0f;
            }

            foreach (var go in _managed)
                if (go) Destroy(go);
            _managed.Clear();

            if (_exteriorMarker != null) { Destroy(_exteriorMarker); _exteriorMarker = null; }

            var emitter = GetComponent<PhysicsWaterEmitter>();
            if (emitter != null) Destroy(emitter);

            foreach (var comp in GetComponents<MonoBehaviour>())
            {
                if (comp != this && comp != null)
                    Destroy(comp);
            }
        }

        void OnDestroy()
        {
            foreach (var go in _managed)
                if (go) Destroy(go);
            if (_exteriorMarker != null) Destroy(_exteriorMarker);
        }

        void Rebuild()
        {
            foreach (var go in _managed)
                if (go) Destroy(go);
            _managed.Clear();

            if (!_valid || _opening == null)
            {
                _lastArea = _opening != null ? _opening.Area : 0f;
                return;
            }

            _lastArea = _opening.Area;
            float holeSide = Mathf.Sqrt(Mathf.Max(_lastArea, 0.01f));
            float half = holeSide * 0.5f;

            float aMin = Mathf.Max(_hitLocal[_axA] - half, _wallBoundsLocal.min[_axA]);
            float aMax = Mathf.Min(_hitLocal[_axA] + half, _wallBoundsLocal.max[_axA]);
            float bMin = Mathf.Max(_hitLocal[_axB] - half, _wallBoundsLocal.min[_axB]);
            float bMax = Mathf.Min(_hitLocal[_axB] + half, _wallBoundsLocal.max[_axB]);

            var holeCenter = Vector3.zero;
            holeCenter[_axA] = (aMin + aMax) * 0.5f;
            holeCenter[_axB] = (bMin + bMax) * 0.5f;
            holeCenter[_thin] = _wallBoundsLocal.center[_thin];

            var holeScale = Vector3.one;
            holeScale[_axA] = aMax - aMin;
            holeScale[_axB] = bMax - bMin;
            holeScale[_thin] = _wallBoundsLocal.size[_thin] * 1.15f;

            MakeCube("BreachHole", holeCenter, holeScale, _holeMat);

            float e = 0.05f;
            float edgeThin = _wallBoundsLocal.size[_thin] * 1.25f;

            MakeEdge(aMin - e, aMax + e, bMax - e, bMax + e, edgeThin);
            MakeEdge(aMin - e, aMax + e, bMin - e, bMin + e, edgeThin);
            MakeEdge(aMin - e, aMin + e, bMin, bMax, edgeThin);
            MakeEdge(aMax - e, aMax + e, bMin, bMax, edgeThin);

            RebuildExteriorMarker(holeSide);
        }

        void RebuildExteriorMarker(float holeSide)
        {
            if (_exteriorMarker != null) Destroy(_exteriorMarker);

            // forward = wall inward normal; exterior is -forward (local Z negative)
            _exteriorMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _exteriorMarker.name = "ExteriorBreachMarker";
            Destroy(_exteriorMarker.GetComponent<Collider>());
            _exteriorMarker.GetComponent<MeshRenderer>().sharedMaterial = _exteriorMarkerMat;
            _exteriorMarker.transform.SetParent(transform, false);
            float markerSize = Mathf.Max(holeSide * 1.5f, 0.4f);
            _exteriorMarker.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            _exteriorMarker.transform.localScale = new Vector3(markerSize, markerSize, 0.05f);
        }

        void MakeEdge(float aMin, float aMax, float bMin, float bMax, float thinSize)
        {
            var center = Vector3.zero;
            center[_axA] = (aMin + aMax) * 0.5f;
            center[_axB] = (bMin + bMax) * 0.5f;
            center[_thin] = _wallBoundsLocal.center[_thin];

            var scale = Vector3.one;
            scale[_axA] = aMax - aMin;
            scale[_axB] = bMax - bMin;
            scale[_thin] = thinSize;

            MakeCube("HoleEdge", center, scale, _edgeMat);
        }

        void MakeCube(string name, Vector3 localCenter, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.SetParent(transform);
            go.transform.localPosition = localCenter;
            go.transform.localScale = localScale;

            _managed.Add(go);
        }
    }
}
