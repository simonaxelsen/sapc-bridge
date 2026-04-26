using UnityEngine;

public class DeathByFalling : MonoBehaviour
{
    [Header("Zone Settings")]
    [Tooltip("Points to add (positive) or subtract (negative) when a bug hits this zone.")]
    public int scoreChange = 10;

    void OnTriggerEnter(Collider other)
    {
        // 1. Check if the object that entered the trigger has the BugBehavior script
        BugBehavior bug = other.GetComponent<BugBehavior>();

        if (bug != null)
        {
            // 2. Announce the score change! 
            // (Later, you will replace this Debug.Log with a call to your GameManager or Score UI)
            Debug.Log($"A {bug.myType} hit the zone! Score change: {scoreChange}");

            // 3. Completely destroy the bug so it gets removed from the game
            Destroy(bug.gameObject);
        }
    }
}