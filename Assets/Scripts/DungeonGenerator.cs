using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.InputSystem;
using TMPro;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Grid Settings")]
    public int gridWidth = 80;
    public int gridHeight = 80;

    [Header("BSP Settings")]
    public int minRoomSize = 8;

    // BSP split threshold (controls room COUNT). Deliberately NOT exposed in the
    // Inspector: Generate() overwrites it on every call from DifficultyManager
    // (floor 1 = BASE_MIN_NODE_SIZE = 22), so any authored default would be dead
    // and contradict the effective runtime value. Kept private/runtime-only so
    // the effective value is unambiguous.
    private int minNodeSize;

    [Header("CA Settings")]
    public int caIterations = 3;

    [Header("Tilemaps - assign only what you need")]
    [Tooltip("Leave empty in gameplay builds. Assign for report/debug screenshots of each pipeline stage.")]
    public Tilemap tilemapBSP;
    [Tooltip("Leave empty in gameplay builds. Assign for report/debug screenshots of each pipeline stage.")]
    public Tilemap tilemapRW;
    [Tooltip("Always assign this one — it's the final playable layout that IsWalkable(), EnemyAI, and PlayerController read from.")]
    public Tilemap tilemapCA;
    public TileBase floorTile;
    public TileBase wallTile;
    [Tooltip("Optional, visual-only. Mostly-transparent strip sprite painted on borderOverlayTilemap (NOT ca) over wall cells that touch floor, rotated to face the floor side. ca's wall cells are never replaced. Leave empty to disable.")]
    public TileBase borderTile;
    [Tooltip("Optional overlay Tilemap above ca (below fog), NO collider. Painted with the directional border strip; ca's wall cells underneath are never replaced. Leave empty to disable.")]
    public Tilemap borderOverlayTilemap;

    [Header("UI")]
    public TextMeshProUGUI metricsText;

    // Fired after a floor finishes generating and rendering, so systems like
    // EnemySpawner can react without DungeonGenerator needing to know about them.
    public System.Action OnFloorGenerated;

    private int[,] grid;
    private List<RectInt> rooms = new List<RectInt>();
    private int currentSeed;
    private int currentFloor = 0;
    private RectInt playerSpawnRoom;
    private RectInt exitRoom;
    private int lastGenAttempts;
    private long lastGenTimeMicros;
    private bool lastConnected;
    private int lastRoomCountMin, lastRoomCountMax;
    private int lastConnectivityAttempts;
    private int lastBranchingRejections;

    // Minimum off-critical-path (optional) rooms a layout must have to be accepted.
    // Guarantees every floor has somewhere optional to go — a generation invariant,
    // not an emergent property of the seed.
    private const int MIN_OFF_PATH_ROOMS = 2;

    // The active floor's BSP topology (see LayoutProfile), chosen once per floor and
    // read by SplitNode on every attempt so the "kind" of floor is stable while the
    // seed varies. lastLayoutProfile is the same value, surfaced for the CSV harness.
    private LayoutProfile currentProfile;
    private LayoutProfile lastLayoutProfile;

    // How many of a node's top splits Rows/Columns force in their signature direction
    // before deeper splits revert to aspect-biased random. Forcing only the top levels
    // keeps the banded/striped SHAPE while letting bands subdivide, so even Rows still
    // produces off-path rooms and clears the branching gate.
    private const int FORCED_SPLIT_DEPTH = 2;

    // Room adjacency graph derived from the finished grid (see RoomGraph), rebuilt
    // each Generate() after spawn/exit are known. Rooms are referenced by their index
    // in the rooms list throughout — this stores no geometry of its own.
    private RoomGraph roomGraph;
    private int spawnRoomIndex = -1;
    private int exitRoomIndex = -1;
    private int keyRoomIndex = -1;
    private int lastRoomsOnCriticalPath;
    private int lastRoomsOffPath;
    private int lastKeyRoomDistance = -1;

    // --- Public accessors for EnemyAI / Pathfinding / EnemySpawner ---
    public int[,] Grid => grid;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public List<RectInt> Rooms => rooms;
    public int CurrentFloor => currentFloor;

    // The seed of the layout currently on screen. Read by DecorationLayer so decoration
    // reproduces with the layout; must be sampled after Generate() has run.
    public int CurrentSeed => currentSeed;

    // The room the player spawns in — always the smallest room on the floor, treated
    // as a safe starting room. Computed once per Generate() call so PlayerController
    // and EnemySpawner both read the exact same room without needing to coordinate
    // with each other (they may subscribe to OnFloorGenerated in either order).
    public RectInt PlayerSpawnRoom => playerSpawnRoom;

    // The floor's goal room — the room with the greatest actual walking distance
    // (BFS over the walkable grid, not straight-line) from the player's spawn room.
    // Reaching it advances to the next floor.
    public RectInt ExitRoom => exitRoom;

    // Diagnostics from the most recent Generate() call — used by DifficultyTestHarness
    // to verify each floor generated cleanly (didn't hit the retry ceiling) rather
    // than needing to parse the human-readable log file.
    public int LastGenAttempts => lastGenAttempts;
    public long LastGenTimeMicros => lastGenTimeMicros;
    public bool LastConnected => lastConnected;
    public int LastRoomCountMin => lastRoomCountMin;
    public int LastRoomCountMax => lastRoomCountMax;

    // Number of full-pipeline outer attempts the last Generate() call needed to
    // produce a fully-connected floor (1 = connected on the first try, i.e. zero
    // retries). Equals the ceiling if it gave up; surfaced so DifficultyTestHarness
    // can log it. NOTE: this is an ATTEMPT count, not a retry count.
    public int LastConnectivityAttempts => lastConnectivityAttempts;

    // How many otherwise-connected layouts the last Generate() call threw away for
    // having fewer than MIN_OFF_PATH_ROOMS off-critical-path rooms (i.e. no optional
    // space to explore). Logged separately from LastConnectivityAttempts so the two
    // rejection reasons stay distinguishable in the CSV. 0 = accepted on branching
    // the first time it was connected.
    public int LastBranchingRejections => lastBranchingRejections;

    // Room adjacency graph for the current floor, and the room indices key systems
    // read from it. Indices are into Rooms; -1 means "not determined this floor".
    public RoomGraph RoomGraph => roomGraph;
    public int PlayerSpawnRoomIndex => spawnRoomIndex;
    public int ExitRoomIndex => exitRoomIndex;

    // The room the floor's key spawns in — chosen off the critical path, deepest from
    // spawn, biased toward dead ends. -1 if no valid key room exists (degenerate floor).
    public int KeyRoomIndex => keyRoomIndex;

    // Graph metrics for the last floor, surfaced for DifficultyTestHarness.
    public int LastRoomsOnCriticalPath => lastRoomsOnCriticalPath;
    public int LastRoomsOffPath => lastRoomsOffPath;
    public int LastKeyRoomDistance => lastKeyRoomDistance;

    // The BSP topology chosen for the last floor, surfaced for DifficultyTestHarness.
    public LayoutProfile LastLayoutProfile => lastLayoutProfile;

    // Re-runs the same generation Space already triggers, but callable from other
    // scripts (e.g. PlayerController when the player reaches ExitRoom).
    public void AdvanceToNextFloor()
    {
        Generate();
    }

    // Testing-only convenience — jumps straight to a specific floor number with a
    // freshly generated layout at that difficulty, rather than requiring the player
    // to actually walk to an exit each time. Floors aren't cached/stored, so this
    // is a NEW random layout at the target floor's difficulty, not a return to
    // whatever layout you previously saw at that floor number.
    public void JumpToFloor(int targetFloor)
    {
        currentFloor = Mathf.Max(0, targetFloor - 1);
        Generate();
    }

    public bool IsWalkable(int x, int y)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
            return false;
        return grid[x, y] == 0;
    }
    // ----------------------------------------------------

    private struct GenerationLog
    {
        public int run;
        public int seed;
        public int roomCount;
        public int floorTiles;
        public long generationTime;
        public bool allConnected;
    }

    private List<GenerationLog> logs = new List<GenerationLog>();
    private int runCounter = 0;
