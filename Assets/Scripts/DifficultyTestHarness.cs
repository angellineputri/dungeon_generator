using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class DifficultyTestHarness : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;

    [Header("Test Settings")]
    public int floorsToTest = 100;

    private bool running = false;

    private struct FloorResult
    {
        public int floor, rooms, roomTargetMin, roomTargetMax, genAttempts;
        public long genTimeMicros;
        public bool connected;
        public int connectivityAttempts;
        public int branchingRejections;
        public string layoutProfile;
        public int enemyBudget, enemiesActual, potionBudget, potionsActual;
        public float hpMultiplier, damageMultiplier;
        public int minRoomSize, caIterations, minNodeSize;
        public float spawnInterval;
        public int roomsOnCriticalPath, roomsOffPath, keyRoomDistance;
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

#if !UNITY_WEBGL
        string csvPath = Path.Combine(Application.dataPath, "difficulty_test_log.csv");
        StreamWriter csv = new StreamWriter(csvPath, false);
        csv.WriteLine("Floor,Rooms,RoomTargetMin,RoomTargetMax,GenAttempts,GenTimeMicros,Connected," +
                      "EnemyBudget,EnemiesActual,PotionBudget,PotionsActual," +
                      "HPMultiplier,DamageMultiplier,MinRoomSize,CAIterations,MinNodeSize,SpawnInterval,ConnectivityAttempts," +
                      "BranchingRejections,LayoutProfile,RoomsOnCriticalPath,RoomsOffPath,KeyRoomDistance");
        try
        {
#endif
            for (int floor = 1; floor <= floorsToTest; floor++)
            {
                dungeon.JumpToFloor(floor);

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
                    connectivityAttempts = dungeon.LastConnectivityAttempts,
                    branchingRejections = dungeon.LastBranchingRejections,
                    layoutProfile = dungeon.LastLayoutProfile.ToString(),
                    enemyBudget = diff.enemyCount,
                    enemiesActual = actualEnemies,
                    potionBudget = diff.potionCount,
                    potionsActual = actualPotions,
                    hpMultiplier = diff.enemyHpMultiplier,
                    damageMultiplier = diff.enemyDamageMultiplier,
                    minRoomSize = diff.minRoomSize,
                    caIterations = diff.caIterations,
                    minNodeSize = diff.minNodeSize,
                    spawnInterval = diff.spawnInterval,
                    roomsOnCriticalPath = dungeon.LastRoomsOnCriticalPath,
                    roomsOffPath = dungeon.LastRoomsOffPath,
                    keyRoomDistance = dungeon.LastKeyRoomDistance
                };
                results.Add(r);

#if !UNITY_WEBGL
                csv.WriteLine(string.Join(",",
                    r.floor, r.rooms, r.roomTargetMin, r.roomTargetMax, r.genAttempts, r.genTimeMicros,
                    r.connected, r.enemyBudget, r.enemiesActual, r.potionBudget, r.potionsActual,
                    r.hpMultiplier.ToString("F2"), r.damageMultiplier.ToString("F2"),
                    r.minRoomSize, r.caIterations, r.minNodeSize, r.spawnInterval.ToString("F1"),
                    r.connectivityAttempts, r.branchingRejections, r.layoutProfile, r.roomsOnCriticalPath, r.roomsOffPath, r.keyRoomDistance
                ));
#endif

                if (r.genAttempts > 20)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor} took {r.genAttempts} generation attempts — target range {r.roomTargetMin}-{r.roomTargetMax} may be too tight.");
                if (!r.connected)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor} was NOT fully connected — this should never happen.");
                if (System.Math.Abs(r.enemiesActual - r.enemyBudget) > r.enemyBudget / 2)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor}: enemy budget was {r.enemyBudget} but only {r.enemiesActual} actually spawned — rooms may be running out.");

                int keyIdx = dungeon.KeyRoomIndex;
                if (keyIdx < 0)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor}: no key room placed (off-path={r.roomsOffPath}).");
                else if (keyIdx == dungeon.PlayerSpawnRoomIndex || keyIdx == dungeon.ExitRoomIndex)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor}: key room {keyIdx} collides with spawn/exit — INVARIANT VIOLATED.");
                else if (dungeon.RoomGraph != null && dungeon.RoomGraph.DistanceFromSpawn(keyIdx) < 0)
                    Debug.LogWarning($"[DifficultyTestHarness] Floor {floor}: key room {keyIdx} is unreachable from spawn — INVARIANT VIOLATED.");

                yield return null;
            }
#if !UNITY_WEBGL
        }
        finally
        {
            csv.Dispose();
        }
#endif

        WriteHumanReadableSummary(results);

#if !UNITY_WEBGL
        Debug.Log($"[DifficultyTestHarness] Done. CSV: {csvPath}");
        Debug.Log($"[DifficultyTestHarness] Summary: {Path.Combine(Application.dataPath, "difficulty_test_summary.txt")}");
#else
        Debug.Log("[DifficultyTestHarness] Done. File output is disabled on WebGL.");
#endif
        running = false;
    }

    void WriteHumanReadableSummary(List<FloorResult> results)
    {
#if !UNITY_WEBGL
        string path = Path.Combine(Application.dataPath, "difficulty_test_summary.txt");
#endif
        var sb = new StringBuilder();

        sb.AppendLine("Difficulty Test Harness — Summary");
        sb.AppendLine($"Floors tested: {results.Count} (Floor 1 to Floor {results.Count})");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine();

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

        sb.AppendLine();
        int offPathZeroOrOne = results.Count(r => r.roomsOffPath <= 1);
        sb.AppendLine($"Rooms off critical path: min {results.Min(r => r.roomsOffPath)}, " +
                       $"max {results.Max(r => r.roomsOffPath)}, avg {results.Average(r => r.roomsOffPath):F1}");
        sb.AppendLine($"Floors with <=1 off-path room: {offPathZeroOrOne}/{results.Count} " +
                       $"({(100.0 * offPathZeroOrOne / results.Count):F0}%). If this is high, layouts are near-linear " +
                       $"and the key-and-lock exploration premise needs rethinking.");
        sb.AppendLine($"Key room distance from spawn: min {results.Min(r => r.keyRoomDistance)}, " +
                       $"max {results.Max(r => r.keyRoomDistance)}, avg {results.Average(r => r.keyRoomDistance):F1}");

#if !UNITY_WEBGL
        File.WriteAllText(path, sb.ToString());
#endif
    }
}
