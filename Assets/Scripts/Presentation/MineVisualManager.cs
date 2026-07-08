using System.Collections.Generic;
using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    public class MineVisualManager : MonoBehaviour
    {
        readonly List<MineState> _mines = new();
        readonly List<GameObject> _visuals = new();
        readonly List<bool> _wasExploded = new();

        Material _mineMat;
        Material _flashMat;

        void Start()
        {
            var bridge = SimulationBridge.Instance;
            var mineSystem = bridge != null ? bridge.MineSystem : null;
            if (mineSystem == null) return;

            _mineMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _mineMat.color = new Color(0.25f, 0.12f, 0.1f, 1f);

            foreach (var mine in mineSystem.Mines)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Mine";
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(mine.X, -mine.Depth, mine.Z);
                go.transform.localScale = Vector3.one * 2.5f;
                go.GetComponent<MeshRenderer>().sharedMaterial = _mineMat;

                _mines.Add(mine);
                _visuals.Add(go);
                _wasExploded.Add(mine.Exploded);
            }
        }

        void Update()
        {
            for (int i = 0; i < _mines.Count; i++)
            {
                if (_wasExploded[i] || !_mines[i].Exploded) continue;

                _wasExploded[i] = true;
                var visual = _visuals[i];
                Vector3 pos = visual != null
                    ? visual.transform.position
                    : new Vector3(_mines[i].X, -_mines[i].Depth, _mines[i].Z);
                if (visual != null)
                {
                    Destroy(visual);
                    _visuals[i] = null;
                }
                SpawnFlash(pos);
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
