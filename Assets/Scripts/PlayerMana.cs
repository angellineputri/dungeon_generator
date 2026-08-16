using UnityEngine;
using TMPro;

/// <summary>
/// Simple mana pool that gates attacks. Regenerates passively, doesn't reset on
/// floor change (same philosophy as PlayerHealth — a fresh full pool every floor
/// would make the resource meaningless). Combined with attackCooldown, this is
/// what turns fighting a stacked room (3-4 enemies at high floors) into a real
/// decision — you can burst a few hits, but sustained fighting drains you faster
/// than you regen, forcing a retreat rather than standing and holding F.
/// </summary>
public class PlayerMana : MonoBehaviour
{
    [Header("Mana")]
    public float maxMana = 100f;
    public float manaRegenPerSecond = 10f;
    private float currentMana;
    public float CurrentMana => currentMana;

    [Header("UI (optional)")]
    public TextMeshProUGUI manaText;

    void Awake()
    {
        currentMana = maxMana;
        UpdateText();
    }

    void Update()
    {
        if (currentMana < maxMana)
        {
            currentMana = Mathf.Min(maxMana, currentMana + manaRegenPerSecond * Time.deltaTime);
            UpdateText();
        }
    }

    // Returns false without spending anything if there isn't enough mana —
    // caller (PlayerController.Attack) treats a false return as a fizzled swing.
    public bool TrySpend(float amount)
    {
        if (currentMana < amount) return false;
        currentMana -= amount;
        UpdateText();
        return true;
    }

    void UpdateText()
    {
        if (manaText != null)
            manaText.text = $"MP: {currentMana:F0} / {maxMana:F0}";
    }
}