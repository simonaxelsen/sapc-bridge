using UnityEngine;
using Tobii.Gaming; 

public class GazeRayCast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [Tooltip("How far into the 3D world the eye-tracking ray should reach.")]
    public float maxDistance = 100f;
    
    // Optional: Use a LayerMask to only hit specific objects (like "Interactables")
    // public LayerMask interactableLayers; 

    void Update()
    {
        // 1. Get the current frame of eye data
        GazePoint gazePoint = TobiiAPI.GetGazePoint();

        // 2. Check if the tracker actually sees your eyes right now
        if (gazePoint.IsValid)
        {
            // 3. Create a Ray starting from the camera, passing through the screen pixel you are looking at
            Ray gazeRay = Camera.main.ScreenPointToRay(gazePoint.Screen);
            RaycastHit hit;

            // 4. Perform the raycast
            // If it hits something within maxDistance, Physics.Raycast returns true
            if (Physics.Raycast(gazeRay, out hit, maxDistance))
            {
                // 5. Log the name of the 3D object we just hit!
                Debug.Log($"Eye tracking hit: {hit.collider.gameObject.name}");
                
                hit.collider.GetComponent<IGazeInteractable>();;
                
                // Optional: Draw a debug line in the Scene view to visualize the raycast
                Debug.DrawLine(gazeRay.origin, hit.point, Color.green);
            }
            else
            {
                // Draw a red line if we are looking at empty space
                Debug.DrawRay(gazeRay.origin, gazeRay.direction * maxDistance, Color.red);
            }
        }
    }
}