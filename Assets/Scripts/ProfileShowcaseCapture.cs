using System.Collections;
using UnityEngine;

public class ProfileShowcaseCapture : MonoBehaviour
{
    public DungeonGenerator dungeon;
    [Tooltip("One representative floor per profile. Defaults cover all four profiles once.")]
    public int[] showcaseFloors = { 5, 6, 7, 8 };
    [Tooltip("Screenshot supersize multiplier (1 = native game-view resolution).")]
    public int superSize = 2;
    public KeyCode captureKey = KeyCode.P;

    void Update()
    {
        if (Input.GetKeyDown(captureKey))
            StartCoroutine(CaptureAll());
    }

    IEnumerator CaptureAll()
    {
        if (dungeon == null) dungeon = FindFirstObjectByType<DungeonGenerator>();
        if (dungeon == null)
        {
            Debug.LogWarning("[ProfileShowcaseCapture] No DungeonGenerator found.");
            yield break;
        }

        foreach (int floor in showcaseFloors)
        {
            dungeon.JumpToFloor(floor);

            yield return null;
            yield return new WaitForEndOfFrame();

            string file = $"profile_{dungeon.LastLayoutProfile}_floor{floor}.png";
            ScreenCapture.CaptureScreenshot(file, Mathf.Max(1, superSize));
            Debug.Log($"[ProfileShowcaseCapture] Captured {file} (profile {dungeon.LastLayoutProfile}).");

            yield return null;
            yield return null;
        }

        Debug.Log("[ProfileShowcaseCapture] Done — PNGs written to the project root (beside Assets/).");
    }
}
