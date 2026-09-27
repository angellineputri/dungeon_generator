using UnityEngine;
using UnityEngine.InputSystem;

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
