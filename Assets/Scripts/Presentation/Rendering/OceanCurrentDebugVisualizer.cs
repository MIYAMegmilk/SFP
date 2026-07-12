using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    /// Debug overlay: draws ocean current vectors as colored arrows in world space.
    /// Toggle with F8 during play mode. Shows a grid of arrows around the camera
    /// at the current depth, colored by speed (blue=slow, cyan=moderate, yellow=fast, red=very fast).
    public sealed class OceanCurrentDebugVisualizer : MonoBehaviour
    {
        const int GridSize = 11;        // 11x11 grid of arrows
        const float Spacing = 30f;      // meters between arrows
        const float ArrowScale = 8f;    // visual length multiplier
        const float ArrowHeadSize = 2f;
        const float DepthSliceSpacing = 100f;
        const int MaxDepthSlices = 5;

        static Material _lineMat;

        bool _enabled;
        bool _showDepthSlices;
        int _depthSliceCount = 1;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.f8Key.wasPressedThisFrame)
                _enabled = !_enabled;

            // F9: toggle depth slice mode
            if (_enabled && kb.f9Key.wasPressedThisFrame)
            {
                _depthSliceCount++;
                if (_depthSliceCount > MaxDepthSlices)
                    _depthSliceCount = 1;
            }
        }

        void OnPostRender()
        {
            if (!_enabled) return;
            var bridge = SimulationBridge.Instance;
            if (bridge?.OceanCurrents == null) return;

            var cam = Camera.main;
            if (cam == null) return;

            EnsureLineMaterial();
            _lineMat.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);

            float baseDepth = Mathf.Max(0f, -cam.transform.position.y);
            float centerX = cam.transform.position.x;
            float centerZ = cam.transform.position.z;

            float halfGrid = (GridSize - 1) * 0.5f * Spacing;

            for (int slice = 0; slice < _depthSliceCount; slice++)
            {
                float depth = baseDepth + slice * DepthSliceSpacing;
                float sliceY = -depth;
                float alpha = slice == 0 ? 1f : 0.5f;

                for (int i = 0; i < GridSize; i++)
                {
                    for (int j = 0; j < GridSize; j++)
                    {
                        float wx = centerX - halfGrid + i * Spacing;
                        float wz = centerZ - halfGrid + j * Spacing;

                        bridge.OceanCurrents.Sample(wx, wz, depth, out float vx, out float vz);
                        float speed = Mathf.Sqrt(vx * vx + vz * vz);

                        Color color = SpeedToColor(speed, alpha);
                        GL.Color(color);

                        Vector3 origin = new Vector3(wx, sliceY, wz);
                        Vector3 tip = origin + new Vector3(vx, 0f, vz) * ArrowScale;

                        // Shaft
                        GL.Vertex(origin);
                        GL.Vertex(tip);

                        // Arrowhead
                        if (speed > 0.01f)
                        {
                            Vector3 dir = new Vector3(vx, 0f, vz).normalized;
                            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                            Vector3 headBase = tip - dir * ArrowHeadSize;
                            GL.Vertex(tip);
                            GL.Vertex(headBase + perp * ArrowHeadSize * 0.5f);
                            GL.Vertex(tip);
                            GL.Vertex(headBase - perp * ArrowHeadSize * 0.5f);
                        }
                    }
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        void OnGUI()
        {
            if (!_enabled) return;

            var cam = Camera.main;
            if (cam == null) return;
            float depth = Mathf.Max(0f, -cam.transform.position.y);

            var bridge = SimulationBridge.Instance;
            if (bridge?.OceanCurrents == null) return;

            bridge.OceanCurrents.Sample(cam.transform.position.x, cam.transform.position.z, depth,
                out float vx, out float vz);
            float speed = Mathf.Sqrt(vx * vx + vz * vz);
            float bearing = Mathf.Atan2(vx, vz) * Mathf.Rad2Deg;
            if (bearing < 0f) bearing += 360f;

            var bgStyle = new GUIStyle(GUI.skin.box);
            GUI.Box(new Rect(10, 10, 260, 80), "", bgStyle);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.cyan }
            };

            GUI.Label(new Rect(16, 14, 250, 18), "OCEAN CURRENT DEBUG [F8 toggle, F9 slices]", style);
            GUI.Label(new Rect(16, 34, 250, 18),
                $"Depth: {depth:F0}m  Speed: {speed:F2} m/s  Bearing: {bearing:F0}°", style);
            GUI.Label(new Rect(16, 54, 250, 18),
                $"Vx: {vx:F3}  Vz: {vz:F3}  Slices: {_depthSliceCount}", style);

            // Color legend
            float legendX = 16;
            float legendY = 74;
            DrawLegendSwatch(legendX, legendY, SpeedToColor(0.2f, 1f), "< 0.5");
            DrawLegendSwatch(legendX + 55, legendY, SpeedToColor(0.7f, 1f), "0.5-1");
            DrawLegendSwatch(legendX + 115, legendY, SpeedToColor(1.3f, 1f), "1-1.5");
            DrawLegendSwatch(legendX + 175, legendY, SpeedToColor(2.0f, 1f), "> 1.5");
        }

        void DrawLegendSwatch(float x, float y, Color color, string label)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, 10, 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var s = new GUIStyle(GUI.skin.label) { fontSize = 9, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x + 12, y - 2, 40, 14), label, s);
        }

        static Color SpeedToColor(float speed, float alpha)
        {
            // blue -> cyan -> yellow -> red
            if (speed < 0.5f)
                return new Color(0.2f, 0.3f, 1f, alpha);
            if (speed < 1.0f)
            {
                float t = (speed - 0.5f) * 2f;
                return new Color(0.2f * (1f - t), 0.3f + 0.7f * t, 1f * (1f - t * 0.3f), alpha);
            }
            if (speed < 1.5f)
            {
                float t = (speed - 1.0f) * 2f;
                return new Color(t, 1f, 0.7f * (1f - t), alpha);
            }
            return new Color(1f, 0.3f, 0.1f, alpha);
        }

        static void EnsureLineMaterial()
        {
            if (_lineMat != null) return;
            var shader = Shader.Find("Hidden/Internal-Colored");
            _lineMat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMat.SetInt("_ZWrite", 0);
            _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<OceanCurrentDebugVisualizer>() != null) return;
            var cam = Camera.main;
            if (cam == null) return;
            cam.gameObject.AddComponent<OceanCurrentDebugVisualizer>();
        }
    }
}
