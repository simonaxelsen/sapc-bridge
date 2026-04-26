using UnityEngine;

// This automatically adds a Rigidbody to your bug if you forget to do it yourself!
[RequireComponent(typeof(Rigidbody))] 
public class BugBehavior : MonoBehaviour
{
    public enum BugType { Moth, Mosquito, Beetle }
    
    [Header("Bug Settings")]
    public BugType myType;
    public float moveSpeed = 2f;

    [Header("Beetle Dance Settings")]
    public float pulseSpeed = 15f;
    public float pulseMagnitude = 0.2f;
    [Tooltip("How fast the beetle sinks while dancing.")]
    public float downwardDanceSpeed = 1f;

    private bool isDefeated = false;
    private Rigidbody rb;

    // State trackers for our special behaviors
    private bool isAttracted = false;
    private Transform lampTarget;
    private bool isDancing = false;
    private Vector3 originalScale;

    void Start()
    {
        // Grab the physics component and make sure gravity is OFF to start
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; 
        
        originalScale = transform.localScale;
    }

    void Update()
    {
        // 1. If defeated, stop controlling the movement and let gravity do the work
        if (isDefeated) return;

        // 2. The Beetle Radio effect (Dance downwards slowly)
        if (isDancing)
        {
            // Move slowly downwards instead of left
            transform.Translate(Vector3.down * downwardDanceSpeed * Time.deltaTime);
            
            // Pulse to the beat
            float scaleMultiplier = 1f + (Mathf.Abs(Mathf.Sin(Time.time * pulseSpeed)) * pulseMagnitude);
            transform.localScale = originalScale * scaleMultiplier;
        }
        // 3. The Moth Lamp effect (Fly towards the bulb)
        else if (isAttracted && lampTarget != null)
        {
            // Move directly towards the lamp's position
            transform.position = Vector3.MoveTowards(transform.position, lampTarget.position, moveSpeed * Time.deltaTime);
        }
        // 4. Standard Behavior (March to the right)
        else
        {
            transform.Translate(Vector3.right * moveSpeed * Time.deltaTime);
        }
    }

    // --- INTERACTION METHODS (Called by your Lamp, Fan, and Radio) ---

    public void Attract(Transform target)
    {
        if (myType != BugType.Moth) return; // Only Moths care about the lamp!
        isAttracted = true;
        lampTarget = target;
    }

    public void DanceAway()
    {
        if (myType != BugType.Beetle) return; // Only Beetles care about the radio!
        isDancing = true;

        // Clean them up after a few seconds so they don't pile up endlessly under the floor
        Destroy(gameObject, 6f);
    }

    public void Defeat()
    {
        if (isDefeated) return; // Don't defeat an already dead bug
        
        isDefeated = true;
        isDancing = false;
        isAttracted = false;
        
        // Turn on gravity so they plummet to the floor!
        rb.useGravity = true;
        
        // Give them a tiny random tumble as they fall for polish
        rb.AddTorque(new Vector3(Random.Range(-10f, 10f), Random.Range(-10f, 10f), Random.Range(-10f, 10f)));

        // Clean up after 5 seconds so your floor doesn't fill up with dead bugs
        Destroy(gameObject, 5f);
    }
}