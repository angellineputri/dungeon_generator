using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Places 1-2 permanent upgrade pickups per floor, ONLY in off-critical-path rooms,
/// weighted toward greater DistanceFromSpawn (via RoomGraph) — so the reward for
/// leaving the direct route to the exit scales with how far off it you go. Mirrors
/// PotionSpawner for spawning and KeyManager for self-collection.
/// </summary>
public class UpgradeSpawner : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    public DungeonGenerator dungeon;
    public Transform player;
    [Tooltip("Optional — shows '+2 Attack' etc. on pickup; otherwise logs to Console.")]
    public TextMeshProUGUI feedbackText;

    [Header("Spawn")]
    public int minPerFloor = 1;
    public int maxPerFloor = 2;

    [Header("Upgrade amounts")]
    public float powerAmount = 2f;      // +attackDamage
    public float vitalityAmount = 20f;  // +maxHealth (never currentHealth)

    [Header("Feedback")]
    public float messageDuration = 2f;

    private readonly List<UpgradePickup> active = new List<UpgradePickup>();
    private PlayerController pc;
    private PlayerHealth ph;
    private float messageClearTime;

    // Read by RunSummary (Build B only). Persists across floors for the whole run.
    public int UpgradesCollected { get; private set; }

    void OnEnable()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (dungeon != null) dungeon.OnFloorGenerated += HandleFloorGenerated;
    }

    void OnDisable()
    {
        if (dungeon != null) dungeon.OnFloorGenerated -= HandleFloorGenerated;
    }

    void ResolvePlayer()
    {
        if (player == null)
        {
            var pcFound = FindFirstObjectByType<PlayerController>();
            if (pcFound != null) player = pcFound.transform;
        }
        if (player != null)
        {
            if (pc == null) pc = player.GetComponent<PlayerController>();
            if (ph == null) ph = player.GetComponent<PlayerHealth>();
        }
    }

    void HandleFloorGenerated()
    {
        foreach (var u in active)
            if (u != null) Destroy(u.gameObject);
        active.Clear();

        SpawnUpgrades(Random.Range(minPerFloor, maxPerFloor + 1));
    }

    void SpawnUpgrades(int count)
    {
        List<RectInt> rooms = dungeon.Rooms;
        if (rooms == null || rooms.Count == 0) return;

        RoomGraph graph = dungeon.RoomGraph;
        int spawnIdx = dungeon.PlayerSpawnRoomIndex;

        // Off-critical-path rooms only, weighted by distance from spawn. Fall back to
        // any non-spawn room (still distance-weighted) if the layout has no off-path
        // rooms this floor, so an upgrade still appears.
        List<int> eligible = new List<int>();
        List<float> weights = new List<float>();
        float total = 0f;

        void Gather(bool offPathOnly)
        {
            eligible.Clear(); weights.Clear(); total = 0f;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (i == spawnIdx) continue;
                if (graph != null && offPathOnly && graph.IsOnCriticalPath(i)) continue;
                float d = graph != null ? Mathf.Max(0, graph.DistanceFromSpawn(i)) : 0;
                float w = 1f + d;
                eligible.Add(i); weights.Add(w); total += w;
            }
        }

        Gather(true);
        if (eligible.Count == 0) Gather(false);
        if (eligible.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            int roomIndex = PickWeighted(eligible, weights, total);
            RectInt room = rooms[roomIndex];
            Vector2Int pos = new Vector2Int(
                Random.Range(room.x, room.x + room.width),
                Random.Range(room.y, room.y + room.height)
            );

            // Alternate Power / Vitality across this floor's spawns (i) so a
            // two-upgrade floor reliably offers one of each; UpgradesCollected shifts
            // which type comes first from floor to floor.
            bool power = (UpgradesCollected + i) % 2 == 0;

            GameObject obj = new GameObject(power ? "Upgrade_Power" : "Upgrade_Vitality");
            UpgradePickup up = obj.AddComponent<UpgradePickup>();
            up.type = power ? UpgradeType.Power : UpgradeType.Vitality;
            up.amount = power ? powerAmount : vitalityAmount;
            obj.transform.position = dungeon.tilemapCA.transform.position + new Vector3(pos.x, pos.y, -0.1f);
            active.Add(up);
        }
    }

    void Update()
    {
        ResolvePlayer();

        if (player != null)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                UpgradePickup u = active[i];
                if (u == null) { active.RemoveAt(i); continue; }
                if (u.TryCollect(player.position, pc, ph))
                {
                    UpgradesCollected++;
                    ShowMessage(u.Label + "!");
                    active.RemoveAt(i);
                }
            }
        }

        if (feedbackText != null && messageClearTime > 0f && Time.time >= messageClearTime)
        {
            feedbackText.text = "";
            messageClearTime = 0f;
        }
    }

    int PickWeighted(List<int> indices, List<float> weights, float totalWeight)
    {
        if (totalWeight <= 0f) return indices[Random.Range(0, indices.Count)];
        float r = Random.value * totalWeight;
        float acc = 0f;
        for (int i = 0; i < indices.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return indices[i];
        }
        return indices[indices.Count - 1];
    }

    void ShowMessage(string msg)
    {
        messageClearTime = Time.time + messageDuration;
        if (feedbackText != null) feedbackText.text = msg;
        else Debug.Log("[UpgradeSpawner] " + msg);
    }
}
