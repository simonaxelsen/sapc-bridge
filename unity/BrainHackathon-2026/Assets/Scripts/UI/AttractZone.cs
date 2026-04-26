using UnityEngine;

public class AttractZone : MonoBehaviour
{
    [Tooltip("Drag the actual LampHead object here so the moth knows exactly where the bulb is.")]
    public Transform targetBulb;

    void OnTriggerEnter(Collider other)
    {
        // Check if the thing that touched the light was a bug
        BugBehavior bug = other.GetComponent<BugBehavior>();

        if (bug != null && bug.myType == BugBehavior.BugType.Moth)
        {
            // If you forgot to assign the bulb in the Inspector, fallback to the center of the zone
            Transform flyTarget = targetBulb != null ? targetBulb : transform;
            
            // Yell at the bug to come to the light!
            bug.Attract(flyTarget);
        }
    }
}