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

        RoomGraph graph = dungeon.RoomGraph;
        int spawnIdx = dungeon.PlayerSpawnRoomIndex;

        // Weight each eligible room (everything except the player's spawn room) toward
        // off-critical-path rooms and toward greater distance from spawn, so potions
        // reward stepping off the main route rather than sitting on it. If the graph
        // isn't available, every eligible room gets equal weight (old behaviour).
        List<int> eligible = new List<int>();
        List<float> weights = new List<float>();
        float totalWeight = 0f;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (i == spawnIdx) continue;

            float w = 1f;
            if (graph != null)
            {
                int d = Mathf.Max(0, graph.DistanceFromSpawn(i));
                float offPathBoost = graph.IsOnCriticalPath(i) ? 1f : 3f;
                w = offPathBoost * (1 + d);
            }

            eligible.Add(i);
            weights.Add(w);
            totalWeight += w;
        }
        if (eligible.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            RectInt room = rooms[PickWeighted(eligible, weights, totalWeight)];
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

    // Weighted random room-index pick; uniform fallback if weights are degenerate.
    int PickWeighted(List<int> indices, List<float> weights, float totalWeight)
    {
        if (totalWeight <= 0f)
            return indices[Random.Range(0, indices.Count)];

        float r = Random.value * totalWeight;
        float acc = 0f;
        for (int i = 0; i < indices.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return indices[i];
        }
        return indices[indices.Count - 1];
    }
}