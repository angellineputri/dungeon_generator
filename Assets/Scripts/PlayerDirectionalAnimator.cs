using UnityEngine;

/// <summary>
/// 8-direction frame-swap animation for the player. Movement direction is derived
/// from the player's ACTUAL per-frame position delta (collision-resolved), bucketed
/// into 8 octants by angle — the same decoupled approach as the previous animator,
/// so it needs no movement state from PlayerController. Attacks are driven by
/// PlayerController.OnAttack (fired on real, cooldown-gated swings), which takes
/// priority over run/idle for the swing's duration with facing frozen at swing
/// start. Put this on the Player GameObject next to its SpriteRenderer and
/// PlayerController; assign the frame arrays in the Inspector.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerDirectionalAnimator : MonoBehaviour
{
    // Octant indices used throughout: 0=Right, 1=UpRight, 2=Up, 3=UpLeft,
    // 4=Left, 5=DownLeft, 6=Down, 7=DownRight.

    [Header("Run (2 frames each)")]
    public Sprite[] runRight;
    public Sprite[] runUpRight;
    public Sprite[] runUp;
    public Sprite[] runUpLeft;
    public Sprite[] runLeft;
    public Sprite[] runDownLeft;
    public Sprite[] runDown;
    public Sprite[] runDownRight;

    [Header("Attack (3 frames each)")]
    public Sprite[] atkRight;
    public Sprite[] atkUpRight;
    public Sprite[] atkUp;
    public Sprite[] atkUpLeft;
    public Sprite[] atkLeft;
    public Sprite[] atkDownLeft;
    public Sprite[] atkDown;
    public Sprite[] atkDownRight;

    [Header("Idle (2 frames each, left/right)")]
    public Sprite[] idleLeft;
    public Sprite[] idleRight;

    [Header("Timing")]
    [Tooltip("Playback speed (frames/sec) for the run animations.")]
    public float runFps = 6f;
    [Tooltip("Playback speed (frames/sec) for the attack swing.")]
    public float attackFps = 12f;
    [Tooltip("Playback speed (frames/sec) for the idle animation.")]
    public float idleFps = 3f;
    [Tooltip("Movement speed (world units/sec) below which the player counts as standing still → idle.")]
    public float moveThreshold = 0.05f;

    private SpriteRenderer sr;
    private PlayerController player;
    private int facing = 6; // default Down
    private bool facingRight = true; // last horizontal orientation, for idle L/R
    private Vector3 lastPos;

    private Sprite[] currentClip;
    private float frameTimer;
    private int frameIndex;

    private bool attacking;
    private float attackTimer;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        player = GetComponent<PlayerController>();
        lastPos = transform.position;
    }

    void OnEnable()
    {
        if (player != null) player.OnAttack += StartAttack;
    }

    void OnDisable()
    {
        if (player != null) player.OnAttack -= StartAttack;
    }

    // Begin an attack swing in the current facing, frozen for its duration.
    void StartAttack()
    {
        Sprite[] clip = AttackClip(facing);
        int frames = (clip != null && clip.Length > 0) ? clip.Length : 3;
        attackTimer = frames / Mathf.Max(0.01f, attackFps);
        attacking = true;
    }

    void Update()
    {
        // Movement from real (collision-resolved) position change since last frame.
        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        bool moving = speed > moveThreshold;

        // Face the movement octant only while moving and not mid-swing, so an
        // attack keeps the direction it started with and idle keeps the last facing.
        if (moving && !attacking)
        {
            facing = Octant(delta);
            // Remember last horizontal orientation for the left/right idle; pure
            // Up (2) / Down (6) movement leaves it unchanged.
            if (facing != 2 && facing != 6)
                facingRight = facing == 0 || facing == 1 || facing == 7;
        }

        if (attacking)
        {
            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
                attacking = false;
        }

        Sprite[] clip;
        float fps;
        if (attacking)
        {
            clip = AttackClip(facing);
            fps = attackFps;
        }
        else if (moving)
        {
            clip = RunClip(facing);
            fps = runFps;
        }
        else
        {
            clip = facingRight ? idleRight : idleLeft;
            fps = idleFps;
        }

        Play(clip, fps);
    }

    int Octant(Vector3 delta)
    {
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        return (Mathf.RoundToInt(angle / 45f) + 8) % 8;
    }

    Sprite[] RunClip(int oct)
    {
        switch (oct)
        {
            case 0: return runRight;
            case 1: return runUpRight;
            case 2: return runUp;
            case 3: return runUpLeft;
            case 4: return runLeft;
            case 5: return runDownLeft;
            case 6: return runDown;
            default: return runDownRight; // 7
        }
    }

    Sprite[] AttackClip(int oct)
    {
        switch (oct)
        {
            case 0: return atkRight;
            case 1: return atkUpRight;
            case 2: return atkUp;
            case 3: return atkUpLeft;
            case 4: return atkLeft;
            case 5: return atkDownLeft;
            case 6: return atkDown;
            default: return atkDownRight; // 7
        }
    }

    void Play(Sprite[] clip, float fps)
    {
        if (clip == null || clip.Length == 0) return;

        // Restart on state change so switching between clips of different lengths
        // never lands on a stale/out-of-range frame.
        if (clip != currentClip)
        {
            currentClip = clip;
            frameIndex = 0;
            frameTimer = 0f;
        }

        frameTimer += Time.deltaTime;
        float step = 1f / Mathf.Max(0.01f, fps);
        while (frameTimer >= step)
        {
            frameTimer -= step;
            frameIndex = (frameIndex + 1) % clip.Length;
        }

        sr.sprite = clip[frameIndex];
    }
}
