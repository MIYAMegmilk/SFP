using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class MineVisualManager : MonoBehaviour
    {
        readonly Dictionary<long, GameObject> _visualDict = new();
        readonly Dictionary<long, bool> _wasExploded = new();

        Material _mineMat;
        Material _flashMat;

        void Start()
        {
            _mineMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _mineMat.color = new Color(0.25f, 0.12f, 0.1f, 1f);
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            var mineSystem = bridge != null ? bridge.MineSystem : null;
            if (mineSystem == null) return;

            var mines = mineSystem.Mines;

            // Track which PersistentIds are still present
            var currentIds = new HashSet<long>();

            for (int i = 0; i < mines.Count; i++)
            {
                var mine = mines[i];
                long id = mine.PersistentId;
                currentIds.Add(id);

                // Create visual for new mines
                if (!_visualDict.ContainsKey(id))
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "Mine";
                    var col = go.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    go.transform.SetParent(transform, false);
                    go.transform.position = new Vector3(mine.X, -mine.Depth, mine.Z);
                    go.transform.localScale = Vector3.one * 2.5f;
                    go.GetComponent<MeshRenderer>().sharedMaterial = _mineMat;

                    _visualDict[id] = go;
                    _wasExploded[id] = false;
                }

                // Handle explosion flashes
                if (!_wasExploded[id] && mine.Exploded)
                {
                    _wasExploded[id] = true;
                    if (_visualDict.TryGetValue(id, out var visual) && visual != null)
                    {
                        Vector3 pos = visual.transform.position;
                        Destroy(visual);
                        _visualDict[id] = null;
                        SpawnFlash(pos);
                    }
                }
            }

            // Destroy visuals for mines no longer present
            var toRemove = new List<long>();
            foreach (var kvp in _visualDict)
            {
                if (!currentIds.Contains(kvp.Key))
                {
                    if (kvp.Value != null)
                        Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
            {
                _visualDict.Remove(id);
                _wasExploded.Remove(id);
            }
        }

        void SpawnFlash(Vector3 pos)
        {
            if (_flashMat == null)
                _flashMat = CreateFlashMaterial();

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "MineExplosionFlash";
            var col = flash.GetComponent<Collider>();
            if (col != null) Destroy(col);
            flash.transform.SetParent(transform, false);
            flash.transform.position = pos;
            flash.transform.localScale = Vector3.one * 2.5f;
            flash.GetComponent<MeshRenderer>().sharedMaterial = _flashMat;

            flash.AddComponent<MineExplosionFlash>().Init(2.5f, 18f, 0.5f);
        }

        static Material CreateFlashMaterial()
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
            mat.color = new Color(1f, 0.5f, 0.1f, 0.6f);
            return mat;
        }
    }

    // Expands a sphere's scale linearly over Duration, then destroys the GameObject.
    public class MineExplosionFlash : MonoBehaviour
    {
        float _startScale;
        float _endScale;
        float _duration;
        float _elapsed;

        public void Init(float startScale, float endScale, float duration)
        {
            _startScale = startScale;
            _endScale = endScale;
            _duration = duration;
            _elapsed = 0f;
        }

        void Update()
        {
            _elapsed += Time.deltaTime;
            float u = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
            transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _endScale, u);
            if (_elapsed >= _duration)
                Destroy(gameObject);
        }
    }
}
