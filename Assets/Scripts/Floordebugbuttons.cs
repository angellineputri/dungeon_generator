using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Testing-only Previous/Next floor buttons, built at runtime, bottom-center.
/// Calls DungeonGenerator.JumpToFloor, which generates a fresh random layout at
/// the target floor's difficulty — floors aren't cached, so "Previous" doesn't
/// return to a specific earlier layout, just to that floor's difficulty tier.
/// Remove or disable this GameObject before any real playtest/demo — it's purely
/// a shortcut for checking difficulty curves across floors without walking there.
/// </summary>
public class FloorDebugButtons : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-found in the scene if left empty.")]
    public Canvas canvas;
    public DungeonGenerator dungeon;

    [Header("Layout")]
    public Vector2 buttonSize = new Vector2(100f, 32f);
    public float bottomOffset = 16f;
    public float gap = 8f;

    void Start()
    {
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null || dungeon == null)
        {
            Debug.LogWarning("[FloorDebugButtons] Missing reference — buttons not built.");
            return;
        }

        BuildButton("< Prev Floor", new Vector2(-(buttonSize.x / 2f + gap / 2f), bottomOffset),
            () => dungeon.JumpToFloor(Mathf.Max(1, dungeon.CurrentFloor - 1)));

        BuildButton("Next Floor >", new Vector2(buttonSize.x / 2f + gap / 2f, bottomOffset),
            () => dungeon.JumpToFloor(dungeon.CurrentFloor + 1));
    }

    void BuildButton(string label, Vector2 anchoredPos, System.Action onClick)
    {
        GameObject btnGO = new GameObject(label + "_Button", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(canvas.transform, false);
        RectTransform rt = btnGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = buttonSize;

        Image img = btnGO.GetComponent<Image>();
        img.sprite = BuildSolidSprite();
        img.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

        Button btn = btnGO.GetComponent<Button>();
        btn.onClick.AddListener(() => onClick());

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(btnGO.transform, false);
        RectTransform textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        Text txt = textGO.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 13;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.text = label;
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