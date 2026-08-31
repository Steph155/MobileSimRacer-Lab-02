using UnityEngine;

/// <summary>
/// Smooth chase camera for the RigRacer prototype. Attach to the scene's
/// Main Camera (or any camera) and point `target` at the CarController.
/// Follows behind the car, lifts with speed, and looks slightly ahead.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CarChaseCamera : MonoBehaviour
{
    [Header("Target")]
    public CarController target;

    [Header("Framing")]
    public float distance = 6.5f;     // behind the car
    public float height = 2.6f;       // above the car
    public float lookAhead = 4f;      // how far ahead of the car to aim
    public float speedHeightGain = 0.012f; // extra height per kph

    [Header("Smoothing")]
    public float positionLerp = 6f;   // higher = snappier
    public float lookLerp = 8f;

    Vector3 currentLook = Vector3.zero;

    void Awake()
    {
        if (target == null) target = FindObjectOfType<CarController>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        Transform car = target.transform;
        Vector3 carPos = car.position;
        Vector3 fwd = car.forward;

        // Desired camera position: behind + above, rising with speed.
        float extraH = target.SpeedKph * speedHeightGain;
        Vector3 desiredPos = carPos - fwd * distance + Vector3.up * (height + extraH);

        // Keep the camera above the ground plane (y = 0) so it never clips under the terrain.
        if (desiredPos.y < 0.5f) desiredPos.y = 0.5f;

        transform.position = Vector3.Lerp(transform.position, desiredPos, 1f - Mathf.Exp(-positionLerp * Time.deltaTime));

        // Look slightly ahead of the car.
        Vector3 desiredLook = carPos + fwd * lookAhead + Vector3.up * 0.5f;
        currentLook = Vector3.Lerp(currentLook, desiredLook, 1f - Mathf.Exp(-lookLerp * Time.deltaTime));
        transform.LookAt(currentLook);
    }
}
