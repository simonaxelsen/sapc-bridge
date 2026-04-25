using UnityEngine;

public class MoveToTarget : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Assign the target here, or leave empty to auto-find by tag 'Player'")]
    public Transform target;

    [Header("Movement")]
    public float moveSpeed = 3f;

    [Header("Stop Distance")]
    [Tooltip("How close to get before stopping (0 = never stop)")]
    public float stoppingDistance = 0f;

    void Start()
    {
        if (target == null)
        {
            GameObject found = GameObject.FindWithTag("Player");
            if (found != null)
                target = found.transform;
            else
                Debug.LogWarning($"[MoveToTarget] No target assigned and none found with tag 'Player' on {gameObject.name}");
        }
    }

    void Update()
    {
        if (target == null) return;

        Vector2 myPos2D     = new Vector2(transform.position.x, transform.position.y);
        Vector2 targetPos2D = new Vector2(target.position.x,    target.position.y);

        float distance = Vector2.Distance(myPos2D, targetPos2D);
        if (distance > stoppingDistance)
        {
            Vector2 direction = (targetPos2D - myPos2D).normalized;
            Vector2 newPos2D  = myPos2D + direction * moveSpeed * Time.deltaTime;

            // XY movement + Z always locked to the target's Z
            transform.position = new Vector3(newPos2D.x, newPos2D.y, target.position.z);
        }
    }
}