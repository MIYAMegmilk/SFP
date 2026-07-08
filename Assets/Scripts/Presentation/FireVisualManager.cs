using System.Collections.Generic;
using UnityEngine;

namespace SFP.Presentation
{
    public class FireVisualManager : MonoBehaviour
    {
        static readonly Color DimEmission = new Color(0.6f, 0.15f, 0.02f, 1f);
        static readonly Color BrightEmission = new Color(1f, 0.85f, 0.3f, 1f);
        const float LightRange = 8f;
        const float LightIntensityScale = 5f;
        const float CoreSize = 0.4f;

        sealed class Visual
        {
            public GameObject Root;
            public Light Light;
            public Material CoreMat;
        }

        readonly List<int> _compartmentIds = new();
        readonly List<Visual> _visuals = new();

        void Start()
        {
            var bridge = SimulationBridge.Instance;
            if (bridge?.FireSystem == null) return;

            var comps = FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            foreach (var comp in comps)
            {
                int id = bridge.GetCompartmentId(comp);
                if (id < 0) continue;

                var root = new GameObject("FireGlow_" + comp.name);
                root.transform.SetParent(comp.transform, false);
                root.transform.localPosition = Vector3.zero;

                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(root.transform, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.45f, 0.1f);
                light.range = LightRange;
                light.intensity = 0f;

                var coreMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                coreMat.color = DimEmission;
                coreMat.EnableKeyword("_EMISSION");
                coreMat.SetColor("_EmissionColor", DimEmission);
                coreMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

                var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
                core.name = "FlameCore";
                var coreCol = core.GetComponent<Collider>();
                if (coreCol != null) Destroy(coreCol);
                core.transform.SetParent(root.transform, false);
                core.transform.localScale = new Vector3(CoreSize, CoreSize, CoreSize);
                core.GetComponent<MeshRenderer>().sharedMaterial = coreMat;

                root.SetActive(false);

                _compartmentIds.Add(id);
                _visuals.Add(new Visual { Root = root, Light = light, CoreMat = coreMat });
            }
        }

        void Update()
        {
            var bridge = SimulationBridge.Instance;
            var fire = bridge != null ? bridge.FireSystem : null;
            if (fire == null) return;

            for (int i = 0; i < _compartmentIds.Count; i++)
            {
                var visual = _visuals[i];
                if (visual.Root == null) continue;

                float intensity = fire.GetFireIntensity(_compartmentIds[i]);
                bool active = intensity > 0.01f;
                if (visual.Root.activeSelf != active)
                    visual.Root.SetActive(active);

                if (!active) continue;

                visual.Light.intensity = intensity * LightIntensityScale;
                Color emission = Color.Lerp(DimEmission, BrightEmission, intensity);
                visual.CoreMat.SetColor("_EmissionColor", emission);
            }
        }
    }
}
