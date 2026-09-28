using UnityEngine;
using TMPro;

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

    void ResetHealth()
    {
        UpdateText();
    }

    public void ResetToFull()
    {
        currentHealth = maxHealth;
        UpdateText();
    }

    public void TakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        UpdateText();

        if (currentHealth <= 0f)
        {
            Debug.Log("[PlayerHealth] Player died.");
        }
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateText();
    }

    public void IncreaseMaxHealth(float amount)
    {
        maxHealth += amount;
        UpdateText();
    }

    void UpdateText()
    {
        if (healthText != null)
            healthText.text = $"HP: {currentHealth:F0} / {maxHealth:F0}";
    }
}
