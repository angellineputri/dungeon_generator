using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class FogOfWar : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;
    public Transform player;
    [Tooltip("A separate, empty Tilemap sibling to tilemapCA, rendered above everything else.")]
    public Tilemap fogTilemap;

    [Header("Visibility")]
    public float visionRadius = 6f;
    [Range(0f, 1f)] public float unexploredAlpha = 1f;
    [Range(0f, 1f)] public float exploredAlpha = 0.6f;
    [Range(0f, 1f)] public float visibleAlpha = 0f;

    private bool[,] explored;
    public bool[,] Explored => explored;
    private Tile fogTile;
    private int gridWidth, gridHeight;
    private Vector2Int lastPlayerGridPos = new Vector2Int(int.MinValue, int.MinValue);
    private readonly List<Vector3Int> currentlyVisibleCells = new List<Vector3Int>();

    void Awake()
    {
        fogTile = BuildSolidTile();
    }

    void OnEnable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated += ResetFog;
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated -= ResetFog;
    }

    void Start()
    {
        if (dungeon != null && dungeon.CurrentFloor > 0)
            ResetFog();
    }

    private bool renderFog = true;

    public void SetFog(bool on)
    {
        renderFog = on;
        enabled = true;

        if (fogTilemap == null) return;
        if (on)
            RepaintFromExplored();
        else
            fogTilemap.ClearAllTiles();
    }

    void ResetFog()
    {
        gridWidth = dungeon.GridWidth;
        gridHeight = dungeon.GridHeight;
        explored = new bool[gridWidth, gridHeight];
        currentlyVisibleCells.Clear();

        fogTilemap.ClearAllTiles();
        if (renderFog)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    Vector3Int pos = new Vector3Int(x, y, 0);
                    fogTilemap.SetTile(pos, fogTile);
                    fogTilemap.SetTileFlags(pos, TileFlags.None);
                    fogTilemap.SetColor(pos, new Color(0, 0, 0, unexploredAlpha));
                }
            }
        }

        lastPlayerGridPos = new Vector2Int(int.MinValue, int.MinValue);
    }

    void RepaintFromExplored()
    {
        if (explored == null) return;
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                fogTilemap.SetTile(pos, fogTile);
                fogTilemap.SetTileFlags(pos, TileFlags.None);
                float a = explored[x, y] ? exploredAlpha : unexploredAlpha;
                fogTilemap.SetColor(pos, new Color(0, 0, 0, a));
            }
        }
        lastPlayerGridPos = new Vector2Int(int.MinValue, int.MinValue);
    }

    void Update()
    {
        if (explored == null || player == null || dungeon.tilemapCA == null) return;

        Vector2Int playerGrid = WorldToGrid(player.position);
        if (playerGrid == lastPlayerGridPos) return;
        lastPlayerGridPos = playerGrid;

        UpdateVisibility(playerGrid);
    }

    [Header("Edge Softening")]
    [Tooltip("Tiles this many units wide, right at the vision boundary, fade gradually instead of cutting off sharply. This is what smooths the otherwise blocky/pixelated circle edge.")]
    public float featherWidth = 2.5f;

    void UpdateVisibility(Vector2Int center)
    {

        if (renderFog)
        {
            foreach (var pos in currentlyVisibleCells)
                fogTilemap.SetColor(pos, new Color(0, 0, 0, exploredAlpha));
        }
        currentlyVisibleCells.Clear();

        int r = Mathf.CeilToInt(visionRadius);
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int x = center.x + dx;
                int y = center.y + dy;
                if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) continue;

                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center.x, center.y));
                if (dist > visionRadius) continue;

                explored[x, y] = true;
                if (!renderFog) continue;

                float innerEdge = Mathf.Max(0f, visionRadius - featherWidth);
                float t = Mathf.InverseLerp(innerEdge, visionRadius, dist);
                float alpha = Mathf.Lerp(visibleAlpha, exploredAlpha, t);

                Vector3Int pos = new Vector3Int(x, y, 0);
                fogTilemap.SetColor(pos, new Color(0, 0, 0, alpha));
                currentlyVisibleCells.Add(pos);
            }
        }
    }

    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 local = worldPos - dungeon.tilemapCA.transform.position;
        return new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y));
    }

    Tile BuildSolidTile()
    {
        Texture2D tex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        Tile t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = sprite;
        t.color = Color.white;
        return t;
    }
}
