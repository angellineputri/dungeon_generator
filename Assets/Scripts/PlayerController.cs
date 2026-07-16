using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float collisionRadius = 0.3f;

    void Start()
    {
        StartCoroutine(SpawnAtValidPosition());
    }

    IEnumerator SpawnAtValidPosition()
    {
        // wait until DungeonGenerator has actually generated rooms
        yield return null; // wait one frame
        while (dungeon.Rooms == null || dungeon.Rooms.Count == 0)
            yield return null;

        RectInt room = dungeon.Rooms[Random.Range(0, dungeon.Rooms.Count)];
        Vector2Int center = new Vector2Int(
            room.x + room.width / 2,
            room.y + room.height / 2
        );

        transform.position = dungeon.tilemapCA.transform.position + new Vector3(center.x, center.y, 0f);
    }

    void Update()
    {
        Vector2 input = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) input.y += 1;
        if (Keyboard.current.sKey.isPressed) input.y -= 1;
        if (Keyboard.current.aKey.isPressed) input.x -= 1;
        if (Keyboard.current.dKey.isPressed) input.x += 1;

        input = input.normalized;
        Vector3 moveDelta = (Vector3)input * moveSpeed * Time.deltaTime;

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
}