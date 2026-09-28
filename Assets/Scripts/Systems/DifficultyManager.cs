using UnityEngine;

public struct DifficultyParams
{
    public float enemyHpMultiplier;
    public float enemyDamageMultiplier;
    public int enemyCount;
    public float spawnInterval;
    public int minRoomSize;
    public int caIterations;
    public int minNodeSize;
    public int potionCount;
}

public static class DifficultyManager
{

    private const float HP_GROWTH_PER_FLOOR = 0.15f;
    private const float DAMAGE_GROWTH_PER_FLOOR = 0.10f;

    private static readonly int[] ENEMY_COUNT_TIERS = { 3, 5, 8, 11, 15, 19, 24, 29, 35, 42 };
    private const int ENEMY_COUNT_TIER_SIZE = 2;
    private const int ENEMY_COUNT_CAP = 90;

    private const int ENEMY_COUNT_LINEAR_START_FLOOR = 19;

    private static readonly float[] SPAWN_INTERVAL_TIERS = { 8f, 6f, 4.5f, 3.5f, 3f, 2.5f, 2.2f };
    private const int SPAWN_TIER_SIZE = 3;

    private const int BASE_MIN_ROOM_SIZE = 8;
    private const int MIN_ROOM_SIZE_FLOOR = 5;
    private const int ROOM_SIZE_STEP_EVERY = 3;

    private const int BASE_CA_ITERATIONS = 3;
    private const int CA_ITERATIONS_FLOOR = 2;
    private const int CA_STEP_EVERY = 4;

    private const int BASE_MIN_NODE_SIZE = 24;
    private const int MIN_NODE_SIZE_FLOOR = 14;
    private const int NODE_SIZE_STEP_EVERY = 3;

    private static readonly int[] POTION_COUNT_TIERS = { 3, 2, 1 };
    private const int POTION_TIER_SIZE = 3;

    public static DifficultyParams GetDifficultyParams(int floor)
    {
        floor = Mathf.Max(1, floor);

        DifficultyParams p = new DifficultyParams();

        p.enemyHpMultiplier = 1f + HP_GROWTH_PER_FLOOR * (floor - 1);
        p.enemyDamageMultiplier = 1f + DAMAGE_GROWTH_PER_FLOOR * (floor - 1);

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

        int spawnTier = Mathf.Min((floor - 1) / SPAWN_TIER_SIZE, SPAWN_INTERVAL_TIERS.Length - 1);
        p.spawnInterval = SPAWN_INTERVAL_TIERS[spawnTier];

        int roomSteps = (floor - 1) / ROOM_SIZE_STEP_EVERY;
        int minRoomSize = BASE_MIN_ROOM_SIZE - roomSteps;
        p.minRoomSize = Mathf.Max(MIN_ROOM_SIZE_FLOOR, minRoomSize);

        int caSteps = (floor - 1) / CA_STEP_EVERY;
        int caIterations = BASE_CA_ITERATIONS - caSteps;
        p.caIterations = Mathf.Max(CA_ITERATIONS_FLOOR, caIterations);

        int nodeSteps = (floor - 1) / NODE_SIZE_STEP_EVERY;
        int minNodeSize = BASE_MIN_NODE_SIZE - nodeSteps * 2;
        p.minNodeSize = Mathf.Max(MIN_NODE_SIZE_FLOOR, minNodeSize);

        int potionTier = Mathf.Min((floor - 1) / POTION_TIER_SIZE, POTION_COUNT_TIERS.Length - 1);
        p.potionCount = POTION_COUNT_TIERS[potionTier];

        return p;
    }
}
