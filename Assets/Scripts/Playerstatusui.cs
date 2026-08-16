using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds simple filled-bar HP and mana indicators in the bottom-left corner at
/// runtime, reading off PlayerHealth/PlayerMana on the same GameObject. No manual
/// Canvas/Image setup needed in the Editor — same "build it in code" approach as
/// ExitMarker and PotionPickup use for their sprites.
///
/// Positioned bottom-left rather than top-left specifically to avoid overlapping
/// the existing debug metrics panel (Floor/Seed/Rooms/etc text), which already
/// occupies the top-left corner.
/// </summary>
public class PlayerStatusUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found in the scene if left empty.")]
    public Canvas canvas;
    [Tooltip("Auto-filled from this GameObject if left empty.")]
    public PlayerHealth playerHealth;
    public PlayerMana playerMana;

    [Header("Layout")]
    public Vector2 barSize = new Vector2(200f, 20f);
    public Vector2 bottomLeftOffset = new Vector2(20f, 20f);
    public float barSpacing = 28f;

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

        healthFill = BuildBar("HealthBar", bottomLeftOffset, new Color(0.85f, 0.25f, 0.25f));
        manaFill = BuildBar("ManaBar", bottomLeftOffset + new Vector2(0, barSpacing), new Color(0.25f, 0.45f, 0.85f));
    }

    void Update()
    {
        if (healthFill != null && playerHealth != null)
            healthFill.fillAmount = playerHealth.CurrentHealth / playerHealth.maxHealth;

        if (manaFill != null && playerMana != null)
            manaFill.fillAmount = playerMana.CurrentMana / playerMana.maxMana;
    }

    Image BuildBar(string name, Vector2 anchoredPos, Color fillColor)
    {
        GameObject bg = new GameObject(name + "_BG", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvas.transform, false);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0);
        bgRT.anchorMax = new Vector2(0, 0);
        bgRT.pivot = new Vector2(0, 0);
        bgRT.anchoredPosition = anchoredPos;
        bgRT.sizeDelta = barSize;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.sprite = BuildSolidSprite();
        bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

        GameObject fill = new GameObject(name + "_Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2, 2);
        fillRT.offsetMax = new Vector2(-2, -2);
        Image fillImg = fill.GetComponent<Image>();
        fillImg.sprite = BuildSolidSprite();
        fillImg.color = fillColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;

        return fillImg;
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