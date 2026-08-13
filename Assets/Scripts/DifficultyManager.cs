using UnityEngine;

/// <summary>
/// Computes floor-based difficulty parameters. Pure calculation only.
/// Does not reference EnemySpawner, DungeonGenerator, or any other
/// gameplay system directly, so scaling curves can be tuned here
/// without touching integration code elsewhere.
/// </summary>
public struct DifficultyParams
{
    public float enemyHpMultiplier;
    public float enemyDamageMultiplier;
    public int enemyCount;
    public float spawnInterval;      // seconds between spawns, lower = harder
    public int minRoomSize;
    public int caIterations;
}

public static class DifficultyManager
{
    // --- Tuning constants (adjust here, nowhere else) ---
    private const float HP_GROWTH_PER_FLOOR = 0.15f;
    private const float DAMAGE_GROWTH_PER_FLOOR = 0.10f;

    // Enemy count now uses explicit tiers rather than a linear step, so it can
    // accelerate faster in the later floors. Old curve topped out at 7 by floor
    // 9-10, which never exceeded typical room count (6-10) — enemies would rarely
    // stack more than 1 per room. This curve reaches 15 by floor 9-10, so once
    // EnemySpawner distributes that budget across ~6-10 rooms, later floors
    // reliably produce 2+ enemies in multiple rooms, not just 1-per-room at best.
    private static readonly int[] ENEMY_COUNT_TIERS = { 3, 5, 8, 11, 15 };
    private const int ENEMY_COUNT_TIER_SIZE = 2;

    // Explicit tier values (floors 1-3, 4-6, 7-9, 10+) rather than a formula,
    // since the approved steps (8 -> 6 -> 4.5 -> 3.5) aren't evenly spaced.
    private static readonly float[] SPAWN_INTERVAL_TIERS = { 8f, 6f, 4.5f, 3.5f };
    private const int SPAWN_TIER_SIZE = 3;

    private const int BASE_MIN_ROOM_SIZE = 8;
    private const int MIN_ROOM_SIZE_FLOOR = 6;         // hard minimum room size
    private const int ROOM_SIZE_STEP_EVERY = 3;

    private const int BASE_CA_ITERATIONS = 3;
    private const int CA_ITERATIONS_FLOOR = 2;         // hard minimum smoothing passes
    private const int CA_STEP_EVERY = 4;

    public static DifficultyParams GetDifficultyParams(int floor)
    {
        floor = Mathf.Max(1, floor);

        DifficultyParams p = new DifficultyParams();

        // Enemy HP / damage: linear per floor
        p.enemyHpMultiplier = 1f + HP_GROWTH_PER_FLOOR * (floor - 1);
        p.enemyDamageMultiplier = 1f + DAMAGE_GROWTH_PER_FLOOR * (floor - 1);

        // Enemy count: explicit tier lookup (floors 1-2, 3-4, 5-6, 7-8, 9-10+)
        int countTier = Mathf.Min((floor - 1) / ENEMY_COUNT_TIER_SIZE, ENEMY_COUNT_TIERS.Length - 1);
        p.enemyCount = ENEMY_COUNT_TIERS[countTier];

        // Spawn rate: explicit tier lookup (floors 1-3, 4-6, 7-9, 10+)
        int spawnTier = Mathf.Min((floor - 1) / SPAWN_TIER_SIZE, SPAWN_INTERVAL_TIERS.Length - 1);
        p.spawnInterval = SPAWN_INTERVAL_TIERS[spawnTier];

        // Min room size: threshold every N floors, shrinks, clamped
        int roomSteps = (floor - 1) / ROOM_SIZE_STEP_EVERY;
        int minRoomSize = BASE_MIN_ROOM_SIZE - roomSteps;
        p.minRoomSize = Mathf.Max(MIN_ROOM_SIZE_FLOOR, minRoomSize);

        // CA iterations: threshold every N floors, decreases, clamped
        int caSteps = (floor - 1) / CA_STEP_EVERY;
        int caIterations = BASE_CA_ITERATIONS - caSteps;
        p.caIterations = Mathf.Max(CA_ITERATIONS_FLOOR, caIterations);

        return p;
    }
}