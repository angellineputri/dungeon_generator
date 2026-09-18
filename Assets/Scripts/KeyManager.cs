using UnityEngine;
using TMPro;

/// <summary>
/// Owns the key-and-lock layer for a floor. Listens to DungeonGenerator.OnFloorGenerated
/// (same pattern as EnemySpawner/PotionSpawner/FogOfWar), and each floor:
///   - clears any previous key and resets HasKey to false (keys never carry between
///     floors — the lock has to be re-solved every floor, like health/mana persist but
///     the objective resets),
///   - spawns one KeyPickup in the room DungeonGenerator chose (KeyRoomIndex).
///
/// PlayerController reads HasKey to gate the exit, and calls NotifyLockedExit() to
/// surface feedback when the player reaches the exit without the key. Keeping both the
/// key state and the feedback here means PlayerController only needs a reference and a
/// couple of lines, and nothing else in the project has to know keys exist.
/// </summary>
public class KeyManager : MonoBehaviour
{
    [Header("References")]
    public DungeonGenerator dungeon;
    public Transform player;
    [Tooltip("Optional — shows key/lock messages if assigned; otherwise logs to Console.")]
    public TextMeshProUGUI feedbackText;

    [Header("Feedback")]
    [Tooltip("How long the locked-exit / key-collected message stays on screen.")]
    public float messageDuration = 2f;

    private GameObject keyObj;
    private KeyPickup keyPickup;
    private bool hasKey;
    private float messageClearTime;

    public bool HasKey => hasKey;

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
        // Reset per-floor state, then place this floor's key.
        if (keyObj != null) Destroy(keyObj);
        keyObj = null;
        keyPickup = null;
        hasKey = false;

        SpawnKey();
    }

    void SpawnKey()
    {
        if (dungeon == null || dungeon.tilemapCA == null) return;

        int keyRoom = dungeon.KeyRoomIndex;
        var rooms = dungeon.Rooms;
        if (rooms == null || keyRoom < 0 || keyRoom >= rooms.Count)
        {
            // No valid off-path/fallback room this floor (degenerate layout). Leave the
            // exit effectively open rather than soft-locking the player: treat as held.
            hasKey = true;
            return;
        }

        RectInt room = rooms[keyRoom];
        Vector2Int center = new Vector2Int(
            room.x + room.width / 2,
            room.y + room.height / 2
        );

        keyObj = new GameObject("Key");
        keyPickup = keyObj.AddComponent<KeyPickup>();
        keyObj.transform.position = dungeon.tilemapCA.transform.position + new Vector3(center.x, center.y, -0.1f);
    }

    void Update()
    {
        if (keyPickup != null && !hasKey && player != null)
        {
            if (keyPickup.TryCollect(player.position))
            {
                hasKey = true;
                keyPickup = null;
                keyObj = null;
                ShowMessage("Key collected — the exit is open.");
            }
        }

        if (feedbackText != null && messageClearTime > 0f && Time.time >= messageClearTime)
        {
            feedbackText.text = "";
            messageClearTime = 0f;
        }
    }

    // Called by PlayerController when the player reaches the exit without the key.
    // Throttled so it doesn't spam every frame the player stands on the exit.
    public void NotifyLockedExit()
    {
        if (hasKey) return;
        if (Time.time < messageClearTime) return; // a message is already showing
        ShowMessage("The exit is locked — find the key first.");
    }

    void ShowMessage(string msg)
    {
        messageClearTime = Time.time + messageDuration;
        if (feedbackText != null)
            feedbackText.text = msg;
        else
            Debug.Log("[KeyManager] " + msg);
    }
}
