using UnityEngine;
using Tobii.Gaming; 

public class GazeRayCast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [Tooltip("How far into the 3D world the eye-tracking ray should reach.")]
    public float maxDistance = 100f;
    
    // We store the object we are currently looking at here
    private IGazeInteractable currentInteractable; 

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
            if (Physics.Raycast(gazeRay, out hit, maxDistance))
            {
                // Try to get the interface from the object we hit
                IGazeInteractable hitInteractable = hit.collider.GetComponent<IGazeInteractable>();

                // Did we just look at a BRAND NEW interactable?
                if (hitInteractable != currentInteractable)
                {
                    // Tell the old object we looked away
                    currentInteractable?.OnLookExit(); 
                    
                    // Update our tracker to the new object
                    currentInteractable = hitInteractable; 
                    
                    // Tell the new object we just looked at it
                    currentInteractable?.OnLookEnter(); 
                }

                // Tell the current object we are still looking at it
                currentInteractable?.OnLookStay();
                
                // We successfully looked at an object, so we exit the Update method here!
                return; 
            }
        }

        // 5. THE CLEAR-OUT LOGIC
        // If the code reaches this point, it means one of two things happened:
        //   A) The raycast hit empty space
        //   B) gazePoint.IsValid was false (you blinked or looked away from the screen)
        // In either case, if we WERE looking at an object, we need to turn it off.
        if (currentInteractable != null)
        {
            currentInteractable.OnLookExit();
            currentInteractable = null; // Clear the tracker
        }
    }
}