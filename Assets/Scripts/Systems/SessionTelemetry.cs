using System.Text;
#if !UNITY_WEBGL
using System.IO;
#endif
using UnityEngine;

public class SessionTelemetry : MonoBehaviour
{
    [Header("References (auto-found if left empty)")]
    public DungeonGenerator dungeon;
    public FogOfWar fog;
    public PlayerHealth playerHealth;
    public LevelProgression progression;

    private const string CsvHeader = "Floor,ExploredPct,SecondsOnFloor,DamageTaken,EffectiveHeals,Outcome";

    private int currentFloorNumber;
    private float floorStartTime;
    private float damageThisFloor;
    private int effectiveHealsThisFloor;
    private float lastKnownHealth;

    private float lastExploredPct;
    private bool floorInProgress;
    private bool sessionEnded;

    private readonly StringBuilder summary = new StringBuilder();

    private bool sessionHasPractice;
    public string SessionSummary =>
        (sessionHasPractice ? "[PRACTICE - EXCLUDE FROM STUDY]\n" : "") + summary.ToString();

    public event System.Action<int, float, string> OnFloorFinalized;

    void Awake()
    {
        summary.AppendLine(CsvHeader);
    }

    void OnEnable()
    {
        ResolveRefs();
        if (dungeon != null)
            dungeon.OnFloorGenerated += HandleFloorGenerated;
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated -= HandleFloorGenerated;
    }

    void ResolveRefs()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (fog == null) fog = FindFirstObjectByType<FogOfWar>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (progression == null) progression = FindFirstObjectByType<LevelProgression>();
    }

    void HandleFloorGenerated()
    {
        if (sessionEnded) return;

        if (floorInProgress)
            FinalizeFloor("Exit");

        currentFloorNumber = dungeon != null ? dungeon.CurrentFloor : currentFloorNumber + 1;
        floorStartTime = Time.time;
        damageThisFloor = 0f;
        effectiveHealsThisFloor = 0;
        lastExploredPct = 0f;
        lastKnownHealth = playerHealth != null ? playerHealth.CurrentHealth : 0f;
        floorInProgress = true;
    }

    void Update()
    {
        if (!floorInProgress || sessionEnded) return;

        lastExploredPct = ComputeExploredPct();

        if (playerHealth == null) return;

        float hp = playerHealth.CurrentHealth;
        float delta = hp - lastKnownHealth;
        if (delta < 0f) damageThisFloor += -delta;
        else if (delta > 0f) effectiveHealsThisFloor += 1;
        lastKnownHealth = hp;

        if (hp <= 0f)
        {
            FinalizeFloor("Death");
            sessionEnded = true;
        }
    }

    float ComputeExploredPct()
    {
        if (dungeon == null || dungeon.Grid == null) return lastExploredPct;

        int[,] grid = dungeon.Grid;
        int w = dungeon.GridWidth;
        int h = dungeon.GridHeight;
        bool[,] explored = fog != null ? fog.Explored : null;

        int walkable = 0, seen = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (grid[x, y] != 0) continue;
                walkable++;

                if (explored != null
                    && x < explored.GetLength(0) && y < explored.GetLength(1)
                    && explored[x, y])
                    seen++;
            }

        return walkable > 0 ? (100f * seen / walkable) : 0f;
    }

    public void FinalizeCurrentFloorAsCleared()
    {
        if (floorInProgress && !sessionEnded)
            FinalizeFloor("Exit");
    }

    public void ResetSession()
    {
        sessionEnded = false;
        floorInProgress = false;
    }

    void FinalizeFloor(string outcome)
    {
        floorInProgress = false;

        bool practice = progression != null && progression.CurrentModeKey == "Practice";
        if (practice) sessionHasPractice = true;
        string loggedOutcome = practice ? outcome + "-PRACTICE" : outcome;

        string row = string.Join(",",
            currentFloorNumber,
            lastExploredPct.ToString("F1"),
            (Time.time - floorStartTime).ToString("F1"),
            damageThisFloor.ToString("F1"),
            effectiveHealsThisFloor,
            loggedOutcome);

        summary.AppendLine(row);

#if UNITY_WEBGL
        Debug.Log("[SessionTelemetry] " + row);
#else
        AppendCsvRow(row);
#endif

        OnFloorFinalized?.Invoke(currentFloorNumber, damageThisFloor, outcome);
    }

#if !UNITY_WEBGL
    void AppendCsvRow(string row)
    {
        string path = Path.Combine(Application.dataPath, "session_telemetry.csv");
        bool isNew = !File.Exists(path);
        using (StreamWriter w = new StreamWriter(path, true))
        {
            if (isNew) w.WriteLine(CsvHeader);
            w.WriteLine(row);
        }
    }
#endif
}
