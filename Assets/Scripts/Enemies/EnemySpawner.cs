using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;
    [Tooltip("Prefab with an EnemyAI component. Make one by dragging your existing Enemy GameObject into Assets/Prefabs.")]
    public GameObject enemyPrefab;
    public Transform player;

    private readonly List<GameObject> activeEnemies = new List<GameObject>();
    private Coroutine spawnRoutine;

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
        foreach (var enemy in activeEnemies)
            if (enemy != null) Destroy(enemy);
        activeEnemies.Clear();

        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);

        DifficultyParams diff = DifficultyManager.GetDifficultyParams(dungeon.CurrentFloor);
        spawnRoutine = StartCoroutine(SpawnLoop(diff));
    }

    IEnumerator SpawnLoop(DifficultyParams diff)
    {
        List<RectInt> allRooms = dungeon.Rooms;
        if (allRooms == null || allRooms.Count == 0 || enemyPrefab == null)
            yield break;

        RectInt playerRoom = dungeon.PlayerSpawnRoom;
        List<RectInt> eligibleRooms = new List<RectInt>();
        foreach (var room in allRooms)
        {
            bool isPlayerRoom = room.x == playerRoom.x && room.y == playerRoom.y
                && room.width == playerRoom.width && room.height == playerRoom.height;
            if (!isPlayerRoom)
                eligibleRooms.Add(room);
        }

        if (eligibleRooms.Count == 0)
            yield break;

        List<int> perRoomCounts = DistributeEnemyCounts(eligibleRooms, diff.enemyCount);

        List<int> spawnQueue = new List<int>();
        for (int i = 0; i < eligibleRooms.Count; i++)
            for (int j = 0; j < perRoomCounts[i]; j++)
                spawnQueue.Add(i);

        Shuffle(spawnQueue);

        foreach (int roomIndex in spawnQueue)
            SpawnOneInRoom(eligibleRooms[roomIndex], diff);

        yield break;
    }

    List<int> DistributeEnemyCounts(List<RectInt> rooms, int totalCount)
    {
        int n = rooms.Count;
        int[] counts = new int[n];
        if (totalCount <= 0 || n == 0)
            return new List<int>(counts);

        float[] areas = new float[n];
        float totalArea = 0f;
        for (int i = 0; i < n; i++)
        {
            areas[i] = rooms[i].width * rooms[i].height;
            totalArea += areas[i];
        }

        if (totalArea <= 0f)
            return new List<int>(counts);

        float[] exact = new float[n];
        int assigned = 0;
        for (int i = 0; i < n; i++)
        {
            exact[i] = (areas[i] / totalArea) * totalCount;
            counts[i] = Mathf.FloorToInt(exact[i]);
            assigned += counts[i];
        }

        int remaining = totalCount - assigned;
        List<int> order = new List<int>();
        for (int i = 0; i < n; i++) order.Add(i);
        order.Sort((a, b) => (exact[b] - counts[b]).CompareTo(exact[a] - counts[a]));

        for (int i = 0; i < remaining && i < order.Count; i++)
            counts[order[i]]++;

        return new List<int>(counts);
    }

    void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void SpawnOneInRoom(RectInt room, DifficultyParams diff)
    {
        GameObject enemyObj = Instantiate(enemyPrefab);
        EnemyAI ai = enemyObj.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.dungeon = dungeon;
            ai.player = player;
            ai.forcedSpawnRoom = room;
            ai.ApplyDifficulty(diff.enemyHpMultiplier, diff.enemyDamageMultiplier);
        }
        else
        {
            Debug.LogWarning("[EnemySpawner] enemyPrefab has no EnemyAI component.");
        }

        activeEnemies.Add(enemyObj);
    }
}
