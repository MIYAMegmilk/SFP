using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using SFP.Presentation;
using SFP.Gameplay;

public static class FloodTestShipBuilder
{
    struct CompSpec
    {
        public string Name;
        public Vector3 Center;
        public float FloorY, Height, LengthX, WidthZ;
        public int Deck; // 0=lower, 1=middle, 2=upper
        public float FloorArea => LengthX * WidthZ;
    }

    struct OpenSpec
    {
        public string Name;
        public int A, B;
        public Vector3 Pos;
        public float Area, Height;
        public SFP.Simulation.OpeningKind Kind;
    }

    static readonly Color[] DeckColors = new[]
    {
        new Color(0.45f, 0.35f, 0.25f, 1f), // lower: brown
        new Color(0.35f, 0.45f, 0.35f, 1f), // middle: green-grey
        new Color(0.3f,  0.35f, 0.5f,  1f), // upper: blue-grey
    };

    [MenuItem("SFP/Build FloodTestShip Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // All rooms: 5m(L) x 10m(W) x 2.5m(H), 4 per deck, 3 decks = 12
        const float L = 5f, W = 10f, H = 2.5f;
        var comps = new CompSpec[]
        {
            // Lower deck (floorY=0)
            new() { Name = "Ballast_Bow",   FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(2.5f,  1.25f, 0), Deck = 0 },
            new() { Name = "Engine",        FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(7.5f,  1.25f, 0), Deck = 0 },
            new() { Name = "Ballast_Stern", FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(12.5f, 1.25f, 0), Deck = 0 },
            new() { Name = "Airlock",       FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(17.5f, 1.25f, 0), Deck = 0 },
            // Middle deck (floorY=2.5)
            new() { Name = "Corridor1",     FloorY = 2.5f, Height = H, LengthX = L, WidthZ = W, Center = new(2.5f,  3.75f, 0), Deck = 1 },
            new() { Name = "Living1",       FloorY = 2.5f, Height = H, LengthX = L, WidthZ = W, Center = new(7.5f,  3.75f, 0), Deck = 1 },
            new() { Name = "Corridor2",     FloorY = 2.5f, Height = H, LengthX = L, WidthZ = W, Center = new(12.5f, 3.75f, 0), Deck = 1 },
            new() { Name = "Living2",       FloorY = 2.5f, Height = H, LengthX = L, WidthZ = W, Center = new(17.5f, 3.75f, 0), Deck = 1 },
            // Upper deck (floorY=5)
            new() { Name = "Corridor3",     FloorY = 5f,   Height = H, LengthX = L, WidthZ = W, Center = new(2.5f,  6.25f, 0), Deck = 2 },
            new() { Name = "Corridor4",     FloorY = 5f,   Height = H, LengthX = L, WidthZ = W, Center = new(7.5f,  6.25f, 0), Deck = 2 },
            new() { Name = "Living3",       FloorY = 5f,   Height = H, LengthX = L, WidthZ = W, Center = new(12.5f, 6.25f, 0), Deck = 2 },
            new() { Name = "Bridge",        FloorY = 5f,   Height = H, LengthX = L, WidthZ = W, Center = new(17.5f, 6.25f, 0), Deck = 2 },
        };

        // Indices: Lower 0-3, Middle 4-7, Upper 8-11
        // Walls between rooms at x = 5, 10, 15
        // Floors between decks at y = 2.5, 5.0
        var opens = new OpenSpec[]
        {
            // Lower deck doors (between adjacent rooms)
            new() { Name = "Door_L0_L1", A = 0, B = 1, Pos = new(5f,  1f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_L1_L2", A = 1, B = 2, Pos = new(10f, 1f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_L2_L3", A = 2, B = 3, Pos = new(15f, 1f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            // Middle deck doors
            new() { Name = "Door_M0_M1", A = 4, B = 5, Pos = new(5f,  3.5f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_M1_M2", A = 5, B = 6, Pos = new(10f, 3.5f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_M2_M3", A = 6, B = 7, Pos = new(15f, 3.5f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            // Upper deck doors
            new() { Name = "Door_U0_U1", A = 8,  B = 9,  Pos = new(5f,  6f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_U1_U2", A = 9,  B = 10, Pos = new(10f, 6f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_U2_U3", A = 10, B = 11, Pos = new(15f, 6f, 0), Area = 1f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            // Hatches lower->middle (y=2.5)
            new() { Name = "Hatch_L0_M0", A = 0, B = 4, Pos = new(2.5f,  2.5f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_L1_M1", A = 1, B = 5, Pos = new(7.5f,  2.5f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_L2_M2", A = 2, B = 6, Pos = new(12.5f, 2.5f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            // Hatches middle->upper (y=5.0)
            new() { Name = "Hatch_M0_U0", A = 4, B = 8,  Pos = new(2.5f,  5f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_M1_U1", A = 5, B = 9,  Pos = new(7.5f,  5f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_M2_U2", A = 6, B = 10, Pos = new(12.5f, 5f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
        };

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        // Deck materials (color-coded)
        var deckMats = new Material[3];
        for (int d = 0; d < 3; d++)
        {
            deckMats[d] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            deckMats[d].color = DeckColors[d];
            string deckName = d == 0 ? "Lower" : d == 1 ? "Middle" : "Upper";
            AssetDatabase.CreateAsset(deckMats[d], $"Assets/Materials/Deck_{deckName}.mat");
        }

        // Water material
        var waterShader = Shader.Find("SFP/WaterSurface");
        Material waterMat = null;
        if (waterShader != null)
        {
            waterMat = new Material(waterShader);
            AssetDatabase.CreateAsset(waterMat, "Assets/Materials/WaterSurface.mat");
        }

        // Door / Hatch / Hole materials
        var doorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        doorMat.color = new Color(0.2f, 0.7f, 0.3f, 1f);
        AssetDatabase.CreateAsset(doorMat, "Assets/Materials/Door.mat");

        var hatchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        hatchMat.color = new Color(0.9f, 0.7f, 0.1f, 1f);
        AssetDatabase.CreateAsset(hatchMat, "Assets/Materials/Hatch.mat");

        var hullParent = new GameObject("Hull");
        var compDefs = new CompartmentDefinition[comps.Length];

        for (int i = 0; i < comps.Length; i++)
        {
            var spec = comps[i];
            var go = new GameObject(spec.Name);
            go.transform.SetParent(hullParent.transform);
            go.transform.position = spec.Center;

            var def = go.AddComponent<CompartmentDefinition>();
            def.FloorY = spec.FloorY;
            def.Height = spec.Height;
            def.FloorArea = spec.FloorArea;
            def.LengthX = spec.LengthX;
            def.WidthZ = spec.WidthZ;
            compDefs[i] = def;

            var water = go.AddComponent<WaterSurfaceRenderer>();
            if (waterMat != null) water.WaterMaterial = waterMat;

            var mat = deckMats[spec.Deck];

            // Floor
            CreateWall($"{spec.Name}_Floor", go.transform,
                new Vector3(0, spec.FloorY - spec.Center.y, 0),
                new Vector3(spec.LengthX, 0.1f, spec.WidthZ), mat);
            // Back wall (north, Z+) — kept for raycasting
            CreateWall($"{spec.Name}_WallN", go.transform,
                new Vector3(0, 0, spec.WidthZ * 0.5f),
                new Vector3(spec.LengthX, spec.Height, 0.1f), mat);
            // Front wall (south, Z-) — REMOVED so player can see inside (cutaway view)
            // East wall
            CreateWall($"{spec.Name}_WallE", go.transform,
                new Vector3(spec.LengthX * 0.5f, 0, 0),
                new Vector3(0.1f, spec.Height, spec.WidthZ), mat);
            // West wall
            CreateWall($"{spec.Name}_WallW", go.transform,
                new Vector3(-spec.LengthX * 0.5f, 0, 0),
                new Vector3(0.1f, spec.Height, spec.WidthZ), mat);
        }

        // Openings
        var openingsParent = new GameObject("Openings");
        openingsParent.transform.SetParent(hullParent.transform);
        for (int i = 0; i < opens.Length; i++)
        {
            var spec = opens[i];
            var go = new GameObject(spec.Name);
            go.transform.SetParent(openingsParent.transform);
            go.transform.position = spec.Pos;

            var def = go.AddComponent<OpeningDefinition>();
            def.Kind = spec.Kind;
            def.CompartmentA = spec.A >= 0 ? compDefs[spec.A] : null;
            def.CompartmentB = spec.B >= 0 ? compDefs[spec.B] : null;
            def.Area = spec.Area;
            def.Height = spec.Height;
            def.IsOpen = false;

            bool isDoor = spec.Kind == SFP.Simulation.OpeningKind.Door;

            var visual = go.AddComponent<OpeningVisual>();

            if (isDoor)
            {
                float doorW = spec.Area / spec.Height;
                float halfW = doorW * 0.5f;
                var panelScale = new Vector3(0.15f, spec.Height, halfW);

                var panelL = CreatePanel("DoorPanel_L", go.transform, new Vector3(0, 0, -halfW * 0.5f), panelScale, doorMat);
                var panelR = CreatePanel("DoorPanel_R", go.transform, new Vector3(0, 0,  halfW * 0.5f), panelScale, doorMat);

                visual.PanelL = panelL.transform;
                visual.PanelR = panelR.transform;
                visual.ClosedOffsetL = new Vector3(0, 0, -halfW * 0.5f);
                visual.ClosedOffsetR = new Vector3(0, 0,  halfW * 0.5f);
                visual.OpenOffsetL   = new Vector3(0, 0, -halfW * 1.5f);
                visual.OpenOffsetR   = new Vector3(0, 0,  halfW * 1.5f);
            }
            else
            {
                float hatchSide = Mathf.Sqrt(spec.Area);
                float halfSide = hatchSide * 0.5f;
                var panelScale = new Vector3(halfSide, 0.15f, hatchSide);

                var panelL = CreatePanel("HatchPanel_L", go.transform, new Vector3(-halfSide * 0.5f, 0, 0), panelScale, hatchMat);
                var panelR = CreatePanel("HatchPanel_R", go.transform, new Vector3( halfSide * 0.5f, 0, 0), panelScale, hatchMat);

                visual.PanelL = panelL.transform;
                visual.PanelR = panelR.transform;
                visual.ClosedOffsetL = new Vector3(-halfSide * 0.5f, 0, 0);
                visual.ClosedOffsetR = new Vector3( halfSide * 0.5f, 0, 0);
                visual.OpenOffsetL   = new Vector3(-halfSide * 1.5f, 0, 0);
                visual.OpenOffsetR   = new Vector3( halfSide * 1.5f, 0, 0);
            }
        }

        // SimulationBridge
        var bridgeGo = new GameObject("SimulationBridge");
        var simBridge = bridgeGo.AddComponent<SimulationBridge>();
        simBridge.SeaLevelY = 100f;
        bridgeGo.AddComponent<DebugOverlay>();

        // Camera: cutaway view from the south, looking at the ship cross-section
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(10f, 5f, -18f);
            cam.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            cam.gameObject.AddComponent<FlyCamera>();
            cam.gameObject.AddComponent<BreachTool>();
            cam.gameObject.AddComponent<DoorInteraction>();
        }

        // Lighting
        var light = Object.FindFirstObjectByType<Light>();
        if (light != null)
        {
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            light.intensity = 1.5f;
        }

        // Pumps
        for (int i = 0; i < compDefs.Length; i++)
        {
            var pumpGo = new GameObject($"Pump_{comps[i].Name}");
            pumpGo.transform.SetParent(compDefs[i].transform);
            pumpGo.transform.localPosition = Vector3.zero;
            var pump = pumpGo.AddComponent<Pump>();
            pump.TargetCompartment = compDefs[i];
            pump.IsActive = false;
        }

        // Reactors in engine room (index 1)
        for (int d = 0; d < 3; d++)
        {
            var devGo = new GameObject($"Reactor_{d}");
            devGo.transform.SetParent(compDefs[1].transform);
            devGo.transform.localPosition = new Vector3(-3f + d * 3f, -0.5f, 0);
            var dev = devGo.AddComponent<DeviceDegradation>();
            dev.Compartment = compDefs[1];
        }

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/FloodTestShip.unity");
        Debug.Log("FloodTestShip scene built: 12 compartments, 14 openings, 12 pumps, 3 reactors");
    }

    static GameObject CreatePanel(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = name;
        panel.transform.SetParent(parent);
        panel.transform.localPosition = localPos;
        panel.transform.localScale = scale;
        if (mat != null)
            panel.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return panel;
    }

    static void CreateWall(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = scale;
        wall.isStatic = true;
        if (mat != null)
            wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
