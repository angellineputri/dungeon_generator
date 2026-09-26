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

    private int minNodeSize;

    [Header("CA Settings")]
    public int caIterations = 3;

    [Header("Dev")]
    [Tooltip("When on, SPACE regenerates the current floor. Leave OFF for tester/playtest builds so a stray Space press can't scramble a run.")]
    public bool devHotkeys = false;

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

    private const int MIN_OFF_PATH_ROOMS = 2;

    private LayoutProfile currentProfile;
    private LayoutProfile lastLayoutProfile;

    private const int FORCED_SPLIT_DEPTH = 2;

    private RoomGraph roomGraph;
    private int spawnRoomIndex = -1;
    private int exitRoomIndex = -1;
    private int keyRoomIndex = -1;
    private int lastRoomsOnCriticalPath;
    private int lastRoomsOffPath;
    private int lastKeyRoomDistance = -1;

    public int[,] Grid => grid;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public List<RectInt> Rooms => rooms;
    public int CurrentFloor => currentFloor;

    public int CurrentSeed => currentSeed;

    public RectInt PlayerSpawnRoom => playerSpawnRoom;

    public RectInt ExitRoom => exitRoom;

    public int LastGenAttempts => lastGenAttempts;
    public long LastGenTimeMicros => lastGenTimeMicros;
    public bool LastConnected => lastConnected;
    public int LastRoomCountMin => lastRoomCountMin;
    public int LastRoomCountMax => lastRoomCountMax;

    public int LastConnectivityAttempts => lastConnectivityAttempts;

    public int LastBranchingRejections => lastBranchingRejections;

    public RoomGraph RoomGraph => roomGraph;
    public int PlayerSpawnRoomIndex => spawnRoomIndex;
    public int ExitRoomIndex => exitRoomIndex;

    public int KeyRoomIndex => keyRoomIndex;

    public int LastRoomsOnCriticalPath => lastRoomsOnCriticalPath;
    public int LastRoomsOffPath => lastRoomsOffPath;
    public int LastKeyRoomDistance => lastKeyRoomDistance;

    public LayoutProfile LastLayoutProfile => lastLayoutProfile;

    public void AdvanceToNextFloor()
    {
        Generate();
    }

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
        if (devHotkeys && Keyboard.current.spaceKey.wasPressedThisFrame)
            Generate();
    }

    void Generate()
    {

        int nextFloor = currentFloor + 1;
        DifficultyParams diff = DifficultyManager.GetDifficultyParams(nextFloor);
        minRoomSize = diff.minRoomSize;
        caIterations = diff.caIterations;
        minNodeSize = diff.minNodeSize;

        int roomCountMin = Mathf.Min(16, 5 + (nextFloor - 1) / 3);
        int roomCountMax = Mathf.Min(18, 10 + (nextFloor - 1));
        roomCountMin = Mathf.Min(roomCountMin, roomCountMax);
        lastRoomCountMin = roomCountMin;
        lastRoomCountMax = roomCountMax;

        currentProfile = ChooseLayoutProfile(nextFloor);
        lastLayoutProfile = currentProfile;

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

            gridBSP = CopyGrid(grid);

            ConnectAllRooms(root);
            gridRW = CopyGrid(grid);

            ApplyCellularAutomata(caIterations);
            gridCA = CopyGrid(grid);

            connected = CheckConnectivity();

            branchingOk = false;
            if (connected)
            {
                branchingOk = CountOffPathRooms() >= MIN_OFF_PATH_ROOMS;
                if (!branchingOk) branchingRejections++;
            }
        }
        while ((!connected || !branchingOk) && outerAttempts < maxOuterAttempts);

        sw.Stop();

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

        int smallestIdx = 0;
        for (int i = 1; i < rooms.Count; i++)
            if (rooms[i].width * rooms[i].height < rooms[smallestIdx].width * rooms[smallestIdx].height)
                smallestIdx = i;
        spawnRoomIndex = smallestIdx;
        playerSpawnRoom = rooms[smallestIdx];

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

        if (borderOverlayTilemap != null)
        {
            borderOverlayTilemap.ClearAllTiles();
            if (borderTile != null)
                for (int x = 0; x < gridWidth; x++)
                    for (int y = 0; y < gridHeight; y++)
                    {
                        if (gridCA[x, y] == 0) continue;
                        float angle;
                        if      (IsFloor(gridCA, x, y + 1)) angle =   0f;
                        else if (IsFloor(gridCA, x, y - 1)) angle = 180f;
                        else if (IsFloor(gridCA, x - 1, y)) angle = -90f;
                        else if (IsFloor(gridCA, x + 1, y)) angle =  90f;

                        else if (IsFloor(gridCA, x - 1, y + 1)) angle =   0f;
                        else if (IsFloor(gridCA, x + 1, y + 1)) angle =   0f;
                        else if (IsFloor(gridCA, x - 1, y - 1)) angle = 180f;
                        else if (IsFloor(gridCA, x + 1, y - 1)) angle = 180f;
                        else continue;
                        Vector3Int p = new Vector3Int(x, y, 0);
                        borderOverlayTilemap.SetTile(p, borderTile);
                        borderOverlayTilemap.SetTileFlags(p, TileFlags.None);
                        borderOverlayTilemap.SetTransformMatrix(p, Matrix4x4.Rotate(Quaternion.Euler(0, 0, angle)));
                    }
        }

        int floorCount = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                if (grid[x, y] == 0) floorCount++;

        runCounter++;
        currentFloor = nextFloor;

        exitRoom = FindFarthestRoom(playerSpawnRoom);
        exitRoomIndex = rooms.IndexOf(exitRoom);

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

    bool DecideSplitHorizontal(BSPNode node)
    {
        bool canHorizontal = node.height >= minNodeSize * 2;
        bool canVertical = node.width >= minNodeSize * 2;
        if (canHorizontal && !canVertical) return true;
        if (canVertical && !canHorizontal) return false;

        switch (currentProfile)
        {
            case LayoutProfile.Rows:

                return node.depth < FORCED_SPLIT_DEPTH ? true : AspectBiasedHorizontal(node);
            case LayoutProfile.Columns:

                return node.depth < FORCED_SPLIT_DEPTH ? false : AspectBiasedHorizontal(node);
            case LayoutProfile.Quad:

                return (node.depth % 2) == 0;
            default:
                return AspectBiasedHorizontal(node);
        }
    }

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
                continue;

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

    int SelectKeyRoom(out bool fallbackUsed)
    {
        fallbackUsed = false;
        if (roomGraph == null) return -1;

        int best = -1, bestDist = -1, bestDegree = int.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (roomGraph.IsOnCriticalPath(i)) continue;
            int d = roomGraph.DistanceFromSpawn(i);
            if (d < 0) continue;
            int deg = roomGraph.Degree(i);
            if (d > bestDist || (d == bestDist && deg < bestDegree))
            {
                best = i; bestDist = d; bestDegree = deg;
            }
        }
        if (best != -1) return best;

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
    public int depth;
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

public enum LayoutProfile { Rows, Columns, Quad, Organic }
