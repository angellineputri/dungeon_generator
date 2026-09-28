using UnityEngine;
using TMPro;

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
                FloatingText.Show("Key collected", player.position, new Color(1f, 0.85f, 0.2f));
            }
        }

        if (feedbackText != null && messageClearTime > 0f && Time.time >= messageClearTime)
        {
            feedbackText.text = "";
            messageClearTime = 0f;
        }
    }

    public void NotifyLockedExit()
    {
        if (hasKey) return;
        if (Time.time < messageClearTime) return;
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
