using UnityEngine;

/// <summary>
/// Smoothly follows the player and holds a much tighter orthographic size than the
/// static full-map view. This alone is one of the biggest levers for "feels like a
/// game" vs "feels like a debug tool" — a fixed wide shot reads as watching a
/// simulation; a close, tracking camera reads as controlling a character.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("References")]
    public Transform target;

    [Header("Follow")]
    public float smoothSpeed = 6f;
    public float orthographicSize = 12f;

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam != null)
            cam.orthographicSize = orthographicSize;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = new Vector3(target.position.x, target.position.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }
}