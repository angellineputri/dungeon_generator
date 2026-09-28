using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class EnemyDirectionalSprite : MonoBehaviour
{
    [Header("Sprites")]
    [Tooltip("Shown while the enemy is moving left.")]
    public Sprite walkLeft;
    [Tooltip("Shown while the enemy is moving right.")]
    public Sprite walkRight;

    [Header("Settings")]
    [Tooltip("Minimum horizontal movement per frame before the facing updates (prevents flicker when barely moving).")]
    public float moveThreshold = 0.002f;
    [Tooltip("Which way the enemy faces before it has moved.")]
    public bool startFacingRight = true;

    private SpriteRenderer sr;
    private float lastX;
    private bool facingRight;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        facingRight = startFacingRight;
        lastX = transform.position.x;
        ApplySprite();
    }

    void LateUpdate()
    {
        float dx = transform.position.x - lastX;
        lastX = transform.position.x;

        if (dx > moveThreshold && !facingRight)
        {
            facingRight = true;
            ApplySprite();
        }
        else if (dx < -moveThreshold && facingRight)
        {
            facingRight = false;
            ApplySprite();
        }
    }

    void ApplySprite()
    {
        if (sr == null) return;
        Sprite s = facingRight ? walkRight : walkLeft;
        if (s != null) sr.sprite = s;
    }
}
