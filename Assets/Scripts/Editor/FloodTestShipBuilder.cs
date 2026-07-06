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
    public static void Build() => DoBuild();

    [MenuItem("SFP/Build FloodTestShip Scene (Primitives)")]
    public static void BuildLegacy() => DoBuild();

    static void DoBuild()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // All rooms: 4m cubes, 4 per deck, 3 decks = 12
        const float L = 4f, W = 4f, H = 4f;
        var comps = new CompSpec[]
        {
            // Lower deck (floorY=0)
            new() { Name = "Ballast_Bow",   FloorY = 0,  Height = H, LengthX = L, WidthZ = W, Center = new(2f,  2f,  0), Deck = 0 },
            new() { Name = "Engine",        FloorY = 0,  Height = H, LengthX = L, WidthZ = W, Center = new(6f,  2f,  0), Deck = 0 },
            new() { Name = "Ballast_Stern", FloorY = 0,  Height = H, LengthX = L, WidthZ = W, Center = new(10f, 2f,  0), Deck = 0 },
            new() { Name = "Airlock",       FloorY = 0,  Height = H, LengthX = L, WidthZ = W, Center = new(14f, 2f,  0), Deck = 0 },
            // Middle deck (floorY=4)
            new() { Name = "Corridor1",     FloorY = 4f, Height = H, LengthX = L, WidthZ = W, Center = new(2f,  6f,  0), Deck = 1 },
            new() { Name = "Living1",       FloorY = 4f, Height = H, LengthX = L, WidthZ = W, Center = new(6f,  6f,  0), Deck = 1 },
            new() { Name = "Corridor2",     FloorY = 4f, Height = H, LengthX = L, WidthZ = W, Center = new(10f, 6f,  0), Deck = 1 },
            new() { Name = "Living2",       FloorY = 4f, Height = H, LengthX = L, WidthZ = W, Center = new(14f, 6f,  0), Deck = 1 },
            // Upper deck (floorY=8)
            new() { Name = "Corridor3",     FloorY = 8f, Height = H, LengthX = L, WidthZ = W, Center = new(2f,  10f, 0), Deck = 2 },
            new() { Name = "Corridor4",     FloorY = 8f, Height = H, LengthX = L, WidthZ = W, Center = new(6f,  10f, 0), Deck = 2 },
            new() { Name = "Living3",       FloorY = 8f, Height = H, LengthX = L, WidthZ = W, Center = new(10f, 10f, 0), Deck = 2 },
            new() { Name = "Bridge",        FloorY = 8f, Height = H, LengthX = L, WidthZ = W, Center = new(14f, 10f, 0), Deck = 2 },
        };

        var opens = new OpenSpec[]
        {
            // Lower deck doors (floorY=0, door center at y=1)
            new() { Name = "Door_L0_L1", A = 0, B = 1, Pos = new(4f,  1f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_L1_L2", A = 1, B = 2, Pos = new(8f,  1f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_L2_L3", A = 2, B = 3, Pos = new(12f, 1f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            // Middle deck doors (floorY=4, door center at y=5)
            new() { Name = "Door_M0_M1", A = 4, B = 5, Pos = new(4f,  5f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_M1_M2", A = 5, B = 6, Pos = new(8f,  5f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_M2_M3", A = 6, B = 7, Pos = new(12f, 5f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            // Upper deck doors (floorY=8, door center at y=9)
            new() { Name = "Door_U0_U1", A = 8,  B = 9,  Pos = new(4f,  9f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_U1_U2", A = 9,  B = 10, Pos = new(8f,  9f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_U2_U3", A = 10, B = 11, Pos = new(12f, 9f, 0), Area = 3f, Height = 2f, Kind = SFP.Simulation.OpeningKind.Door },
            // Hatches lower->middle (y=4)
            new() { Name = "Hatch_L0_M0", A = 0, B = 4, Pos = new(2f,  4f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_L1_M1", A = 1, B = 5, Pos = new(6f,  4f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_L2_M2", A = 2, B = 6, Pos = new(10f, 4f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            // Hatches middle->upper (y=8)
            new() { Name = "Hatch_M0_U0", A = 4, B = 8,  Pos = new(2f,  8f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_M1_U1", A = 5, B = 9,  Pos = new(6f,  8f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_M2_U2", A = 6, B = 10, Pos = new(10f, 8f, 0), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
        };

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var deckMats = new Material[3];
        for (int d = 0; d < 3; d++)
        {
            deckMats[d] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            deckMats[d].color = DeckColors[d];
            string deckName = d == 0 ? "Lower" : d == 1 ? "Middle" : "Upper";
            AssetDatabase.CreateAsset(deckMats[d], $"Assets/Materials/Deck_{deckName}.mat");
        }

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

            go.AddComponent<WaterMeshRenderer>();

            var mat = deckMats[spec.Deck];

            CreateWall($"{spec.Name}_Floor", go.transform,
                new Vector3(0, spec.FloorY - spec.Center.y, 0),
                new Vector3(spec.LengthX, 0.1f, spec.WidthZ), mat);
            CreateWall($"{spec.Name}_WallN", go.transform,
                new Vector3(0, 0, spec.WidthZ * 0.5f),
                new Vector3(spec.LengthX, spec.Height, 0.1f), mat);
            CreateWall($"{spec.Name}_WallE", go.transform,
                new Vector3(spec.LengthX * 0.5f, 0, 0),
                new Vector3(0.1f, spec.Height, spec.WidthZ), mat);
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
        simBridge.InitialDepth = 200f;
        simBridge.HullVolume = 850f;
        simBridge.SubmarineDryMass = 1025f * 850f;
        bridgeGo.AddComponent<DebugOverlay>();
        bridgeGo.AddComponent<FlowVisualManager>();

        BuildOceanEnvironment();

        // Spectator camera
        var specCamGo = Object.FindFirstObjectByType<Camera>()?.gameObject;
        if (specCamGo != null)
        {
            specCamGo.tag = "Untagged";
            specCamGo.transform.position = new Vector3(8f, 8f, -16f);
            specCamGo.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            specCamGo.GetComponent<Camera>().backgroundColor = new Color(0.01f, 0.04f, 0.1f);
            specCamGo.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            specCamGo.GetComponent<Camera>().enabled = false;
            specCamGo.AddComponent<FlyCamera>().enabled = false;
            specCamGo.AddComponent<BreachTool>().enabled = false;
            specCamGo.AddComponent<DoorInteraction>().enabled = false;
            specCamGo.AddComponent<RepairTool>().enabled = false;
            specCamGo.AddComponent<PumpInteraction>().enabled = false;
        }

        // Player
        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(8f, 5.1f, 0f);
        var cc = playerGo.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        playerGo.AddComponent<PlayerController>();
        playerGo.AddComponent<BreachTool>();
        playerGo.AddComponent<DoorInteraction>();
        playerGo.AddComponent<RepairTool>();
        playerGo.AddComponent<PumpInteraction>();
        playerGo.AddComponent<PlayerHUD>();

        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(playerGo.transform);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var playerCam = camGo.AddComponent<Camera>();
        playerCam.backgroundColor = new Color(0.01f, 0.04f, 0.1f);
        playerCam.clearFlags = CameraClearFlags.SolidColor;
        playerCam.nearClipPlane = 0.1f;

        bridgeGo.AddComponent<CameraModeSwitcher>();

        // Underwater lighting
        var mainLight = Object.FindFirstObjectByType<Light>();
        if (mainLight != null)
        {
            mainLight.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            mainLight.color = new Color(0.5f, 0.7f, 0.9f);
            mainLight.intensity = 0.8f;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.015f;
        RenderSettings.fogColor = new Color(0.02f, 0.08f, 0.15f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.03f, 0.08f, 0.15f);

        // ===== Pumps (logic only) =====
        for (int i = 0; i < compDefs.Length; i++)
        {
            var pumpGo = new GameObject($"Pump_{comps[i].Name}");
            pumpGo.transform.SetParent(compDefs[i].transform);
            pumpGo.transform.localPosition = new Vector3(1.2f, comps[i].FloorY - comps[i].Center.y + 0.25f, 1.2f);
            var pump = pumpGo.AddComponent<Pump>();
            pump.TargetCompartment = compDefs[i];
            pump.IsActive = false;
        }

        // ===== Reactor =====
        {
            var reactorGo = new GameObject("Reactor");
            reactorGo.transform.SetParent(compDefs[1].transform);
            reactorGo.transform.localPosition = new Vector3(0f, comps[1].FloorY - comps[1].Center.y + 0.6f, 0f);
            var rd = reactorGo.AddComponent<ReactorDefinition>();
            rd.Compartment = compDefs[1];
            rd.MaxPowerOutput = 2000f;
            reactorGo.AddComponent<DeviceDegradation>().Compartment = compDefs[1];
        }

        // ===== Junction boxes =====
        int[] jboxComps = { 4, 6, 10 };
        for (int j = 0; j < jboxComps.Length; j++)
        {
            int ci = jboxComps[j];
            var jboxGo = new GameObject($"JunctionBox_{j}");
            jboxGo.transform.SetParent(compDefs[ci].transform);
            var jd = jboxGo.AddComponent<JunctionBoxDefinition>();
            jd.Compartment = compDefs[ci];
            jd.MaxLoad = 800f;
            jboxGo.AddComponent<DeviceDegradation>().Compartment = compDefs[ci];
        }

        // ===== Battery =====
        {
            var battGo = new GameObject("Battery");
            battGo.transform.SetParent(compDefs[1].transform);
            var bd = battGo.AddComponent<BatteryDefinition>();
            bd.Compartment = compDefs[1];
            bd.MaxCharge = 1000f;
            bd.InitialCharge = 500f;
            battGo.AddComponent<DeviceDegradation>().Compartment = compDefs[1];
        }

        // ===== Submarine lights =====
        for (int i = 0; i < compDefs.Length; i++)
        {
            var lightGo = new GameObject($"Light_{comps[i].Name}");
            lightGo.transform.SetParent(compDefs[i].transform);
            lightGo.transform.localPosition = new Vector3(0f, comps[i].FloorY - comps[i].Center.y + H - 0.1f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.range = 5f;
            lt.intensity = 1.5f;
            lt.color = new Color(0.9f, 0.85f, 0.7f);
            var sl = lightGo.AddComponent<SubmarineLight>();
            sl.Compartment = compDefs[i];
            sl.PowerConsumption = 10f;
        }

        // ===== Oxygen generators =====
        int[] o2Comps = { 5, 6 };
        for (int oi = 0; oi < o2Comps.Length; oi++)
        {
            int ci = o2Comps[oi];
            var o2Go = new GameObject($"OxygenGenerator_{oi}");
            o2Go.transform.SetParent(compDefs[ci].transform);
            var od = o2Go.AddComponent<OxygenGeneratorDefinition>();
            od.TargetCompartment = compDefs[ci];
            od.ProductionRate = 0.05f;
            od.PowerConsumption = 80f;
            o2Go.AddComponent<OxygenGeneratorInteraction>();
            o2Go.AddComponent<DeviceDegradation>().Compartment = compDefs[ci];
        }

        // ===== Engine =====
        {
            var engineGo = new GameObject("Engine");
            engineGo.transform.SetParent(compDefs[1].transform);
            var ed = engineGo.AddComponent<EngineDefinition>();
            ed.Compartment = compDefs[1];
            ed.MaxThrust = 50000f;
            ed.PowerConsumption = 200f;
            engineGo.AddComponent<DeviceDegradation>().Compartment = compDefs[1];
        }

        // ===== Ballast tanks =====
        int[] ballastComps = { 0, 2 };
        for (int bi = 0; bi < ballastComps.Length; bi++)
        {
            int ci = ballastComps[bi];
            var bpGo = new GameObject($"BallastPump_{comps[ci].Name}");
            bpGo.transform.SetParent(compDefs[ci].transform);
            var btd = bpGo.AddComponent<BallastTankDefinition>();
            btd.BallastCompartment = compDefs[ci];
            btd.PumpRate = 0.3f;
            btd.PowerConsumption = 40f;
            bpGo.AddComponent<DeviceDegradation>().Compartment = compDefs[ci];
        }

        // ===== Navigation terminal =====
        {
            var navGo = new GameObject("NavigationTerminal");
            navGo.transform.SetParent(compDefs[11].transform);
            navGo.AddComponent<NavigationTerminalDefinition>().Compartment = compDefs[11];
        }

        // ===== Sonar =====
        {
            var sonarGo = new GameObject("Sonar");
            sonarGo.transform.SetParent(compDefs[11].transform);
            var sd = sonarGo.AddComponent<SonarDefinition>();
            sd.Compartment = compDefs[11];
            sd.ActiveRange = 500f;
            sd.PowerConsumption = 100f;
        }

        // ===== Status monitor =====
        {
            var monitorGo = new GameObject("StatusMonitor");
            monitorGo.transform.SetParent(compDefs[6].transform);
            monitorGo.AddComponent<StatusMonitorDefinition>().Compartment = compDefs[6];
        }

        // ===== Fabricator =====
        {
            var fabGo = new GameObject("Fabricator");
            fabGo.transform.SetParent(compDefs[5].transform);
            var fd = fabGo.AddComponent<FabricatorDefinition>();
            fd.Compartment = compDefs[5];
            fd.IsMedical = false;
            fd.PowerConsumption = 80f;
        }

        // ===== Medical fabricator =====
        {
            var medGo = new GameObject("MedicalFabricator");
            medGo.transform.SetParent(compDefs[7].transform);
            var fd = medGo.AddComponent<FabricatorDefinition>();
            fd.Compartment = compDefs[7];
            fd.IsMedical = true;
            fd.PowerConsumption = 80f;
        }

        // ===== Diving suit locker =====
        {
            var lockerGo = new GameObject("DivingSuitLocker");
            lockerGo.transform.SetParent(compDefs[3].transform);
            var dsl = lockerGo.AddComponent<DivingSuitLockerDefinition>();
            dsl.Compartment = compDefs[3];
            dsl.SuitCount = 2;
        }

        // ===== Turret =====
        {
            var turretGo = new GameObject("Turret_Coilgun");
            turretGo.transform.SetParent(compDefs[8].transform);
            var td = turretGo.AddComponent<TurretDefinition>();
            td.Compartment = compDefs[8];
            td.Type = SFP.Simulation.TurretType.Coilgun;
            td.PowerConsumption = 150f;
            td.InitialAmmo = 50;
        }

        // ===== Suppression systems =====
        int[] suppComps = { 1, 5 };
        for (int si = 0; si < suppComps.Length; si++)
        {
            int ci = suppComps[si];
            var suppGo = new GameObject($"Suppression_{comps[ci].Name}");
            suppGo.transform.SetParent(compDefs[ci].transform);
            var ssd = suppGo.AddComponent<SuppressionSystemDefinition>();
            ssd.TargetCompartment = compDefs[ci];
            ssd.ExtinguishRate = 0.5f;
            ssd.WaterReserve = 100f;
            ssd.PowerConsumption = 30f;
        }

        // ===== Interactions on player =====
        playerGo.AddComponent<ReactorInteraction>();
        playerGo.AddComponent<FabricatorInteraction>();
        playerGo.AddComponent<DivingSuitInteraction>();
        playerGo.AddComponent<SteeringInteraction>();
        playerGo.AddComponent<SonarInteraction>();
        playerGo.AddComponent<StatusMonitorInteraction>();
        playerGo.AddComponent<TurretInteraction>();
        playerGo.AddComponent<SuppressionInteraction>();

        // ===== Ladders =====
        for (int i = 9; i < opens.Length; i++)
        {
            var spec = opens[i];
            var ladderGo = new GameObject($"Ladder_{spec.Name}");
            ladderGo.transform.position = spec.Pos + new Vector3(0.5f, 0f, 0f);
            var ladder = ladderGo.AddComponent<Ladder>();
            ladder.DeckHeight = H;

            var triggerGo = new GameObject("LadderTrigger");
            triggerGo.transform.SetParent(ladderGo.transform, false);
            triggerGo.transform.localPosition = new Vector3(0f, H * 0.5f, 0f);
            var box = triggerGo.AddComponent<BoxCollider>();
            box.size = new Vector3(0.9f, H, 0.9f);
            box.isTrigger = true;
        }

        bridgeGo.AddComponent<CrushDepthDamage>();

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/FloodTestShip.unity");
        Debug.Log("FloodTestShip scene built (Primitives): 12 compartments, 15 openings, full equipment suite");
    }

    // ===== Helpers =====

    static void BuildOceanEnvironment()
    {
        var rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        rockMat.color = new Color(0.12f, 0.1f, 0.08f, 1f);
        AssetDatabase.CreateAsset(rockMat, "Assets/Materials/Rock.mat");

        var envParent = new GameObject("Environment");

        CreateEnvironmentBlock("Seabed", envParent.transform,
            new Vector3(8f, -30.5f, 0f), new Vector3(200f, 1f, 200f), rockMat);
        CreateEnvironmentBlock("WallNorth", envParent.transform,
            new Vector3(8f, 5f, 30f), new Vector3(200f, 80f, 20f), rockMat);
        CreateEnvironmentBlock("WallSouth", envParent.transform,
            new Vector3(8f, 5f, -30f), new Vector3(200f, 80f, 20f), rockMat);
        CreateEnvironmentBlock("Ceiling", envParent.transform,
            new Vector3(8f, 40f, 0f), new Vector3(200f, 5f, 60f), rockMat);
        CreateEnvironmentBlock("Rock1", envParent.transform,
            new Vector3(-15f, -5f, 10f), new Vector3(6f, 40f, 6f), rockMat);
        CreateEnvironmentBlock("Rock2", envParent.transform,
            new Vector3(30f, -8f, -10f), new Vector3(8f, 35f, 8f), rockMat);
        CreateEnvironmentBlock("Rock3", envParent.transform,
            new Vector3(-10f, -3f, -15f), new Vector3(5f, 30f, 7f), rockMat);
        CreateEnvironmentBlock("Rock4", envParent.transform,
            new Vector3(35f, -10f, 15f), new Vector3(7f, 28f, 5f), rockMat);
    }

    static void CreateEnvironmentBlock(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent);
        block.transform.position = pos;
        block.transform.localScale = scale;
        block.isStatic = true;
        if (mat != null)
            block.GetComponent<MeshRenderer>().sharedMaterial = mat;
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
