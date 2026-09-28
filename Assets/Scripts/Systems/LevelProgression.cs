using UnityEngine;

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
    [Tooltip("How many floors are exposed as selectable levels in Progression, and the floor whose clear triggers the victory screen. NOTE: this is a reach/testing cap, not a balance claim — per the evaluation, 1v1 combat becomes effectively unwinnable past ~floor 25, so deaths on deep floors are expected. The generator can produce all these floors; this only controls how many are surfaced.")]
    public int floorCap = 100;

    public string CurrentModeKey = "Hard";

    public bool SuppressSave = false;

    string TierKey(int floor) => $"floortier_{CurrentModeKey}_{floor}";
    string MaxKey() => $"maxClearedFloor_{CurrentModeKey}";

    string EndlessBestKey() => $"endlessBest_{CurrentModeKey}";
    public int EndlessBest => PlayerPrefs.GetInt(EndlessBestKey(), 0);
    public void RecordEndlessFloor(int floor)
    {
        if (floor > PlayerPrefs.GetInt(EndlessBestKey(), 0))
        {
            PlayerPrefs.SetInt(EndlessBestKey(), floor);
            PlayerPrefs.Save();
        }
    }

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

        bool practice = CurrentModeKey == "Practice";
        bool skipSave = practice || SuppressSave;
        if (!skipSave) SaveBest(floor, tier);

        string saveNote = skipSave
            ? (practice ? " | PRACTICE (not saved)" : " | ENDLESS (not saved)")
            : $" | stored best: {GetBestTier(floor)} | highest cleared: {HighestClearedFloor}";
        Debug.Log($"[LevelProgression] Floor {floor} cleared — this run: {tier} ({lossPct * 100f:F0}% HP lost, attacked={attempted}){saveNote}");

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
