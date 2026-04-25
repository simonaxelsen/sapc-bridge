using UnityEngine;
using UnityEngine.InputSystem;

public class Beam_scale : MonoBehaviour
{
    [Header("EEG Input")]
    [Tooltip("Reference to the SAPCReceiver that provides EEG values")]
    public SAPCReceiver sapcReceiver;

    [Header("Capsule Control")]
    [Tooltip("The 3D capsule to scale (leave empty to use this GameObject)")]
    public Transform targetCapsule;

    [Tooltip("Minimum Y scale when value is 0.0")]
    public float minScale = 0.1f;

    [Tooltip("Maximum Y scale when value is 1.0")]
    public float maxScale = 2.0f;

    [Tooltip("Visual smoothing (higher = faster)")]
    public float smoothing = 8f;

    [Header("Dev Controls")]
    [Tooltip("Enable arrow-key rotation in Play mode")]
    public bool devRotationEnabled = true;

    [Tooltip("Degrees per second when holding an arrow key")]
    public float rotationSpeed = 90f;

    // Fixed world-space position of the bottom anchor
    private Vector3 _baseWorldPos;

    // Actual bottom point of the mesh in local space
    private Vector3 _baseLocalPoint;

    void Start()
    {
        if (targetCapsule == null)
            targetCapsule = transform;

        // Find the actual bottom point of the mesh in local space
        MeshFilter mf = targetCapsule.GetComponent<MeshFilter>();

        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            _baseLocalPoint = new Vector3(0f, b.min.y, 0f);
        }
        else
        {
            // Fallback for a default Unity capsule-like object
            _baseLocalPoint = new Vector3(0f, -1f, 0f);
        }

        // Store the original world-space position of the bottom anchor
        _baseWorldPos = targetCapsule.TransformPoint(_baseLocalPoint);

        // Auto-find SAPCReceiver if not assigned
        if (sapcReceiver == null)
            sapcReceiver = GetComponent<SAPCReceiver>();

        if (sapcReceiver == null)
            Debug.LogWarning("Beam_scale: No SAPCReceiver assigned and none found on this GameObject!");
        else
            Debug.Log("Beam_scale: Connected to SAPCReceiver");
    }

    void Update()
    {
        HandleDevRotation();
        HandleScaling();
    }

    // ── Arrow-key rotation, pivoting around the locked bottom anchor ──────────
    private void HandleDevRotation()
    {
        if (!devRotationEnabled) return;
        if (Keyboard.current == null) return;

        float rotInput = 0f;

        if (Keyboard.current.leftArrowKey.isPressed)
            rotInput = 1f;

        if (Keyboard.current.rightArrowKey.isPressed)
            rotInput = -1f;

        if (rotInput == 0f) return;

        targetCapsule.RotateAround(
            _baseWorldPos,
            Vector3.forward,
            rotInput * rotationSpeed * Time.deltaTime
        );

        RepositionToBase();
    }

    // ── Y-only scaling anchored to the original bottom point ──────────────────
    private void HandleScaling()
    {
        float rawValue = sapcReceiver != null ? sapcReceiver.CurrentValue : 0.5f;
        rawValue = Mathf.Clamp01(rawValue);

        float targetY = Mathf.Lerp(minScale, maxScale, rawValue);

        Vector3 currentScale = targetCapsule.localScale;
        float newY = Mathf.Lerp(currentScale.y, targetY, Time.deltaTime * smoothing);

        targetCapsule.localScale = new Vector3(
            currentScale.x,
            newY,
            currentScale.z
        );

        // Move the object so the original bottom point stays fixed
        RepositionToBase();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RepositionToBase()
    {
        Vector3 currentBaseWorld = targetCapsule.TransformPoint(_baseLocalPoint);
        Vector3 correction = _baseWorldPos - currentBaseWorld;

        targetCapsule.position += correction;
    }

    private Vector3 GetCurrentBaseWorldPos()
    {
        if (targetCapsule == null)
            return transform.position;

        return targetCapsule.TransformPoint(_baseLocalPoint);
    }

    void OnDrawGizmosSelected()
    {
        if (targetCapsule == null)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(Application.isPlaying ? _baseWorldPos : GetCurrentBaseWorldPos(), 0.1f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(GetCurrentBaseWorldPos(), 0.06f);
    }
}