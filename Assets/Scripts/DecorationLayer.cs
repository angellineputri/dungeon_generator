using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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
    [Range(0f, 1f)] public float spawnRoomDensity = 0.02f;
    [Range(0f, 1f)] public float criticalPathDensity = 0.05f;
    [Range(0f, 1f)] public float offPathDensity = 0.14f;
    [Range(0f, 1f)] public float offPathCrackDensity = 0.10f;
    [Tooltip("Torches scattered in the key room (on interior tiles).")]
    public int keyRoomTorches = 3;

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

                Scatter(interior, rng, offPathDensity, propTiles);
                Scatter(interior, rng, offPathCrackDensity, crackTiles);
            }
        }
    }

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

    void Scatter(List<Vector2Int> interior, System.Random rng, float density, TileBase[] set)
    {
        if (set == null || set.Length == 0 || density <= 0f) return;
        foreach (var t in interior)
        {
            if (rng.NextDouble() >= density) continue;
            if (decorationTilemap.GetTile(new Vector3Int(t.x, t.y, 0)) != null) continue;
            decorationTilemap.SetTile(new Vector3Int(t.x, t.y, 0), set[rng.Next(set.Length)]);
        }
    }

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
        t.colliderType = Tile.ColliderType.None;
        return t;
    }
}
