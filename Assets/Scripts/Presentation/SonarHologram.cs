using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class SonarHologram : MonoBehaviour
    {
        // Preferred link: the sonar console driving this hologram. SonarIndex is assigned at
        // runtime by SimulationBridge in scene-scan order, so a baked index is unreliable.
        public SonarDefinition Source;
        public int SonarIndex = 0;
        public float SampleRadius = 250f;
        public float HologramDiameter = 1.2f;
        public float VerticalExaggeration = 2f;
        public float RebuildInterval = 1f;
        public float MaxDepthShown = 650f;
        // World-space spacing of the reference grid drawn on the terrain (m). At the default
        // SampleRadius 250 this yields 10 cells across the hologram.
        public float GridSpacing = 50f;

        const int RES = 48;
        const float CeilingEpsilon = 0.01f;

        GameObject _floorGo, _ceilingGo, _subMarkerGo, _crushPlaneGo;
        Mesh _floorMesh, _ceilingMesh;
        readonly List<GameObject> _minePool = new();

        Material _floorMat, _ceilingMat, _markerMat, _crushMat, _mineMat;

        float _rebuildTimer;
        bool _wasActive;

        Texture2D _gridTex;

        // Repeating tile: 2px bright line along two edges, dimmer translucent fill elsewhere.
        // Applied as base+emission map so grid lines glow brighter than the terrain fill.
        static Texture2D CreateGridTexture()
        {
            const int size = 64;
            const int line = 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "SonarGrid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var fill = new Color(0.55f, 0.55f, 0.55f, 0.55f);
            var lineC = Color.white;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, (x < line || y < line) ? lineC : fill);
            tex.Apply(true);
            return tex;
        }

        void Start()
        {
            _floorMat = CreateHologramMaterial(new Color(0.1f, 0.9f, 0.3f, 0.35f), 1.6f);
            _gridTex = CreateGridTexture();
            _floorMat.SetTexture("_BaseMap", _gridTex);
            _floorMat.SetTexture("_EmissionMap", _gridTex);
            _ceilingMat = CreateHologramMaterial(new Color(0.1f, 0.9f, 0.3f, 0.2f), 1.0f);
            _ceilingMat.SetTexture("_BaseMap", _gridTex);
            _ceilingMat.SetTexture("_EmissionMap", _gridTex);
            _markerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _markerMat.color = new Color(0.2f, 1f, 1f, 1f);
            MakeEmissive(_markerMat, _markerMat.color, 2.5f);
            _crushMat = CreateHologramMaterial(new Color(0.9f, 0.1f, 0.1f, 0.15f), 1.2f);
            _mineMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _mineMat.color = new Color(1f, 0.15f, 0.1f, 1f);
            MakeEmissive(_mineMat, _mineMat.color, 2.5f);

            _floorMesh = new Mesh { name = "SonarFloor" };
            _floorGo = CreateMeshChild("FloorMesh", _floorMesh, _floorMat);

            _ceilingMesh = new Mesh { name = "SonarCeiling" };
            _ceilingGo = CreateMeshChild("CeilingMesh", _ceilingMesh, _ceilingMat);

            _subMarkerGo = CreateSubMarker();
            _crushPlaneGo = CreateCrushPlane();

            // Force an immediate rebuild the first time we go active.
            _rebuildTimer = RebuildInterval;

            SetChildrenActive(false);
        }

        GameObject CreateMeshChild(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        GameObject CreateSubMarker()
        {
            var go = new GameObject("SubMarker");
            go.transform.SetParent(transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.015f, 0.015f, 0.03f);
            SetupUnshadowedRenderer(body.GetComponent<MeshRenderer>(), _markerMat);

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            nose.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            nose.transform.localScale = new Vector3(0.01f, 0.012f, 0.012f);
            SetupUnshadowedRenderer(nose.GetComponent<MeshRenderer>(), _markerMat);

            return go;
        }

        GameObject CreateCrushPlane()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CrushPlane";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(HologramDiameter, 0.003f, HologramDiameter);
            SetupUnshadowedRenderer(go.GetComponent<MeshRenderer>(), _crushMat);
            return go;
        }

        static void SetupUnshadowedRenderer(MeshRenderer mr, Material mat)
        {
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Material CreateHologramMaterial(Color color, float emissionIntensity)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.color = color;
            MakeEmissive(mat, color, emissionIntensity);
            return mat;
        }

        // Holograms are self-lit: emit the base color so they glow even when the
        // interior lights are unpowered.
        static void MakeEmissive(Material mat, Color baseColor, float intensity)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", baseColor * intensity);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            int sonarIdx = Source != null ? Source.SonarIndex : SonarIndex;
            var sonar = bridge != null ? bridge.GetSonar(sonarIdx) : null;
            bool active = sonar != null && sonar.IsActive && sonar.HasPower && bridge.Map != null;

            if (active != _wasActive)
            {
                SetChildrenActive(active);
                _wasActive = active;
                if (active) _rebuildTimer = RebuildInterval;
            }

            if (!active) return;

            // The hologram is parented under the Bridge compartment, which now rides under the
            // moving/rotating ShipRoot (M6 Phase 1). Keep the hologram itself north-up so it
            // still reads as a top-down map regardless of the ship's current heading; only its
            // position should follow the parent hierarchy.
            transform.rotation = Quaternion.identity;

            var sub = bridge.SubState;
            float heightScale = HologramDiameter * 0.5f * VerticalExaggeration / MaxDepthShown;

            _subMarkerGo.transform.localPosition = new Vector3(0f, -sub.Depth * heightScale, 0f);
            _subMarkerGo.transform.localRotation = Quaternion.Euler(0f, sub.Heading, 0f);

            _crushPlaneGo.transform.localPosition = new Vector3(0f, -sub.CrushDepth * heightScale, 0f);

            _rebuildTimer += Time.deltaTime;
            if (_rebuildTimer >= RebuildInterval)
            {
                _rebuildTimer = 0f;
                Rebuild(bridge, sub, heightScale);
            }
        }

        void SetChildrenActive(bool active)
        {
            _floorGo.SetActive(active);
            _ceilingGo.SetActive(active);
            _subMarkerGo.SetActive(active);
            _crushPlaneGo.SetActive(active);
            if (!active)
            {
                foreach (var mine in _minePool)
                    if (mine != null) mine.SetActive(false);
            }
        }

        void Rebuild(SimulationBridge bridge, SubmarineState sub, float heightScale)
        {
            var map = bridge.Map;
            if (map == null) return;

            BuildTerrainMesh(map, sub, heightScale, floor: true);
            BuildTerrainMesh(map, sub, heightScale, floor: false);
            RebuildMines(bridge, sub, heightScale);
        }

        void BuildTerrainMesh(ProceduralMapData map, SubmarineState sub, float heightScale, bool floor)
        {
            var vertices = new Vector3[RES * RES];
            // UVs anchor the grid to world coordinates so lines stay glued to terrain features
            // as the sub moves (the tile texture repeats every GridSpacing meters).
            var uvs = new Vector2[RES * RES];
            // Only needed for the ceiling mesh: tracks which grid vertices sit over rock, so
            // cells with no overhead rock at any corner can be skipped entirely below instead
            // of stretching into a flat lid over open water (local +0.02).
            bool[] hasCeiling = floor ? null : new bool[RES * RES];

            for (int j = 0; j < RES; j++)
            {
                for (int i = 0; i < RES; i++)
                {
                    float u = (float)i / (RES - 1) - 0.5f;
                    float v = (float)j / (RES - 1) - 0.5f;
                    float worldX = sub.PositionX + u * 2f * SampleRadius;
                    float worldZ = sub.PositionZ + v * 2f * SampleRadius;

                    float localX = u * HologramDiameter;
                    float localZ = v * HologramDiameter;

                    uvs[j * RES + i] = new Vector2(worldX / GridSpacing, worldZ / GridSpacing);

                    float y;
                    if (floor)
                    {
                        float depth = map.GetFloorDepthAt(worldX, worldZ);
                        y = -depth * heightScale;
                    }
                    else
                    {
                        float ceiling = map.GetCeilingDepthAt(worldX, worldZ);
                        bool ceilingHere = ceiling > CeilingEpsilon;
                        y = ceilingHere ? -ceiling * heightScale : 0.02f;
                        hasCeiling[j * RES + i] = ceilingHere;
                    }

                    vertices[j * RES + i] = new Vector3(localX, y, localZ);
                }
            }

            var triangles = new List<int>((RES - 1) * (RES - 1) * 6);
            for (int j = 0; j < RES - 1; j++)
            {
                for (int i = 0; i < RES - 1; i++)
                {
                    int i0 = j * RES + i;
                    int i1 = i0 + 1;
                    int i2 = i0 + RES;
                    int i3 = i2 + 1;

                    if (floor)
                    {
                        triangles.Add(i0);
                        triangles.Add(i2);
                        triangles.Add(i1);

                        triangles.Add(i1);
                        triangles.Add(i2);
                        triangles.Add(i3);
                    }
                    else
                    {
                        // Emit this cell only if at least one corner has overhead rock; fully
                        // open-water cells are skipped so no flat lid forms over open water.
                        if (!hasCeiling[i0] && !hasCeiling[i1] && !hasCeiling[i2] && !hasCeiling[i3])
                            continue;

                        // Reversed winding vs. the floor so faces point down.
                        triangles.Add(i0);
                        triangles.Add(i1);
                        triangles.Add(i2);

                        triangles.Add(i1);
                        triangles.Add(i3);
                        triangles.Add(i2);
                    }
                }
            }

            var mesh = floor ? _floorMesh : _ceilingMesh;
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void RebuildMines(SimulationBridge bridge, SubmarineState sub, float heightScale)
        {
            var mines = bridge.MineSystem?.Mines;
            int used = 0;

            if (mines != null)
            {
                for (int i = 0; i < mines.Count; i++)
                {
                    var mine = mines[i];
                    if (mine.Exploded) continue;

                    float dx = mine.X - sub.PositionX;
                    float dz = mine.Z - sub.PositionZ;
                    if (dx * dx + dz * dz > SampleRadius * SampleRadius) continue;

                    var sphere = GetOrCreateMineSphere(used);
                    used++;

                    float localX = dx / (2f * SampleRadius) * HologramDiameter;
                    float localZ = dz / (2f * SampleRadius) * HologramDiameter;
                    float localY = -mine.Depth * heightScale;

                    sphere.transform.localPosition = new Vector3(localX, localY, localZ);
                    sphere.SetActive(true);
                }
            }

            for (int i = used; i < _minePool.Count; i++)
                _minePool[i].SetActive(false);
        }

        GameObject GetOrCreateMineSphere(int index)
        {
            if (index < _minePool.Count) return _minePool[index];

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MineContact";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
            SetupUnshadowedRenderer(go.GetComponent<MeshRenderer>(), _mineMat);
            _minePool.Add(go);
            return go;
        }
    }
}
