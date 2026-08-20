using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Press L to run a batch test: jumps through floors 1-100 (JumpToFloor, which
/// each generates a fresh layout and fires OnFloorGenerated — so EnemySpawner
/// and PotionSpawner actually populate each floor for real, not simulated),
/// and logs one row per floor covering everything needed to judge:
///
///   PLAYABILITY — did every floor generate cleanly (GenAttempts low, not
///   hitting the 100-attempt ceiling), is connectivity always true, did the
///   spawners actually place close to their target enemy/potion counts (a
///   large gap between budget and actual means rooms ran out to place them in).
///
///   SCALABILITY — do EnemyCount, HPMultiplier, DamageMultiplier, RoomCount,
///   MinRoomSize, MinNodeSize all show a genuine upward/downward trend across
///   the 100 rows rather than flatlining partway through (the exact problem
///   this harness exists to catch, per the floor-19 plateau found earlier).
///
/// Writes two files to the project's Assets folder:
///   - difficulty_test_log.csv         raw data, one row per floor, for charting
///   - difficulty_test_summary.txt     aligned, human-readable table + a summary
///                                     block (min/max/avg per column) — this is
///                                     the one to screenshot for the report.
///
/// One frame is yielded between floors so 100 back-to-back generations don't
/// freeze the editor in a single frame.
/// </summary>
public class DifficultyTestHarness : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;

    [Header("Test Settings")]
    public int floorsToTest = 100;

    private bool running = false;

    // One row of results, kept in memory so the summary pass can compute
    // min/max/avg after the full run finishes, instead of streaming straight
    // to disk with no way to aggregate at the end.
    private struct FloorResult
    {
        public int floor, rooms, roomTargetMin, roomTargetMax, genAttempts;
        public long genTimeMicros;
        public bool connected;
        public int enemyBudget, enemiesActual, potionBudget, potionsActual;
        public float hpMultiplier, damageMultiplier;
        public int minRoomSize, caIterations, minNodeSize;
        public float spawnInterval;
    }

    void Update()
    {
        if (Keyboard.current.lKey.wasPressedThisFrame && !running)
            StartCoroutine(RunTest());
    }

    IEnumerator RunTest()
    {
        if (dungeon == null)
        {
            Debug.LogWarning("[DifficultyTestHarness] No DungeonGenerator assigned — cannot run.");
            yield break;
        }

        running = true;
        Debug.Log($"[DifficultyTestHarness] Starting batch test: floors 1-{floorsToTest}...");

        var results = new List<FloorResult>(floorsToTest);
        string csvPath = Path.Combine(Application.dataPath, "difficulty_test_log.csv");

        using (StreamWriter csv = new StreamWriter(csvPath, false))
        {
            csv.WriteLine("Floor,Rooms,RoomTargetMin,RoomTargetMax,GenAttempts,GenTimeMicros,Connected," +
                          "EnemyBudget,EnemiesActual,PotionBudget,PotionsActual," +
                          "HPMultiplier,DamageMultiplier,MinRoomSize,CAIterations,MinNodeSize,SpawnInterval");

            for (int floor = 1; floor <= floorsToTest; floor++)
            {
                dungeon.JumpToFloor(floor);

                // Wait one frame before counting — Destroy() is deferred to end of
                // frame in Unity, so counting immediately would double-count the
                // previous floor's not-yet-actually-removed enemies/potions on top
                // of the new floor's.
                yield return null;

                DifficultyParams diff = DifficultyManager.GetDifficultyParams(floor);
                int actualEnemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).Length;
                int actualPotions = FindObjectsByType<PotionPickup>(FindObjectsSortMode.None).Length;

                var r = new FloorResult
                {
                    floor = floor,
                    rooms = dungeon.Rooms.Count,
                    roomTargetMin = dungeon.LastRoomCountMin,
                    roomTargetMax = dungeon.LastRoomCountMax,
                    genAttempts = dungeon.LastGenAttempts,
                    genTimeMicros = dungeon.LastGenTimeMicros,
                    connected = dungeon.LastConnected,
                    enemyBudget = diff.enemyCount,
                    enemiesActual = actualEnemies,
                    potionBudget = diff.potionCount,
                    potionsActual = actualPotions,
                    hpMultiplier = diff.enemyHpMultiplier,
                    damageMultiplier = diff.enemyDamageMultiplier,
                    minRoomSize = diff.minRoomSize,
                    caIterations = diff.caIterations,
                    minNodeSize = diff.minNodeSize,
                    spawnInterval = diff.spawnInterval
                };
                results.Add(r);

                csv.WriteLine(string.Join(",",
                    r.floor, r.rooms, r.roomTargetMin, r.roomTargetMax, r.genAttempts, r.genTimeMicros,
                    r.connected, r.enemyBudget, r.enemiesActual, r.potionBudget, r.potionsActual,
                    r.hpMultiplier.ToString("F2"), r.damageMultiplier.ToString("F2"),
                    r.minRoomSize, r.caIterations, r.minNodeSize, r.spawnInterval.ToString("F1")
                ));

                if (r.genAttempts > 20)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor} took {r.genAttempts} generation attempts — target range {r.roomTargetMin}-{r.roomTargetMax} may be too tight.");
                if (!r.connected)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor} was NOT fully connected — this should never happen.");
                if (System.Math.Abs(r.enemiesActual - r.enemyBudget) > r.enemyBudget / 2)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor}: enemy budget was {r.enemyBudget} but only {r.enemiesActual} actually spawned — rooms may be running out.");

                yield return null;
            }
        }

        WriteHumanReadableSummary(results);

        Debug.Log($"[DifficultyTestHarness] Done. CSV: {csvPath}");
        Debug.Log($"[DifficultyTestHarness] Summary: {Path.Combine(Application.dataPath, "difficulty_test_summary.txt")}");
        running = false;
    }

    // Aligned plain-text table plus a min/max/avg summary block. Deliberately
    // NOT comma-separated — this is the version meant to be read directly or
    // screenshotted for the report, not parsed by a spreadsheet.
    void WriteHumanReadableSummary(List<FloorResult> results)
    {
        string path = Path.Combine(Application.dataPath, "difficulty_test_summary.txt");
        var sb = new StringBuilder();

        sb.AppendLine("Difficulty Test Harness — Summary");
        sb.AppendLine($"Floors tested: {results.Count} (Floor 1 to Floor {results.Count})");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine();

        // Sample every 10th floor for the table body so it stays readable —
        // the full detail is still in the CSV for anyone who wants every row.
        sb.AppendLine("Sampled floors (every 10th, plus first and last):");
        sb.AppendLine($"{"Floor",-7}{"Rooms",-8}{"Attempts",-10}{"Connected",-11}{"Enemies",-10}{"Potions",-9}{"HP x",-7}{"Dmg x",-7}");
        sb.AppendLine(new string('-', 78));

        var sampleFloors = new HashSet<int> { 1, results.Count };
        for (int f = 10; f <= results.Count; f += 10) sampleFloors.Add(f);

        foreach (var r in results.Where(r => sampleFloors.Contains(r.floor)).OrderBy(r => r.floor))
        {
            sb.AppendLine(
                $"{r.floor,-7}{r.rooms,-8}{r.genAttempts,-10}{(r.connected ? "Yes" : "NO"),-11}" +
                $"{r.enemiesActual + "/" + r.enemyBudget,-10}{r.potionsActual + "/" + r.potionBudget,-9}" +
                $"{r.hpMultiplier.ToString("F2"),-7}{r.damageMultiplier.ToString("F2"),-7}"
            );
        }

        sb.AppendLine();
        sb.AppendLine(new string('=', 78));
        sb.AppendLine("Summary across all floors:");
        sb.AppendLine();

        int connectedCount = results.Count(r => r.connected);
        sb.AppendLine($"Connectivity: {connectedCount}/{results.Count} floors fully connected " +
                       $"({(100.0 * connectedCount / results.Count):F0}%)");

        sb.AppendLine($"Generation attempts: min {results.Min(r => r.genAttempts)}, " +
                       $"max {results.Max(r => r.genAttempts)}, " +
                       $"avg {results.Average(r => r.genAttempts):F1}");

        sb.AppendLine($"Generation time (µs): min {results.Min(r => r.genTimeMicros)}, " +
                       $"max {results.Max(r => r.genTimeMicros)}, " +
                       $"avg {results.Average(r => r.genTimeMicros):F1}");

        sb.AppendLine($"Room count: min {results.Min(r => r.rooms)}, " +
                       $"max {results.Max(r => r.rooms)}, " +
                       $"avg {results.Average(r => r.rooms):F1}");

        sb.AppendLine($"Enemy count: floor 1 = {results.First().enemyBudget}, " +
                       $"floor {results.Count} = {results.Last().enemyBudget} " +
                       $"(cap = 90)");

        int firstFlatFloor = -1;
        for (int i = 1; i < results.Count; i++)
        {
            if (results[i].enemyBudget == results[i - 1].enemyBudget &&
                results[i].floor > 20 && firstFlatFloor == -1)
            {
                // only flag if it stays flat for a long stretch, not a normal
                // same-tier neighbour — checked 5 floors ahead
                bool staysFlat = true;
                for (int j = i; j < Mathf.Min(i + 5, results.Count); j++)
                    if (results[j].enemyBudget != results[i].enemyBudget) staysFlat = false;
                if (staysFlat) firstFlatFloor = results[i].floor;
            }
        }
        sb.AppendLine(firstFlatFloor == -1
            ? "Enemy count plateau: none detected — count keeps increasing across the full range."
            : $"Enemy count plateau: WARNING — count stopped increasing from floor {firstFlatFloor} onward.");

        int mismatchCount = results.Count(r => System.Math.Abs(r.enemiesActual - r.enemyBudget) > r.enemyBudget / 2);
        sb.AppendLine($"Enemy spawn mismatches (>50% under budget): {mismatchCount}/{results.Count} floors");

        File.WriteAllText(path, sb.ToString());
    }
}