using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class GameStateManager : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public DungeonGenerator dungeon;
    public PlayerHealth playerHealth;
    public SessionTelemetry telemetry;
    public LevelProgression progression;
    public PlayerMana playerMana;
    public FogOfWar fog;

    [Header("UI Assets (assign in Inspector)")]
    public Sprite panelSprite;
    public Sprite buttonSprite;
    public Sprite buttonPressedSprite;
    public Sprite starSprite;
    public TMP_FontAsset titleFont;
    public TMP_FontAsset bodyFont;

    [Header("Meta")]
    public string gameTitle = "DUNGEON";

    private enum State { MainMenu, Start, Playing, FloorCleared, Progression, Paused, GameOver }
    private State state = State.Start;

    public enum Mode { Hard, Normal, Practice }
    private Mode gameMode = Mode.Normal;

    // Practice: testers-only sandbox — every floor unlocked, fog off, nothing saved.
    private bool PracticeMode => gameMode == Mode.Practice;

    public string ExtraSummary { get; set; } = "";
    public bool IsPlaying => state == State.Playing;

    static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
    static readonly Color C_PANEL   = Hex(0x26, 0x2c, 0x40);
    static readonly Color C_TEXT    = Hex(0xee, 0xf1, 0xfb);
    static readonly Color C_TEXT2   = Hex(0x9a, 0xa3, 0xbf);
    static readonly Color C_AMBER   = Hex(0xf0, 0xc9, 0x4a);
    static readonly Color C_BRONZE  = Hex(0xc9, 0x7b, 0x3d);
    static readonly Color C_SILVER  = Hex(0xc7, 0xcc, 0xd9);
    static readonly Color C_GOLD    = Hex(0xf0, 0xc9, 0x4a);
    static readonly Color C_DIAMOND = Hex(0x7f, 0xe3, 0xe8);
    static readonly Color C_DIM     = Hex(0x3a, 0x40, 0x54);
    static readonly Color C_RED     = Hex(0xd0, 0x4a, 0x4a);
    static readonly Color C_FLOOR   = Hex(0x5f, 0x69, 0x7d);
    static readonly Color C_ACTIVE  = Hex(0xe0, 0x59, 0x6b);
    static readonly Color C_PRIMARY = Hex(0x3a, 0x45, 0x68);
    static readonly Color C_GHOST   = Hex(0x23, 0x28, 0x38);
    static readonly Color C_DANGER  = Hex(0x5a, 0x24, 0x30);
    static readonly Color C_NAVY    = Hex(0x17, 0x1b, 0x28);
    static readonly Color C_BTN     = Hex(0x32, 0x3a, 0x52);
    static readonly Color C_HINT    = Hex(0x6b, 0x73, 0x91);
    static readonly Color C_PRACTICE = Hex(0x5a, 0x7d, 0xa8);  // steel blue — testers-only mode, visually its own thing

    private GameObject startPanel, overlayPanel, howToPlayPanel;
    private TextMeshProUGUI startText, overlayText, modeHint;
    private Image normalChipImg, hardChipImg, practiceChipImg;
    private bool overlayVisible;

    private GameObject floorClearedPanel;
    private TextMeshProUGUI fcTitle, fcFloor, fcDamage, fcTier, fcFlavor;
    private Image[] fcStars;
    private Coroutine shimmer;

    private GameObject gameOverPanel, goIcon;
    private TextMeshProUGUI goStats, goTitle, goFellLine, goFloor;

    private GameObject progressionPanel;
    private Transform progressionGrid;
    private Image newestCard;
    private Coroutine progressionPulse;
    private TextMeshProUGUI progFloorsLine;
    private Image progNormalChipImg, progHardChipImg, progPracticeChipImg;

    private GameObject pausePanel;
    private GameObject pauseButton;

    void Awake()
    {
        ResolveRefs();
        BuildUI();
        SetMode(gameMode);
        EnterStart();
    }

    void ResolveRefs()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (telemetry == null) telemetry = FindFirstObjectByType<SessionTelemetry>();
        if (progression == null) progression = FindFirstObjectByType<LevelProgression>();
        if (playerMana == null) playerMana = FindFirstObjectByType<PlayerMana>();
        if (fog == null) fog = FindFirstObjectByType<FogOfWar>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (pauseButton != null) pauseButton.SetActive(state == State.Playing);

        if (state != State.Start && kb.tKey.wasPressedThisFrame)
            ToggleOverlay();

        switch (state)
        {
            case State.Start:
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    StartRun();
                break;
            case State.Playing:
                if (kb.escapeKey.wasPressedThisFrame) { Pause(); break; }
                if (playerHealth != null && playerHealth.CurrentHealth <= 0f)
                    TriggerGameOver();
                break;
            case State.Paused:
                if (kb.escapeKey.wasPressedThisFrame)
                    ResumeFromPause();
                break;
            case State.FloorCleared:
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    ContinueFromFloorCleared();
                break;
            case State.Progression:
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    AdvanceFromProgression();
                break;
            case State.GameOver:
                if (kb.rKey.wasPressedThisFrame)
                    RetryCurrentFloor();
                break;
        }
    }

    void EnterStart()
    {
        state = State.Start;
        Time.timeScale = 0f;
        RefreshModeUI();
        startPanel.SetActive(true);
        gameOverPanel.SetActive(false);
        floorClearedPanel.SetActive(false);
        progressionPanel.SetActive(false);
        pausePanel.SetActive(false);
        SetOverlay(false);
    }

    void StartRun()
    {
        startPanel.SetActive(false);
        OpenProgression();
    }

    void OpenProgression()
    {
        Time.timeScale = 0f;
        PopulateProgression();
        state = State.Progression;
        progressionPanel.SetActive(true);
        StartProgressionPulse();
    }

    void TriggerGameOver()
    {
        state = State.GameOver;
        Time.timeScale = 0f;

        int reached = dungeon != null ? dungeon.CurrentFloor : 0;

        goTitle.text = "YOU DIED";
        goTitle.color = C_ACTIVE;
        goIcon.SetActive(true);
        goFellLine.gameObject.SetActive(true);
        goFloor.gameObject.SetActive(true);
        goFloor.color = C_TEXT;
        goFloor.text = $"FLOOR {reached}";
        goStats.text = "Press T for your run log (paste into the form)";
        gameOverPanel.SetActive(true);
    }

    void RetryCurrentFloor()
    {
        StopShimmer();
        floorClearedPanel.SetActive(false);
        gameOverPanel.SetActive(false);
        progressionPanel.SetActive(false);
        pausePanel.SetActive(false);

        if (telemetry != null) telemetry.ResetSession();
        if (playerMana != null) playerMana.ResetToFull();

        state = State.Playing;
        Time.timeScale = 1f;
        ApplyMode();

        int floor = dungeon != null ? dungeon.CurrentFloor : 1;
        if (dungeon != null) dungeon.JumpToFloor(floor);
    }

    void GoToMainMenu()
    {
        gameOverPanel.SetActive(false);
        floorClearedPanel.SetActive(false);
        EnterStart();
    }

    void ApplyMode()
    {
        if (fog != null) fog.SetFog(gameMode == Mode.Hard);
    }

    void SetMode(Mode m)
    {
        gameMode = m;
        if (progression != null) progression.CurrentModeKey = m.ToString();
        RefreshModeUI();
        if (state == State.Progression)
        {
            PopulateProgression();
            StartProgressionPulse();   // re-attach the pulse to the freshly-rebuilt "next" card
        }
    }

    void ToggleMode() => SetMode(gameMode == Mode.Hard ? Mode.Normal : Mode.Hard);

    void RefreshModeUI()
    {
        // Each chip highlights only when its own mode is selected. Hard/Normal use the
        // red C_ACTIVE highlight; Practice uses its own steel-blue so it reads as a
        // separate, testers-only thing.
        Color hardCol     = gameMode == Mode.Hard     ? C_ACTIVE   : C_GHOST;
        Color normalCol   = gameMode == Mode.Normal   ? C_ACTIVE   : C_GHOST;
        Color practiceCol = gameMode == Mode.Practice ? C_PRACTICE : C_GHOST;

        if (hardChipImg != null) hardChipImg.color = hardCol;
        if (normalChipImg != null) normalChipImg.color = normalCol;
        if (practiceChipImg != null) practiceChipImg.color = practiceCol;
        if (progHardChipImg != null) progHardChipImg.color = hardCol;
        if (progNormalChipImg != null) progNormalChipImg.color = normalCol;
        if (progPracticeChipImg != null) progPracticeChipImg.color = practiceCol;

        if (modeHint != null)
            modeHint.text = gameMode == Mode.Hard ? "Fog of war active"
                          : gameMode == Mode.Practice ? "Practice — all floors unlocked"
                          : "Full visibility, no fog";
    }

    void ShowHowToPlay()
    {
        if (howToPlayPanel != null) howToPlayPanel.SetActive(true);
    }

    void HideHowToPlay()
    {
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
    }

    void BuildHowToPlayPanel(Transform canvas)
    {
        howToPlayPanel = BuildBackdrop(canvas);
        howToPlayPanel.GetComponent<Image>().color = C_NAVY;
        Transform p = BuildBeveledPanel(howToPlayPanel.transform, new Vector2(760, 680)).transform;

        const float divW = 680f;

        var title = BuildTMP(p, titleFont, 26, C_GOLD, new Vector2(0, 306), new Vector2(700, 50), TextAlignmentOptions.Center);
        title.text = "HOW TO PLAY";

        // --- CONTROLS ---
        SectionHeader(p, "CONTROLS", 268, divW);
        BuildKeyCap(p, new Vector2(-320, 220), new Vector2(36, 36), "W");
        BuildKeyCap(p, new Vector2(-278, 220), new Vector2(36, 36), "A");
        BuildKeyCap(p, new Vector2(-236, 220), new Vector2(36, 36), "S");
        BuildKeyCap(p, new Vector2(-194, 220), new Vector2(36, 36), "D");
        var moveLbl = BuildTMP(p, bodyFont, 20, C_TEXT, new Vector2(40, 220), new Vector2(320, 30), TextAlignmentOptions.Left);
        moveLbl.text = "Move";
        BuildKeyCap(p, new Vector2(-268, 176), new Vector2(120, 36), "SPACE");
        var atkLbl = BuildTMP(p, bodyFont, 20, C_TEXT, new Vector2(40, 176), new Vector2(320, 30), TextAlignmentOptions.Left);
        atkLbl.text = "Attack  (costs mana)";

        // --- OBJECTIVE ---
        SectionHeader(p, "OBJECTIVE", 138, divW);
        var obj = BuildTMP(p, bodyFont, 19, C_TEXT, new Vector2(0, 96), new Vector2(divW, 56), TextAlignmentOptions.TopLeft);
        obj.text =
            "Reach the exit stairs to clear each floor.\n" +
            "Less damage taken = higher medal.";

        // --- MEDALS ---
        SectionHeader(p, "MEDALS", 44, divW);
        BuildMedalRow(p, 10,  1, C_BRONZE,  "BRONZE");
        BuildMedalRow(p, -20, 2, C_SILVER,  "SILVER");
        BuildMedalRow(p, -50, 3, C_GOLD,    "GOLD");
        BuildMedalRow(p, -80, 3, C_DIAMOND, "DIAMOND  —  flawless, no attacks");

        // --- DIFFICULTY ---
        SectionHeader(p, "DIFFICULTY", -122, divW);
        var hardLine = BuildTMP(p, bodyFont, 19, C_ACTIVE, new Vector2(0, -162), new Vector2(divW, 26), TextAlignmentOptions.Left);
        hardLine.text = "HARD  —  fog of war limits your vision";
        var normLine = BuildTMP(p, bodyFont, 19, C_TEXT, new Vector2(0, -190), new Vector2(divW, 26), TextAlignmentOptions.Left);
        normLine.text = "NORMAL  —  the full floor is always visible";

        BuildButton(p, "BACK", new Vector2(0, -288), new Vector2(240, 60), HideHowToPlay, C_PRIMARY).fontSize = 26;

        howToPlayPanel.SetActive(false);
    }

    void SectionHeader(Transform parent, string label, float y, float dividerWidth)
    {
        var h = BuildTMP(parent, titleFont, 16, C_GOLD, new Vector2(0, y), new Vector2(dividerWidth, 26), TextAlignmentOptions.Left);
        h.text = label;
        BuildDivider(parent, new Vector2(0, y - 16), dividerWidth);
    }

    void BuildKeyCap(Transform parent, Vector2 pos, Vector2 size, string label)
    {
        GameObject k = new GameObject("KeyCap", typeof(RectTransform), typeof(Image));
        k.transform.SetParent(parent, false);
        RectTransform rt = k.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        Image img = k.GetComponent<Image>();
        img.sprite = buttonSprite;
        img.type = Image.Type.Sliced;
        img.color = C_BTN;
        var t = BuildTMP(k.transform, bodyFont, size.y * 0.5f, C_TEXT, Vector2.zero, size, TextAlignmentOptions.Center);
        t.text = label;
    }

    void BuildMedalRow(Transform parent, float y, int filled, Color col, string name)
    {
        const float ss = 24f, gap = 6f;
        float rowW = ss * 3 + gap * 2;
        float cx = -320f + rowW / 2f;
        Image[] stars = BuildStarRow(parent, new Vector2(cx, y), ss, gap);
        for (int i = 0; i < 3; i++) stars[i].color = i < filled ? col : C_DIM;
        var lbl = BuildTMP(parent, bodyFont, 21, col, new Vector2(40, y), new Vector2(360, 28), TextAlignmentOptions.Left);
        lbl.text = name;
    }

    void Pause()
    {
        if (state != State.Playing) return;
        state = State.Paused;
        Time.timeScale = 0f;
        pausePanel.SetActive(true);
    }

    void ResumeFromPause()
    {
        pausePanel.SetActive(false);
        state = State.Playing;
        Time.timeScale = 1f;
    }

    public void NotifyFloorCleared()
    {
        if (state != State.Playing) return;

        if (telemetry != null) telemetry.FinalizeCurrentFloorAsCleared();

        state = State.FloorCleared;
        Time.timeScale = 0f;

        int floor = progression != null ? progression.LastFloor : (dungeon != null ? dungeon.CurrentFloor : 0);
        LevelProgression.Tier tier = progression != null ? progression.LastTier : LevelProgression.Tier.Bronze;
        float lossPct = progression != null ? progression.LastLossPct : 0f;

        fcFloor.text = $"FLOOR {floor}";
        fcDamage.text = $"Damage taken: {lossPct * 100f:F0}%";
        fcTier.text = tier.ToString().ToUpper();
        fcTier.color = TierColor(tier);

        bool diamond = tier == LevelProgression.Tier.Diamond;
        fcFlavor.gameObject.SetActive(diamond);
        if (diamond) fcFlavor.text = "No enemy was harmed this floor!";

        SetStars(tier);
        floorClearedPanel.SetActive(true);
    }

    void ContinueFromFloorCleared()
    {
        StopShimmer();
        floorClearedPanel.SetActive(false);
        OpenProgression();
    }

    void StartProgressionPulse()
    {
        if (progressionPulse != null) StopCoroutine(progressionPulse);
        if (newestCard != null) progressionPulse = StartCoroutine(PulseNewest());
    }

    IEnumerator PulseNewest()
    {
        Image card = newestCard;
        while (card != null && progressionPanel.activeSelf)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 3.5f) + 1f) * 0.5f;
            card.color = Color.Lerp(new Color(0.82f, 0.74f, 0.45f), new Color(1f, 0.98f, 0.82f), t);
            yield return null;
        }
    }

    void SelectFloor(int floor)
    {
        bool unlocked = PracticeMode ||
                        progression == null ||
                        progression.HasCleared(floor) ||
                        floor == progression.HighestClearedFloor + 1;
        if (!unlocked) return;

        progressionPanel.SetActive(false);
        if (telemetry != null) telemetry.ResetSession();
        state = State.Playing;
        Time.timeScale = 1f;
        ApplyMode();
        if (dungeon != null) dungeon.JumpToFloor(floor);
    }

    void AdvanceFromProgression()
    {
        if (progression != null && progression.DungeonComplete)
        {
            EnterComplete();
            return;
        }
        progressionPanel.SetActive(false);
        if (telemetry != null) telemetry.ResetSession();
        state = State.Playing;
        Time.timeScale = 1f;
        ApplyMode();
        int next = (progression != null ? progression.HighestClearedFloor : 0) + 1;
        if (dungeon != null) dungeon.JumpToFloor(next);
    }

    void EnterComplete()
    {
        progressionPanel.SetActive(false);
        state = State.GameOver;
        Time.timeScale = 0f;
        int cap = progression != null ? progression.floorCap : 25;
        goTitle.text = "DUNGEON COMPLETE!";
        goTitle.color = C_DIAMOND;
        goIcon.SetActive(false);
        goFellLine.gameObject.SetActive(false);
        goFloor.gameObject.SetActive(false);
        goStats.text =
            $"You cleared all {cap} floors.\n\n" +
            $"Press T for your run log (paste into the form)";
        gameOverPanel.SetActive(true);
    }

    Color TierColor(LevelProgression.Tier t) =>
        t == LevelProgression.Tier.Bronze ? C_BRONZE :
        t == LevelProgression.Tier.Silver ? C_SILVER :
        t == LevelProgression.Tier.Diamond ? C_DIAMOND : C_GOLD;

    void AddDropShadow(TextMeshProUGUI tmp)
    {
        Material mat = tmp.fontMaterial;
        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 1f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -1f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
        mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.15f);
    }

    GameObject BuildDivider(Transform parent, Vector2 pos, float width)
    {
        GameObject d = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        d.transform.SetParent(parent, false);
        RectTransform rt = d.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, 2f);
        rt.anchoredPosition = pos;
        d.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
        return d;
    }

    GameObject BuildIconBox(Transform parent, Vector2 pos, float box, string glyph, Color glyphColor)
    {
        GameObject g = new GameObject("IconBox", typeof(RectTransform), typeof(Image));
        g.transform.SetParent(parent, false);
        RectTransform rt = g.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(box, box);
        rt.anchoredPosition = pos;
        g.GetComponent<Image>().color = C_GHOST;
        var t = BuildTMP(g.transform, titleFont, box * 0.5f, glyphColor, Vector2.zero, new Vector2(box, box), TextAlignmentOptions.Center);
        t.text = glyph;
        return g;
    }

    void SetStars(LevelProgression.Tier tier)
    {
        StopShimmer();
        int filled = tier == LevelProgression.Tier.Bronze ? 1
                   : tier == LevelProgression.Tier.Silver ? 2 : 3;
        Color fill = tier == LevelProgression.Tier.Bronze ? C_BRONZE
                   : tier == LevelProgression.Tier.Silver ? C_SILVER
                   : tier == LevelProgression.Tier.Diamond ? C_DIAMOND : C_GOLD;

        for (int i = 0; i < fcStars.Length; i++)
            fcStars[i].color = i < filled ? fill : C_DIM;

        if (tier == LevelProgression.Tier.Diamond)
            shimmer = StartCoroutine(Shimmer());
    }

    void StopShimmer()
    {
        if (shimmer != null) { StopCoroutine(shimmer); shimmer = null; }
    }

    IEnumerator Shimmer()
    {
        while (true)
        {
            float t = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f;
            Color c = Color.Lerp(C_DIAMOND * 0.55f, Color.white, t);
            c.a = 1f;
            foreach (var s in fcStars) s.color = c;
            yield return null;
        }
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

        overlayPanel = BuildPanel(canvasObj.transform, new Color(0f, 0f, 0f, 0.85f), out overlayText, 26);
        overlayText.alignment = TextAlignmentOptions.TopLeft;
        overlayText.textWrappingMode = TextWrappingModes.NoWrap;

        BuildFloorClearedScreen(canvasObj.transform);
        BuildGameOverScreen(canvasObj.transform);
        BuildProgressionScreen(canvasObj.transform);
        BuildMainMenuScreen(canvasObj.transform);
        BuildPauseScreen(canvasObj.transform);
        BuildPauseButton(canvasObj.transform);

        overlayPanel.SetActive(false);
    }

    void BuildPauseScreen(Transform canvas)
    {
        pausePanel = BuildBackdrop(canvas);
        Transform p = BuildBeveledPanel(pausePanel.transform, new Vector2(680, 480)).transform;

        var title = BuildTMP(p, titleFont, 40, C_AMBER, new Vector2(0, 160), new Vector2(600, 80), TextAlignmentOptions.Center);
        title.text = "PAUSED";
        BuildButton(p, "RESUME", new Vector2(0, 45), new Vector2(340, 84), ResumeFromPause);
        BuildButton(p, "RETRY FLOOR", new Vector2(0, -50), new Vector2(340, 84), RetryCurrentFloor);
        BuildButton(p, "MAIN MENU", new Vector2(0, -145), new Vector2(340, 84), GoToMainMenu);

        pausePanel.SetActive(false);
    }

    void BuildPauseButton(Transform canvas)
    {
        GameObject b = new GameObject("PauseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        b.transform.SetParent(canvas, false);
        RectTransform rt = b.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(64, 64);
        rt.anchoredPosition = new Vector2(16, -16);

        Image img = b.GetComponent<Image>();
        img.sprite = buttonSprite;
        img.type = Image.Type.Sliced;
        img.color = C_BTN;

        Button btn = b.GetComponent<Button>();
        btn.targetGraphic = img;
        if (buttonSprite != null && buttonPressedSprite != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            SpriteState ss = btn.spriteState;
            ss.pressedSprite = buttonPressedSprite;
            ss.highlightedSprite = buttonSprite;
            ss.selectedSprite = buttonSprite;
            btn.spriteState = ss;
        }
        btn.onClick.AddListener(() => Pause());

        var lbl = BuildTMP(b.transform, bodyFont, 26, C_TEXT, Vector2.zero, new Vector2(64, 64), TextAlignmentOptions.Center);
        lbl.text = "II";

        pauseButton = b;
        pauseButton.SetActive(false);
    }

    void BuildMainMenuScreen(Transform canvas)
    {
        startPanel = BuildBackdrop(canvas);
        startPanel.GetComponent<Image>().color = C_NAVY;
        Transform bg = startPanel.transform;

        // Title + tagline sit on the navy backdrop; 56px major outer gaps to the panel.
        var titleTop = BuildTMP(bg, titleFont, 46, C_TEXT, new Vector2(0, 265), new Vector2(720, 70), TextAlignmentOptions.Center);
        titleTop.text = "DUNGEON";
        var titleBot = BuildTMP(bg, titleFont, 46, C_GOLD, new Vector2(0, 210), new Vector2(720, 70), TextAlignmentOptions.Center);
        titleBot.text = "GENERATOR";
        startText = BuildTMP(bg, bodyFont, 26, C_TEXT2, new Vector2(0, 158), new Vector2(700, 44), TextAlignmentOptions.Center);
        startText.text = "Descend as deep as you can.";

        // Narrow panel: 420 wide (~1/3 of a 1280 canvas), 32px inner padding.
        RectTransform pr = BuildBeveledPanel(startPanel.transform, new Vector2(420, 330)).rectTransform;
        pr.anchoredPosition = new Vector2(0, -34);
        Transform p = pr;

        // DIFFICULTY cluster: label -> chips -> hint, tight 10px gaps.
        var diffLabel = BuildTMP(p, bodyFont, 24, C_TEXT2, new Vector2(0, 121), new Vector2(356, 34), TextAlignmentOptions.Center);
        diffLabel.text = "DIFFICULTY";
        diffLabel.characterSpacing = 8f;

        // Three chips across the 356px inner width: 108 wide, 8px gaps, at x -116/0/116.
        var normalChip = BuildButton(p, "NORMAL", new Vector2(-116, 76), new Vector2(108, 46), () => SetMode(Mode.Normal), C_GHOST);
        var hardChip = BuildButton(p, "HARD", new Vector2(0, 76), new Vector2(108, 46), () => SetMode(Mode.Hard), C_GHOST);
        var practiceChip = BuildButton(p, "PRACTICE", new Vector2(116, 76), new Vector2(108, 46), () => SetMode(Mode.Practice), C_GHOST);
        normalChip.fontSize = 18; hardChip.fontSize = 18; practiceChip.fontSize = 18;
        normalChipImg = normalChip.transform.parent.GetComponent<Image>();
        hardChipImg = hardChip.transform.parent.GetComponent<Image>();
        practiceChipImg = practiceChip.transform.parent.GetComponent<Image>();

        modeHint = BuildTMP(p, bodyFont, 20, C_HINT, new Vector2(0, 33), new Vector2(356, 30), TextAlignmentOptions.Center);
        modeHint.text = "Fog of war active";

        // 22px major gaps below the cluster to PLAY, then to HOW TO PLAY.
        BuildButton(p, "PLAY", new Vector2(0, -30), new Vector2(356, 62), StartRun, C_PRIMARY).fontSize = 30;
        BuildButton(p, "HOW TO PLAY", new Vector2(0, -106), new Vector2(356, 46), ShowHowToPlay, C_GHOST).fontSize = 22;

        BuildHowToPlayPanel(canvas);
        startPanel.SetActive(false);
    }

    void BuildProgressionScreen(Transform canvas)
    {
        // Mockup structure: the title, the "X / Y FLOORS CLEARED" line and the
        // DIFFICULTY chips sit directly on the plain navy backdrop with NO border
        // (like the Main Menu title floating outside its panel). ONLY the floor-card
        // grid gets the beveled panel. BACK / NEXT FLOOR sit below it, on the backdrop.
        // Vertical gaps from the mockup: 8 (title->floors), 14 (->chips), 26 (->grid),
        // 26 (grid->buttons). Centered stack spans y 491..-491 within the 1080 canvas.
        progressionPanel = BuildBackdrop(canvas);
        progressionPanel.GetComponent<Image>().color = C_NAVY;
        Transform bg = progressionPanel.transform;

        // Title "PROGRESSION": Press Start 2P, 30, white (#eef1fb) — on the backdrop.
        var title = BuildTMP(bg, titleFont, 30, C_TEXT, new Vector2(0, 471), new Vector2(900, 40), TextAlignmentOptions.Center);
        title.text = "PROGRESSION";

        // "X / Y FLOORS CLEARED": 8px below title, 24, #9aa3bf — on the backdrop.
        progFloorsLine = BuildTMP(bg, bodyFont, 24, C_TEXT2, new Vector2(0, 428), new Vector2(900, 30), TextAlignmentOptions.Center);
        progFloorsLine.text = "0 / 25 FLOORS CLEARED";

        // Difficulty chips: 14px below the floors line — on the backdrop. Interactive
        // (mirror Main Menu); switching mode re-populates the grid (see SetMode).
        var normalChip = BuildButton(bg, "NORMAL", new Vector2(-184, 376), new Vector2(172, 46), () => SetMode(Mode.Normal), C_GHOST);
        var hardChip = BuildButton(bg, "HARD", new Vector2(0, 376), new Vector2(172, 46), () => SetMode(Mode.Hard), C_GHOST);
        var practiceChip = BuildButton(bg, "PRACTICE", new Vector2(184, 376), new Vector2(172, 46), () => SetMode(Mode.Practice), C_GHOST);
        normalChip.fontSize = 22; hardChip.fontSize = 22; practiceChip.fontSize = 22;
        progNormalChipImg = normalChip.transform.parent.GetComponent<Image>();
        progHardChipImg = hardChip.transform.parent.GetComponent<Image>();
        progPracticeChipImg = practiceChip.transform.parent.GetComponent<Image>();

        // The ONLY beveled panel: wraps the grid. Width = grid content width =
        // 6*152 + 5*14 + 2*28 = 1038px. 26px below the chips (top edge y=327).
        const float gridPanelW = 1038f;   // 912 cells + 70 gaps + 56 padding
        const float gridPanelH = 720f;
        const float gridPanelY = -33f;    // top 327, bottom -393
        RectTransform gprt = BuildBeveledPanel(bg, new Vector2(gridPanelW, gridPanelH)).rectTransform;
        gprt.anchoredPosition = new Vector2(0f, gridPanelY);
        Transform gp = gprt;

        // Scroll view fills the panel; the GridLayoutGroup's 28px padding keeps cells
        // off the bevel. 25 floors = 5 rows (~872px) exceed the 720px panel -> scrolls.
        GameObject scrollObj = new GameObject("Grid", typeof(RectTransform), typeof(ScrollRect));
        scrollObj.transform.SetParent(gp, false);
        StretchFull(scrollObj.GetComponent<RectTransform>());
        ScrollRect sr = scrollObj.GetComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollObj.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0); // transparent raycast target so wheel/drag scroll works over empty areas
        sr.viewport = viewport.GetComponent<RectTransform>();

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;
        GridLayoutGroup glg = content.GetComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(152, 152);
        glg.spacing = new Vector2(14, 14);
        glg.padding = new RectOffset(28, 28, 28, 28);
        glg.childAlignment = TextAnchor.UpperCenter;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 6;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = crt;
        progressionGrid = content.transform;

        // Slim vertical scrollbar inside the panel's right 28px padding gutter.
        const float sbW = 12f;
        GameObject sbObj = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        sbObj.transform.SetParent(gp, false);
        RectTransform sbrt = sbObj.GetComponent<RectTransform>();
        sbrt.anchorMin = sbrt.anchorMax = new Vector2(0.5f, 0.5f);
        sbrt.sizeDelta = new Vector2(sbW, gridPanelH - 56f);
        sbrt.anchoredPosition = new Vector2(gridPanelW / 2f - 8f - sbW / 2f, 0f);
        sbObj.GetComponent<Image>().color = C_GHOST;
        Scrollbar sb = sbObj.GetComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(sbObj.transform, false);
        StretchFull(handle.GetComponent<RectTransform>());
        handle.GetComponent<Image>().color = C_FLOOR;
        sb.targetGraphic = handle.GetComponent<Image>();
        sb.handleRect = handle.GetComponent<RectTransform>();
        sr.verticalScrollbar = sb;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        // Bottom row (on the backdrop, below the grid panel): BACK + NEXT FLOOR, 18px gap.
        BuildButton(bg, "BACK", new Vector2(-149, -455), new Vector2(200, 72), GoToMainMenu, C_GHOST);
        BuildButton(bg, "NEXT FLOOR >", new Vector2(109, -455), new Vector2(280, 72), AdvanceFromProgression, C_PRIMARY);

        progressionPanel.SetActive(false);
    }

    void PopulateProgression()
    {
        for (int i = progressionGrid.childCount - 1; i >= 0; i--)
            Destroy(progressionGrid.GetChild(i).gameObject);

        newestCard = null;
        int cap = progression != null ? progression.floorCap : 25;
        int clearedCount = progression != null ? progression.HighestClearedFloor : 0;
        if (progFloorsLine != null)
            progFloorsLine.text = PracticeMode
                ? "PRACTICE — ALL FLOORS UNLOCKED"
                : $"{clearedCount} / {cap} FLOORS CLEARED";
        for (int floor = 1; floor <= cap; floor++)
            BuildFloorCard(progressionGrid, floor);
    }

    void BuildFloorCard(Transform grid, int floor)
    {
        GameObject card = new GameObject($"Floor{floor}", typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(grid, false);
        Image cimg = card.GetComponent<Image>();
        int f = floor;
        card.GetComponent<Button>().onClick.AddListener(() => SelectFloor(f));
        cimg.sprite = panelSprite;
        cimg.type = Image.Type.Sliced;

        // Practice: every floor is a plain unlocked card (no stars/NEXT/lock, all clickable).
        bool cleared = !PracticeMode && progression != null && progression.HasCleared(floor);
        bool isNext = !PracticeMode && progression != null && floor == progression.HighestClearedFloor + 1;
        bool locked = !PracticeMode && !cleared && !isNext;

        if (panelSprite != null)
            cimg.color = isNext ? new Color(1f, 0.95f, 0.72f) : (locked ? new Color(0.5f, 0.53f, 0.62f) : Color.white);
        else
            cimg.color = isNext ? C_AMBER : (locked ? C_DIM : C_PANEL);
        if (isNext) newestCard = cimg;

        var num = BuildTMP(card.transform, bodyFont, 24, locked ? C_DIM : (cleared ? C_TEXT : C_TEXT2),
                           new Vector2(0, 20), new Vector2(100, 28), TextAlignmentOptions.Center);
        num.text = floor.ToString();

        if (cleared)
        {
            LevelProgression.Tier t = progression.GetBestTier(floor);
            int filled = t == LevelProgression.Tier.Bronze ? 1
                       : t == LevelProgression.Tier.Silver ? 2 : 3;
            Color fill = t == LevelProgression.Tier.Bronze ? C_BRONZE
                       : t == LevelProgression.Tier.Silver ? C_SILVER
                       : t == LevelProgression.Tier.Diamond ? C_DIAMOND : C_GOLD;
            BuildMiniStars(card.transform, new Vector2(0, -18), 18, 3, filled, fill);
        }
        else if (isNext)
        {
            var lbl = BuildTMP(card.transform, bodyFont, 16, C_AMBER,
                               new Vector2(0, -18), new Vector2(100, 24), TextAlignmentOptions.Center);
            lbl.text = "NEXT";
        }
        else if (locked)
        {
            GameObject lk = new GameObject("Lock", typeof(RectTransform), typeof(Image));
            lk.transform.SetParent(card.transform, false);
            RectTransform lrt = lk.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(28, 28);
            lrt.anchoredPosition = new Vector2(0, -18);
            Image limg = lk.GetComponent<Image>();
            limg.sprite = GetLockSprite();
            limg.color = C_DIM;
            limg.preserveAspect = true;
        }
    }

    private Sprite cachedLock;
    Sprite GetLockSprite()
    {
        if (cachedLock != null) return cachedLock;
        int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0), w = Color.white;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, clear);
        for (int y = 4; y <= 18; y++)
            for (int x = 8; x <= 24; x++)
                tex.SetPixel(x, y, w);
        for (int y = 18; y < 28; y++)
            for (int x = 6; x < 27; x++)
            {
                float dx = x - 16f, dy = y - 18f, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 8f && d >= 5f) tex.SetPixel(x, y, w);
            }
        for (int y = 8; y <= 14; y++)
            for (int x = 15; x <= 17; x++)
                tex.SetPixel(x, y, clear);
        tex.Apply();
        cachedLock = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
        return cachedLock;
    }

    void BuildMiniStars(Transform parent, Vector2 center, float size, float gap, int filled, Color fill)
    {
        float totalW = size * 3 + gap * 2;
        float startX = -totalW / 2f + size / 2f;
        for (int i = 0; i < 3; i++)
        {
            GameObject s = new GameObject($"S{i}", typeof(RectTransform), typeof(Image));
            s.transform.SetParent(parent, false);
            RectTransform rt = s.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(center.x + startX + i * (size + gap), center.y);
            Image img = s.GetComponent<Image>();
            img.sprite = starSprite;
            img.color = i < filled ? fill : C_DIM;
            img.preserveAspect = true;
        }
    }

    void BuildFloorClearedScreen(Transform canvas)
    {
        floorClearedPanel = BuildBackdrop(canvas);
        Transform p = BuildBeveledPanel(floorClearedPanel.transform, new Vector2(780, 680)).transform;

        fcTitle = BuildTMP(p, titleFont, 40, C_TEXT, new Vector2(0, 268), new Vector2(700, 80), TextAlignmentOptions.Center);
        fcTitle.text = "FLOOR CLEARED";
        AddDropShadow(fcTitle);

        fcFloor = BuildTMP(p, bodyFont, 40, C_TEXT, new Vector2(0, 200), new Vector2(700, 56), TextAlignmentOptions.Center);
        fcStars = BuildStarRow(p, new Vector2(0, 118), 88, 26);
        fcTier = BuildTMP(p, titleFont, 24, C_GOLD, new Vector2(0, 46), new Vector2(700, 44), TextAlignmentOptions.Center);
        fcFlavor = BuildTMP(p, bodyFont, 22, C_DIAMOND, new Vector2(0, 4), new Vector2(700, 40), TextAlignmentOptions.Center);
        BuildDivider(p, new Vector2(0, -34), 560f);
        fcDamage = BuildTMP(p, bodyFont, 30, C_TEXT2, new Vector2(0, -78), new Vector2(700, 50), TextAlignmentOptions.Center);

        BuildButton(p, "RETRY", new Vector2(-175, -222), new Vector2(300, 92), RetryCurrentFloor, C_GHOST);
        BuildButton(p, "CONTINUE", new Vector2(175, -222), new Vector2(300, 92), ContinueFromFloorCleared, C_PRIMARY);

        floorClearedPanel.SetActive(false);
    }

    void BuildGameOverScreen(Transform canvas)
    {
        gameOverPanel = BuildBackdrop(canvas);
        Transform p = BuildBeveledPanel(gameOverPanel.transform, new Vector2(780, 560)).transform;

        goTitle = BuildTMP(p, titleFont, 46, C_ACTIVE, new Vector2(0, 210), new Vector2(700, 90), TextAlignmentOptions.Center);
        goTitle.text = "YOU DIED";
        AddDropShadow(goTitle);

        goIcon = BuildIconBox(p, new Vector2(0, 118), 64f, "X", C_ACTIVE);
        goFellLine = BuildTMP(p, bodyFont, 24, C_TEXT2, new Vector2(0, 58), new Vector2(700, 40), TextAlignmentOptions.Center);
        goFellLine.text = "You fell on";
        goFloor = BuildTMP(p, titleFont, 30, C_TEXT, new Vector2(0, 12), new Vector2(700, 56), TextAlignmentOptions.Center);
        goStats = BuildTMP(p, bodyFont, 24, C_TEXT2, new Vector2(0, -56), new Vector2(700, 90), TextAlignmentOptions.Center);

        BuildButton(p, "MAIN MENU", new Vector2(-175, -200), new Vector2(300, 92), GoToMainMenu, C_GHOST);
        BuildButton(p, "RETRY", new Vector2(175, -200), new Vector2(300, 92), RetryCurrentFloor, C_DANGER);

        gameOverPanel.SetActive(false);
    }

    GameObject BuildBackdrop(Transform parent)
    {
        GameObject bg = new GameObject("Screen", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        StretchFull(bg.GetComponent<RectTransform>());
        bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        return bg;
    }

    Image BuildBeveledPanel(Transform parent, Vector2 size)
    {
        GameObject panel = new GameObject("Bevel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        Image img = panel.GetComponent<Image>();
        img.sprite = panelSprite;
        img.type = Image.Type.Sliced;
        img.color = panelSprite != null ? Color.white : C_PANEL;
        return img;
    }

    TextMeshProUGUI BuildTMP(Transform parent, TMP_FontAsset font, float size, Color color,
                             Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align)
    {
        GameObject t = new GameObject("Text", typeof(RectTransform));
        t.transform.SetParent(parent, false);
        RectTransform rt = t.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = pos;
        TextMeshProUGUI tmp = t.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        return tmp;
    }

    TextMeshProUGUI BuildButton(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick, Color? bg = null)
    {
        GameObject b = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        b.transform.SetParent(parent, false);
        RectTransform rt = b.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        Image img = b.GetComponent<Image>();
        img.sprite = buttonSprite;
        img.type = Image.Type.Sliced;
        img.color = bg ?? C_BTN;

        Button btn = b.GetComponent<Button>();
        btn.targetGraphic = img;
        if (buttonSprite != null && buttonPressedSprite != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            SpriteState ss = btn.spriteState;
            ss.pressedSprite = buttonPressedSprite;
            ss.highlightedSprite = buttonSprite;
            ss.selectedSprite = buttonSprite;
            btn.spriteState = ss;
        }
        btn.onClick.AddListener(() => onClick());

        TextMeshProUGUI tmp = BuildTMP(b.transform, bodyFont, 28, C_TEXT, Vector2.zero, size, TextAlignmentOptions.Center);
        tmp.text = label;
        return tmp;
    }

    Image[] BuildStarRow(Transform parent, Vector2 center, float starSize, float gap)
    {
        Image[] stars = new Image[3];
        float totalW = starSize * 3 + gap * 2;
        float startX = -totalW / 2f + starSize / 2f;
        for (int i = 0; i < 3; i++)
        {
            GameObject s = new GameObject($"Star{i}", typeof(RectTransform), typeof(Image));
            s.transform.SetParent(parent, false);
            RectTransform rt = s.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(starSize, starSize);
            rt.anchoredPosition = new Vector2(center.x + startX + i * (starSize + gap), center.y);
            Image img = s.GetComponent<Image>();
            img.sprite = starSprite;
            img.color = C_DIM;
            img.preserveAspect = true;
            stars[i] = img;
        }
        return stars;
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
