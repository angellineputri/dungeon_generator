using UnityEngine;

/// <summary>
/// The floor's single key. Same pattern as PotionPickup: builds its own sprite so it
/// works with no imported art, and is collected by a distance check rather than a
/// physics trigger (consistent with potions and the exit marker). KeyManager owns the
/// "player is holding the key" state; this component is just the pickup in the world.
/// </summary>
public class KeyPickup : MonoBehaviour
{
    public float pickupRadius = 0.6f;

    private SpriteRenderer sr;

    void Awake()
    {
        sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = BuildKeySprite();
        sr.color = new Color(1f, 0.85f, 0.2f); // gold, matching the exit marker
        sr.sortingOrder = 6;
        transform.localScale = Vector3.one * 0.5f;
    }

    // Called each frame by KeyManager. Destroys itself and returns true when the
    // player is within pickupRadius, so the caller can flip its "has key" flag.
    public bool TryCollect(Vector3 playerWorldPos)
    {
        if (Vector3.Distance(transform.position, playerWorldPos) > pickupRadius)
            return false;

        Destroy(gameObject);
        return true;
    }

    // A small diamond/key blob — just needs to read as a distinct pickup; swap for
    // real art by assigning a sprite on the spawned object if desired.
    Sprite BuildKeySprite()
    {
        int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Manhattan distance => diamond shape, to distinguish from the round potion.
                float d = Mathf.Abs(x - c.x) + Mathf.Abs(y - c.y);
                tex.SetPixel(x, y, d <= r ? Color.white : new Color(0, 0, 0, 0));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
