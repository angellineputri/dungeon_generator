using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Listens for DungeonGenerator.OnFloorGenerated and places diff.potionCount potions
/// at random walkable positions across the floor's rooms (excluding the player's
/// spawn room, so there's always a reason to actually explore).
/// </summary>
public class PotionSpawner : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;

    private readonly List<GameObject> activePotions = new List<GameObject>();

    void OnEnable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated += HandleFloorGenerated;
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated -= HandleFloorGenerated;
    }

    void HandleFloorGenerated()
    {
        foreach (var potion in activePotions)
            if (potion != null) Destroy(potion);
        activePotions.Clear();

        DifficultyParams diff = DifficultyManager.GetDifficultyParams(dungeon.CurrentFloor);
        SpawnPotions(diff.potionCount);
    }

    void SpawnPotions(int count)
    {
        List<RectInt> rooms = dungeon.Rooms;
        if (rooms == null || rooms.Count == 0) return;

        RectInt playerRoom = dungeon.PlayerSpawnRoom;
        List<RectInt> eligible = new List<RectInt>();
        foreach (var room in rooms)
        {
            bool isPlayerRoom = room.x == playerRoom.x && room.y == playerRoom.y
                && room.width == playerRoom.width && room.height == playerRoom.height;
            if (!isPlayerRoom)
                eligible.Add(room);
        }
        if (eligible.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            RectInt room = eligible[Random.Range(0, eligible.Count)];
            Vector2Int pos = new Vector2Int(
                Random.Range(room.x, room.x + room.width),
                Random.Range(room.y, room.y + room.height)
            );

            GameObject potionObj = new GameObject("Potion");
            potionObj.AddComponent<PotionPickup>();
            potionObj.transform.position = dungeon.tilemapCA.transform.position + new Vector3(pos.x, pos.y, -0.1f);
            activePotions.Add(potionObj);
        }
    }
}