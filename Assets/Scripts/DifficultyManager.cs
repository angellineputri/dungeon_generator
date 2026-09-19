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
    public int minNodeSize;      // controls BSP split depth, i.e. room COUNT (separate from minRoomSize, which controls room dimensions)
    public int potionCount;      // health potions to spawn this floor
}

public static class DifficultyManager
{
    // --- Tuning constants (adjust here, nowhere else) ---
    private const float HP_GROWTH_PER_FLOOR = 0.15f;
    private const float DAMAGE_GROWTH_PER_FLOOR = 0.10f;

    // Enemy count uses explicit tiers through floor 19, then grows linearly
    // beyond that (see ENEMY_COUNT_LINEAR_START_FLOOR below). The tier array
    // alone used to plateau hard at 42 from floor 19 onward, confirmed via the
    // 100-floor harness — every floor from 19 to 100 produced an identical
    // enemyCount, since Mathf.Min was clamping to the last array index forever.
    private static readonly int[] ENEMY_COUNT_TIERS = { 3, 5, 8, 11, 15, 19, 24, 29, 35, 42 };
    private const int ENEMY_COUNT_TIER_SIZE = 2;
    private const int ENEMY_COUNT_CAP = 90;
    // Floor at which the tier array runs out (floor 19, tier index 9) — enemy
    // count grows +1 per floor beyond this point instead of freezing at 42.
    private const int ENEMY_COUNT_LINEAR_START_FLOOR = 19;

    // Extended alongside enemy count for the same reason — floors 10+ were all
    // stuck at 3.5s forever. Slows its own rate of change near the end (3.5 -> 3
    // -> 2.5 -> 2.2) rather than continuing to drop sharply, since spawn timing
    // approaching 0 would eventually break rather than just get harder.
    private static readonly float[] SPAWN_INTERVAL_TIERS = { 8f, 6f, 4.5f, 3.5f, 3f, 2.5f, 2.2f };
    private const int SPAWN_TIER_SIZE = 3;

    private const int BASE_MIN_ROOM_SIZE = 8;
    private const int MIN_ROOM_SIZE_FLOOR = 5;         // was 6 — one more step of room-size escalation
    private const int ROOM_SIZE_STEP_EVERY = 3;

    private const int BASE_CA_ITERATIONS = 3;
    private const int CA_ITERATIONS_FLOOR = 2;         // hard minimum smoothing passes
    private const int CA_STEP_EVERY = 4;

    // Controls BSP split depth (room COUNT), separate from minRoomSize (room
    // dimensions). BASE lowered 26 -> 24: at 26 floor 1 naturally produced only 4
    // rooms, which could never satisfy the roomCountMin=5 gate, so floors 1-2
    // thrashed to the 100-attempt retry ceiling AND shipped with 0-1 off-path rooms
    // (key fell on the critical path). 24 is measured to yield ~7 rooms with 2
    // off-path on floor 1, clearing the gate and giving real exploration on the
    // floors testers see first. STEP_EVERY raised 2 -> 3 so the descent reaches
    // MIN_NODE_SIZE_FLOOR (14) at floor 16 instead of 13, extending the range over
    // which structural difficulty scaling operates. This schedule is deliberately
    // paired with DungeonGenerator's roomCountMin base (5): a desync between the
    // node-size schedule and the room-count target previously drove floors into
    // the retry ceiling.
    private const int BASE_MIN_NODE_SIZE = 24;
    private const int MIN_NODE_SIZE_FLOOR = 14;         // reached at ~floor 16 with STEP_EVERY = 3
    private const int NODE_SIZE_STEP_EVERY = 3;

    // Potions get scarcer as floors get harder, per the original design (limited
    // per-floor healing to force active decisions rather than passive stockpiling).
    // Floor-clamped at 1 so the game never becomes literally unwinnable.
    private static readonly int[] POTION_COUNT_TIERS = { 3, 2, 1 };
    private const int POTION_TIER_SIZE = 3;

    public static DifficultyParams GetDifficultyParams(int floor)
    {
        floor = Mathf.Max(1, floor);

        DifficultyParams p = new DifficultyParams();

        // Enemy HP / damage: linear per floor
        p.enemyHpMultiplier = 1f + HP_GROWTH_PER_FLOOR * (floor - 1);
        p.enemyDamageMultiplier = 1f + DAMAGE_GROWTH_PER_FLOOR * (floor - 1);

        // Enemy count: tier lookup through floor 19, then +1 enemy per floor
        // beyond that, capped at ENEMY_COUNT_CAP.
        int enemyCount;
        if (floor <= ENEMY_COUNT_LINEAR_START_FLOOR)
        {
            int countTier = (floor - 1) / ENEMY_COUNT_TIER_SIZE;
            enemyCount = ENEMY_COUNT_TIERS[countTier];
        }
        else
        {
            int floorsPastStart = floor - ENEMY_COUNT_LINEAR_START_FLOOR;
            enemyCount = ENEMY_COUNT_TIERS[ENEMY_COUNT_TIERS.Length - 1] + floorsPastStart;
        }
        p.enemyCount = Mathf.Min(enemyCount, ENEMY_COUNT_CAP);

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

        // Min node size: threshold every N floors, shrinks, clamped — this is
        // what actually grows room COUNT on deeper floors, separate from room size.
        int nodeSteps = (floor - 1) / NODE_SIZE_STEP_EVERY;
        int minNodeSize = BASE_MIN_NODE_SIZE - nodeSteps * 2;
        p.minNodeSize = Mathf.Max(MIN_NODE_SIZE_FLOOR, minNodeSize);

        // Potion count: explicit tier lookup, floors 1-3 / 4-6 / 7-10+
        int potionTier = Mathf.Min((floor - 1) / POTION_TIER_SIZE, POTION_COUNT_TIERS.Length - 1);
        p.potionCount = POTION_COUNT_TIERS[potionTier];

        return p;
    }
}