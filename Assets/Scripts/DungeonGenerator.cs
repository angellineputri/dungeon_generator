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
    public int minNodeSize = 28;
    public int minRoomSize = 8;

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

    // --- Public accessors for EnemyAI / Pathfinding / EnemySpawner ---
    public int[,] Grid => grid;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public List<RectInt> Rooms => rooms;
    public int CurrentFloor => currentFloor;

    // The room the player spawns in — always the smallest room on the floor, treated
    // as a safe starting room. Computed once per Generate() call so PlayerController
    // and EnemySpawner both read the exact same room without needing to coordinate
    // with each other (they may subscribe to OnFloorGenerated in either order).
    public RectInt PlayerSpawnRoom => playerSpawnRoom;

    // The floor's goal room — the room with the greatest actual walking distance
    // (BFS over the walkable grid, not straight-line) from the player's spawn room.
    // Reaching it advances to the next floor.
    public RectInt ExitRoom => exitRoom;

    // Re-runs the same generation Space already triggers, but callable from other
    // scripts (e.g. PlayerController when the player reaches ExitRoom).
    public void AdvanceToNextFloor()
    {
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
    private string logPath;

    void Start()
    {
        logPath = Path.Combine(Application.dataPath, "generation_log.txt");
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

        int attempts = 0;
        BSPNode root = null;

        Stopwatch sw = Stopwatch.StartNew();

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

            attempts++;
            if (attempts > 100)
            {
                UnityEngine.Debug.LogWarning($"Gave up after {attempts} attempts, final room count: {rooms.Count}");
                break;
            }
        }
        while (rooms.Count < 6 || rooms.Count > 10);

        // Player always spawns in the smallest room on the floor — computed here, once,
        // so PlayerController and EnemySpawner both read the same room via PlayerSpawnRoom
        // regardless of which order they handle OnFloorGenerated in.
        RectInt smallest = rooms[0];
        foreach (var r in rooms)
            if (r.width * r.height < smallest.width * smallest.height)
                smallest = r;
        playerSpawnRoom = smallest;

        // stage 1 - BSP only
        int[,] gridBSP = CopyGrid(grid);

        // stage 2 - random walk corridors on top of BSP
        ConnectAllRooms(root);
        int[,] gridRW = CopyGrid(grid);

        // stage 3 - CA smoothing on top of RW
        ApplyCellularAutomata(caIterations);
        int[,] gridCA = CopyGrid(grid);

        sw.Stop();

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

        // metrics
        int floorCount = 0;
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                if (grid[x, y] == 0) floorCount++;

        bool connected = CheckConnectivity();
        runCounter++;
        currentFloor = nextFloor;

        // Exit room: whichever room is farthest by actual walkable-path distance from
        // the player's spawn room, found via BFS over the final grid (same technique
        // as CheckConnectivity, just tracking distance instead of a visited flag).
        exitRoom = FindFarthestRoom(playerSpawnRoom);

        GenerationLog entry = new GenerationLog
        {
            run = runCounter,
            seed = currentSeed,
            roomCount = rooms.Count,
            floorTiles = floorCount,
            generationTime = sw.ElapsedTicks * 1000000L / Stopwatch.Frequency,
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
                $"Generation Time: {sw.ElapsedTicks * 1000000L / Stopwatch.Frequency}μs\n" +
                $"Rooms: {rooms.Count}\n" +
                $"Floor Tiles: {floorCount}\n" +
                $"All Connected: {connected}\n" +
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
    }

    void SplitNode(BSPNode node)
    {
        if (node.width < minNodeSize * 2 && node.height < minNodeSize * 2)
            return;

        bool splitHorizontal = Random.value > 0.5f;
        if (node.width > node.height * 1.5f)
            splitHorizontal = false;
        else if (node.height > node.width * 1.5f)
            splitHorizontal = true;

        if (splitHorizontal)
        {
            int split = Random.Range((int)(node.height * 0.4f), (int)(node.height * 0.6f));
            node.left = new BSPNode(node.x, node.y, node.width, split);
            node.right = new BSPNode(node.x, node.y + split, node.width, node.height - split);
        }
        else
        {
            int split = Random.Range((int)(node.width * 0.4f), (int)(node.width * 0.6f));
            node.left = new BSPNode(node.x, node.y, split, node.height);
            node.right = new BSPNode(node.x + split, node.y, node.width - split, node.height);
        }

        SplitNode(node.left);
        SplitNode(node.right);
    }

    void CarveRooms(BSPNode node)
    {
        if (node.left == null && node.right == null)
        {
            int roomW = Random.Range(minRoomSize, node.width - 2);
            int roomH = Random.Range(minRoomSize, node.height - 2);
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
}

public class BSPNode
{
    public int x, y, width, height;
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