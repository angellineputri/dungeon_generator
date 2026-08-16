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

    [Header("Exit Trigger")]
    [Tooltip("How close (in tiles) the player must be to the exit marker's exact position to advance, rather than anywhere in the exit room.")]
    public float exitTriggerRadius = 1f;

    [Header("Combat")]
    [Tooltip("Press F to attack. Deals damage to any enemy within attackRange.")]
    public float attackDamage = 15f;
    public float attackRange = 2.2f;
    public float attackCooldown = 0.9f;
    [Tooltip("Mana spent per attack. Fizzles (no damage, cooldown still applies) if you don't have enough.")]
    public float manaCost = 25f;
    private float attackCooldownTimer;

    void OnEnable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated += RespawnAtValidPosition;
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated -= RespawnAtValidPosition;
    }

    void Start()
    {
        // Fallback for the very first floor, in case OnEnable subscribed after
        // DungeonGenerator already finished its first Generate() call (shouldn't
        // normally happen given Unity's Awake->OnEnable->Start ordering, but this
        // keeps the player from getting stuck at the origin if it ever does).
        if (dungeon != null && dungeon.CurrentFloor > 0)
            RespawnAtValidPosition();
        else
            StartCoroutine(WaitForFirstFloor());
    }

    IEnumerator WaitForFirstFloor()
    {
        yield return null;
        while (dungeon.Rooms == null || dungeon.Rooms.Count == 0)
            yield return null;
        RespawnAtValidPosition();
    }

    // Called on the very first floor and again every time the dungeon regenerates
    // (Space key), via DungeonGenerator.OnFloorGenerated. No need to wait/coroutine
    // here since Rooms is already fully populated by the time this event fires.
    void RespawnAtValidPosition()
    {
        if (dungeon.Rooms == null || dungeon.Rooms.Count == 0) return;

        // Always the smallest room on the floor, decided once by DungeonGenerator
        // so EnemySpawner excludes this exact room too.
        RectInt room = dungeon.PlayerSpawnRoom;
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

        CheckExitReached();
        CheckPotionPickups();
        HandleAttackInput();
    }

    void HandleAttackInput()
    {
        attackCooldownTimer -= Time.deltaTime;

        if (Keyboard.current.fKey.wasPressedThisFrame && attackCooldownTimer <= 0f)
        {
            Attack();
            attackCooldownTimer = attackCooldown;
        }
    }

    // Simple radius melee swing — no aiming needed on a top-down grid. Hits every
    // enemy within attackRange, consistent with the distance-check pattern already
    // used everywhere else in this project (potion pickup, exit trigger, contact damage).
    // Costs mana; fizzles with no damage (but cooldown still applies) if you're dry —
    // that's the actual point, since it's what stops holding F from being free forever.
    void Attack()
    {
        PlayerMana mana = GetComponent<PlayerMana>();
        if (mana != null && !mana.TrySpend(manaCost))
        {
            ShowAttackEffect(false, outOfMana: true);
            return;
        }

        EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        bool hitAnything = false;

        foreach (var enemy in enemies)
        {
            if (Vector3.Distance(transform.position, enemy.transform.position) <= attackRange)
            {
                enemy.TakeDamage(attackDamage);
                hitAnything = true;
            }
        }

        ShowAttackEffect(hitAnything, outOfMana: false);
    }

    // Brief expanding ring at the player's position so the attack is visible on
    // screen (and in the demo video) even without a sprite animation system.
    // Faint blue-gray means "fizzled, out of mana" — distinct from a whiffed
    // (gray) or landed (yellow) hit so it's clear on screen why nothing happened.
    void ShowAttackEffect(bool hit, bool outOfMana)
    {
        GameObject fx = new GameObject("AttackSwing");
        fx.transform.position = transform.position;

        SpriteRenderer sr = fx.AddComponent<SpriteRenderer>();
        sr.sprite = BuildRingSprite();
        if (outOfMana)
            sr.color = new Color(0.4f, 0.4f, 0.7f, 0.4f);
        else
            sr.color = hit ? new Color(1f, 0.9f, 0.3f) : new Color(0.8f, 0.8f, 0.8f, 0.6f);
        sr.sortingOrder = 15;
        fx.transform.localScale = Vector3.one * attackRange * 2f;

        Destroy(fx, 0.12f);
    }

    Sprite BuildRingSprite()
    {
        int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float outerR = size / 2f - 2f;
        float innerR = outerR - 5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                bool inRing = d <= outerR && d >= innerR;
                tex.SetPixel(x, y, inRing ? Color.white : new Color(0, 0, 0, 0));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // Advances to the next floor only when the player is close to the exit marker's
    // exact position, not just anywhere inside the (potentially large) exit room.
    void CheckExitReached()
    {
        RectInt exit = dungeon.ExitRoom;
        Vector2Int center = new Vector2Int(
            exit.x + exit.width / 2,
            exit.y + exit.height / 2
        );
        Vector3 exitWorldPos = dungeon.tilemapCA.transform.position + new Vector3(center.x, center.y, 0f);

        if (Vector3.Distance(transform.position, exitWorldPos) <= exitTriggerRadius)
            dungeon.AdvanceToNextFloor();
    }

    void CheckPotionPickups()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        // FindObjectsByType is fine here — potion counts per floor are tiny (1-3),
        // this isn't a hot path that needs a cached registry.
        PotionPickup[] potions = FindObjectsByType<PotionPickup>(FindObjectsSortMode.None);
        foreach (var potion in potions)
            potion.TryCollect(transform.position, health);
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