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

    // World-space position of the base (bottom tip) — stays fixed as Y scales
    private Vector3 _baseWorldPos;

    void Start()
    {
        if (targetCapsule == null)
            targetCapsule = transform;

        // Snapshot the base anchor in world space
        _baseWorldPos = GetBaseWorldPos();

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

    // ── Arrow-key rotation (Z axis, so beam sweeps in the XY plane) ──────────
    private void HandleDevRotation()
    {
        if (!devRotationEnabled) return;

        float rotInput = 0f;
        if (Keyboard.current.leftArrowKey.isPressed)  rotInput =  1f;
        if (Keyboard.current.rightArrowKey.isPressed) rotInput = -1f;

        if (rotInput == 0f) return;

        // Rotate around Z, pivoting about the base anchor so it swings like a turret
        targetCapsule.RotateAround(_baseWorldPos, Vector3.forward, rotInput * rotationSpeed * Time.deltaTime);

        // After rotating, re-lock the base to its fixed world position
        RepositionToBase();
    }

    // ── Y-only scaling anchored to the base ──────────────────────────────────
    private void HandleScaling()
    {
        float rawValue = (sapcReceiver != null) ? sapcReceiver.CurrentValue : 0.5f;

        float targetY = Mathf.Lerp(minScale, maxScale, rawValue);
        Vector3 current = targetCapsule.localScale;
        float newY = Mathf.Lerp(current.y, targetY, Time.deltaTime * smoothing);

        targetCapsule.localScale = new Vector3(current.x, newY, current.z);

        // Directly pin center = base + up * scaleY so only the tip end moves
        targetCapsule.position = _baseWorldPos + targetCapsule.up * newY;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // World-space base = center minus up * scaleY
    private Vector3 GetBaseWorldPos()
    {
        return targetCapsule.position - targetCapsule.up * targetCapsule.localScale.y;
    }

    // After rotation, repin position so base stays locked
    private void RepositionToBase()
    {
        targetCapsule.position = _baseWorldPos + targetCapsule.up * targetCapsule.localScale.y;
    }

    // Show base anchor and current base point in the editor
    void OnDrawGizmosSelected()
    {
        if (targetCapsule == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(GetBaseWorldPos(), 0.1f);
    }


}