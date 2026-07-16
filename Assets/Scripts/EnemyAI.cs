using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;
    public Transform player;

    [Header("AI Settings")]
    public float detectionRadius = 8f;
    public float loseRadius = 14f;
    public float pathRecalcInterval = 0.4f;
    public float moveSpeed = 3f;
    public float patrolSpeedMultiplier = 0.5f;
    public float closeRangeDistance = 2f;
    public float collisionRadius = 0.15f;

    private enum State { Patrol, Chase }
    private State currentState = State.Patrol;

    private List<Vector2Int> currentPath;
    private int pathIndex;
    private float recalcTimer;
    private Vector2Int patrolTarget;
    private bool isReady = false;

    void Start()
    {
        StartCoroutine(SpawnAtValidPosition());
    }

    IEnumerator SpawnAtValidPosition()
    {
        yield return null;
        while (dungeon.Rooms == null || dungeon.Rooms.Count == 0)
            yield return null;

        RectInt room = dungeon.Rooms[Random.Range(0, dungeon.Rooms.Count)];
        Vector2Int center = new Vector2Int(
            room.x + room.width / 2,
            room.y + room.height / 2
        );

        transform.position = dungeon.tilemapCA.transform.position + new Vector3(center.x, center.y, 0f);

        PickNewPatrolTarget();
        RecalculatePathTo(patrolTarget);
        isReady = true;
    }

    void Update()
    {
        if (!isReady) return;

        float distToPlayer = Vector3.Distance(transform.position, player.position);
        State previousState = currentState;

        if (currentState == State.Patrol && distToPlayer <= detectionRadius)
            currentState = State.Chase;
        else if (currentState == State.Chase && distToPlayer > loseRadius)
            currentState = State.Patrol;

        // if we just switched states, force a fresh path immediately
        bool justSwitched = currentState != previousState;

        recalcTimer -= Time.deltaTime;

        if (currentState == State.Chase)
        {
            if (distToPlayer <= closeRangeDistance)
            {
                MoveDirectlyTowardsPlayerSafe();
            }
            else
            {
                if (justSwitched || recalcTimer <= 0f || currentPath == null || pathIndex >= currentPath.Count)
                {
                    RecalculatePathTo(WorldToGrid(player.position));
                    recalcTimer = pathRecalcInterval;
                }
                FollowPath(moveSpeed);
            }
        }
        else // Patrol
        {
            if (justSwitched || currentPath == null || pathIndex >= currentPath.Count)
            {
                if (currentPath == null || pathIndex >= currentPath.Count)
                    PickNewPatrolTarget();
                RecalculatePathTo(patrolTarget);
            }
            FollowPath(moveSpeed * patrolSpeedMultiplier);
        }
    }

    // close range only — no A* here, so this still needs a wall check
    void MoveDirectlyTowardsPlayerSafe()
    {
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

    // shared by both chase (target = player) and patrol (target = patrolTarget)
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
        pathIndex = 0;
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

    // A* guarantees each waypoint is walkable and connected — no manual
    // collision check needed here, used by both chase and patrol now
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
        if (dungeon.Rooms.Count == 0) return;
        RectInt room = dungeon.Rooms[Random.Range(0, dungeon.Rooms.Count)];
        patrolTarget = new Vector2Int(
            Random.Range(room.x, room.x + room.width),
            Random.Range(room.y, room.y + room.height)
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