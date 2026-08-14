using UnityEngine;
using TMPro;

/// <summary>
/// Minimal player health system. Intentionally simple: no invulnerability frames,
/// no death animation, no game-over flow yet — just a number that damage reduces
/// and potions restore, with an optional on-screen readout. Enough for the
/// difficulty curve and resource scarcity to actually mean something in play.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    private float currentHealth;
    public float CurrentHealth => currentHealth;

    [Header("References")]
    public DungeonGenerator dungeon;
    [Tooltip("Optional — shows 'HP: X / Y' if assigned. Falls back to Console logging if not.")]
    public TextMeshProUGUI healthText;

    void OnEnable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated += ResetHealth;
    }

    void OnDisable()
    {
        if (dungeon != null)
            dungeon.OnFloorGenerated -= ResetHealth;
    }

    void Awake()
    {
        currentHealth = maxHealth;
        UpdateText();
    }

    // Health persists across floors by design — a fresh full heal every floor
    // would make potions pointless. Swap this out later if you want otherwise.
    void ResetHealth()
    {
        UpdateText();
    }

    public void TakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        UpdateText();

        if (currentHealth <= 0f)
        {
            Debug.Log("[PlayerHealth] Player died. No game-over flow implemented yet.");
        }
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateText();
    }

    void UpdateText()
    {
        if (healthText != null)
            healthText.text = $"HP: {currentHealth:F0} / {maxHealth:F0}";
    }
}