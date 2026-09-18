using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Room adjacency derived from the FINISHED tile grid (post-BSP, post-corridors,
/// post-CA), NOT from the BSP tree. The BSP tree is thrown away by the time this
/// runs — corridors and CA smoothing can merge or pinch rooms in ways the tree no
/// longer describes, so adjacency is measured on the tiles that actually exist.
///
/// Rooms keep the identity they have everywhere else in the project: their index in
/// DungeonGenerator.Rooms. This class stores no room geometry and does not touch the
/// List&lt;RectInt&gt; — it only produces graph facts keyed by that index.
///
/// Pipeline:
///   1. Region labelling by multi-source BFS. Every walkable tile inside a room is
///      seeded with that room's index (all seeds at distance 0), then the frontier
///      expands over walkable tiles only. Because it's a single FIFO BFS from all
///      seeds at once, each tile ends up owned by its NEAREST room, and a corridor
///      running between two rooms is split down the middle where the frontiers meet.
///   2. Adjacency: rooms i and j are neighbours iff some tile labelled i is
///      4-adjacent to some tile labelled j.
///   3. Graph BFS from the spawn room gives DistanceFromSpawn; the parent pointers
///      reconstruct the critical path (spawn -> exit); degree is neighbour count.
/// </summary>
public class RoomGraph
{
    private readonly int roomCount;
    private readonly List<int>[] adjacency;
    private readonly int[] distanceFromSpawn;
    private readonly int[] degree;
    private readonly bool[] onCriticalPath;
    private readonly List<int> criticalPath = new List<int>();

    public int SpawnIndex { get; }
    public int ExitIndex { get; }
    public int RoomCount => roomCount;

    /// <summary>Ordered room sequence from the spawn room to the exit room inclusive.</summary>
    public IReadOnlyList<int> CriticalPath => criticalPath;

    public RoomGraph(int[,] grid, int width, int height, List<RectInt> rooms, int spawnIndex, int exitIndex)
    {
        roomCount = rooms.Count;
        SpawnIndex = spawnIndex;
        ExitIndex = exitIndex;

        adjacency = new List<int>[roomCount];
        for (int i = 0; i < roomCount; i++) adjacency[i] = new List<int>();
        distanceFromSpawn = new int[roomCount];
        degree = new int[roomCount];
        onCriticalPath = new bool[roomCount];

        int[,] label = LabelRegions(grid, width, height, rooms);
        BuildAdjacency(grid, width, height, label);
        ComputeDistancesAndPath(spawnIndex, exitIndex);
    }

    // --- Public API (room index in, graph fact out) ---

    public IReadOnlyList<int> Neighbours(int i) =>
        (i >= 0 && i < roomCount) ? adjacency[i] : System.Array.Empty<int>();

    public int DistanceFromSpawn(int i) =>
        (i >= 0 && i < roomCount) ? distanceFromSpawn[i] : -1;

    public bool IsOnCriticalPath(int i) =>
        i >= 0 && i < roomCount && onCriticalPath[i];

    public int Degree(int i) =>
        (i >= 0 && i < roomCount) ? degree[i] : 0;

    // --- Stage 1: multi-source BFS region labelling ---

    int[,] LabelRegions(int[,] grid, int width, int height, List<RectInt> rooms)
    {
        int[,] label = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                label[x, y] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        // Seed every walkable tile inside every room with that room's index. All
        // seeds go in at "distance 0" before any expansion, which is what makes the
        // single shared BFS resolve each later tile to its nearest room.
        for (int i = 0; i < rooms.Count; i++)
        {
            RectInt r = rooms[i];
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;
                    if (grid[x, y] != 0) continue;       // walkable only
                    if (label[x, y] != -1) continue;      // don't re-seed an overlap
                    label[x, y] = i;
                    queue.Enqueue(new Vector2Int(x, y));
                }
        }

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int lab = label[c.x, c.y];
            for (int k = 0; k < 4; k++)
            {
                int nx = c.x + dx[k];
                int ny = c.y + dy[k];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if (grid[nx, ny] != 0) continue;
                if (label[nx, ny] != -1) continue;
                label[nx, ny] = lab;
                queue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        return label;
    }

    // --- Stage 2: adjacency from 4-adjacent differing labels ---

    void BuildAdjacency(int[,] grid, int width, int height, int[,] label)
    {
        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] != 0) continue;
                int a = label[x, y];
                if (a < 0) continue;

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + dx[k];
                    int ny = y + dy[k];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    if (grid[nx, ny] != 0) continue;
                    int b = label[nx, ny];
                    if (b < 0 || b == a) continue;

                    // roomCount is tiny (<= ~18), so a Contains check to dedupe is
                    // cheaper and clearer than maintaining a pair hash set.
                    if (!adjacency[a].Contains(b)) adjacency[a].Add(b);
                }
            }

        for (int i = 0; i < roomCount; i++) degree[i] = adjacency[i].Count;
    }

    // --- Stage 3: BFS distances + critical path reconstruction ---

    void ComputeDistancesAndPath(int spawnIndex, int exitIndex)
    {
        for (int i = 0; i < roomCount; i++) distanceFromSpawn[i] = -1;
        int[] parent = new int[roomCount];
        for (int i = 0; i < roomCount; i++) parent[i] = -1;

        if (spawnIndex < 0 || spawnIndex >= roomCount) return;

        Queue<int> queue = new Queue<int>();
        distanceFromSpawn[spawnIndex] = 0;
        queue.Enqueue(spawnIndex);

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach (int nb in adjacency[cur])
            {
                if (distanceFromSpawn[nb] != -1) continue;
                distanceFromSpawn[nb] = distanceFromSpawn[cur] + 1;
                parent[nb] = cur;
                queue.Enqueue(nb);
            }
        }

        // Critical path = spawn -> exit walked back through parent pointers.
        criticalPath.Clear();
        if (exitIndex >= 0 && exitIndex < roomCount && distanceFromSpawn[exitIndex] != -1)
        {
            List<int> reversed = new List<int>();
            int cur = exitIndex;
            while (cur != -1)
            {
                reversed.Add(cur);
                if (cur == spawnIndex) break;
                cur = parent[cur];
            }
            reversed.Reverse();
            criticalPath.AddRange(reversed);
        }
        else
        {
            // Exit unreachable or same as spawn (e.g. a degenerate single-room floor):
            // the path is just the spawn room.
            criticalPath.Add(spawnIndex);
        }

        foreach (int c in criticalPath)
            if (c >= 0 && c < roomCount) onCriticalPath[c] = true;
    }
}
