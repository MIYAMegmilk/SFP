using UnityEngine;

namespace SFP.Presentation
{
    public static class FlowMaterials
    {
        static Material _streak;
        static Material _foam;
        static Texture2D _streakTex;
        static Texture2D _foamTex;

        public static Material Streak
        {
            get
            {
                if (_streak == null) Build();
                return _streak;
            }
        }

        public static Material Foam
        {
            get
            {
                if (_foam == null) Build();
                return _foam;
            }
        }

        static void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");

            // Streak texture: vertical noise streaks
            _streakTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            _streakTex.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.15f, y * 0.04f + x * 0.01f);
                float fade = 1f - Mathf.Abs(x / 64f - 1f) * 0.3f;
                float a = Mathf.Clamp01(n * fade);
                _streakTex.SetPixel(x, y, new Color(0.7f, 0.85f, 1f, a));
            }
            _streakTex.Apply();

            // Foam texture: blobby high-frequency noise
            _foamTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            _foamTex.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.3f + 100f, y * 0.3f + 100f);
                float a = n > 0.45f ? Mathf.Clamp01((n - 0.45f) * 4f) : 0f;
                _foamTex.SetPixel(x, y, new Color(0.9f, 0.95f, 1f, a));
            }
            _foamTex.Apply();

            _streak = new Material(shader);
            _streak.SetTexture("_BaseMap", _streakTex);
            _streak.SetColor("_BaseColor", new Color(0.45f, 0.65f, 0.85f, 0.7f));
            SetTransparent(_streak);

            _foam = new Material(shader);
            _foam.SetTexture("_BaseMap", _foamTex);
            _foam.SetColor("_BaseColor", new Color(0.7f, 0.85f, 1f, 0.5f));
            SetTransparent(_foam);
        }

        static void SetTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_Cull", 0f); // both sides
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 3010;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
        }
    }
}
