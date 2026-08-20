using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keeps the debug metrics panel (Floor/Seed/Rooms/etc) hidden during normal play
/// and only shows it while Tab is held. That readout is genuinely useful for
/// testing and for report screenshots, but leaving it always visible is a big part
/// of why the game reads as a debug build rather than a game — this keeps it
/// available without it dominating the screen.
/// </summary>
public class DebugPanelToggle : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The MetricsText GameObject (or its parent panel) to show/hide.")]
    public GameObject debugPanel;

    void Start()
    {
        if (debugPanel != null)
            debugPanel.SetActive(false);
    }

    void Update()
    {
        if (debugPanel == null) return;

        bool held = Keyboard.current.tabKey.isPressed;
        if (debugPanel.activeSelf != held)
            debugPanel.SetActive(held);
    }
}