using UnityEngine;
using UnityEngine.InputSystem;
using SFP.Presentation;
using SFP.Simulation;

namespace SFP.Gameplay
{
    public class FabricatorInteraction : MonoBehaviour
    {
        public float MaxDistance = 3f;

        bool _isOpen;
        FabricatorDefinition _activeFab;
        int _selectedRecipe;
        Vector2 _scrollPos;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (_isOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                {
                    _isOpen = false;
                    _activeFab = null;
                    ConsoleFocus.Release(this);
                }
                return;
            }

            if (!kb.eKey.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, MaxDistance)) return;

            var fab = hit.collider.GetComponentInParent<FabricatorDefinition>();
            if (fab == null) return;

            _activeFab = fab;
            _isOpen = true;
            ConsoleFocus.Acquire(this);
            _selectedRecipe = 0;
        }

        void OnGUI()
        {
            if (!_isOpen || _activeFab == null) return;

            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            var state = bridge.GetFabricator(_activeFab.FabricatorIndex);
            if (state == null) return;

            var recipes = _activeFab.IsMedical
                ? ItemDatabase.MedicalRecipes
                : ItemDatabase.FabricatorRecipes;

            float cx = Screen.width * 0.5f;
            float top = Screen.height * 0.15f;
            float panelW = 360f;
            float panelH = 380f;

            GUI.Box(new Rect(cx - panelW * 0.5f, top, panelW, panelH), "");

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = _activeFab.IsMedical ? new Color(1f, 0.4f, 0.4f) : Color.cyan }
            };
            string title = _activeFab.IsMedical ? "MEDICAL FABRICATOR" : "FABRICATOR";
            GUI.Label(new Rect(cx - panelW * 0.5f, top + 5, panelW, 25), title, titleStyle);

            var style = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Color.white } };
            float lx = cx - panelW * 0.5f + 10;
            float y = top + 35;

            // Recipes
            for (int i = 0; i < recipes.Length; i++)
            {
                var r = recipes[i];
                bool canCraft = state.CanStartCraft(r);
                bool selected = i == _selectedRecipe;

                Color bg = selected ? new Color(0.3f, 0.3f, 0.5f) : new Color(0.15f, 0.15f, 0.15f);
                GUI.color = bg;
                GUI.DrawTexture(new Rect(lx, y, panelW - 20, 22), Texture2D.whiteTexture);
                GUI.color = Color.white;

                style.normal.textColor = canCraft ? Color.white : Color.gray;
                string countStr = r.OutputCount > 1 ? $" x{r.OutputCount}" : "";
                GUI.Label(new Rect(lx + 5, y + 2, panelW - 30, 18),
                    $"{ItemDatabase.GetDisplayName(r.Output)}{countStr}  ({r.CraftTime:F0}s)", style);

                if (GUI.Button(new Rect(lx, y, panelW - 20, 22), "", GUIStyle.none))
                    _selectedRecipe = i;
                y += 24f;
            }

            y += 10f;

            // Selected recipe details
            if (_selectedRecipe >= 0 && _selectedRecipe < recipes.Length)
            {
                var selected = recipes[_selectedRecipe];
                style.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                GUI.Label(new Rect(lx, y, panelW, 18), "Requires:", style);
                y += 18f;
                foreach (var input in selected.Inputs)
                {
                    int have = state.InputInventory.GetCount(input.Key);
                    style.normal.textColor = have >= input.Value ? Color.green : Color.red;
                    GUI.Label(new Rect(lx + 10, y, panelW, 18),
                        $"{ItemDatabase.GetDisplayName(input.Key)}: {have}/{input.Value}", style);
                    y += 16f;
                }

                y += 5f;
                if (state.IsCrafting)
                {
                    style.normal.textColor = Color.yellow;
                    GUI.Label(new Rect(lx, y, panelW, 20),
                        $"Crafting... {state.CraftFraction * 100f:F0}%", style);
                    DrawBar(lx, y + 20, panelW - 20, 8, state.CraftFraction, Color.yellow);
                }
                else if (state.CanStartCraft(selected))
                {
                    if (GUI.Button(new Rect(lx, y, 100, 25), "CRAFT"))
                        state.StartCraft(selected);
                }
            }

            // Input/Output inventory display
            y = top + panelH - 60;
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(lx, y, panelW, 18), "Input:", style);
            string inputStr = "";
            foreach (var kv in state.InputInventory.Items)
                inputStr += $"{ItemDatabase.GetDisplayName(kv.Key)}x{kv.Value} ";
            GUI.Label(new Rect(lx + 40, y, panelW - 50, 18), inputStr, style);
            y += 18f;
            GUI.Label(new Rect(lx, y, panelW, 18), "Output:", style);
            string outputStr = "";
            foreach (var kv in state.OutputInventory.Items)
                outputStr += $"{ItemDatabase.GetDisplayName(kv.Key)}x{kv.Value} ";
            GUI.Label(new Rect(lx + 50, y, panelW - 60, 18), outputStr, style);

            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.5f) }
            };
            GUI.Label(new Rect(cx - panelW * 0.5f, top + panelH - 22, panelW, 20),
                "E / Esc: Close", hintStyle);
        }

        void DrawBar(float x, float y, float w, float h, float fraction, Color color)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(fraction), h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
