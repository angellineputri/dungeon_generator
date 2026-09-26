using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

public class GameStateManager : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public DungeonGenerator dungeon;
    public PlayerHealth playerHealth;
    public SessionTelemetry telemetry;

    [Header("Meta")]
    public string gameTitle = "DUNGEON";
    private const string BestFloorKey = "BestFloor";

    private enum State { Start, Playing, GameOver }
    private State state = State.Start;

    public string ExtraSummary { get; set; } = "";

    public bool IsPlaying => state == State.Playing;

    private GameObject startPanel;
    private GameObject gameOverPanel;
    private GameObject overlayPanel;
    private TextMeshProUGUI startText;
    private TextMeshProUGUI gameOverText;
    private TextMeshProUGUI overlayText;
    private bool overlayVisible;

    void Awake()
    {
        ResolveRefs();
        BuildUI();
        EnterStart();
    }

    void ResolveRefs()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (telemetry == null) telemetry = FindFirstObjectByType<SessionTelemetry>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (state != State.Start && kb.tKey.wasPressedThisFrame)
            ToggleOverlay();

        switch (state)
        {
            case State.Start:
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    StartRun();
                break;

            case State.Playing:
                if (playerHealth != null && playerHealth.CurrentHealth <= 0f)
                    TriggerGameOver();
                break;

            case State.GameOver:
                if (kb.rKey.wasPressedThisFrame)
                    Restart();
                break;
        }
    }

    void EnterStart()
    {
        state = State.Start;
        Time.timeScale = 0f;
        int best = PlayerPrefs.GetInt(BestFloorKey, 0);
        startText.text =
            $"{gameTitle}\n\n" +
            $"Descend as deep as you can.\n" +
            (best > 0 ? $"Best depth reached: Floor {best}\n\n" : "\n") +
            $"Press SPACE to begin";
        startPanel.SetActive(true);
        gameOverPanel.SetActive(false);
        SetOverlay(false);
    }

    void StartRun()
    {
        state = State.Playing;
        startPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    void TriggerGameOver()
    {
        state = State.GameOver;
        Time.timeScale = 0f;

        int reached = dungeon != null ? dungeon.CurrentFloor : 0;
        int best = PlayerPrefs.GetInt(BestFloorKey, 0);
        if (reached > best)
        {
            best = reached;
            PlayerPrefs.SetInt(BestFloorKey, best);
            PlayerPrefs.Save();
        }

        string summary =
            $"YOU DIED\n\n" +
            $"Floor reached: {reached}\n" +
            $"Best depth: {best}\n";

        if (!string.IsNullOrEmpty(ExtraSummary))
            summary += "\n" + ExtraSummary + "\n";

        summary +=
            $"\nPress T to view your run log (copy it into the form)\n" +
            $"Press R to restart";

        gameOverText.text = summary;
        gameOverPanel.SetActive(true);
    }

    void Restart()
    {

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void ToggleOverlay() => SetOverlay(!overlayVisible);

    void SetOverlay(bool visible)
    {
        overlayVisible = visible;
        if (overlayPanel == null) return;

        if (visible && telemetry != null)
        {
            string body = telemetry.SessionSummary;
            overlayText.text = "SESSION LOG  (press T to hide — copied to clipboard, paste into the form)\n\n" +
                               (string.IsNullOrWhiteSpace(body) ? "(no floors recorded yet)" : body);
            if (!string.IsNullOrWhiteSpace(body))
                GUIUtility.systemCopyBuffer = body;
        }
        overlayPanel.SetActive(visible);
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("GameStateCanvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        startPanel = BuildPanel(canvasObj.transform, new Color(0.04f, 0.05f, 0.08f, 1f), out startText, 54);
        gameOverPanel = BuildPanel(canvasObj.transform, new Color(0.10f, 0.02f, 0.02f, 0.96f), out gameOverText, 48);
        overlayPanel = BuildPanel(canvasObj.transform, new Color(0f, 0f, 0f, 0.85f), out overlayText, 26);

        overlayText.alignment = TextAlignmentOptions.TopLeft;
        overlayText.textWrappingMode = TextWrappingModes.NoWrap;

        startPanel.SetActive(false);
        gameOverPanel.SetActive(false);
        overlayPanel.SetActive(false);
    }

    GameObject BuildPanel(Transform parent, Color bg, out TextMeshProUGUI text, float fontSize)
    {
        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        RectTransform prt = panel.GetComponent<RectTransform>();
        StretchFull(prt);
        Image img = panel.AddComponent<Image>();
        img.color = bg;

        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(panel.transform, false);
        RectTransform trt = textObj.GetComponent<RectTransform>();
        StretchFull(trt);
        trt.offsetMin = new Vector2(60, 60);
        trt.offsetMax = new Vector2(-60, -60);

        text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.Normal;

        return panel;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
