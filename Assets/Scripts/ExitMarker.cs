using UnityEngine;

public class ExitMarker : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;

    [Header("Appearance")]
    public Color markerColor = new Color(1f, 0.85f, 0.2f);
    public float markerScale = 0.6f;
    [Tooltip("Optional — assign your own sprite here to skip the generated placeholder circle.")]
    public Sprite markerSprite;

    private SpriteRenderer sr;

    void Awake()
    {
        sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = markerSprite != null ? markerSprite : BuildCircleSprite();
        sr.color = markerColor;
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

    Sprite BuildCircleSprite()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                tex.SetPixel(x, y, d <= r ? Color.white : new Color(0, 0, 0, 0));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