#if !UNITY_WEBGL
    private string logPath;
#endif

    void Start()
    {
#if !UNITY_WEBGL
        logPath = Path.Combine(Application.dataPath, "generation_log.txt");
#endif
        Generate();
    }

    void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            Generate();
    }

    void Generate()
    {
        // Difficulty scaling: pull this floor's parameters before generating anything,
        // so BSP/CA use the right minRoomSize/caIterations for the floor about to be built.
        int nextFloor = currentFloor + 1;
        DifficultyParams diff = DifficultyManager.GetDifficultyParams(nextFloor);
        minRoomSize = diff.minRoomSize;
        caIterations = diff.caIterations;
        minNodeSize = diff.minNodeSize;

        // Room count target widens on deeper floors too — otherwise every floor,
        // no matter how far down, would still be forced into the original fixed
        // 6-10 range even once minNodeSize allows more rooms to actually form.
        // UNVERIFIED beyond floor 1 — watch the Console for "gave up after N
        // attempts" warnings, which would mean a floor's target range doesn't
        // reliably form within the 100-attempt budget below.
        // Room count target — ramp made faster still, and final cap raised from
        // 16 to 18. Re-testing the previous fix found floors 10-12 STILL hitting
        // the retry ceiling: minNodeSize drops to 16 exactly at floor 10, and its
        // natural output (16-18 rooms, confirmed in test data) already exceeded
        // the target max (14-15) at that point, since the target didn't reach its
        // own cap until floor 13. This version reaches its cap by floor ~9,
        // comfortably ahead of every minNodeSize step-down, with headroom to 18
        // to absorb occasional high-outlier seeds rather than needing an exact
        // ceiling match.
        int roomCountMin = Mathf.Min(16, 5 + (nextFloor - 1) / 3); // base 5, resynced with BASE_MIN_NODE_SIZE 24 (floor 1 yields ~7 rooms, clears gate)
        int roomCountMax = Mathf.Min(18, 10 + (nextFloor - 1));
        roomCountMin = Mathf.Min(roomCountMin, roomCountMax); // safety: min can never exceed max
        lastRoomCountMin = roomCountMin;
        lastRoomCountMax = roomCountMax;

        // Pick this floor's BSP topology once, before the attempt loop, so every retry
        // regenerates the SAME kind of floor with a fresh seed (the profile is the
        // floor's identity; the seed is the variation within it). Deterministic in the
        // floor number, so a given floor is reliably the same kind across harness runs.
        currentProfile = ChooseLayoutProfile(nextFloor);
        lastLayoutProfile = currentProfile;

        // Outer attempt loop: the ENTIRE pipeline (room-count loop -> corridors ->
        // CA -> connectivity check) is re-run from scratch until CheckConnectivity()
        // passes, or we hit the ceiling. This deliberately wraps the whole pipeline
        // instead of folding the stopwatch / ConnectAllRooms / ApplyCellularAutomata
        // into the inner room-count do/while — a previous version that did so caused
        // an infinite hang and a lost BSP root reference.
        const int maxOuterAttempts = 25;
        int outerAttempts = 0;
        int innerAttempts = 0;
        int branchingRejections = 0;
        bool connected = false;
        bool branchingOk = false;
        BSPNode root = null;
        int[,] gridBSP = null, gridRW = null, gridCA = null;

        Stopwatch sw = Stopwatch.StartNew();

        do
        {
            outerAttempts++;

            // Inner room-count loop: nothing but layout generation lives in here.
            innerAttempts = 0;
            root = null;
            do
            {
                currentSeed = Random.Range(0, 999999);
                Random.InitState(currentSeed);

                grid = new int[gridWidth, gridHeight];
                for (int x = 0; x < gridWidth; x++)
                    for (int y = 0; y < gridHeight; y++)
                        grid[x, y] = 1;

                rooms = new List<RectInt>();

                root = new BSPNode(0, 0, gridWidth, gridHeight);
                SplitNode(root);
                CarveRooms(root);

                innerAttempts++;
                if (innerAttempts > 100)
                {
                    UnityEngine.Debug.LogWarning($"Gave up after {innerAttempts} attempts, final room count: {rooms.Count} " +
                        $"(target was {roomCountMin}-{roomCountMax}) | nextFloor={nextFloor} minNodeSize={minNodeSize} minRoomSize={minRoomSize} caIterations={caIterations}");
                    break;
                }
            }
            while (rooms.Count < roomCountMin || rooms.Count > roomCountMax);

            // stage 1 - BSP only
            gridBSP = CopyGrid(grid);

            // stage 2 - random walk corridors on top of BSP
            ConnectAllRooms(root);
            gridRW = CopyGrid(grid);

            // stage 3 - CA smoothing on top of RW
            ApplyCellularAutomata(caIterations);
            gridCA = CopyGrid(grid);

            // Gate the floor on full connectivity. If it fails we throw the whole
            // layout away and start a fresh outer attempt rather than shipping a
            // floor with unreachable rooms.
            connected = CheckConnectivity();

            // Second acceptance gate: reject an otherwise-connected layout that has
            // no optional space to explore. A straight chain of rooms (every room on
            // the critical path) makes the key-placement fallback fire and puts the
            // key directly on the route to the exit — which defeats the exploration
            // premise on exactly the low floors seed luck tends to produce it. Same
            // ceiling / ship-anyway behaviour as the connectivity gate: never hang.
            branchingOk = false;
            if (connected)
            {
                branchingOk = CountOffPathRooms() >= MIN_OFF_PATH_ROOMS;
                if (!branchingOk) branchingRejections++;
            }
        }
        while ((!connected || !branchingOk) && outerAttempts < maxOuterAttempts);

        sw.Stop();

        // Never block the game: if we exhausted the ceiling without a connected
        // layout, log loudly and ship the last one anyway.
        if (!connected)
        {
            UnityEngine.Debug.LogWarning($"[DungeonGenerator] CONNECTIVITY FAILED — no fully-connected layout after " +
                $"{maxOuterAttempts} outer attempts on floor {nextFloor} (last seed {currentSeed}). " +
                $"Shipping the last (disconnected) layout rather than hanging the game.");
        }
        else if (!branchingOk)
        {
            UnityEngine.Debug.LogWarning($"[DungeonGenerator] BRANCHING FAILED — no connected layout with at least " +
                $"{MIN_OFF_PATH_ROOMS} off-critical-path rooms after {maxOuterAttempts} outer attempts on floor " +
                $"{nextFloor} (last seed {currentSeed}). Shipping the last (near-linear) layout rather than hanging the game.");
        }

        lastGenAttempts = innerAttempts;
        lastConnectivityAttempts = outerAttempts;
        lastBranchingRejections = branchingRejections;
        lastConnected = connected;
        lastGenTimeMicros = sw.ElapsedTicks * 1000000L / Stopwatch.Frequency;

        // Player always spawns in the smallest room on the floor — computed here, once,
        // so PlayerController and EnemySpawner both read the same room via PlayerSpawnRoom
        // regardless of which order they handle OnFloorGenerated in.
        int smallestIdx = 0;
        for (int i = 1; i < rooms.Count; i++)
            if (rooms[i].width * rooms[i].height < rooms[smallestIdx].width * rooms[smallestIdx].height)
                smallestIdx = i;
        spawnRoomIndex = smallestIdx;
        playerSpawnRoom = rooms[smallestIdx];

        // Render each stage only if its tilemap is assigned in the Inspector.
        // Gameplay builds: assign only tilemapCA, leave tilemapBSP/tilemapRW empty.
        // Report/debug screenshots: assign all 3 to see each pipeline stage.
        Tilemap[] tilemaps = { tilemapBSP, tilemapRW, tilemapCA };
        int[][,] grids = { gridBSP, gridRW, gridCA };

        for (int i = 0; i < 3; i++)
        {
            if (tilemaps[i] == null) continue;

            tilemaps[i].ClearAllTiles();
            for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                {
                    Vector3Int pos = new Vector3Int(x, y, 0);
                    tilemaps[i].SetTile(pos, grids[i][x, y] == 0 ? floorTile : wallTile);
                }
        }

        // Visual-only directional border overlay on a SEPARATE tilemap above ca. Paints a
        // mostly-transparent strip on each wall cell touching floor, rotated so the strip
        // faces the floor side; ca's wall cells underneath stay plain wallTile — never
        // replaced. Keyed off gridCA (the playable layout), painted once. Reads gridCA
        // only — never writes it — and runs after generation is finalized, so it cannot
        // affect grid, rooms, the room graph, or any metric the harness measures.
        if (borderOverlayTilemap != null)
        {
            borderOverlayTilemap.ClearAllTiles();
            if (borderTile != null)
                for (int x = 0; x < gridWidth; x++)
                    for (int y = 0; y < gridHeight; y++)
                    {
                        if (gridCA[x, y] == 0) continue; // wall cells only
                        float angle;
                        if      (IsFloor(gridCA, x, y + 1)) angle =   0f; // floor above → strip up
                        else if (IsFloor(gridCA, x, y - 1)) angle = 180f; // floor below
                        else if (IsFloor(gridCA, x - 1, y)) angle = -90f; // floor left
                        else if (IsFloor(gridCA, x + 1, y)) angle =  90f; // floor right
                        // Diagonal fallback: fills concave-notch cells that touch floor only
                        // at a corner. No flush answer for a straight strip, so bias to the
                        // vertical orientation matching the orthogonal priority above.
                        else if (IsFloor(gridCA, x - 1, y + 1)) angle =   0f; // floor up-left
                        else if (IsFloor(gridCA, x + 1, y + 1)) angle =   0f; // floor up-right
                        else if (IsFloor(gridCA, x - 1, y - 1)) angle = 180f; // floor down-left
                        else if (IsFloor(gridCA, x + 1, y - 1)) angle = 180f; // floor down-right
                        else continue;                                    // no floor neighbor at all
                        Vector3Int p = new Vector3Int(x, y, 0);
                        borderOverlayTilemap.SetTile(p, borderTile);
                        borderOverlayTilemap.SetTileFlags(p, TileFlags.None); // required for rotation
                        borderOverlayTilemap.SetTransformMatrix(p, Matrix4x4.Rotate(Quaternion.Euler(0, 0, angle)));
                    }
        }

        // metrics
        int floorCount = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                if (grid[x, y] == 0) floorCount++;

        runCounter++;
        currentFloor = nextFloor;

        // Exit room: whichever room is farthest by actual walkable-path distance from
        // the player's spawn room, found via BFS over the final grid (same technique
        // as CheckConnectivity, just tracking distance instead of a visited flag).
        exitRoom = FindFarthestRoom(playerSpawnRoom);
        exitRoomIndex = rooms.IndexOf(exitRoom);

        // Build the room adjacency graph from the FINISHED grid, then choose this
        // floor's key room and record graph metrics. Done before OnFloorGenerated
        // fires so KeyManager/PotionSpawner can read RoomGraph and KeyRoomIndex.
        roomGraph = new RoomGraph(grid, gridWidth, gridHeight, rooms, spawnRoomIndex, exitRoomIndex);
        keyRoomIndex = SelectKeyRoom(out bool keyFallback);
        lastRoomsOnCriticalPath = roomGraph.CriticalPath.Count;
        lastRoomsOffPath = rooms.Count - lastRoomsOnCriticalPath;
        lastKeyRoomDistance = keyRoomIndex >= 0 ? roomGraph.DistanceFromSpawn(keyRoomIndex) : -1;

        if (keyRoomIndex < 0)
            UnityEngine.Debug.LogWarning($"[DungeonGenerator] No valid key room on floor {nextFloor} " +
                $"(rooms={rooms.Count}, offPath={lastRoomsOffPath}); floor ships without a key/lock.");
        else if (keyFallback)
            UnityEngine.Debug.LogWarning($"[DungeonGenerator] Key-room fallback on floor {nextFloor}: no off-path " +
                $"rooms, key placed in deepest non-exit room (index {keyRoomIndex}, dist {lastKeyRoomDistance}).");

        GenerationLog entry = new GenerationLog
        {
            run = runCounter,
            seed = currentSeed,
            roomCount = rooms.Count,
            floorTiles = floorCount,
            generationTime = lastGenTimeMicros,
            allConnected = connected
        };

        if (logs.Count >= 50) logs.RemoveAt(0);
        logs.Add(entry);

        WriteLog();

        if (metricsText != null)
        {
            metricsText.text =
                $"Floor: {currentFloor}\n" +
                $"Seed: {currentSeed}\n" +
                $"Generation Time: {lastGenTimeMicros}μs\n" +
                $"Rooms: {rooms.Count}\n" +
                $"Floor Tiles: {floorCount}\n" +
                $"All Connected: {connected}\n" +
                $"Connectivity Attempts: {outerAttempts}\n" +
                $"Layout Profile: {lastLayoutProfile}\n" +
                $"Off-path Rooms: {lastRoomsOffPath}/{rooms.Count}\n" +
                $"Total Runs Logged: {logs.Count}";
        }

        OnFloorGenerated?.Invoke();
    }

    int[,] CopyGrid(int[,] source)
    {
        int[,] copy = new int[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                copy[x, y] = source[x, y];
        return copy;
    }

    void WriteLog()
    {
#if !UNITY_WEBGL
        using (StreamWriter writer = new StreamWriter(logPath, false))
        {
            writer.WriteLine("--- DUNGEON GENERATION LOG ---");
            writer.WriteLine($"Last {logs.Count} runs (max 50)");
            writer.WriteLine("--------------------------------\n");


            foreach (var log in logs)
            {
                writer.WriteLine(
                    $"Run #{log.run} | Seed: {log.seed} | Rooms: {log.roomCount} | " +
                    $"Floor Tiles: {log.floorTiles} | Time: {log.generationTime}μs | " +
                    $"Connected: {log.allConnected}"
                );
            }

            writer.WriteLine("\n--- SUMMARY ---");

            int minRooms = int.MaxValue, maxRooms = int.MinValue;
            int minTiles = int.MaxValue, maxTiles = int.MinValue;
            long minTime = long.MaxValue, maxTime = long.MinValue;
            float avgRooms = 0, avgTiles = 0, avgTime = 0;
            int connectedCount = 0;

            foreach (var log in logs)
            {
                if (log.roomCount < minRooms) minRooms = log.roomCount;
                if (log.roomCount > maxRooms) maxRooms = log.roomCount;
                if (log.floorTiles < minTiles) minTiles = log.floorTiles;
                if (log.floorTiles > maxTiles) maxTiles = log.floorTiles;
                if (log.generationTime < minTime) minTime = log.generationTime;
                if (log.generationTime > maxTime) maxTime = log.generationTime;
                avgRooms += log.roomCount;
                avgTiles += log.floorTiles;
                avgTime += log.generationTime;
                if (log.allConnected) connectedCount++;
            }

            avgRooms /= logs.Count;
            avgTiles /= logs.Count;
            avgTime /= logs.Count;

            writer.WriteLine($"Rooms       — Min: {minRooms} | Max: {maxRooms} | Avg: {avgRooms:F1}");
            writer.WriteLine($"Floor Tiles — Min: {minTiles} | Max: {maxTiles} | Avg: {avgTiles:F1}");
            writer.WriteLine($"Time (μs)   — Min: {minTime} | Max: {maxTime} | Avg: {avgTime:F1}");
            writer.WriteLine($"Connectivity — {connectedCount}/{logs.Count} runs fully connected ({(float)connectedCount / logs.Count * 100:F1}%)");
        }
#endif
    }

    // Deterministic per-floor topology pick, tiered to introduce layout asymmetry
    // progressively rather than uniformly from floor 1 (aligned with the difficulty
    // curve). Rows/Columns force the top splits one way, which on a small floor can
    // collapse toward a near-linear chain and make the branching gate thrash — so the
    // banded profiles are held back to later, larger floors. Bands keep more than one
    // profile so no long run of floors shares an identical layout kind:
    //   floors 1-15  : {Quad, Organic}                 (early, safe — no forced banding)
    //   floors 16-24 : {Quad, Organic, Rows}           (asymmetry ramps in near the curve)
    //   floors 25+   : {Quad, Organic, Rows, Columns}  (full set past the inflection point)
    // The floor*31+7 hash keeps selection within each band deterministic per floor.
    LayoutProfile ChooseLayoutProfile(int floor)
    {
        LayoutProfile[] band;
        if (floor <= 15)
            band = new[] { LayoutProfile.Quad, LayoutProfile.Organic };
        else if (floor <= 24)
            band = new[] { LayoutProfile.Quad, LayoutProfile.Organic, LayoutProfile.Rows };
        else
            band = new[] { LayoutProfile.Quad, LayoutProfile.Organic, LayoutProfile.Rows, LayoutProfile.Columns };

        return band[Mathf.Abs(floor * 31 + 7) % band.Length];
    }

    void SplitNode(BSPNode node)
    {
        if (node.width < minNodeSize * 2 && node.height < minNodeSize * 2)
            return;

        bool splitHorizontal = DecideSplitHorizontal(node);

        // Split ratio widened 0.4-0.6 -> 0.3-0.7 so children (and therefore rooms) are
        // visibly uneven in size rather than near-halved every time.
        if (splitHorizontal)
        {
            int split = Random.Range((int)(node.height * 0.3f), (int)(node.height * 0.7f));
            node.left = new BSPNode(node.x, node.y, node.width, split) { depth = node.depth + 1 };
            node.right = new BSPNode(node.x, node.y + split, node.width, node.height - split) { depth = node.depth + 1 };
        }
        else
        {
            int split = Random.Range((int)(node.width * 0.3f), (int)(node.width * 0.7f));
            node.left = new BSPNode(node.x, node.y, split, node.height) { depth = node.depth + 1 };
            node.right = new BSPNode(node.x + split, node.y, node.width - split, node.height) { depth = node.depth + 1 };
        }

        SplitNode(node.left);
        SplitNode(node.right);
    }

    // Chooses split direction (true = horizontal) for the active profile. A node is only
    // split in a direction whose dimension is large enough to yield two carveable children;
    // if the profile's preferred direction isn't viable, the only viable one is used so a
    // forced profile never produces slivers. The SplitNode entry guard guarantees at least
    // one direction is viable here.
    bool DecideSplitHorizontal(BSPNode node)
    {
        bool canHorizontal = node.height >= minNodeSize * 2;
        bool canVertical = node.width >= minNodeSize * 2;
        if (canHorizontal && !canVertical) return true;
        if (canVertical && !canHorizontal) return false;

        switch (currentProfile)
        {
            case LayoutProfile.Rows:
                // Stacked bands: force the top splits horizontal, then let bands subdivide.
                return node.depth < FORCED_SPLIT_DEPTH ? true : AspectBiasedHorizontal(node);
            case LayoutProfile.Columns:
                // Tall strips: force the top splits vertical, then let strips subdivide.
                return node.depth < FORCED_SPLIT_DEPTH ? false : AspectBiasedHorizontal(node);
            case LayoutProfile.Quad:
                // Strict alternation by depth -> grid-like cells.
                return (node.depth % 2) == 0;
            default: // Organic
                return AspectBiasedHorizontal(node);
        }
    }

    // Original behaviour: random direction with a 1.5x aspect bias so very elongated
    // nodes split across their long axis.
    bool AspectBiasedHorizontal(BSPNode node)
    {
        bool splitHorizontal = Random.value > 0.5f;
        if (node.width > node.height * 1.5f)
            splitHorizontal = false;
        else if (node.height > node.width * 1.5f)
            splitHorizontal = true;
        return splitHorizontal;
    }

    void CarveRooms(BSPNode node)
    {
        if (node.left == null && node.right == null)
        {
            // Each room independently fills 55-100% of its leaf on each axis, so rooms are
            // visibly asymmetric rather than near-uniform. Clamped to >= minRoomSize and to
            // the leaf interior; Unity's int Random.Range returns min when min >= max, so a
            // degenerate tiny leaf can't throw.
            int maxW = Mathf.Max(minRoomSize, node.width - 2);
            int maxH = Mathf.Max(minRoomSize, node.height - 2);
            int roomW = Mathf.Clamp((int)(node.width * Random.Range(0.55f, 1.0f)), minRoomSize, maxW);
            int roomH = Mathf.Clamp((int)(node.height * Random.Range(0.55f, 1.0f)), minRoomSize, maxH);
            int roomX = node.x + Random.Range(1, node.width - roomW - 1);
            int roomY = node.y + Random.Range(1, node.height - roomH - 1);

            roomX = Mathf.Clamp(roomX, 1, gridWidth - roomW - 1);
            roomY = Mathf.Clamp(roomY, 1, gridHeight - roomH - 1);

            node.room = new RectInt(roomX, roomY, roomW, roomH);
            rooms.Add(node.room);

            for (int x = roomX; x < roomX + roomW; x++)
                for (int y = roomY; y < roomY + roomH; y++)
                    if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                        grid[x, y] = 0;
        }
        else
        {
            if (node.left != null)
                CarveRooms(node.left);
            if (node.right != null)
                CarveRooms(node.right);
        }
    }

    void ConnectAllRooms(BSPNode node)
    {
        if (node.left == null || node.right == null)
            return;

        RectInt roomA = GetRoom(node.left);
        RectInt roomB = GetRoom(node.right);

        Vector2Int pointA = new Vector2Int(
            roomA.x + roomA.width / 2,
            roomA.y + roomA.height / 2
        );
        Vector2Int pointB = new Vector2Int(
            roomB.x + roomB.width / 2,
            roomB.y + roomB.height / 2
        );

        Vector2Int current = pointA;

        while (current != pointB)
        {
            for (int i = 0; i <= 1; i++)
            {
                int cx = Mathf.Clamp(current.x + i, 0, gridWidth - 1);
                int cy = Mathf.Clamp(current.y + i, 0, gridHeight - 1);
                grid[current.x, cy] = 0;
                grid[cx, current.y] = 0;
            }

            int dx = pointB.x - current.x;
            int dy = pointB.y - current.y;

            if (dx == 0)
            {
                if (dy > 0) current.y += 1;
                else current.y -= 1;
            }
            else if (dy == 0)
            {
                if (dx > 0) current.x += 1;
                else current.x -= 1;
            }
            else
            {
                float bias = 0.7f;
                if (Random.value < bias)
                {
                    if (Random.value < 0.5f)
                    {
                        if (dx > 0) current.x += 1;
                        else current.x -= 1;
                    }
                    else
                    {
                        if (dy > 0) current.y += 1;
                        else current.y -= 1;
                    }
                }
                else
                {
                    if (Random.value < 0.5f)
                    {
                        if (Random.value < 0.5f) current.x += 1;
                        else current.x -= 1;
                    }
                    else
                    {
                        if (Random.value < 0.5f) current.y += 1;
                        else current.y -= 1;
                    }
                }

                current.x = Mathf.Clamp(current.x, 0, gridWidth - 1);
                current.y = Mathf.Clamp(current.y, 0, gridHeight - 1);
            }
        }

        ConnectAllRooms(node.left);
        ConnectAllRooms(node.right);
    }

    // True only if (x,y) is a real floor cell (grid value 0) inside the grid. Off-grid
    // returns false so wall cells facing outside the map into nothing are not bordered.
    bool IsFloor(int[,] g, int x, int y)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) return false;
        return g[x, y] == 0;
    }

    void ApplyCellularAutomata(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            int[,] newGrid = new int[gridWidth, gridHeight];

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    int wallCount = 0;

                    for (int nx = x - 1; nx <= x + 1; nx++)
                        for (int ny = y - 1; ny <= y + 1; ny++)
                        {
                            if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                                wallCount++;
                            else if (grid[nx, ny] == 1)
                                wallCount++;
                        }

                    if (wallCount >= 5)
                        newGrid[x, y] = 1;
                    else
                        newGrid[x, y] = 0;
                }
            }

            grid = newGrid;
        }
    }

    RectInt GetRoom(BSPNode node)
    {
        if (node.left == null && node.right == null)
            return node.room;
        if (node.left != null)
            return GetRoom(node.left);
        return GetRoom(node.right);
    }

    bool CheckConnectivity()
    {
        Vector2Int start = new Vector2Int(-1, -1);
        for (int x = 0; x < gridWidth && start.x == -1; x++)
            for (int y = 0; y < gridHeight && start.x == -1; y++)
                if (grid[x, y] == 0)
                    start = new Vector2Int(x, y);

        if (start.x == -1) return false;

        bool[,] visited = new bool[gridWidth, gridHeight];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        visited[start.x, start.y] = true;

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = curr.x + dx[i];
                int ny = curr.y + dy[i];
                if (nx >= 0 && nx < gridWidth && ny >= 0 && ny < gridHeight
                    && !visited[nx, ny] && grid[nx, ny] == 0)
                {
                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                if (grid[x, y] == 0 && !visited[x, y])
                    return false;

        return true;
    }

    // BFS distance flood-fill from the centre of startRoom, then returns whichever
    // room's centre has the largest distance. Same traversal pattern as
    // CheckConnectivity, but records a distance per tile instead of just visited/not.
    // Off-path (optional) room count for the CURRENT grid/rooms, used as the branching
    // acceptance gate inside the generation loop. Deliberately mirrors the post-loop
    // spawn/exit/graph computation exactly — smallest room = spawn, farthest = exit —
    // so the gate's decision and the shipped RoomsOffPath metric can never disagree.
    int CountOffPathRooms()
    {
        if (rooms.Count == 0) return 0;

        int smallest = 0;
        for (int i = 1; i < rooms.Count; i++)
            if (rooms[i].width * rooms[i].height < rooms[smallest].width * rooms[smallest].height)
                smallest = i;

        RectInt exit = FindFarthestRoom(rooms[smallest]);
        int exitIdx = rooms.IndexOf(exit);
        RoomGraph g = new RoomGraph(grid, gridWidth, gridHeight, rooms, smallest, exitIdx);
        return rooms.Count - g.CriticalPath.Count;
    }

    RectInt FindFarthestRoom(RectInt startRoom)
    {
        Vector2Int start = new Vector2Int(
            Mathf.Clamp(startRoom.x + startRoom.width / 2, 0, gridWidth - 1),
            Mathf.Clamp(startRoom.y + startRoom.height / 2, 0, gridHeight - 1)
        );

        int[,] dist = new int[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                dist[x, y] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        dist[start.x, start.y] = 0;
        queue.Enqueue(start);

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = curr.x + dx[i];
                int ny = curr.y + dy[i];
                if (nx >= 0 && nx < gridWidth && ny >= 0 && ny < gridHeight
                    && grid[nx, ny] == 0 && dist[nx, ny] == -1)
                {
                    dist[nx, ny] = dist[curr.x, curr.y] + 1;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        RectInt farthest = startRoom;
        int farthestDist = -1;
        foreach (var room in rooms)
        {
            if (room.x == startRoom.x && room.y == startRoom.y
                && room.width == startRoom.width && room.height == startRoom.height)
                continue; // skip the spawn room itself

            int cx = Mathf.Clamp(room.x + room.width / 2, 0, gridWidth - 1);
            int cy = Mathf.Clamp(room.y + room.height / 2, 0, gridHeight - 1);
            int d = dist[cx, cy];
            if (d > farthestDist)
            {
                farthestDist = d;
                farthest = room;
            }
        }

        return farthest;
    }

    // Picks the floor's key room from the room graph: among rooms NOT on the critical
    // path, the one farthest from spawn, tie-broken toward dead ends (lowest degree).
    // If every room is on the critical path, falls back to the deepest room that is
    // neither spawn nor exit and reports it via fallbackUsed. Returns -1 only when no
    // room qualifies at all (e.g. a floor of just the spawn and exit rooms).
    int SelectKeyRoom(out bool fallbackUsed)
    {
        fallbackUsed = false;
        if (roomGraph == null) return -1;

        int best = -1, bestDist = -1, bestDegree = int.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (roomGraph.IsOnCriticalPath(i)) continue;
            int d = roomGraph.DistanceFromSpawn(i);
            if (d < 0) continue; // unreachable — shouldn't happen once connectivity is enforced
            int deg = roomGraph.Degree(i);
            if (d > bestDist || (d == bestDist && deg < bestDegree))
            {
                best = i; bestDist = d; bestDegree = deg;
            }
        }
        if (best != -1) return best;

        // Fallback: no off-path rooms exist. Deepest room that isn't spawn or exit.
        fallbackUsed = true;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (i == spawnRoomIndex || i == exitRoomIndex) continue;
            int d = roomGraph.DistanceFromSpawn(i);
            if (d < 0) continue;
            if (d > bestDist) { best = i; bestDist = d; }
        }
        return best;
    }
}

public class BSPNode
{
    public int x, y, width, height;
    public int depth;              // split depth from the root (root = 0); drives per-profile split direction
    public BSPNode left, right;
    public RectInt room;

    public BSPNode(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }
}

// Per-floor BSP topology. Chosen once per floor (see ChooseLayoutProfile) so floors
// differ in KIND, not just in seed arrangement. Rows/Columns force the top splits one
// way to produce banded/striped layouts; Quad alternates for a grid; Organic is the
// original aspect-biased random.
public enum LayoutProfile { Rows, Columns, Quad, Organic }