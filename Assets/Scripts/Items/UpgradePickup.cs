using UnityEngine;

public enum UpgradeType { Power, Vitality }

public class UpgradePickup : MonoBehaviour
{
    public UpgradeType type = UpgradeType.Power;
    public float amount = 2f;
    public float pickupRadius = 0.6f;

    private SpriteRenderer sr;

    void Awake()
    {
        sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = gameObject.AddComponent<SpriteRenderer>();

        sr.sprite = type == UpgradeType.Power ? BuildChevronSprite() : BuildCrossSprite();
        sr.color = type == UpgradeType.Power ? new Color(1f, 0.35f, 0.3f) : new Color(0.4f, 0.6f, 1f);
        sr.sortingOrder = 6;
        transform.localScale = Vector3.one * 0.55f;
    }

    public bool TryCollect(Vector3 playerWorldPos, PlayerController pc, PlayerHealth ph)
    {
        if (Vector3.Distance(transform.position, playerWorldPos) > pickupRadius)
            return false;

        if (type == UpgradeType.Power)
        {
            if (pc != null) pc.attackDamage += amount;
        }
        else
        {
            if (ph != null) ph.IncreaseMaxHealth(amount);
        }

        FloatingText.Show(Label, playerWorldPos, sr.color);

        Destroy(gameObject);
        return true;
    }

    public string Label => type == UpgradeType.Power
        ? $"+{amount:0} Attack"
        : $"+{amount:0} Max HP";

    Sprite BuildChevronSprite()
    {
        int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {

                float dx = Mathf.Abs(x - c.x);
                bool band = Mathf.Abs((y - 6) - dx) <= 5 && y >= 6 && y <= size - 6;
                tex.SetPixel(x, y, band ? Color.white : new Color(0, 0, 0, 0));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    Sprite BuildCrossSprite()
    {
        int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(size / 2f, size / 2f);
        float arm = size * 0.16f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool vert = Mathf.Abs(x - c.x) <= arm && Mathf.Abs(y - c.y) <= size * 0.38f;
                bool horiz = Mathf.Abs(y - c.y) <= arm && Mathf.Abs(x - c.x) <= size * 0.38f;
                tex.SetPixel(x, y, (vert || horiz) ? Color.white : new Color(0, 0, 0, 0));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
