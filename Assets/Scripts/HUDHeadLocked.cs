using UnityEngine;

/// <summary>
/// XR-friendly head-locked HUD with a rotational dead zone.
/// - HUD stays near center but does NOT follow every tiny head rotation.
/// - When the camera rotates beyond the dead-zone angle, the HUD smoothly catches up.
/// Attach this to HUDAnchor only.
/// </summary>
public class HUDHeadLocked : MonoBehaviour
{
    [Header("Target Camera")]
    [SerializeField] private Transform targetCamera;

    [Header("Camera-space Offset (meters)")]
    [SerializeField] private float forward = 0.50f;
    [SerializeField] private float up = -0.08f;
    [SerializeField] private float right = 0f;

    [Header("Dead Zone (Rotation)")]
    [Tooltip("Rotation inside this angle will NOT move the HUD (degrees).")]
    [SerializeField] private float deadZoneDegrees = 6f;

    [Tooltip("How fast the HUD recenters after leaving the dead zone (larger = faster).")]
    [SerializeField] private float recenterSpeed = 10f;

    [Tooltip("If the head turns very fast, multiply recenter speed to avoid losing the HUD.")]
    [SerializeField] private float fastTurnBoost = 1.8f;

    [Tooltip("If angular speed (deg/s) is above this, apply fastTurnBoost.")]
    [SerializeField] private float fastTurnDegPerSec = 120f;

    [Header("Lock Behavior")]
    [SerializeField] private bool matchCameraRotation = true;

    // Internal state: the "frozen" yaw/pitch direction the HUD is currently anchored to.
    private Quaternion anchorRotation;
    private Quaternion lastCamRotation;
    private bool initialized = false;

    private void OnEnable()
    {
        Application.onBeforeRender += OnBeforeRender;
        TryAutoBindCamera();
        initialized = false;
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRender;
    }

    private void LateUpdate()
    {
        ApplyDeadZoneLock();
    }

    private void OnBeforeRender()
    {
        // XR pose can update right before render; ensure HUD is placed with the newest pose.
        ApplyDeadZoneLock();
    }

    private void TryAutoBindCamera()
    {
        if (targetCamera != null) return;
        if (Camera.main != null) targetCamera = Camera.main.transform;
    }

    private void ApplyDeadZoneLock()
    {
        if (targetCamera == null)
        {
            TryAutoBindCamera();
            if (targetCamera == null) return;
        }

        // Initialize anchor on first frame
        if (!initialized)
        {
            anchorRotation = targetCamera.rotation;
            lastCamRotation = targetCamera.rotation;
            initialized = true;
        }

        // Estimate angular speed (deg/s) for fast-turn boost
        float deltaAngle = Quaternion.Angle(lastCamRotation, targetCamera.rotation);
        float angSpeed = (Time.deltaTime > 0f) ? (deltaAngle / Time.deltaTime) : 0f;
        lastCamRotation = targetCamera.rotation;

        // How far is the camera away from our current anchor orientation?
        float drift = Quaternion.Angle(anchorRotation, targetCamera.rotation);

        if (drift <= Mathf.Max(0.1f, deadZoneDegrees))
        {
            // Inside dead zone: keep anchorRotation unchanged (HUD stays stable)
        }
        else
        {
            // Outside dead zone: move anchorRotation toward camera rotation, but only by the "excess" beyond dead zone
            float dt = Time.deltaTime;

            float speed = recenterSpeed;
            if (angSpeed >= fastTurnDegPerSec) speed *= fastTurnBoost;

            // Exponential smoothing factor
            float t = 1f - Mathf.Exp(-speed * dt);

            // Lerp anchor toward camera
            anchorRotation = Quaternion.Slerp(anchorRotation, targetCamera.rotation, t);
        }

        // Place HUD using the anchor rotation (not directly the camera rotation)
        Vector3 desiredPos =
            targetCamera.position +
            (anchorRotation * Vector3.forward) * forward +
            (anchorRotation * Vector3.up) * up +
            (anchorRotation * Vector3.right) * right;

        Quaternion desiredRot = matchCameraRotation
            ? anchorRotation
            : Quaternion.LookRotation((anchorRotation * Vector3.forward), (anchorRotation * Vector3.up));

        transform.SetPositionAndRotation(desiredPos, desiredRot);
    }
}
