using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using SFP.Presentation;
using SFP.Gameplay;

public static class FloodTestShipBuilder
{
    struct CompSpec
    {
        public string Name;
        public Vector3 Center;
        public float FloorY, Height, LengthX, WidthZ;
        public int Deck;
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

    const float L = 6f, W = 6f, H = 6f;
    const float WallYScale = H / 9f;

    [MenuItem("SFP/Build FloodTestShip Scene")]
    public static void Build() => DoBuild();

    static void DoBuild()
    {
        // Registers the underwater renderer feature + generates its Volume profiles if this is
        // the first build in a fresh checkout (idempotent, design doc §9).
        UnderwaterRenderingSetup.EnsureSetup();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var comps = new CompSpec[]
        {
            // Lower deck (floorY=0)
            new() { Name = "Ballast_Bow",   FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(3f,   3f,  3f), Deck = 0 },
            new() { Name = "Engine",        FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(9f,   3f,  3f), Deck = 0 },
            new() { Name = "Ballast_Stern", FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(15f,  3f,  3f), Deck = 0 },
            new() { Name = "Workshop",      FloorY = 0,    Height = H, LengthX = L, WidthZ = W, Center = new(21f,  3f,  3f), Deck = 0 },
            // Middle deck (floorY=6)
            new() { Name = "Corridor1",     FloorY = 6f,   Height = H, LengthX = L, WidthZ = W, Center = new(3f,   9f,  3f), Deck = 1 },
            new() { Name = "Living1",       FloorY = 6f,   Height = H, LengthX = L, WidthZ = W, Center = new(9f,   9f,  3f), Deck = 1 },
            new() { Name = "Corridor2",     FloorY = 6f,   Height = H, LengthX = L, WidthZ = W, Center = new(15f,  9f,  3f), Deck = 1 },
            new() { Name = "Living2",       FloorY = 6f,   Height = H, LengthX = L, WidthZ = W, Center = new(21f,  9f,  3f), Deck = 1 },
            // Upper deck (floorY=12)
            new() { Name = "Corridor3",     FloorY = 12f,  Height = H, LengthX = L, WidthZ = W, Center = new(3f,   15f, 3f), Deck = 2 },
            new() { Name = "Corridor4",     FloorY = 12f,  Height = H, LengthX = L, WidthZ = W, Center = new(9f,   15f, 3f), Deck = 2 },
            new() { Name = "Living3",       FloorY = 12f,  Height = H, LengthX = L, WidthZ = W, Center = new(15f,  15f, 3f), Deck = 2 },
            new() { Name = "Bridge",        FloorY = 12f,  Height = H, LengthX = L, WidthZ = W, Center = new(21f,  15f, 3f), Deck = 2 },
            // Sail deck (floorY=18) — conning tower with airlock
            new() { Name = "EVA_Prep",      FloorY = 18f,  Height = H, LengthX = L, WidthZ = W, Center = new(15f,  21f, 3f), Deck = 3 },
            new() { Name = "Airlock",       FloorY = 18f,  Height = H, LengthX = L, WidthZ = W, Center = new(21f,  21f, 3f), Deck = 3 },
        };

        var opens = new OpenSpec[]
        {
            // Lower deck doors (door center Y = floorY + Height/2)
            new() { Name = "Door_L0_L1", A = 0, B = 1, Pos = new(6f,  1.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_L1_L2", A = 1, B = 2, Pos = new(12f, 1.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_L2_L3", A = 2, B = 3, Pos = new(18f, 1.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            // Middle deck doors
            new() { Name = "Door_M0_M1", A = 4, B = 5, Pos = new(6f,  7.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_M1_M2", A = 5, B = 6, Pos = new(12f, 7.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_M2_M3", A = 6, B = 7, Pos = new(18f, 7.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            // Upper deck doors
            new() { Name = "Door_U0_U1", A = 8,  B = 9,  Pos = new(6f,  13.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_U1_U2", A = 9,  B = 10, Pos = new(12f, 13.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            new() { Name = "Door_U2_U3", A = 10, B = 11, Pos = new(18f, 13.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            // Hatches lower->middle (Y=6)
            new() { Name = "Hatch_L0_M0", A = 0, B = 4, Pos = new(3f,  6f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_L1_M1", A = 1, B = 5, Pos = new(9f,  6f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_L2_M2", A = 2, B = 6, Pos = new(15f, 6f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            // Hatches middle->upper (Y=12)
            new() { Name = "Hatch_M0_U0", A = 4, B = 8,  Pos = new(3f,  12f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_M1_U1", A = 5, B = 9,  Pos = new(9f,  12f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_M2_U2", A = 6, B = 10, Pos = new(15f, 12f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            // Hatches upper->sail (Y=18)
            new() { Name = "Hatch_U2_S0", A = 10, B = 12, Pos = new(15f, 18f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "Hatch_U3_S1", A = 11, B = 13, Pos = new(21f, 18f, 3f), Area = 0.8f, Height = 0.5f, Kind = SFP.Simulation.OpeningKind.Hatch },
            // Sail deck door
            new() { Name = "Door_S0_S1", A = 12, B = 13, Pos = new(18f, 19.5f, 3f), Area = 6f, Height = 3f, Kind = SFP.Simulation.OpeningKind.Door },
            // Airlock sea openings (now on sail deck, compDef 13)
            new() { Name = "OuterHatch_Airlock", A = 13, B = -1, Pos = new(23f, 18.5f, 4.5f), Area = 0.8f, Height = 1f, Kind = SFP.Simulation.OpeningKind.Hatch },
            new() { Name = "FloodValve_Airlock", A = 13, B = -1, Pos = new(23f, 18.2f, 1.0f), Area = 0.2f, Height = 0.4f, Kind = SFP.Simulation.OpeningKind.Hatch },
        };

        // Load kit prefabs
        var wallPlain = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/SciFi Warehouse Kit/Prefabs/Structures/Walls/Wall Plain.prefab");
        var floorTile = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/SciFi Warehouse Kit/Prefabs/Structures/Floor/Floor Tile 01.prefab");
        var ladderMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/SciFi Warehouse Kit/Art/Materials/Stairs Mat.mat");

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var doorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        doorMat.color = new Color(0.2f, 0.7f, 0.3f, 1f);
        AssetDatabase.CreateAsset(doorMat, "Assets/Materials/Door.mat");

        var hatchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        hatchMat.color = new Color(0.9f, 0.7f, 0.1f, 1f);
        AssetDatabase.CreateAsset(hatchMat, "Assets/Materials/Hatch.mat");

        var hullMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        hullMat.color = new Color(0.32f, 0.35f, 0.40f, 1f);
        AssetDatabase.CreateAsset(hullMat, "Assets/Materials/Hull.mat");

        // ShipRoot: everything interior (Hull, structures, devices, ladders, player) is parented
        // under this transform. ShipRootDriver moves/rotates it every frame from
        // SimulationBridge.SubState so the whole ship physically travels through the ocean world
        // (M6 Phase 1) instead of being represented by a separate remote proxy. Environment
        // content (Terrain, Mines, Creatures, UnderwaterEnvironment) and the spectator camera stay
        // outside it, at world/map coordinates.
        var shipRootGo = new GameObject("ShipRoot");
        shipRootGo.AddComponent<ShipRootDriver>();

        // Hull
        var hullParent = new GameObject("Hull");
        hullParent.transform.SetParent(shipRootGo.transform);
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
        }

        // Walls & Floors
        var structParent = new GameObject("Structures");
        structParent.transform.SetParent(hullParent.transform);

        // Decks 0-2: full width (4 columns each)
        for (int deck = 0; deck < 3; deck++)
        {
            float floorY = deck * H;

            // Floors (4 per deck)
            for (int col = 0; col < 4; col++)
                PlaceFloor(structParent.transform, floorTile, col * L, floorY, 0f);

            // Ceiling for cols 0-1 on deck 2 (no deck 3 above them)
            if (deck == 2)
            {
                for (int col = 0; col < 2; col++)
                    PlaceFloor(structParent.transform, floorTile, col * L, floorY + H, 0f);
            }

            // WallX faces (5 boundaries: X=0,6,12,18,24)
            for (int bx = 0; bx <= 4; bx++)
            {
                float x = bx * L;
                // Skip east exterior wall on upper deck — replaced by window assembly
                if (deck == 2 && bx == 4) continue;
                var prefab = wallPlain;
                PlaceWallX(structParent.transform, prefab, x, floorY, 0f);
            }

            // WallZ faces (south Z=0 and north Z=6, 4 segments each)
            for (int col = 0; col < 4; col++)
            {
                PlaceWallZ(structParent.transform, wallPlain, col * L, floorY, 0f);
                PlaceWallZ(structParent.transform, wallPlain, col * L, floorY, W);
            }
        }

        // Deck 3 (sail): partial width — cols 2-3 only (x=12..24)
        {
            float floorY = 3 * H;
            // Floor tiles (also serve as deck 2's ceiling for cols 2-3)
            for (int col = 2; col < 4; col++)
                PlaceFloor(structParent.transform, floorTile, col * L, floorY, 0f);
            // Ceiling (roof of sail deck)
            for (int col = 2; col < 4; col++)
                PlaceFloor(structParent.transform, floorTile, col * L, floorY + H, 0f);
            // WallX: boundaries at x=12, 18, 24
            for (int bx = 2; bx <= 4; bx++)
                PlaceWallX(structParent.transform, wallPlain, bx * L, floorY, 0f);
            // WallZ: south and north for cols 2-3
            for (int col = 2; col < 4; col++)
            {
                PlaceWallZ(structParent.transform, wallPlain, col * L, floorY, 0f);
                PlaceWallZ(structParent.transform, wallPlain, col * L, floorY, W);
            }
        }

        // Openings
        var openingsParent = new GameObject("Openings");
        openingsParent.transform.SetParent(hullParent.transform);
        var openingDefs = new OpeningDefinition[opens.Length];
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
            openingDefs[i] = def;

            bool isDoor = spec.Kind == SFP.Simulation.OpeningKind.Door;
            var visual = go.AddComponent<OpeningVisual>();
            visual.SkipWallCutting = true;

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

                // Underside invisible trigger — raycastable from the deck below
                if (spec.B != -1)
                {
                    var handle = new GameObject("HatchHandleTrigger");
                    handle.transform.SetParent(go.transform);
                    handle.transform.localPosition = new Vector3(0f, -0.4f, 0f);
                    var box = handle.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.6f, 0.3f, 0.6f);
                    box.isTrigger = true;
                }
            }
        }

        // Hull shell: the physical exterior hull, replacing the old SubmarineProxy. Parented
        // under ShipRoot so it rides along with the interior it wraps.
        BuildHullShell(shipRootGo.transform, comps, hullMat);

        // Fire visuals: reads CompartmentDefinitions at runtime, so it's parented under Hull.
        var fireVisGo = new GameObject("FireVisuals");
        fireVisGo.transform.SetParent(hullParent.transform);
        fireVisGo.AddComponent<FireVisualManager>();

        // SimulationBridge
        var bridgeGo = new GameObject("SimulationBridge");
        var simBridge = bridgeGo.AddComponent<SimulationBridge>();
        simBridge.DebugUnlimitedPower = true;
        simBridge.InitialDepth = 200f;
        simBridge.HullVolume = 3200f;
        // Trimmed so the boat is neutral with the MBTs at 50% (2 tanks × 240 m³ × 0.5 = 240 m³):
        // ρ·V_hull = DryMass + ρ·240 → DryMass = ρ·(3200 − 240). The 240 m³ full-blow reserve
        // exceeds one 216 m³ compartment (one-compartment standard) — a single fully flooded
        // room is survivable by blowing tanks.
        simBridge.SubmarineDryMass = 1025f * (3200f - 240f);
        simBridge.ShipRootRef = shipRootGo.transform;
        bridgeGo.AddComponent<SFP.Presentation.DebugOverlay>();
        bridgeGo.AddComponent<DamageEventPresenter>();
        bridgeGo.AddComponent<FlowVisualManager>();
        var bsm = bridgeGo.AddComponent<BuiltStructureManager>();
        bsm.CellSize = 6f;
        bsm.CellHeight = 6f;
        bsm.GridOrigin = new Vector3(0f, 0f, 0f);
        bsm.WallPrefab = wallPlain;
        bsm.FloorPrefab = floorTile;

        // Network infrastructure
        var networkGo = new GameObject("Network");
        networkGo.AddComponent<NetworkManager>();
        networkGo.AddComponent<UnityTransport>();
        networkGo.AddComponent<NetworkBootstrap>();

        var syncGo = new GameObject("NetworkSync");
        syncGo.AddComponent<NetworkObject>();
        syncGo.AddComponent<SimSnapshotSync>();
        syncGo.AddComponent<DeviceRpcRelay>();

        // Lobby UI
        var lobbyGo = new GameObject("LobbyUI");
        lobbyGo.AddComponent<LobbyUI>();

        BuildOceanEnvironment();

        // Spectator camera
        var specCamGo = Object.FindFirstObjectByType<Camera>()?.gameObject;
        if (specCamGo != null)
        {
            specCamGo.tag = "Untagged";
            specCamGo.transform.position = new Vector3(12f, 12f, -20f);
            specCamGo.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            specCamGo.GetComponent<Camera>().backgroundColor = new Color(0.01f, 0.04f, 0.1f);
            specCamGo.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            specCamGo.GetComponent<Camera>().enabled = false;
            var specListener = specCamGo.GetComponent<AudioListener>();
            if (specListener != null) Object.DestroyImmediate(specListener);
            specCamGo.AddComponent<FlyCamera>().enabled = false;
            specCamGo.AddComponent<BreachTool>().enabled = false;
            specCamGo.AddComponent<DoorInteraction>().enabled = false;
            specCamGo.AddComponent<RepairTool>().enabled = false;
            specCamGo.AddComponent<PumpInteraction>().enabled = false;
            specCamGo.AddComponent<BuildTool>().enabled = false;
        }

        // Player
        var playerGo = new GameObject("Player");
        playerGo.transform.SetParent(shipRootGo.transform);
        playerGo.transform.position = new Vector3(12f, 7.1f, 3f);
        var cc = playerGo.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        playerGo.AddComponent<PlayerController>();
        playerGo.AddComponent<BreachTool>();
        playerGo.AddComponent<DoorInteraction>();
        playerGo.AddComponent<RepairTool>();
        playerGo.AddComponent<PumpInteraction>();
        playerGo.AddComponent<BuildTool>();
        playerGo.AddComponent<PlayerHUD>();

        var camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(playerGo.transform);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var playerCam = camGo.AddComponent<Camera>();
        playerCam.backgroundColor = new Color(0.01f, 0.04f, 0.1f);
        playerCam.clearFlags = CameraClearFlags.SolidColor;
        playerCam.nearClipPlane = 0.1f;
        camGo.AddComponent<AudioListener>();

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

        // Pumps
        for (int i = 0; i < compDefs.Length; i++)
        {
            var pumpGo = new GameObject($"Pump_{comps[i].Name}");
            pumpGo.transform.SetParent(compDefs[i].transform);
            pumpGo.transform.localPosition = new Vector3(1.5f, comps[i].FloorY - comps[i].Center.y + 0.25f, 1.5f);
            var pump = pumpGo.AddComponent<Pump>();
            pump.TargetCompartment = compDefs[i];
            pump.StartActive = false;
            AddConsole(pumpGo, new Vector3(0.5f, 0.5f, 0.5f), new Color(0.3f, 0.6f, 0.9f));
        }

        // Reactor
        {
            var reactorGo = new GameObject("Reactor");
            reactorGo.transform.SetParent(compDefs[1].transform);
            // Moved off room center (was blocking the ceiling ladder column at local (0.8, *, 0)).
            reactorGo.transform.localPosition = new Vector3(-1.2f, -2.4f, 2.0f);
            var rd = reactorGo.AddComponent<ReactorDefinition>();
            rd.Compartment = compDefs[1];
            rd.MaxPowerOutput = 2000f;
            reactorGo.AddComponent<DeviceDegradation>().Compartment = compDefs[1];
            AddConsole(reactorGo, new Vector3(1.6f, 1.8f, 1.6f), new Color(0.8f, 0.3f, 0.2f));
        }

        // Junction boxes
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
            PlaceDeviceConsole(jboxGo, new Vector3(2.6f, -1.4f, -2.3f), new Vector3(0.6f, 0.9f, 0.3f), new Color(0.8f, 0.75f, 0.2f));
        }

        // Battery
        {
            var battGo = new GameObject("Battery");
            battGo.transform.SetParent(compDefs[1].transform);
            var bd = battGo.AddComponent<BatteryDefinition>();
            bd.Compartment = compDefs[1];
            bd.MaxCharge = 1000f;
            bd.InitialCharge = 500f;
            battGo.AddComponent<DeviceDegradation>().Compartment = compDefs[1];
            PlaceDeviceConsole(battGo, new Vector3(2.4f, -2.6f, 2.2f), new Vector3(1.2f, 0.8f, 0.7f), new Color(0.2f, 0.5f, 0.8f));
        }

        // Submarine lights
        for (int i = 0; i < compDefs.Length; i++)
        {
            var lightGo = new GameObject($"Light_{comps[i].Name}");
            lightGo.transform.SetParent(compDefs[i].transform);
            lightGo.transform.localPosition = new Vector3(0f, comps[i].FloorY - comps[i].Center.y + H - 0.2f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.range = 8f;
            lt.intensity = 1.5f;
            lt.color = new Color(0.9f, 0.85f, 0.7f);
            var sl = lightGo.AddComponent<SubmarineLight>();
            sl.Compartment = compDefs[i];
            sl.PowerConsumption = 10f;
        }

        // Oxygen generators
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
            // Moved off the west-door centerline (was dead-center in the doorway keep-clear strip).
            PlaceDeviceConsole(o2Go, new Vector3(-2.4f, -1.9f, -2.3f), new Vector3(0.8f, 1.2f, 0.8f), new Color(0.7f, 0.9f, 1.0f));
        }

        // CO2 scrubbers (same compartments as O2 generators)
        int[] scrubberComps = { 5, 6 };
        for (int si = 0; si < scrubberComps.Length; si++)
        {
            int ci = scrubberComps[si];
            var scrubGo = new GameObject($"CO2Scrubber_{si}");
            scrubGo.transform.SetParent(compDefs[ci].transform);
            var sd = scrubGo.AddComponent<CO2ScrubberDefinition>();
            sd.TargetCompartment = compDefs[ci];
            sd.ProcessRate = 1.0f;
            sd.Efficiency = 0.95f;
            sd.PowerConsumption = 60f;
            scrubGo.AddComponent<CO2ScrubberInteraction>();
            scrubGo.AddComponent<DeviceDegradation>().Compartment = compDefs[ci];
            PlaceDeviceConsole(scrubGo, new Vector3(2.4f, -1.9f, -2.3f), new Vector3(0.6f, 1.0f, 0.6f), new Color(0.2f, 0.8f, 0.8f));
        }

        // HVAC vents (duct connections between adjacent compartments)
        // Engine(1) <-> Corridor1(4), Living1(5) <-> Living2(7), Corridor2(6) <-> Bridge(11)
        int[,] ventPairs = { { 1, 4 }, { 5, 7 }, { 6, 11 } };
        for (int vi = 0; vi < ventPairs.GetLength(0); vi++)
        {
            int ciA = ventPairs[vi, 0];
            int ciB = ventPairs[vi, 1];
            var ventGo = new GameObject($"Vent_{comps[ciA].Name}_{comps[ciB].Name}");
            ventGo.transform.SetParent(compDefs[ciA].transform);
            ventGo.transform.localPosition = new Vector3(0f, H * 0.5f - 0.3f, 0f);
            var vd = ventGo.AddComponent<VentDefinition>();
            vd.CompartmentA = compDefs[ciA];
            vd.CompartmentB = compDefs[ciB];
            vd.DuctArea = 0.1f;
            vd.FanFlowRate = 1.5f;
            vd.PowerConsumption = 25f;
            ventGo.AddComponent<VentInteraction>();
            ventGo.AddComponent<DeviceDegradation>().Compartment = compDefs[ciA];
            PlaceDeviceConsole(ventGo, new Vector3(-2.2f, 1.5f, -2.2f), new Vector3(0.8f, 0.4f, 0.8f), new Color(0.6f, 0.6f, 0.8f));
        }

        // Engine
        {
            var engineGo = new GameObject("Engine");
            engineGo.transform.SetParent(compDefs[1].transform);
            var ed = engineGo.AddComponent<EngineDefinition>();
            ed.Compartment = compDefs[1];
            ed.MaxThrust = 50000f;
            ed.PowerConsumption = 200f;
            engineGo.AddComponent<DeviceDegradation>().Compartment = compDefs[1];
            // Moved off the west-door centerline (was blocking the door to comp 0).
            PlaceDeviceConsole(engineGo, new Vector3(1.6f, -2.2f, -2.1f), new Vector3(2.2f, 1.6f, 1.6f), new Color(0.5f, 0.5f, 0.55f));
        }

        // External MBT pods (saddle tanks below the hull). The console inside only commands
        // the pumps — ballast water never enters the compartment graph.
        int[] ballastComps = { 0, 2 };
        for (int bi = 0; bi < ballastComps.Length; bi++)
        {
            int ci = ballastComps[bi];
            var bpGo = new GameObject($"BallastPump_{comps[ci].Name}");
            bpGo.transform.SetParent(compDefs[ci].transform);
            var btd = bpGo.AddComponent<BallastTankDefinition>();
            btd.Capacity = 240f;
            btd.InitialFillLevel = 0.5f;
            btd.PumpRate = 0.1f;
            btd.PowerConsumption = 40f;
            bpGo.AddComponent<DeviceDegradation>().Compartment = compDefs[ci];
            PlaceDeviceConsole(bpGo, new Vector3(-1.5f, -2.6f, 1.5f), new Vector3(1.0f, 0.8f, 1.0f), new Color(0.3f, 0.6f, 0.9f));

            var pod = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pod.name = $"BallastTankPod_{comps[ci].Name}";
            Object.DestroyImmediate(pod.GetComponent<Collider>());
            pod.transform.SetParent(compDefs[ci].transform);
            pod.transform.localPosition = new Vector3(0f, -H * 0.5f - 1.1f, 0f);
            pod.transform.localScale = new Vector3(5.8f, 2.0f, 9f);
            var podMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            podMat.color = new Color(0.16f, 0.2f, 0.28f);
            pod.GetComponent<MeshRenderer>().sharedMaterial = podMat;
        }

        // Navigation terminal
        {
            var navGo = new GameObject("NavigationTerminal");
            navGo.transform.SetParent(compDefs[11].transform);
            navGo.AddComponent<NavigationTerminalDefinition>().Compartment = compDefs[11];
            PlaceDeviceConsole(navGo, new Vector3(0f, -2.3f, 2.5f), new Vector3(1.4f, 1.4f, 0.4f), new Color(0.1f, 0.8f, 0.9f));
        }

        // Sonar Tier 1: standalone 2D sonar, no 3D hologram.
        {
            var sonarGo = new GameObject("Sonar_T1");
            sonarGo.transform.SetParent(compDefs[11].transform);
            var sd = sonarGo.AddComponent<SonarDefinition>();
            sd.Compartment = compDefs[11];
            sd.ActiveRange = 500f;
            sd.PowerConsumption = 100f;
            sd.Tier = 1;
            sonarGo.AddComponent<AudioSource>();
            sonarGo.AddComponent<SonarAudio>();
            PlaceDeviceConsole(sonarGo, new Vector3(-1.8f, -2.3f, 2.5f), new Vector3(1.2f, 1.4f, 0.4f), new Color(0.1f, 0.9f, 0.3f));
        }

        // Fused nav+sonar consoles: one E press opens the helm and the 2D sonar together
        // (both definitions live on the same GameObject, so both interactions trigger).
        // Tier 2 (Living3): backup conning station, no hologram.
        // Tier 3 (Bridge): fused console plus the 3D holographic terrain map above it.
        {
            var t2Go = new GameObject("NavSonarConsole_T2");
            t2Go.transform.SetParent(compDefs[10].transform);
            var t2Nav = t2Go.AddComponent<NavigationTerminalDefinition>();
            t2Nav.Compartment = compDefs[10];
            t2Nav.Tier = 2;
            var t2Sonar = t2Go.AddComponent<SonarDefinition>();
            t2Sonar.Compartment = compDefs[10];
            t2Sonar.ActiveRange = 500f;
            t2Sonar.PowerConsumption = 120f;
            t2Sonar.Tier = 2;
            t2Go.AddComponent<AudioSource>();
            t2Go.AddComponent<SonarAudio>();
            t2Go.AddComponent<DeviceDegradation>().Compartment = compDefs[10];
            PlaceDeviceConsole(t2Go, new Vector3(0f, -2.3f, 2.5f), new Vector3(1.6f, 1.4f, 0.4f), new Color(0.1f, 0.85f, 0.75f));

            var t3Go = new GameObject("NavSonarConsole_T3");
            t3Go.transform.SetParent(compDefs[11].transform);
            var t3Nav = t3Go.AddComponent<NavigationTerminalDefinition>();
            t3Nav.Compartment = compDefs[11];
            t3Nav.Tier = 3;
            var t3Sonar = t3Go.AddComponent<SonarDefinition>();
            t3Sonar.Compartment = compDefs[11];
            t3Sonar.ActiveRange = 500f;
            t3Sonar.PowerConsumption = 150f;
            t3Sonar.Tier = 3;
            t3Go.AddComponent<AudioSource>();
            t3Go.AddComponent<SonarAudio>();
            t3Go.AddComponent<DeviceDegradation>().Compartment = compDefs[11];
            PlaceDeviceConsole(t3Go, new Vector3(1.8f, -2.3f, 2.5f), new Vector3(1.6f, 1.4f, 0.4f), new Color(0.3f, 0.7f, 1f));

            var hologramGo = new GameObject("SonarHologram");
            hologramGo.transform.SetParent(compDefs[11].transform);
            hologramGo.transform.localPosition = new Vector3(1.8f, -0.6f, 1.6f);
            hologramGo.AddComponent<SonarHologram>().Source = t3Sonar;

            // Wall monitors (Corridor3): passive screens mirroring device displays.
            // The sonar monitor shows the T3 console's picture once that sonar is switched on.
            var mSonarGo = new GameObject("Monitor_Sonar");
            mSonarGo.transform.SetParent(compDefs[8].transform);
            var mSonar = mSonarGo.AddComponent<MonitorDefinition>();
            mSonar.SourceDevice = t3Sonar;
            mSonar.ScreenSize = new Vector2(1.5f, 1.5f);
            PlaceDeviceConsole(mSonarGo, new Vector3(1.5f, -1.0f, 2.8f), new Vector3(1.7f, 1.7f, 0.15f), new Color(0.08f, 0.1f, 0.12f));

            var mStatusGo = new GameObject("Monitor_Status");
            mStatusGo.transform.SetParent(compDefs[8].transform);
            var mStatus = mStatusGo.AddComponent<MonitorDefinition>();
            mStatus.ScreenSize = new Vector2(1.5f, 1.2f);
            PlaceDeviceConsole(mStatusGo, new Vector3(-1.5f, -1.0f, 2.8f), new Vector3(1.7f, 1.4f, 0.15f), new Color(0.08f, 0.1f, 0.12f));
        }

        // ADCP (ocean current sensor) in Bridge
        {
            var adcpGo = new GameObject("ADCP");
            adcpGo.transform.SetParent(compDefs[11].transform);
            var ad = adcpGo.AddComponent<ADCPDefinition>();
            ad.Compartment = compDefs[11];
            ad.PowerConsumption = 50f;
            ad.MaxRange = 600f;
            PlaceDeviceConsole(adcpGo, new Vector3(-1.8f, -2.3f, 0.8f), new Vector3(1.0f, 1.4f, 0.4f), new Color(0.1f, 0.7f, 0.9f));
        }

        // Status monitor
        {
            var monitorGo = new GameObject("StatusMonitor");
            monitorGo.transform.SetParent(compDefs[6].transform);
            monitorGo.AddComponent<StatusMonitorDefinition>().Compartment = compDefs[6];
            PlaceDeviceConsole(monitorGo, new Vector3(0f, -1.7f, 2.85f), new Vector3(1.6f, 1.0f, 0.2f), new Color(0.9f, 0.6f, 0.1f));
        }

        // Fabricator
        {
            var fabGo = new GameObject("Fabricator");
            fabGo.transform.SetParent(compDefs[5].transform);
            var fd = fabGo.AddComponent<FabricatorDefinition>();
            fd.Compartment = compDefs[5];
            fd.IsMedical = false;
            fd.PowerConsumption = 80f;
            PlaceDeviceConsole(fabGo, new Vector3(2.3f, -2.3f, 2.3f), new Vector3(1.4f, 1.4f, 0.9f), new Color(0.6f, 0.4f, 0.8f));
        }

        // Medical fabricator
        {
            var medGo = new GameObject("MedicalFabricator");
            medGo.transform.SetParent(compDefs[7].transform);
            var fd = medGo.AddComponent<FabricatorDefinition>();
            fd.Compartment = compDefs[7];
            fd.IsMedical = true;
            fd.PowerConsumption = 80f;
            PlaceDeviceConsole(medGo, new Vector3(2.3f, -2.3f, 2.3f), new Vector3(1.4f, 1.4f, 0.9f), new Color(0.9f, 0.9f, 0.95f));
        }

        // Diving suit locker (in EVA_Prep room, compDefs[12])
        {
            var lockerGo = new GameObject("DivingSuitLocker");
            lockerGo.transform.SetParent(compDefs[12].transform);
            var dsl = lockerGo.AddComponent<DivingSuitLockerDefinition>();
            dsl.Compartment = compDefs[12];
            dsl.SuitCount = 2;
            PlaceDeviceConsole(lockerGo, new Vector3(2.5f, -2.0f, 0f), new Vector3(1.0f, 2.0f, 0.6f), new Color(0.9f, 0.5f, 0.1f));
        }

        // Airlock console + definition (in Airlock room, compDefs[13])
        {
            OpeningDefinition outerHatchDef = null, floodValveDef = null, innerDoorDef = null, floorHatchDef = null;
            for (int i = 0; i < openingDefs.Length; i++)
            {
                if (opens[i].Name == "OuterHatch_Airlock") outerHatchDef = openingDefs[i];
                else if (opens[i].Name == "FloodValve_Airlock") { floodValveDef = openingDefs[i]; floodValveDef.IsGasSealed = true; }
                else if (opens[i].Name == "Door_S0_S1") innerDoorDef = openingDefs[i];
                else if (opens[i].Name == "Hatch_U3_S1") floorHatchDef = openingDefs[i];
            }

            var airlockGo = new GameObject("AirlockConsole");
            airlockGo.transform.SetParent(compDefs[13].transform);
            var ad = airlockGo.AddComponent<AirlockDefinition>();
            ad.Compartment = compDefs[13];
            ad.InnerDoor = innerDoorDef;
            ad.OuterHatch = outerHatchDef;
            ad.FloodValve = floodValveDef;
            ad.FloorHatch = floorHatchDef;
            ad.PowerConsumption = 200f;
            PlaceDeviceConsole(airlockGo, new Vector3(-2.5f, -1.5f, 2.3f), new Vector3(0.6f, 1.2f, 0.6f), new Color(0.2f, 0.8f, 0.8f));
        }

        // Turret
        {
            var turretGo = new GameObject("Turret_Coilgun");
            turretGo.transform.SetParent(compDefs[8].transform);
            var td = turretGo.AddComponent<TurretDefinition>();
            td.Compartment = compDefs[8];
            td.Type = SFP.Simulation.TurretType.Coilgun;
            td.PowerConsumption = 150f;
            td.InitialAmmo = 50;
            PlaceDeviceConsole(turretGo, new Vector3(0f, -2.2f, -2.3f), new Vector3(1.2f, 1.6f, 1.0f), new Color(0.4f, 0.4f, 0.45f));
        }

        // Suppression systems
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
            // Corner placement differs per compartment: comp 1 (Engine) uses the west-wall
            // corner outside its z-band door strips; comp 5 (Living1) uses the west-wall
            // corner on the +z side, clear of the west door strip and the ladder column.
            Vector3 suppPos = ci == 1
                ? new Vector3(-2.6f, -1.8f, -2.2f)
                : new Vector3(-2.7f, -1.8f, 2.3f);
            PlaceDeviceConsole(suppGo, suppPos, new Vector3(0.5f, 1.0f, 0.5f), new Color(0.8f, 0.1f, 0.1f));
        }

        // Player interactions
        playerGo.AddComponent<ReactorInteraction>();
        playerGo.AddComponent<FabricatorInteraction>();
        playerGo.AddComponent<DivingSuitInteraction>();
        playerGo.AddComponent<SteeringInteraction>();
        playerGo.AddComponent<SonarInteraction>();
        playerGo.AddComponent<StatusMonitorInteraction>();
        playerGo.AddComponent<TurretInteraction>();
        playerGo.AddComponent<SuppressionInteraction>();
        playerGo.AddComponent<BallastInteraction>();
        playerGo.AddComponent<ExtinguisherInteraction>();
        playerGo.AddComponent<CO2ScrubberInteraction>();
        playerGo.AddComponent<VentInteraction>();
        playerGo.AddComponent<CrewCommandInteraction>();
        playerGo.AddComponent<AirlockInteraction>();
        playerGo.AddComponent<ADCPInteraction>();
        playerGo.AddComponent<EVAWeaponController>();

        // Crew visuals manager (under ShipRoot)
        var crewVisualsGo = new GameObject("CrewVisuals");
        crewVisualsGo.transform.SetParent(shipRootGo.transform);
        crewVisualsGo.transform.localPosition = Vector3.zero;
        crewVisualsGo.transform.localRotation = Quaternion.identity;
        crewVisualsGo.AddComponent<CrewVisualManager>();

        // Opening state sync (crew-opened doors → visual)
        bridgeGo.AddComponent<OpeningStateSyncManager>();

        // Crew spawn points (sorted alphabetically for deterministic ids)
        var spawn1 = new GameObject("CrewSpawn_1");
        spawn1.transform.SetParent(shipRootGo.transform);
        spawn1.transform.localPosition = new Vector3(9f, 6f, 3f);
        spawn1.AddComponent<CrewSpawnDefinition>().Job = SFP.Simulation.CrewJobKind.Engineer;

        var spawn2 = new GameObject("CrewSpawn_2");
        spawn2.transform.SetParent(shipRootGo.transform);
        spawn2.transform.localPosition = new Vector3(21f, 6f, 3f);
        spawn2.AddComponent<CrewSpawnDefinition>().Job = SFP.Simulation.CrewJobKind.Mechanic;

        var spawn3 = new GameObject("CrewSpawn_3");
        spawn3.transform.SetParent(shipRootGo.transform);
        spawn3.transform.localPosition = new Vector3(21f, 12f, 3f);
        spawn3.AddComponent<CrewSpawnDefinition>().Job = SFP.Simulation.CrewJobKind.DamageControl;

        // Ladders for vertical hatches (inter-deck openings with two compartments)
        for (int i = 0; i < opens.Length; i++)
        {
            var spec = opens[i];
            if (spec.Kind != SFP.Simulation.OpeningKind.Hatch) continue;
            if (spec.B < 0) continue; // sea opening, no ladder

            var ladderGo = new GameObject($"Ladder_{spec.Name}");
            ladderGo.transform.SetParent(shipRootGo.transform);
            ladderGo.transform.position = spec.Pos + new Vector3(0.8f, 0f, 0f);
            var ladder = ladderGo.AddComponent<Ladder>();
            ladder.DeckHeight = H;
            ladder.Hatch = openingDefs[i];

            var triggerGo = new GameObject("LadderTrigger");
            triggerGo.transform.SetParent(ladderGo.transform, false);
            triggerGo.transform.localPosition = new Vector3(0f, H * 0.5f, 0f);
            var box = triggerGo.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, H, 1f);
            box.isTrigger = true;

            BuildLadderVisual(ladderGo.transform, H, ladderMat);
        }

        bridgeGo.AddComponent<CrushDepthDamage>();

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/FloodTestShip.unity");
        Debug.Log($"FloodTestShip scene built: {comps.Length} compartments ({L}m cells), SciFi Warehouse Kit walls");
    }

    // ===== Wall/Floor placement helpers =====

    static void BuildLadderVisual(Transform parent, float height, Material mat)
    {
        void Bar(string name, Vector3 localPos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
        }

        Bar("RailL", new Vector3(0f, height * 0.5f, -0.25f), new Vector3(0.08f, height, 0.08f));
        Bar("RailR", new Vector3(0f, height * 0.5f, 0.25f), new Vector3(0.08f, height, 0.08f));
        for (float y = 0.3f; y < height - 0.1f; y += 0.35f)
            Bar("Rung", new Vector3(0f, y, 0f), new Vector3(0.06f, 0.05f, 0.5f));
    }

    static void PlaceWallX(Transform parent, GameObject prefab, float x, float floorY, float z)
    {
        if (prefab == null) return;
        var go = Object.Instantiate(prefab, parent);
        go.name = $"WallX_{x}_{floorY}";
        go.transform.position = new Vector3(x, floorY, z);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = new Vector3(1f, WallYScale, 1f);
        // NOT marked static: this is now a child of the moving ShipRoot (M6 Phase 1). Static
        // batching would bake it into a fixed world-space mesh and it would stop moving with
        // the ship.
    }

    static void PlaceWallZ(Transform parent, GameObject prefab, float x, float floorY, float z)
    {
        if (prefab == null) return;
        var go = Object.Instantiate(prefab, parent);
        go.name = $"WallZ_{x}_{floorY}_{z}";
        go.transform.position = new Vector3(x, floorY, z);
        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        go.transform.localScale = new Vector3(1f, WallYScale, 1f);
        // NOT marked static — see PlaceWallX.
    }

    static void PlaceFloor(Transform parent, GameObject prefab, float x, float y, float z)
    {
        if (prefab == null) return;
        var go = Object.Instantiate(prefab, parent);
        go.name = $"Floor_{x}_{y}";
        go.transform.position = new Vector3(x, y, z);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        // NOT marked static — see PlaceWallX.
    }

    // ===== Hull shell (exterior visual, replaces the old SubmarineProxy) =====

    // Builds a thin 6-slab shell around the interior bounds (NOT a solid cube — the player is
    // inside!), plus a sail and headlights, parented under ShipRoot so they ride with the ship.
    static void BuildHullShell(Transform shipRoot, CompSpec[] comps, Material hullMat)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var spec in comps)
        {
            float hx = spec.LengthX * 0.5f, hz = spec.WidthZ * 0.5f;
            if (spec.Center.x - hx < minX) minX = spec.Center.x - hx;
            if (spec.Center.x + hx > maxX) maxX = spec.Center.x + hx;
            if (spec.Center.z - hz < minZ) minZ = spec.Center.z - hz;
            if (spec.Center.z + hz > maxZ) maxZ = spec.Center.z + hz;
            if (spec.FloorY < minY) minY = spec.FloorY;
            float ceil = spec.FloorY + spec.Height;
            if (ceil > maxY) maxY = ceil;
        }

        // Shell sits slightly outside the interior bounds (e.g. interior 0..24, 0..18, 0..6 ->
        // shell roughly -0.4..24.4, -0.6..18.4, -0.6..6.4).
        const float padX = 0.4f, padYBottom = 0.6f, padYTop = 0.4f, padZSouth = 0.6f, padZNorth = 0.4f;
        float ox0 = minX - padX, ox1 = maxX + padX;
        float oy0 = minY - padYBottom, oy1 = maxY + padYTop;
        float oz0 = minZ - padZSouth, oz1 = maxZ + padZNorth;

        var shellParent = new GameObject("HullShell");
        shellParent.transform.SetParent(shipRoot, false);

        void Slab(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = hullMat;
            go.transform.SetParent(shellParent.transform, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            // NOT marked static: parented under the moving ShipRoot (see PlaceWallX).
        }

        // Floor & ceiling (span the full outer X/Z footprint).
        Slab("ShellFloor", new Vector3((ox0 + ox1) * 0.5f, (oy0 + minY) * 0.5f, (oz0 + oz1) * 0.5f),
            new Vector3(ox1 - ox0, minY - oy0, oz1 - oz0));
        Slab("ShellCeiling", new Vector3((ox0 + ox1) * 0.5f, (maxY + oy1) * 0.5f, (oz0 + oz1) * 0.5f),
            new Vector3(ox1 - ox0, oy1 - maxY, oz1 - oz0));
        // West & east walls (span the full outer Y/Z).
        Slab("ShellWest", new Vector3((ox0 + minX) * 0.5f, (oy0 + oy1) * 0.5f, (oz0 + oz1) * 0.5f),
            new Vector3(minX - ox0, oy1 - oy0, oz1 - oz0));

        // East wall (bow): split around the Bridge window opening.
        const float winY0 = 13.5f, winY1 = 16.5f; // 3m tall, centered on Bridge eye-level
        const float winZ0 = 1.0f, winZ1 = 5.0f;   // 4m wide, centered on z=3
        float eastThick = ox1 - maxX;
        float eastCX = (maxX + ox1) * 0.5f;
        // Bottom strip (full Z, below window)
        Slab("ShellEast_Bottom", new Vector3(eastCX, (oy0 + winY0) * 0.5f, (oz0 + oz1) * 0.5f),
            new Vector3(eastThick, winY0 - oy0, oz1 - oz0));
        // Top strip (full Z, above window)
        Slab("ShellEast_Top", new Vector3(eastCX, (winY1 + oy1) * 0.5f, (oz0 + oz1) * 0.5f),
            new Vector3(eastThick, oy1 - winY1, oz1 - oz0));
        // Left pillar (south side of window)
        Slab("ShellEast_Left", new Vector3(eastCX, (winY0 + winY1) * 0.5f, (oz0 + winZ0) * 0.5f),
            new Vector3(eastThick, winY1 - winY0, winZ0 - oz0));
        // Right pillar (north side of window)
        Slab("ShellEast_Right", new Vector3(eastCX, (winY0 + winY1) * 0.5f, (winZ1 + oz1) * 0.5f),
            new Vector3(eastThick, winY1 - winY0, oz1 - winZ1));

        // Glass window panel — URP/Lit transparent requires full keyword + property setup
        var glassMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        glassMat.SetFloat("_Surface", 1f);
        glassMat.SetFloat("_Blend", 0f);
        glassMat.SetFloat("_AlphaClip", 0f);
        glassMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        glassMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glassMat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        glassMat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        glassMat.SetFloat("_ZWrite", 0f);
        glassMat.SetFloat("_Smoothness", 0.95f);
        glassMat.SetFloat("_Metallic", 0f);
        glassMat.SetOverrideTag("RenderType", "Transparent");
        glassMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        glassMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glassMat.SetShaderPassEnabled("ShadowCaster", false);
        glassMat.color = new Color(0.55f, 0.8f, 0.85f, 0.35f);
        glassMat.SetColor("_EmissionColor", new Color(0.05f, 0.12f, 0.15f, 1f));
        glassMat.EnableKeyword("_EMISSION");
        glassMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        AssetDatabase.CreateAsset(glassMat, "Assets/Materials/Glass.mat");

        // Glass panel — flush with interior wall face so it's visible from inside
        var windowGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        windowGo.name = "BridgeWindow";
        var windowCol = windowGo.GetComponent<BoxCollider>();
        windowCol.isTrigger = false; // solid — blocks player
        windowGo.GetComponent<MeshRenderer>().sharedMaterial = glassMat;
        windowGo.transform.SetParent(shellParent.transform, false);
        windowGo.transform.localPosition = new Vector3(maxX, (winY0 + winY1) * 0.5f, (winZ0 + winZ1) * 0.5f);
        windowGo.transform.localScale = new Vector3(0.1f, winY1 - winY0, winZ1 - winZ0);

        // Window frame — interior trim panels so the opening doesn't look bare
        var frameMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        frameMat.color = new Color(0.25f, 0.28f, 0.32f, 1f);
        frameMat.SetFloat("_Metallic", 0.6f);
        frameMat.SetFloat("_Smoothness", 0.4f);
        AssetDatabase.CreateAsset(frameMat, "Assets/Materials/WindowFrame.mat");

        float frameDepth = 0.15f; // frame protrusion into room
        float frameThick = 0.2f;  // frame band width
        float winCY = (winY0 + winY1) * 0.5f;
        float winCZ = (winZ0 + winZ1) * 0.5f;
        float winH = winY1 - winY0;
        float winW = winZ1 - winZ0;
        float frameX = maxX - frameDepth * 0.5f;

        void FramePanel(string name, Vector3 pos, Vector3 size)
        {
            var fp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fp.name = name;
            Object.DestroyImmediate(fp.GetComponent<Collider>());
            fp.GetComponent<MeshRenderer>().sharedMaterial = frameMat;
            fp.transform.SetParent(shellParent.transform, false);
            fp.transform.localPosition = pos;
            fp.transform.localScale = size;
        }

        // Bottom frame
        FramePanel("WinFrame_Bottom", new Vector3(frameX, winY0 - frameThick * 0.5f, winCZ),
            new Vector3(frameDepth, frameThick, winW + frameThick * 2f));
        // Top frame
        FramePanel("WinFrame_Top", new Vector3(frameX, winY1 + frameThick * 0.5f, winCZ),
            new Vector3(frameDepth, frameThick, winW + frameThick * 2f));
        // Left frame (south)
        FramePanel("WinFrame_Left", new Vector3(frameX, winCY, winZ0 - frameThick * 0.5f),
            new Vector3(frameDepth, winH, frameThick));
        // Right frame (north)
        FramePanel("WinFrame_Right", new Vector3(frameX, winCY, winZ1 + frameThick * 0.5f),
            new Vector3(frameDepth, winH, frameThick));

        // Fill the rest of the east wall face (around window) with interior panels
        // so it doesn't look like a bare hull backface
        void InteriorPanel(string name, Vector3 pos, Vector3 size)
        {
            var ip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ip.name = name;
            Object.DestroyImmediate(ip.GetComponent<Collider>());
            ip.GetComponent<MeshRenderer>().sharedMaterial = hullMat;
            ip.transform.SetParent(shellParent.transform, false);
            ip.transform.localPosition = pos;
            ip.transform.localScale = size;
        }

        // Interior wall below window (covers deck 2 area: y=12..13.5)
        float intX = maxX - 0.05f; // thin panel at interior face
        float intThick = 0.1f;
        InteriorPanel("EastWall_BelowWin", new Vector3(intX, (12f + winY0) * 0.5f, 3f),
            new Vector3(intThick, winY0 - 12f, W));
        // Interior wall above window (y=16.5..24, covers upper deck + sail deck east face)
        InteriorPanel("EastWall_AboveWin", new Vector3(intX, (winY1 + 24f) * 0.5f, 3f),
            new Vector3(intThick, 24f - winY1, W));
        // Interior wall left of window (z=0..1)
        InteriorPanel("EastWall_LeftWin", new Vector3(intX, winCY, winZ0 * 0.5f),
            new Vector3(intThick, winH, winZ0));
        // Interior wall right of window (z=5..6)
        InteriorPanel("EastWall_RightWin", new Vector3(intX, winCY, (winZ1 + W) * 0.5f),
            new Vector3(intThick, winH, W - winZ1));
        // South & north walls (span the full outer X/Y).
        Slab("ShellSouth", new Vector3((ox0 + ox1) * 0.5f, (oy0 + oy1) * 0.5f, (oz0 + minZ) * 0.5f),
            new Vector3(ox1 - ox0, oy1 - oy0, minZ - oz0));
        Slab("ShellNorth", new Vector3((ox0 + ox1) * 0.5f, (oy0 + oy1) * 0.5f, (maxZ + oz1) * 0.5f),
            new Vector3(ox1 - ox0, oy1 - oy0, oz1 - maxZ));

        float shipCenterX = (minX + maxX) * 0.5f;
        float shipCenterZ = (minZ + maxZ) * 0.5f;

        // Sail: small box on top, offset toward the bow (+X — ship forward, see ShipRootDriver).
        float sailLength = Mathf.Min((maxX - minX) * 0.15f, 4f);
        float sailWidth = (maxZ - minZ) * 0.5f;
        float sailHeight = (maxY - minY) * 0.3f;
        Vector3 sailCenter = new(shipCenterX + (maxX - minX) * 0.15f, maxY + sailHeight * 0.5f, shipCenterZ);
        Slab("Sail", sailCenter, new Vector3(sailLength, sailHeight, sailWidth));

        // Headlights at the bow (+X face), pointing along authored +X (ship forward).
        CreateHeadlight(shellParent.transform, "HeadlightL", new Vector3(maxX, (minY + maxY) * 0.5f, shipCenterZ - 1.5f));
        CreateHeadlight(shellParent.transform, "HeadlightR", new Vector3(maxX, (minY + maxY) * 0.5f, shipCenterZ + 1.5f));

        var hullGlowGo = new GameObject("HullGlow");
        hullGlowGo.transform.SetParent(shellParent.transform, false);
        hullGlowGo.transform.localPosition = new Vector3(shipCenterX, maxY + 2f, shipCenterZ);
        var hullGlow = hullGlowGo.AddComponent<Light>();
        hullGlow.type = LightType.Point;
        hullGlow.range = 60f;
        hullGlow.intensity = 2.5f;
        hullGlow.color = new Color(0.5f, 0.7f, 0.9f);
        hullGlow.shadows = LightShadows.None;
    }

    // Spot light whose local forward (+Z) is rotated to point along ship-local +X (the bow
    // direction once ShipRootDriver maps authored +X to the sim heading vector).
    static void CreateHeadlight(Transform parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.spotAngle = 70f;
        light.range = 120f;
        light.intensity = 4f;
        light.color = new Color(1f, 0.95f, 0.8f);
        light.shadows = LightShadows.None;

        // Self-register for backscatter light cone rendering at runtime
        go.AddComponent<BackscatterLightRegistrar>();
    }

    // ===== Environment =====

    static void BuildOceanEnvironment()
    {
        var rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        rockMat.color = new Color(0.12f, 0.1f, 0.08f, 1f);
        AssetDatabase.CreateAsset(rockMat, "Assets/Materials/Rock.mat");

        var envParent = new GameObject("Environment");

        var terrainGo = new GameObject("Terrain");
        terrainGo.transform.SetParent(envParent.transform);
        var terrainRenderer = terrainGo.AddComponent<TerrainRenderer>();
        terrainRenderer.RockMaterial = rockMat;

        // No separate submarine proxy anymore: the real hull (built by BuildHullShell) lives
        // under ShipRoot and physically moves through this environment (M6 Phase 1).

        var minesGo = new GameObject("Mines");
        minesGo.transform.SetParent(envParent.transform);
        minesGo.AddComponent<MineVisualManager>();

        var creaturesGo = new GameObject("Creatures");
        creaturesGo.transform.SetParent(envParent.transform);
        creaturesGo.AddComponent<CreatureVisualManager>();

        // Replaces UnderwaterAmbience (design doc §6.1): drives ambient/fog/sun and the _SFP*
        // shader globals the Underwater renderer feature reads.
        var underwaterEnvGo = new GameObject("UnderwaterEnvironment");
        underwaterEnvGo.transform.SetParent(envParent.transform);
        underwaterEnvGo.AddComponent<UnderwaterEnvironmentController>();

        // Marine snow: camera-following particle box for deep-ocean atmosphere (§5.1)
        var snowGo = new GameObject("MarineSnow");
        snowGo.transform.SetParent(envParent.transform);
        snowGo.AddComponent<MarineSnowController>();

        // Backscatter: selects top-4 spot lights and feeds shader arrays (§6.3)
        var bsGo = new GameObject("BackscatterLightManager");
        bsGo.transform.SetParent(envParent.transform);
        bsGo.AddComponent<BackscatterLightManager>();

        BuildUnderwaterVolumes(envParent.transform);
    }

    // Global Volumes carrying the Underwater optical profiles (design doc §4.3). Ocean is the
    // exterior baseline at fixed weight 1; Interior starts at weight 0 and is driven upward at
    // runtime by UnderwaterEnvironmentController while the camera is submerged inside a flooded
    // compartment, so the two profiles cross-blend automatically.
    static void BuildUnderwaterVolumes(Transform envParent)
    {
        var oceanProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/UnderwaterOceanProfile.asset");
        var interiorProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/UnderwaterInteriorProfile.asset");

        var volumesGo = new GameObject("UnderwaterVolumes");
        volumesGo.transform.SetParent(envParent);

        var oceanGo = new GameObject("UnderwaterOcean");
        oceanGo.transform.SetParent(volumesGo.transform);
        var oceanVolume = oceanGo.AddComponent<Volume>();
        oceanVolume.isGlobal = true;
        oceanVolume.priority = 10f;
        oceanVolume.weight = 1f;
        oceanVolume.sharedProfile = oceanProfile;

        var interiorGo = new GameObject("UnderwaterInterior");
        interiorGo.transform.SetParent(volumesGo.transform);
        var interiorVolume = interiorGo.AddComponent<Volume>();
        interiorVolume.isGlobal = true;
        interiorVolume.priority = 20f;
        interiorVolume.weight = 0f; // Controller manages weight at runtime
        interiorVolume.sharedProfile = interiorProfile;
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

    // ===== Device console placement =====

    // Positions the device at localPos (fixing the world-position-stays-at-origin bug from
    // SetParent) and gives it a visible, raycastable console cube.
    static void PlaceDeviceConsole(GameObject deviceGo, Vector3 localPos, Vector3 size, Color color)
    {
        deviceGo.transform.localPosition = localPos;
        AddConsole(deviceGo, size, color);
    }

    // Adds only the console cube, for devices (e.g. Reactor) that already have their
    // localPosition set and must not be moved.
    static void AddConsole(GameObject deviceGo, Vector3 size, Color color)
    {
        var console = GameObject.CreatePrimitive(PrimitiveType.Cube);
        console.name = "Console";
        console.transform.SetParent(deviceGo.transform);
        console.transform.localPosition = Vector3.zero;
        console.transform.localScale = size;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        console.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
