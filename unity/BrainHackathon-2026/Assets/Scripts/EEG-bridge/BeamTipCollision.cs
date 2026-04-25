using UnityEngine;

public class BeamTipCollision : MonoBehaviour
{
    [Header("Tip Detection")]
    [Tooltip("Radius of the overlap sphere at the tip (tune to match capsule radius)")]
    public float tipRadius = 0.3f;

    [Tooltip("Layer mask — set to your Meteor layer for performance")]
    public LayerMask meteorLayer = Physics.AllLayers;

    [Header("Debug")]
    public string debugLogName = "MeteorHit";

    // Unity's default capsule: local height = 2, tip at local Y = +1
    // World tip = position + up * scaleY
    private Vector3 TipPosition =>
        transform.position + transform.up * transform.localScale.y;

    void Update()
    {
        Collider[] hits = Physics.OverlapSphere(TipPosition, tipRadius, meteorLayer);

        foreach (Collider hit in hits)
        {
            if (hit.GetComponent<MoveToTarget>() == null) continue;

            Debug.Log(debugLogName);
            Destroy(hit.gameObject);
        }
    }

    // Visualise the tip sphere in the editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(TipPosition, tipRadius);
    }
}