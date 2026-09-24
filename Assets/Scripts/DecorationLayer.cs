using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Purely-cosmetic decoration pass. Renders props, floor cracks, torches and stairs onto
/// a SEPARATE Tilemap that sits above the floor, keyed off room roles from RoomGraph.
///
/// HARD GUARANTEE — decoration can never touch gameplay:
///   - writes only to its own decorationTilemap; never to DungeonGenerator.Grid, which is
///     what pathfinding, fog of war and generation read.
///   - creates tiles with ColliderType.None, so nothing physical is added to the world.
///   - draws only on INTERIOR floor tiles (every 4-neighbour is floor), which by
///     construction excludes room edges and corridor mouths — so a prop can never sit in
///     a doorway or block a route.
///   - uses its OWN System.Random seeded from the floor seed, so it consumes zero
///     UnityEngine.Random draws and cannot shift the sequence other OnFloorGenerated
///     subscribers (e.g. spawners) depend on. Same seed -> same decoration, reproducibly.
///
/// Design intent: density is a wayfinding signal. Off-critical-path rooms are visibly
/// richer (denser props + cracks) than the main route, so the player can read that a side
/// room is worth entering before committing to walking there. The spawn room is kept
/// clean and readable; the key room is marked with torches; the exit with stairs.
/// </summary>
public class DecorationLayer : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public DungeonGenerator dungeon;
    [Tooltip("Separate Tilemap above the floor tilemap (higher sorting order), NO collider, " +
             "and BELOW the fog-of-war overlay so fog still hides it. Auto-found by name if empty.")]
    public Tilemap decorationTilemap;

    [Header("Sprites — assign sliced sprites; import settings are yours to set")]
    [Tooltip("General props from Objects.png (barrels, bones, rubble, etc.).")]
    public Sprite[] propSprites;
    [Tooltip("Floor cracks from decorative_cracks_floor.png — used to enrich off-path rooms.")]
    public Sprite[] floorCrackSprites;
    [Tooltip("One static frame from fire_animation.png — marks the key room.")]
    public Sprite torchSprite;
    [Tooltip("Stairs from Objects.png — marks the exit room.")]
    public Sprite stairsSprite;

    [Header("Density per role (chance per interior floor tile)")]
    [Range(0f, 1f)] public float spawnRoomDensity = 0.02f;   // sparse & clean — readable
    [Range(0f, 1f)] public float criticalPathDensity = 0.05f; // light props on the main route
    [Range(0f, 1f)] public float offPathDensity = 0.14f;     // denser props off the route
    [Range(0f, 1f)] public float offPathCrackDensity = 0.10f; // + cracks, so optional space reads richer
    [Tooltip("Torches scattered in the key room (on interior tiles).")]
    public int keyRoomTorches = 3;

    // Runtime Tiles wrapping the assigned sprites, built once and reused. Kept separate
    // from any project Tile assets so nothing on disk is mutated.
    private TileBase[] propTiles;
    private TileBase[] crackTiles;
    private TileBase torchTile;
    private TileBase stairsTile;
    private bool tilesBuilt;
    private bool warnedNoSprites;

    void OnEnable()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (dungeon != null) dungeon.OnFloorGenerated += Decorate;
    }

    void OnDisable()
    {
        if (dungeon != null) dungeon.OnFloorGenerated -= Decorate;
    }

    void Decorate()
    {
        if (dungeon == null) return;
        if (decorationTilemap == null)
        {
            // Fall back to a child Tilemap named "Decoration" if one wasn't wired up.
            foreach (var tm in GetComponentsInChildren<Tilemap>())
                if (tm.name == "Decoration") { decorationTilemap = tm; break; }
            if (decorationTilemap == null) return;
        }

        decorationTilemap.ClearAllTiles();
        EnsureTiles();

        var rooms = dungeon.Rooms;
        if (rooms == null || rooms.Count == 0) return;
        if (!HasAnySprites())
        {
            if (!warnedNoSprites)
            {
                Debug.LogWarning("[DecorationLayer] No sprites assigned — decoration pass is a no-op until " +
                                 "propSprites/floorCrackSprites/torchSprite/stairsSprite are set in the Inspector.");
                warnedNoSprites = true;
            }
            return;
        }

        // Same seed as the layout -> decoration is reproducible with the floor. Own RNG,
        // so no UnityEngine.Random draws are consumed (can't perturb other subscribers).
        System.Random rng = new System.Random(dungeon.CurrentSeed);

        RoomGraph graph = dungeon.RoomGraph;
        int spawnIdx = dungeon.PlayerSpawnRoomIndex;
        int exitIdx = dungeon.ExitRoomIndex;
        int keyIdx = dungeon.KeyRoomIndex;

        for (int i = 0; i < rooms.Count; i++)
        {
            List<Vector2Int> interior = InteriorTiles(rooms[i]);
            if (interior.Count == 0) continue;

            if (i == exitIdx)
            {
                // Stairs mark the goal — a single tile at the room's most central interior
                // spot, plus a couple of light props so the room isn't bare.
                PlaceCentral(interior, stairsTile);
                Scatter(interior, rng, criticalPathDensity, propTiles);
            }
            else if (i == keyIdx)
            {
                ScatterCount(interior, rng, keyRoomTorches, torchTile);
                Scatter(interior, rng, criticalPathDensity, propTiles);
            }
            else if (i == spawnIdx)
            {
                Scatter(interior, rng, spawnRoomDensity, propTiles);
            }
            else if (graph != null && graph.IsOnCriticalPath(i))
            {
                Scatter(interior, rng, criticalPathDensity, propTiles);
            }
            else
            {
                // Off the critical path: denser props AND floor cracks, so optional rooms
                // read as visually richer than the main route.
                Scatter(interior, rng, offPathDensity, propTiles);
                Scatter(interior, rng, offPathCrackDensity, crackTiles);
            }
        }
    }

    // Interior floor tiles of a room: floor tiles whose 4 orthogonal neighbours are ALL
    // floor. This excludes every edge tile, so decoration never lands on a doorway,
    // corridor mouth, or the one-tile-wide corridors themselves.
    List<Vector2Int> InteriorTiles(RectInt room)
    {
        var result = new List<Vector2Int>();
        int w = dungeon.GridWidth, h = dungeon.GridHeight;
        int[,] grid = dungeon.Grid;
        if (grid == null) return result;

        for (int x = room.xMin; x < room.xMax; x++)
            for (int y = room.yMin; y < room.yMax; y++)
            {
                if (x <= 0 || x >= w - 1 || y <= 0 || y >= h - 1) continue;
                if (grid[x, y] != 0) continue;
                if (grid[x + 1, y] != 0 || grid[x - 1, y] != 0 ||
                    grid[x, y + 1] != 0 || grid[x, y - 1] != 0) continue;
                result.Add(new Vector2Int(x, y));
            }
        return result;
    }

    // Independent per-tile roll: each interior tile has `density` chance of a random tile
    // from the set. Density scales the count with room size for free.
    void Scatter(List<Vector2Int> interior, System.Random rng, float density, TileBase[] set)
    {
        if (set == null || set.Length == 0 || density <= 0f) return;
        foreach (var t in interior)
        {
            if (rng.NextDouble() >= density) continue;
            if (decorationTilemap.GetTile(new Vector3Int(t.x, t.y, 0)) != null) continue; // don't stack
            decorationTilemap.SetTile(new Vector3Int(t.x, t.y, 0), set[rng.Next(set.Length)]);
        }
    }

    // Place exactly `count` of one tile at distinct random interior tiles.
    void ScatterCount(List<Vector2Int> interior, System.Random rng, int count, TileBase tile)
    {
        if (tile == null || count <= 0 || interior.Count == 0) return;
        var pool = new List<Vector2Int>(interior);
        int n = Mathf.Min(count, pool.Count);
        for (int k = 0; k < n; k++)
        {
            int idx = rng.Next(pool.Count);
            Vector2Int t = pool[idx];
            pool.RemoveAt(idx);
            decorationTilemap.SetTile(new Vector3Int(t.x, t.y, 0), tile);
        }
    }

    // Single tile at the interior position closest to the room's centre.
    void PlaceCentral(List<Vector2Int> interior, TileBase tile)
    {
        if (tile == null || interior.Count == 0) return;
        float cx = 0f, cy = 0f;
        foreach (var t in interior) { cx += t.x; cy += t.y; }
        cx /= interior.Count; cy /= interior.Count;

        Vector2Int best = interior[0];
        float bestD = float.MaxValue;
        foreach (var t in interior)
        {
            float d = (t.x - cx) * (t.x - cx) + (t.y - cy) * (t.y - cy);
            if (d < bestD) { bestD = d; best = t; }
        }
        decorationTilemap.SetTile(new Vector3Int(best.x, best.y, 0), tile);
    }

    bool HasAnySprites() =>
        (propSprites != null && propSprites.Length > 0) ||
        (floorCrackSprites != null && floorCrackSprites.Length > 0) ||
        torchSprite != null || stairsSprite != null;

    // Wrap each assigned sprite in a non-colliding runtime Tile, once. Built here rather
    // than requiring the user to author Tile assets — they only import/slice the sheets.
    void EnsureTiles()
    {
        if (tilesBuilt) return;
        propTiles = BuildTiles(propSprites);
        crackTiles = BuildTiles(floorCrackSprites);
        torchTile = torchSprite != null ? MakeTile(torchSprite) : null;
        stairsTile = stairsSprite != null ? MakeTile(stairsSprite) : null;
        tilesBuilt = true;
    }

    TileBase[] BuildTiles(Sprite[] sprites)
    {
        if (sprites == null) return System.Array.Empty<TileBase>();
        var list = new List<TileBase>(sprites.Length);
        foreach (var s in sprites)
            if (s != null) list.Add(MakeTile(s));
        return list.ToArray();
    }

    TileBase MakeTile(Sprite sprite)
    {
        Tile t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = sprite;
        t.colliderType = Tile.ColliderType.None; // never physical — cannot affect play
        return t;
    }
}
