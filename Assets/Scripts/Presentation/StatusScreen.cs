using System.Collections.Generic;
using UnityEngine;

namespace SFP.Presentation
{
    // Compartment status board rendered into a texture for wall monitors: one cell per
    // compartment (rows = decks top-down, columns = west→east), water level drawn as a
    // blue fill rising from the cell bottom, fire as a red bar along the cell top.
    public sealed class StatusScreen
    {
        const int CellPx = 21;   // 20px cell + 1px shared border
        const float RebuildIntervalSeconds = 0.25f;

        Texture2D _tex;
        float _timer;
        int[][] _cellCompIds;    // [row][col] = compartment id, row 0 = top deck

        public Texture2D Texture => _tex;

        public void Tick(float dt, SimulationBridge bridge)
        {
            if (bridge == null || bridge.Graph == null) return;
            EnsureLayout(bridge);
            if (_cellCompIds == null) return;

            _timer -= dt;
            if (_tex != null && _timer > 0f) return;
            _timer = RebuildIntervalSeconds;
            Rebuild(bridge);
        }

        void EnsureLayout(SimulationBridge bridge)
        {
            if (_cellCompIds != null) return;

            var defs = Object.FindObjectsByType<CompartmentDefinition>(FindObjectsSortMode.None);
            if (defs.Length == 0) return;

            // Group by deck (FloorY), order decks top-down and rooms west→east in ship space.
            var byDeck = new SortedDictionary<float, List<CompartmentDefinition>>();
            foreach (var def in defs)
            {
                if (!byDeck.TryGetValue(def.FloorY, out var list))
                    byDeck[def.FloorY] = list = new List<CompartmentDefinition>();
                list.Add(def);
            }

            var rows = new List<int[]>();
            foreach (var deck in byDeck.Keys)
            {
                var list = byDeck[deck];
                list.Sort((a, b) =>
                    bridge.WorldToShip(a.transform.position).x
                        .CompareTo(bridge.WorldToShip(b.transform.position).x));
                var ids = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                    ids[i] = bridge.GetCompartmentId(list[i]);
                rows.Add(ids);
            }
            rows.Reverse(); // top deck first
            _cellCompIds = rows.ToArray();
        }

        void Rebuild(SimulationBridge bridge)
        {
            int cols = 0;
            foreach (var row in _cellCompIds)
                if (row.Length > cols) cols = row.Length;
            int rowsN = _cellCompIds.Length;

            int w = cols * CellPx + 1;
            int h = rowsN * CellPx + 1;
            if (_tex == null)
            {
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var frame = new Color(0f, 0.3f, 0.1f, 1f);
            var cellOk = new Color(0.01f, 0.1f, 0.04f, 1f);
            var water = new Color(0.1f, 0.45f, 0.95f, 1f);
            var fireC = new Color(1f, 0.25f, 0.1f, 1f);

            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = frame;

            for (int r = 0; r < rowsN; r++)
            {
                var row = _cellCompIds[r];
                for (int col = 0; col < row.Length; col++)
                {
                    int compId = row[col];
                    var comp = compId >= 0 ? bridge.Graph.GetCompartment(compId) : null;
                    float waterFrac = comp != null ? comp.WaterFraction : 0f;
                    float fire = compId >= 0 && bridge.FireSystem != null
                        ? bridge.FireSystem.GetFireIntensity(compId) : 0f;

                    // Texture row 0 is the BOTTOM; deck row r=0 (top deck) must land at the top.
                    int y0 = (rowsN - 1 - r) * CellPx + 1;
                    int x0 = col * CellPx + 1;
                    int inner = CellPx - 1;

                    int waterRows = Mathf.RoundToInt(Mathf.Clamp01(waterFrac) * inner);
                    int fireRows = fire > 0.01f ? Mathf.Max(2, Mathf.RoundToInt(fire * 4f)) : 0;

                    for (int y = 0; y < inner; y++)
                    {
                        bool isWater = y < waterRows;
                        bool isFire = y >= inner - fireRows;
                        var c = isFire ? fireC : isWater ? water : cellOk;
                        int rowBase = (y0 + y) * w + x0;
                        for (int x = 0; x < inner; x++)
                            pixels[rowBase + x] = c;
                    }
                }
            }

            _tex.SetPixels(pixels);
            _tex.Apply(false);
        }
    }
}
