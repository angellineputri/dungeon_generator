using UnityEngine;

public class ExitMarker : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;

    [Header("Appearance")]
    [Tooltip("Tint applied ONLY when you assign your own markerSprite below. The generated portal oval is already coloured in code.")]
    public Color markerColor = new Color(0.09f, 0.106f, 0.157f, 1f);
    public float markerScale = 0.6f;
    [Tooltip("Optional — assign your own sprite here to replace the generated portal oval.")]
    public Sprite markerSprite;

    private SpriteRenderer sr;

    void Awake()
    {
        sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = gameObject.AddComponent<SpriteRenderer>();

        bool custom = markerSprite != null;
        sr.sprite = custom ? markerSprite : BuildPortalOvalSprite();
        sr.color = custom ? markerColor : Color.white;
        sr.sortingOrder = 10;
        transform.localScale = Vector3.one * markerScale;
    }

    void OnEnable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated += Reposition;
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated -= Reposition;
    }

    void Start()
    {
        if (dungeon != null && dungeon.CurrentFloor > 0)
            Reposition();
    }

    void Reposition()
    {
        if (dungeon == null || dungeon.tilemapCA == null) return;

        RectInt exit = dungeon.ExitRoom;
        Vector2Int center = new Vector2Int(
            exit.x + exit.width / 2,
            exit.y + exit.height / 2
        );

        transform.position = dungeon.tilemapCA.transform.position + new Vector3(center.x, center.y, -0.1f);
    }

    Sprite BuildPortalOvalSprite()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Vector2 c = new Vector2(size / 2f, size / 2f);
        float rx = size * 0.30f;
        float ry = size * 0.46f;

        Color core  = new Color(0.09f, 0.106f, 0.157f);
        Color rim   = new Color(0.23f, 0.27f, 0.41f);
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f - c.x) / rx;
                float ny = (y + 0.5f - c.y) / ry;
                float d = Mathf.Sqrt(nx * nx + ny * ny);

                if (d > 1f) { tex.SetPixel(x, y, clear); continue; }

                float rimAmount = Mathf.SmoothStep(0f, 1f, (d - 0.6f) / 0.4f);
                float alpha = Mathf.Clamp01((1f - d) / 0.08f);
                Color col = Color.Lerp(core, rim, rimAmount);
                col.a = alpha;
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
