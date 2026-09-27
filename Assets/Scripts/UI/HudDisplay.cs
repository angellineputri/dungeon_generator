using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HudDisplay : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public DungeonGenerator dungeon;
    public KeyManager keyManager;

    private TextMeshProUGUI floorText;
    private TextMeshProUGUI keyText;

    void Awake()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (keyManager == null) keyManager = FindFirstObjectByType<KeyManager>();
        BuildUI();
    }

    void Update()
    {
        if (dungeon != null)
            floorText.text = $"FLOOR {dungeon.CurrentFloor}";

        if (keyManager != null)
        {
            bool has = keyManager.HasKey;
            keyText.text = has ? "KEY: HELD" : "KEY: FIND IT";
            keyText.color = has ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.8f, 0.3f);
        }
        else
        {
            keyText.text = "";
        }
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("HudCanvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        floorText = BuildLabel(canvasObj.transform, "FloorText", 64,
            new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(600, 90));
        floorText.fontStyle = FontStyles.Bold;

        keyText = BuildLabel(canvasObj.transform, "KeyText", 34,
            new Vector2(0.5f, 1f), new Vector2(0, -140), new Vector2(600, 50));
    }

    TextMeshProUGUI BuildLabel(Transform parent, string name, float fontSize,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        TextMeshProUGUI t = obj.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.text = "";
        return t;
    }
}
