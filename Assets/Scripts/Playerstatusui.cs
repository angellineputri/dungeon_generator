using UnityEngine;
using UnityEngine.UI;

public class PlayerStatusUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found in the scene if left empty.")]
    public Canvas canvas;
    [Tooltip("Auto-filled from this GameObject if left empty.")]
    public PlayerHealth playerHealth;
    public PlayerMana playerMana;

    [Header("Layout")]
    public Vector2 barSize = new Vector2(180f, 16f);
    public Vector2 topLeftOffset = new Vector2(16f, -16f);
    public float barSpacing = 26f;
    public float panelPadding = 12f;

    private Image healthFill;
    private Image manaFill;

    void Start()
    {
        if (playerHealth == null) playerHealth = GetComponent<PlayerHealth>();
        if (playerMana == null) playerMana = GetComponent<PlayerMana>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogWarning("[PlayerStatusUI] No Canvas found in scene — cannot build bars.");
            return;
        }

        BuildPanel();
    }

    void BuildPanel()
    {

        float panelW = barSize.x + panelPadding * 2f + 22f;
        float panelH = barSpacing * 2f + panelPadding * 1.5f;

        GameObject panel = new GameObject("StatusPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        RectTransform panelRT = panel.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0, 1);
        panelRT.anchorMax = new Vector2(0, 1);
        panelRT.pivot = new Vector2(0, 1);
        panelRT.anchoredPosition = topLeftOffset;
        panelRT.sizeDelta = new Vector2(panelW, panelH);
        Image panelImg = panel.GetComponent<Image>();
        panelImg.sprite = BuildSolidSprite();
        panelImg.color = new Color(0.05f, 0.05f, 0.06f, 0.72f);

        GameObject border = new GameObject("StatusPanel_Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(canvas.transform, false);
        border.transform.SetSiblingIndex(panel.transform.GetSiblingIndex());
        RectTransform borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(0, 1);
        borderRT.anchorMax = new Vector2(0, 1);
        borderRT.pivot = new Vector2(0, 1);
        borderRT.anchoredPosition = new Vector2(topLeftOffset.x - 2, topLeftOffset.y + 2);
        borderRT.sizeDelta = new Vector2(panelW + 4, panelH + 4);
        Image borderImg = border.GetComponent<Image>();
        borderImg.sprite = BuildSolidSprite();
        borderImg.color = new Color(0.85f, 0.83f, 0.78f, 0.35f);

        Vector2 innerPos = new Vector2(panelPadding + 22f, panelH - panelPadding - barSize.y);
        healthFill = BuildBarInsidePanel(panel.transform, "HealthBar", innerPos, new Color(0.85f, 0.25f, 0.25f));
        BuildIcon(panel.transform, new Vector2(panelPadding, innerPos.y + 1f), new Color(0.85f, 0.25f, 0.25f));

        Vector2 innerPos2 = innerPos - new Vector2(0, barSpacing);
        manaFill = BuildBarInsidePanel(panel.transform, "ManaBar", innerPos2, new Color(0.25f, 0.45f, 0.85f));
        BuildIcon(panel.transform, new Vector2(panelPadding, innerPos2.y + 1f), new Color(0.25f, 0.45f, 0.85f));
    }

    Image BuildBarInsidePanel(Transform parent, string name, Vector2 anchoredPos, Color fillColor)
    {
        GameObject bg = new GameObject(name + "_BG", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(0, 0);
        bgRT.pivot = new Vector2(0, 0);
        bgRT.anchoredPosition = anchoredPos;
        bgRT.sizeDelta = barSize;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.sprite = BuildSolidSprite();
        bgImg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

        GameObject fill = new GameObject(name + "_Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(1.5f, 1.5f);
        fillRT.offsetMax = new Vector2(-1.5f, -1.5f);
        Image fillImg = fill.GetComponent<Image>();
        fillImg.sprite = BuildSolidSprite();
        fillImg.color = fillColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;

        return fillImg;
    }

    void BuildIcon(Transform parent, Vector2 anchoredPos, Color color)
    {
        GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(parent, false);
        RectTransform rt = icon.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(14, 14);
        Image img = icon.GetComponent<Image>();
        img.sprite = BuildSolidSprite();
        img.color = color;
    }

    void Update()
    {
        if (healthFill != null && playerHealth != null)
            healthFill.fillAmount = playerHealth.CurrentHealth / playerHealth.maxHealth;

        if (manaFill != null && playerMana != null)
            manaFill.fillAmount = playerMana.CurrentMana / playerMana.maxMana;
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
