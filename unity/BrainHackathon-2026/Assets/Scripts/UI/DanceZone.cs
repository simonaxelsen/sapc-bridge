using UnityEngine;

public class DanceZone : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        // Check if the thing that entered the sound wave was a bug
        BugBehavior bug = other.GetComponent<BugBehavior>();

        if (bug != null && bug.myType == BugBehavior.BugType.Beetle)
        {
            // Hit 'em with the bass!
            bug.DanceAway();
        }
    }
}