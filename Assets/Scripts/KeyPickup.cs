using UnityEngine;

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
        sr.color = new Color(1f, 0.85f, 0.2f);
        sr.sortingOrder = 6;
        transform.localScale = Vector3.one * 0.5f;
    }

    public bool TryCollect(Vector3 playerWorldPos)
    {
        if (Vector3.Distance(transform.position, playerWorldPos) > pickupRadius)
            return false;

        Destroy(gameObject);
        return true;
    }

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

                float d = Mathf.Abs(x - c.x) + Mathf.Abs(y - c.y);
                tex.SetPixel(x, y, d <= r ? Color.white : new Color(0, 0, 0, 0));
            }
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
