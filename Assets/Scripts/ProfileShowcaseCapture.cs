using System.Collections;
using UnityEngine;

/// <summary>
/// Report helper: captures one screenshot per LayoutProfile so the four images can be
/// assembled into a four-up "layout variety" comparison figure (criterion 2 evidence).
///
/// Press P in Play mode. It jumps to one representative floor per profile, waits for the
/// floor (and its decoration) to build, and writes a PNG per profile into the project root
/// (next to Assets/). Filenames carry the actual LastLayoutProfile so they're correct even
/// if the per-floor profile assignment changes later.
///
/// The showcase floors are the first block where each profile appears once with a fresh
/// seed: 5=Quad, 6=Columns, 7=Rows, 8=Organic (floors 1-3 are biased to Quad/Organic, so
/// the banded profiles first appear from floor 4 up).
///
/// Framing is up to the camera in the scene — for a clean top-down layout shot, point a
/// camera at the whole floor before pressing P. Capture is game-view only.
/// </summary>
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
            // Let the floor generate, subscribers (incl. DecorationLayer) run, and the
            // frame render before grabbing it.
            yield return null;
            yield return new WaitForEndOfFrame();

            string file = $"profile_{dungeon.LastLayoutProfile}_floor{floor}.png";
            ScreenCapture.CaptureScreenshot(file, Mathf.Max(1, superSize));
            Debug.Log($"[ProfileShowcaseCapture] Captured {file} (profile {dungeon.LastLayoutProfile}).");

            // CaptureScreenshot is asynchronous; give it frames to flush before the next
            // JumpToFloor changes the layout under it.
            yield return null;
            yield return null;
        }

        Debug.Log("[ProfileShowcaseCapture] Done — PNGs written to the project root (beside Assets/).");
    }
}
