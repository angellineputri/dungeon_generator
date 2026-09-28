using UnityEngine;

public class PotionPickup : MonoBehaviour
{
    public float healAmount = 25f;
    public float pickupRadius = 0.6f;

    private SpriteRenderer sr;

    void Awake()
    {
        sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = BuildCircleSprite();
        sr.color = new Color(0.3f, 1f, 0.4f);
        sr.sortingOrder = 5;
        transform.localScale = Vector3.one * 0.4f;
    }

    public bool TryCollect(Vector3 playerWorldPos, PlayerHealth playerHealth)
    {
        if (Vector3.Distance(transform.position, playerWorldPos) > pickupRadius)
            return false;

        if (playerHealth != null)
            playerHealth.Heal(healAmount);

        FloatingText.Show($"+{healAmount:0} HP", playerWorldPos, new Color(0.4f, 1f, 0.5f));

        Destroy(gameObject);
        return true;
    }

    Sprite BuildCircleSprite()
    {
        int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float r = size / 2f - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                tex.SetPixel(x, y, d <= r ? Color.white : new Color(0, 0, 0, 0));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
