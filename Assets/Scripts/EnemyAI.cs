using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;
    public Transform player;

    [Header("AI Settings")]
    public float detectionRadius = 20f;
    public float loseRadius = 28f;

    [Header("Territory / Leash")]
    [Tooltip("How many tiles beyond its home room's edges this enemy can chase into " +
             "(covers the corridor mouth just outside the room). Once the player moves " +
             "further than this from the home room, the enemy gives up and returns home.")]
    public int leashMargin = 3;

    [Header("Path Recalculation")]
    [Tooltip("Minimum time between any two recalculations, no matter what triggers them.")]
    public float minRecalcInterval = 0.12f;
    [Tooltip("Fallback recalculation interval used when the player's tile hasn't changed.")]
    public float pathRecalcInterval = 0.4f;

    public float moveSpeed = 3f;
    public float patrolSpeedMultiplier = 0.5f;
    public float closeRangeDistance = 2f;
    [Tooltip("Arrival radius for close combat. Once within this distance the enemy holds position instead of seeking, preventing per-frame overshoot jitter around the player. Keep below closeRangeDistance so it still deals contact damage while parked.")]
    public float stopDistance = 0.6f;
    public float collisionRadius = 0.15f;

    [Header("Difficulty-scaled stats")]
    [Tooltip("Base values before difficulty scaling. EnemySpawner calls ApplyDifficulty() " +
             "after spawning to multiply these by the current floor's DifficultyParams.")]
    public float baseHP = 20f;
    public float baseDamage = 5f;
    private float currentHP;
    private float currentDamage;
    public float CurrentHP => currentHP;
    public float CurrentDamage => currentDamage;

    public void ApplyDifficulty(float hpMultiplier, float damageMultiplier)
    {
        currentHP = baseHP * hpMultiplier;
        currentDamage = baseDamage * damageMultiplier;
    }

    public void TakeDamage(float amount)
    {
        currentHP -= amount;

        if (currentHP <= 0f)
            Die();
        else
            StartCoroutine(HitFlash());
    }

    void Die()
    {
        Destroy(gameObject);
    }

    IEnumerator HitFlash()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) yield break;

        Color original = sr.color;
        sr.color = Color.white;
        yield return new WaitForSeconds(0.08f);
        if (sr != null)
            sr.color = original;
    }

    [Header("Spawn Override")]
    [Tooltip("Set by EnemySpawner right after Instantiate, before Start() runs, so this " +
             "enemy's home room is chosen by the spawner instead of picked randomly.")]
    public RectInt? forcedSpawnRoom = null;

    private enum State { Patrol, Chase, Returning }
    private State currentState = State.Patrol;

    private RectInt homeRoom;
    private Vector2Int HomeCenter => new Vector2Int(
        homeRoom.x + homeRoom.width / 2,
        homeRoom.y + homeRoom.height / 2
    );

    private List<Vector2Int> currentPath;
    private int pathIndex;
    private float recalcTimer;
    private float recalcCooldown;
    private Vector2Int patrolTarget;
    private Vector2Int lastPlayerGridPos;
    private bool isReady = false;

    void Start()
    {
        currentHP = baseHP;
        currentDamage = baseDamage;

        damageTickTimer = damageTickInterval;
        StartCoroutine(SpawnAtValidPosition());
    }

    IEnumerator SpawnAtValidPosition()
    {
        yield return null;
        while (dungeon.Rooms == null || dungeon.Rooms.Count == 0)
            yield return null;

        homeRoom = forcedSpawnRoom ?? dungeon.Rooms[Random.Range(0, dungeon.Rooms.Count)];

        Vector2Int center = new Vector2Int(
            homeRoom.x + homeRoom.width / 2,
            homeRoom.y + homeRoom.height / 2
        );

        transform.position = dungeon.tilemapCA.transform.position + new Vector3(center.x, center.y, 0f);

        PickNewPatrolTarget();
        RecalculatePathTo(patrolTarget);
        isReady = true;
    }

    void Update()
    {
        if (!isReady) return;

        Vector2Int playerGridPos = WorldToGrid(player.position);
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        bool playerInOwnRoom = IsInsideRoom(playerGridPos, dungeon.PlayerSpawnRoom);
        bool playerInLeash = IsWithinLeash(playerGridPos) && !playerInOwnRoom;

        State previousState = currentState;

        switch (currentState)
        {
            case State.Patrol:
                if (distToPlayer <= detectionRadius && playerInLeash)
                    currentState = State.Chase;
                break;

            case State.Chase:

                if (!playerInLeash || distToPlayer > loseRadius)
                    currentState = State.Returning;
                break;

            case State.Returning:

                if (distToPlayer <= detectionRadius && playerInLeash)
                    currentState = State.Chase;
                break;
        }

        bool justSwitched = currentState != previousState;

        recalcTimer -= Time.deltaTime;
        recalcCooldown -= Time.deltaTime;

        if (currentState == State.Chase)
            HandleChase(playerGridPos, distToPlayer, justSwitched);
        else if (currentState == State.Returning)
            HandleReturning(justSwitched);
        else
            HandlePatrol(justSwitched);
    }

    [Header("Combat")]
    [Tooltip("Seconds between contact-damage ticks while within closeRangeDistance of the player.")]
    public float damageTickInterval = 1f;
    private float damageTickTimer;

    void HandleChase(Vector2Int playerGridPos, float distToPlayer, bool justSwitched)
    {
        if (distToPlayer <= closeRangeDistance)
        {
            MoveDirectlyTowardsPlayerSafe();

            damageTickTimer -= Time.deltaTime;
            if (damageTickTimer <= 0f)
            {
                PlayerHealth ph = player.GetComponent<PlayerHealth>();
                if (ph != null)
                    ph.TakeDamage(currentDamage);
                damageTickTimer = damageTickInterval;
            }
            return;
        }
        else
        {

            damageTickTimer = damageTickInterval;
        }

        bool playerMoved = playerGridPos != lastPlayerGridPos;
        bool cooldownReady = recalcCooldown <= 0f;
        bool shouldRecalc = justSwitched
            || currentPath == null
            || pathIndex >= currentPath.Count
            || (playerMoved && cooldownReady)
            || recalcTimer <= 0f;

        if (shouldRecalc)
        {
            RecalculatePathTo(playerGridPos);
            lastPlayerGridPos = playerGridPos;
            recalcTimer = pathRecalcInterval;
            recalcCooldown = minRecalcInterval;
        }
        FollowPath(moveSpeed);
    }

    void HandleReturning(bool justSwitched)
    {
        Vector2Int homeCenter = HomeCenter;

        if (justSwitched || currentPath == null || pathIndex >= currentPath.Count)
            RecalculatePathTo(homeCenter);

        FollowPath(moveSpeed);

        bool arrived = (currentPath == null)
            || pathIndex >= currentPath.Count
            || WorldToGrid(transform.position) == homeCenter;

        if (arrived)
        {
            currentState = State.Patrol;
            currentPath = null;
        }
    }

    void HandlePatrol(bool justSwitched)
    {
        if (justSwitched || currentPath == null || pathIndex >= currentPath.Count)
        {
            if (currentPath == null || pathIndex >= currentPath.Count)
                PickNewPatrolTarget();
            RecalculatePathTo(patrolTarget);
        }
        FollowPath(moveSpeed * patrolSpeedMultiplier);
    }

    bool IsWithinLeash(Vector2Int pos)
    {
        int minX = homeRoom.x - leashMargin;
        int maxX = homeRoom.x + homeRoom.width - 1 + leashMargin;
        int minY = homeRoom.y - leashMargin;
        int maxY = homeRoom.y + homeRoom.height - 1 + leashMargin;
        return pos.x >= minX && pos.x <= maxX && pos.y >= minY && pos.y <= maxY;
    }

    bool IsInsideRoom(Vector2Int pos, RectInt room)
    {
        return pos.x >= room.x && pos.x < room.x + room.width
            && pos.y >= room.y && pos.y < room.y + room.height;
    }

    void MoveDirectlyTowardsPlayerSafe()
    {

        if (Vector3.Distance(transform.position, player.position) <= stopDistance)
            return;

        Vector3 dir = (player.position - transform.position).normalized;
        Vector3 moveDelta = dir * moveSpeed * Time.deltaTime;

        Vector3 nextX = transform.position + new Vector3(moveDelta.x, 0f, 0f);
        if (IsPositionWalkable(nextX))
            transform.position = nextX;

        Vector3 nextY = transform.position + new Vector3(0f, moveDelta.y, 0f);
        if (IsPositionWalkable(nextY))
            transform.position = nextY;
    }

    bool IsPositionWalkable(Vector3 worldPos)
    {
        Vector3 local = worldPos - dungeon.tilemapCA.transform.position;

        Vector2[] checkOffsets =
        {
            new Vector2(0, 0),
            new Vector2(collisionRadius, 0),
            new Vector2(-collisionRadius, 0),
            new Vector2(0, collisionRadius),
            new Vector2(0, -collisionRadius)
        };

        foreach (var offset in checkOffsets)
        {
            int gx = Mathf.RoundToInt(local.x + offset.x);
            int gy = Mathf.RoundToInt(local.y + offset.y);
            if (!dungeon.IsWalkable(gx, gy))
                return false;
        }

        return true;
    }

    void RecalculatePathTo(Vector2Int targetGrid)
    {
        Vector2Int start = FindNearestWalkable(WorldToGrid(transform.position));
        Vector2Int target = FindNearestWalkable(targetGrid);

        if (start.x == -1 || target.x == -1)
        {
            currentPath = null;
            return;
        }

        currentPath = Pathfinding.FindPath(dungeon, start, target);

        pathIndex = (currentPath != null && currentPath.Count > 1) ? 1 : 0;
    }

    Vector2Int FindNearestWalkable(Vector2Int origin, int maxRadius = 3)
    {
        if (dungeon.IsWalkable(origin.x, origin.y))
            return origin;

        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    int gx = origin.x + dx;
                    int gy = origin.y + dy;
                    if (dungeon.IsWalkable(gx, gy))
                        return new Vector2Int(gx, gy);
                }
            }
        }

        return new Vector2Int(-1, -1);
    }

    void FollowPath(float speed)
    {
        if (currentPath == null || pathIndex >= currentPath.Count) return;

        Vector3 targetWorldPos = GridToWorld(currentPath[pathIndex]);
        transform.position = Vector3.MoveTowards(transform.position, targetWorldPos, speed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetWorldPos) < 0.15f)
            pathIndex++;
    }

    void PickNewPatrolTarget()
    {
        patrolTarget = new Vector2Int(
            Random.Range(homeRoom.x, homeRoom.x + homeRoom.width),
            Random.Range(homeRoom.y, homeRoom.y + homeRoom.height)
        );
    }

    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 local = worldPos - dungeon.tilemapCA.transform.position;
        return new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y));
    }

    Vector3 GridToWorld(Vector2Int gridPos)
    {
        return dungeon.tilemapCA.transform.position + new Vector3(gridPos.x, gridPos.y, 0f);
    }
}
