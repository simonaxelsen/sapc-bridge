using UnityEngine;

public class BugSpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    [Tooltip("Drag your blue Bug Prefabs from the Project window into this list.")]
    public GameObject[] bugPrefabs; // The [] brackets turn this into a list/array!
    
    [Tooltip("How many seconds between each bug spawn?")]
    public float spawnInterval = 3f;

    private float timer = 0f;

    void Update()
    {
        // Add the time that has passed since the last frame
        timer += Time.deltaTime;

        // If enough time has passed, spawn a bug and reset the clock!
        if (timer >= spawnInterval)
        {
            SpawnBug();
            timer = 0f; // Reset the timer
        }
    }

    void SpawnBug()
    {
        // Safety check: make sure the list exists and actually has things in it
        if (bugPrefabs == null || bugPrefabs.Length == 0) return;

        // Pick a random number between 0 and the total number of bugs in your list
        int randomIndex = Random.Range(0, bugPrefabs.Length);
        
        // Grab the specific bug prefab at that random slot
        GameObject bugToSpawn = bugPrefabs[randomIndex];

        // Safety check in case there's an empty slot in your Inspector list
        if (bugToSpawn == null) return;

        // Clone the randomly selected bug
        Instantiate(bugToSpawn, transform.position, Quaternion.identity);
    }
}