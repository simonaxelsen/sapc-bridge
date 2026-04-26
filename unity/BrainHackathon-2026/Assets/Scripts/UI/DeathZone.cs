using UnityEngine;

public class DeathZone : MonoBehaviour
{
    [Header("Zone Settings")]
    [Tooltip("Which specific bug type does this zone defeat?")]
    public BugBehavior.BugType targetBug;

    // This is a built-in Unity method that fires the exact moment another 
    // collider touches this object's Trigger Collider.
    void OnTriggerEnter(Collider other)
    {
        // Check if the thing that touched the zone was a bug
        BugBehavior bug = other.GetComponent<BugBehavior>();

        if (bug != null)
        {
            // Check if the bug's type matches the one we set in the Inspector!
            if (bug.myType == targetBug)
            {
                // Swat it down!
                bug.Defeat();
            }
        }
    }
}