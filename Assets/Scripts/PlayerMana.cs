using UnityEngine;
using TMPro;

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
