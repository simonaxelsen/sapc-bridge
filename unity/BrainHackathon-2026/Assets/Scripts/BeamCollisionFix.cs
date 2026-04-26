using UnityEngine;

[RequireComponent(typeof(CapsuleCollider))]
public class BeamCollisionFix : MonoBehaviour
{
    private CapsuleCollider col;

    [Header("Beam")]
    public float beamLength = 5f;
    public float beamRadius = 0.3f;

    void Awake()
    {
        col = GetComponent<CapsuleCollider>();

        // Important
        col.direction = 1; // Y axis
        col.isTrigger = true;
    }

    void Update()
    {
        // Force collider to match beam size
        col.radius = beamRadius;
        col.height = beamLength * 2f;
        col.center = Vector3.zero;

        // IMPORTANT: prevent scaling issues
        transform.localScale = new Vector3(1, beamLength, 1);
    }

    void OnTriggerEnter(Collider other)
    {
        GazeBeautyBreakable beautyBreakable = other.GetComponentInParent<GazeBeautyBreakable>();
        if (beautyBreakable != null && beautyBreakable.TriggerFromBeamHit())
        {
            Debug.Log("Beam hit beauty target: " + beautyBreakable.name);
            return;
        }

        MoveToTarget meteor = other.GetComponentInParent<MoveToTarget>();

        if (meteor == null)
            return;

        Debug.Log("MeteorHit: " + meteor.name);

        GpuPrefabBurstSpawner.PlayIfPresent(meteor.gameObject);
        Destroy(meteor.gameObject);
    }
}
