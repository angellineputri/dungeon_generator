using UnityEngine;

// Tracks how far the player has gotten and how well. When a floor is finalized as
// cleared, it works out a medal tier (Bronze->Diamond) from how much HP was lost,
// and saves the best tier per floor + the highest floor cleared to PlayerPrefs.
// Saves are namespaced per mode (Hard/Normal); Practice mode is never saved.
public class LevelProgression : MonoBehaviour
{
    public enum Tier { Bronze, Silver, Gold, Diamond }

    [Header("References (auto-found if left empty)")]
    public SessionTelemetry telemetry;
    public PlayerHealth playerHealth;
    public PlayerController player;

    [Header("Tier thresholds (fraction of max HP lost on the floor)")]
    [Tooltip("Lose at most this fraction of max HP on a floor → Gold.")]
    [Range(0f, 1f)] public float goldMaxLossPct = 0.15f;
    [Tooltip("Lose less than this (but above the Gold cutoff) → Silver; at or above → Bronze.")]
    [Range(0f, 1f)] public float silverMaxLossPct = 0.50f;

    public Tier LastTier { get; private set; }
    public int LastFloor { get; private set; }
    public float LastLossPct { get; private set; }

    [Header("Level cap")]
    [Tooltip("Final level. Grounded in the difficulty curve: 1v1 combat becomes mathematically unwinnable past ~floor 25 (Evaluation ch.), so 25 is the last beatable level. Clearing it is a victory; floors beyond it do not exist.")]
    public int floorCap = 25;

    public string CurrentModeKey = "Hard";
    string TierKey(int floor) => $"floortier_{CurrentModeKey}_{floor}";
    string MaxKey() => $"maxClearedFloor_{CurrentModeKey}";

    public bool IsFinalFloor(int floor) => floor >= floorCap;
    public bool DungeonComplete => HighestClearedFloor >= floorCap;

    public bool HasCleared(int floor) => PlayerPrefs.GetInt(TierKey(floor), 0) > 0;
    public Tier GetBestTier(int floor) => (Tier)Mathf.Max(0, PlayerPrefs.GetInt(TierKey(floor), 0) - 1);
    public int HighestClearedFloor => PlayerPrefs.GetInt(MaxKey(), 0);

    void OnEnable()
    {
        ResolveRefs();
        if (telemetry != null)
            telemetry.OnFloorFinalized += HandleFloorFinalized;
    }

    void OnDisable()
    {
        if (telemetry != null)
            telemetry.OnFloorFinalized -= HandleFloorFinalized;
    }

    void ResolveRefs()
    {
        if (telemetry == null) telemetry = FindFirstObjectByType<SessionTelemetry>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    void HandleFloorFinalized(int floor, float damageTaken, string outcome)
    {
        if (outcome != "Exit") return;

        bool attempted = player != null && player.AttemptedAttackThisFloor;
        float maxHp = playerHealth != null ? playerHealth.maxHealth : 0f;
        float lossPct = maxHp > 0f ? damageTaken / maxHp : 1f;
        Tier tier = ComputeTier(lossPct, attempted);
        LastTier = tier;
        LastFloor = floor;
        LastLossPct = lossPct;

        // Practice is a scratch sandbox: compute the tier for the Floor Cleared screen
        // (LastTier above) but never persist it, so it can't pollute real Hard/Normal data.
        bool practice = CurrentModeKey == "Practice";
        if (!practice) SaveBest(floor, tier);

        Debug.Log($"[LevelProgression] Floor {floor} cleared — this run: {tier} ({lossPct * 100f:F0}% HP lost, attacked={attempted}){(practice ? " | PRACTICE (not saved)" : $" | stored best: {GetBestTier(floor)} | highest cleared: {HighestClearedFloor}")}");

        if (player != null) player.ResetAttackAttempt();
    }

    void SaveBest(int floor, Tier tier)
    {
        int candidate = (int)tier + 1;
        int stored = PlayerPrefs.GetInt(TierKey(floor), 0);
        if (candidate > stored)
            PlayerPrefs.SetInt(TierKey(floor), candidate);
        if (floor > PlayerPrefs.GetInt(MaxKey(), 0))
            PlayerPrefs.SetInt(MaxKey(), floor);
        PlayerPrefs.Save();
    }

    Tier ComputeTier(float lossPct, bool attemptedAttack)
    {
        if (!attemptedAttack) return Tier.Diamond;
        if (lossPct <= goldMaxLossPct) return Tier.Gold;
        if (lossPct < silverMaxLossPct) return Tier.Silver;
        return Tier.Bronze;
    }
}
