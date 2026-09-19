using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Minimal run lifecycle for the study build: Start screen -> Playing -> Game Over ->
/// Restart, plus the press-T session-summary overlay that testers use to submit data.
///
/// Self-contained by the same logic as the other systems: it builds its own full-screen
/// Canvas at runtime (no scene UI to wire, no imported art), auto-resolves its references
/// via FindFirstObjectByType, and detects death by polling PlayerHealth.CurrentHealth <= 0
/// (the baseline has no game-over hook — same reason SessionTelemetry polls). Gameplay is
/// gated with Time.timeScale so nothing moves behind the Start/Game Over screens.
///
/// Best-floor is persisted here via PlayerPrefs because the Start screen has to show it;
/// the richer run summary (enemies killed / upgrades / weapons) is pushed in by a separate
/// RunSummary component via ExtraSummary, so this stays independent of that later work.
/// </summary>
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

    // Extra text a RunSummary component can inject onto the game-over screen.
    // Left empty until that component exists, so this class has no dependency on it.
    public string ExtraSummary { get; set; } = "";

    public bool IsPlaying => state == State.Playing;

    // --- UI, all built in Awake ---
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

        // Press-T session summary is available whenever a run is in progress or over.
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

    // --- State transitions ---

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
        // Full reset — reloads everything fresh. timeScale is restored on the new
        // scene's Start screen (Awake -> EnterStart sets it to 0, StartRun to 1).
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // --- press-T overlay ---

    void ToggleOverlay() => SetOverlay(!overlayVisible);

    void SetOverlay(bool visible)
    {
        overlayVisible = visible;
        if (overlayPanel == null) return;

        if (visible && telemetry != null)
        {
            string body = telemetry.SessionSummary;
            overlayText.text = "SESSION LOG  (press T to hide)\n\n" +
                               (string.IsNullOrWhiteSpace(body) ? "(no floors recorded yet)" : body);
        }
        overlayPanel.SetActive(visible);
    }

    // --- Runtime UI construction ---

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("GameStateCanvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above the gameplay HUD
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        startPanel = BuildPanel(canvasObj.transform, new Color(0.04f, 0.05f, 0.08f, 1f), out startText, 54);
        gameOverPanel = BuildPanel(canvasObj.transform, new Color(0.10f, 0.02f, 0.02f, 0.96f), out gameOverText, 48);
        overlayPanel = BuildPanel(canvasObj.transform, new Color(0f, 0f, 0f, 0.85f), out overlayText, 26);

        // The session log is long and left-aligned/monospace-ish for readability.
        overlayText.alignment = TextAlignmentOptions.TopLeft;
        overlayText.textWrappingMode = TextWrappingModes.NoWrap;

        startPanel.SetActive(false);
        gameOverPanel.SetActive(false);
        overlayPanel.SetActive(false);
    }

    // Full-screen dark panel with a single centered text child.
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
