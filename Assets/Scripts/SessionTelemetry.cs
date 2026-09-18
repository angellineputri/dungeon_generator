using System.Text;
#if !UNITY_WEBGL
using System.IO;
#endif
using UnityEngine;

/// <summary>
/// Per-floor session telemetry for the Build A / Build B evaluation.
///
/// SELF-CONTAINED BY DESIGN. This script modifies no existing script: it subscribes
/// to the already-public DungeonGenerator.OnFloorGenerated event and otherwise only
/// READS public state (PlayerHealth.CurrentHealth, FogOfWar.Explored,
/// DungeonGenerator.Grid). That makes it a clean single-file cherry-pick onto the
/// frozen baseline (commit d458e00) — it references only symbols that already exist
/// there. See the report for the full symbol list.
///
/// It records, per floor: floor number, % of WALKABLE tiles explored, seconds on the
/// floor, damage taken, effective heals (count of potion pickups that actually
/// restored HP), and whether the floor ended by Exit or Death.
///
///   - Floor boundaries come from OnFloorGenerated. The first fire starts floor 1;
///     every later fire means the previous floor was left via the exit, so it's
///     finalized as "Exit".
///   - Death is detected by CurrentHealth reaching 0. Dying does not regenerate a
///     floor (no game-over flow in the baseline), so there is no OnFloorGenerated to
///     ride on — it's caught by polling instead, which also keeps the death case
///     hook-free.
///   - Damage taken / effective heals are derived from frame-to-frame CurrentHealth
///     deltas: a drop is damage, a rise is an effective heal (a potion is the only
///     thing that raises HP in the baseline). A potion collected at full HP heals
///     nothing and is therefore not counted — "EffectiveHeals" is exactly the count
///     of heals that recovered HP, which is the intended metric, not a caveat.
///     IMPORTANT: this holds ONLY while nothing else raises CurrentHealth. If player
///     progression is added, it must raise maxHealth/attackDamage WITHOUT bumping
///     CurrentHealth, or level-ups would be miscounted here as heals.
///
/// Output: in the Editor, one appended row per floor in Assets/session_telemetry.csv
/// (compiled out under WebGL, exactly like DungeonGenerator/DifficultyTestHarness).
/// Under WebGL it Debug.Logs each row instead, and accumulates a copyable session
/// summary (header + all rows) exposed via SessionSummary for a game-over UI to show.
/// </summary>
public class SessionTelemetry : MonoBehaviour
{
    [Header("References (auto-found if left empty)")]
    public DungeonGenerator dungeon;
    public FogOfWar fog;
    public PlayerHealth playerHealth;

    private const string CsvHeader = "Floor,ExploredPct,SecondsOnFloor,DamageTaken,EffectiveHeals,Outcome";

    // --- current-floor accumulators ---
    private int currentFloorNumber;
    private float floorStartTime;
    private float damageThisFloor;
    private int effectiveHealsThisFloor;
    private float lastKnownHealth;
    // Exploration % is polled every frame and cached here, so a floor's final value
    // is the last one captured BEFORE FogOfWar.ResetFog (which also listens to
    // OnFloorGenerated) wipes the explored array — no dependence on listener order.
    private float lastExploredPct;
    private bool floorInProgress;
    private bool sessionEnded; // set on death; recording stops afterwards

    // Header + one line per floor, always accumulated so the game-over UI can show it
    // on any platform. This is the string the WebGL player copies into the form.
    private readonly StringBuilder summary = new StringBuilder();
    public string SessionSummary => summary.ToString();

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
    }

    // Fires on every floor (re)generation. Finalize the previous floor as an exit
    // (if one was in progress), then start fresh accumulators for the new floor.
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

        // Poll exploration against the CURRENT grid/fog every frame, so the cached
        // value is always the true end-of-floor figure by the time we finalize.
        lastExploredPct = ComputeExploredPct();

        if (playerHealth == null) return;

        float hp = playerHealth.CurrentHealth;
        float delta = hp - lastKnownHealth;
        if (delta < 0f) damageThisFloor += -delta;   // took damage
        else if (delta > 0f) effectiveHealsThisFloor += 1;  // HP rose => an effective heal
        lastKnownHealth = hp;

        if (hp <= 0f)
        {
            FinalizeFloor("Death");
            sessionEnded = true;
        }
    }

    // Fraction of walkable tiles that have been seen. Denominator counts ONLY
    // walkable tiles (grid value 0); numerator counts walkable tiles also flagged
    // explored by the fog.
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
                if (grid[x, y] != 0) continue; // walls don't count
                walkable++;
                // Guard bounds — the fog array can momentarily be a different size
                // than the grid during a floor reset.
                if (explored != null
                    && x < explored.GetLength(0) && y < explored.GetLength(1)
                    && explored[x, y])
                    seen++;
            }

        return walkable > 0 ? (100f * seen / walkable) : 0f;
    }

    void FinalizeFloor(string outcome)
    {
        floorInProgress = false;

        string row = string.Join(",",
            currentFloorNumber,
            lastExploredPct.ToString("F1"),
            (Time.time - floorStartTime).ToString("F1"),
            damageThisFloor.ToString("F1"),
            effectiveHealsThisFloor,
            outcome);

        summary.AppendLine(row);

#if UNITY_WEBGL
        Debug.Log("[SessionTelemetry] " + row);
#else
        AppendCsvRow(row);
#endif
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
