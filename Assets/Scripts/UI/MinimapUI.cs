using UnityEngine;
using UnityEngine.UI;

public class MinimapUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found in the scene if left empty.")]
    public Canvas canvas;
    public DungeonGenerator dungeon;
    public FogOfWar fogOfWar;
    public Transform player;

    [Header("Layout")]
    public Vector2 size = new Vector2(160f, 160f);
    public Vector2 topRightOffset = new Vector2(-16f, -16f);
    [Tooltip("Seconds between minimap redraws — doesn't need to be every frame.")]
    public float refreshInterval = 0.25f;

    private Texture2D mapTex;
    private float refreshTimer;
    private Text floorLabel;

    void Start()
    {
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null || dungeon == null || fogOfWar == null)
        {
            Debug.LogWarning("[MinimapUI] Missing a required reference — minimap not built.");
            return;
        }

        BuildUI();
    }

    void BuildUI()
    {

        GameObject border = new GameObject("Minimap_Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(canvas.transform, false);
        RectTransform borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(1, 1);
        borderRT.anchorMax = new Vector2(1, 1);
        borderRT.pivot = new Vector2(1, 1);
        borderRT.anchoredPosition = new Vector2(topRightOffset.x + 3, topRightOffset.y + 3);
        borderRT.sizeDelta = size + new Vector2(6, 6);
        Image borderImg = border.GetComponent<Image>();
        borderImg.sprite = BuildSolidSprite();
        borderImg.color = new Color(0.85f, 0.83f, 0.78f, 0.35f);

        GameObject backdrop = new GameObject("Minimap_Backdrop", typeof(RectTransform), typeof(Image));
        backdrop.transform.SetParent(canvas.transform, false);
        RectTransform backdropRT = backdrop.GetComponent<RectTransform>();
        backdropRT.anchorMin = new Vector2(1, 1);
        backdropRT.anchorMax = new Vector2(1, 1);
        backdropRT.pivot = new Vector2(1, 1);
        backdropRT.anchoredPosition = topRightOffset;
        backdropRT.sizeDelta = size;
        Image backdropImg = backdrop.GetComponent<Image>();
        backdropImg.sprite = BuildSolidSprite();
        backdropImg.color = new Color(0.03f, 0.03f, 0.04f, 0.85f);

        GameObject go = new GameObject("Minimap", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(canvas.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = topRightOffset;
        rt.sizeDelta = size;

        RawImage rawImage = go.GetComponent<RawImage>();

        mapTex = new Texture2D(dungeon.GridWidth, dungeon.GridHeight, TextureFormat.RGBA32, false);
        mapTex.filterMode = FilterMode.Point;
        ClearTexture();
        rawImage.texture = mapTex;

        BuildFloorLabel();
    }

    void BuildFloorLabel()
    {
        float labelHeight = 24f;
        float gap = 6f;
        Vector2 labelPos = new Vector2(topRightOffset.x, topRightOffset.y - size.y - gap);

        GameObject border = new GameObject("FloorLabel_Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(canvas.transform, false);
        RectTransform borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(1, 1);
        borderRT.anchorMax = new Vector2(1, 1);
        borderRT.pivot = new Vector2(1, 1);
        borderRT.anchoredPosition = new Vector2(labelPos.x + 3, labelPos.y + 3);
        borderRT.sizeDelta = new Vector2(size.x + 6, labelHeight + 6);
        Image borderImg = border.GetComponent<Image>();
        borderImg.sprite = BuildSolidSprite();
        borderImg.color = new Color(0.85f, 0.83f, 0.78f, 0.35f);

        GameObject bg = new GameObject("FloorLabel_BG", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(1, 1);
        bgRT.anchorMax = new Vector2(1, 1);
        bgRT.pivot = new Vector2(1, 1);
        bgRT.anchoredPosition = labelPos;
        bgRT.sizeDelta = new Vector2(size.x, labelHeight);
        Image bgImg = bg.GetComponent<Image>();
        bgImg.sprite = BuildSolidSprite();
        bgImg.color = new Color(0.03f, 0.03f, 0.04f, 0.85f);

        GameObject textGO = new GameObject("FloorLabel_Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(canvas.transform, false);
        RectTransform textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = new Vector2(1, 1);
        textRT.anchorMax = new Vector2(1, 1);
        textRT.pivot = new Vector2(1, 1);
        textRT.anchoredPosition = labelPos;
        textRT.sizeDelta = new Vector2(size.x, labelHeight);

        floorLabel = textGO.GetComponent<Text>();
        floorLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        floorLabel.fontSize = 13;
        floorLabel.alignment = TextAnchor.MiddleCenter;
        floorLabel.color = new Color(0.85f, 0.83f, 0.78f, 1f);
        floorLabel.text = "Floor 1";
    }

    void ClearTexture()
    {
        Color[] clear = new Color[dungeon.GridWidth * dungeon.GridHeight];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color(0, 0, 0, 0);
        mapTex.SetPixels(clear);
        mapTex.Apply();
    }

    void Update()
    {
        if (mapTex == null) return;

        if (floorLabel != null && dungeon != null)
            floorLabel.text = $"Floor {dungeon.CurrentFloor}";

        refreshTimer -= Time.deltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = refreshInterval;

        RedrawMap();
    }

    [Header("Testing")]
    [Tooltip("When true, shows the entire generated layout regardless of what's actually been explored — for testing/debugging only. Turn off before any real playtest or demo, since it defeats the point of fog of war.")]
    public bool showFullMapNoFog = false;

    void RedrawMap()
    {
        bool[,] explored = showFullMapNoFog ? null : fogOfWar.Explored;
        if (!showFullMapNoFog && explored == null) return;

        int gw = dungeon.GridWidth;
        int gh = dungeon.GridHeight;

        for (int x = 0; x < gw; x++)
        {
            for (int y = 0; y < gh; y++)
            {
                bool revealed = showFullMapNoFog || explored[x, y];
                Color c;
                if (revealed)
                    c = dungeon.IsWalkable(x, y) ? new Color(0.75f, 0.75f, 0.7f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
                else
                    c = new Color(0, 0, 0, 0);
                mapTex.SetPixel(x, y, c);
            }
        }

        if (player != null && dungeon.tilemapCA != null)
        {
            Vector3 local = player.position - dungeon.tilemapCA.transform.position;
            int px = Mathf.RoundToInt(local.x);
            int py = Mathf.RoundToInt(local.y);
            DrawMarker(px, py, new Color(0.3f, 0.65f, 1f, 1f));
        }

        mapTex.Apply();
    }

    void DrawMarker(int cx, int cy, Color color)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x >= 0 && x < mapTex.width && y >= 0 && y < mapTex.height)
                    mapTex.SetPixel(x, y, color);
            }
        }
    }

    Sprite BuildSolidSprite()
    {
        Texture2D tex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
    }
}
