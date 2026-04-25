using UnityEngine;

public class BeamTipCollision : MonoBehaviour
{
    [Header("Tip Detection")]
    [Tooltip("Base radius of the overlap sphere (original/non-scaled size)")]
    public float tipRadius = 0.3f;

    [Tooltip("Layer mask — set to your Meteor layer for performance")]
    public LayerMask meteorLayer = Physics.AllLayers;

    [Header("Beam Scaling")]
    [Tooltip("Check this to make the tip follow the scaled beam length")]
    public bool tipFollowsScaledBeam = true;

    [Tooltip("The original Y-scale of the beam (when unscaled)")]
    public float originalScaleY = 1f;

    [Tooltip("Half-height of your mesh in local space (Unity default capsule = 1)")]
    public float meshHalfHeight = 1f;

    [Tooltip("If true, collision radius grows with beam thickness. If false, stays at original size.")]
    public bool scaleRadiusWithBeamThickness = false;

    [Header("Debug")]
    public string debugLogName = "MeteorHit";

    // World-space tip position
    private Vector3 TipPosition
    {
        get
        {
            // lossyScale gives true world size even if parented under scaled objects
            float effectiveScaleY = tipFollowsScaledBeam 
                ? transform.lossyScale.y 
                : originalScaleY;

            return transform.position + transform.up * (meshHalfHeight * effectiveScaleY);
        }
    }

    // World-space collision radius
    private float CurrentRadius
    {
        get
        {
            if (scaleRadiusWithBeamThickness)
            {
                float thickness = (transform.lossyScale.x + transform.lossyScale.z) * 0.5f;
                return tipRadius * thickness;
            }
            return tipRadius; // stays original size regardless of scale
        }
    }

    void Update()
    {
        Collider[] hits = Physics.OverlapSphere(TipPosition, CurrentRadius, meteorLayer);

        foreach (Collider hit in hits)
        {
            if (hit.GetComponent<MoveToTarget>() == null) continue;

            Debug.Log(debugLogName);
            Destroy(hit.gameObject);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(TipPosition, CurrentRadius);

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Gizmos.DrawLine(transform.position, TipPosition);
    }
}