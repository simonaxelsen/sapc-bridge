using UnityEngine;

public class MyInteractable : MonoBehaviour
{
    [Header("Sine Wave Settings")]
    [Tooltip("How fast the object pulses.")]
    public float pulseSpeed = 5f;
    [Tooltip("How much bigger and smaller it gets.")]
    public float pulseMagnitude = 0.2f;

    private Vector3 originalScale;
    private bool isBeingLookedAt = false;

    void Start()
    {
        // Remember the starting size so we always have a baseline to scale from
        originalScale = transform.localScale;
    }

    // Called by EyeTrackerRaycastTest every frame the raycast hits this object
    public void OnLook()
    {
        isBeingLookedAt = true;
    }

    // LateUpdate runs after standard Updates, ensuring the Raycast has already fired this frame
    void LateUpdate()
    {
        if (isBeingLookedAt)
        {
            // Mathf.Sin(Time.time) creates a continuous wave from -1 to 1. 
            // We multiply by magnitude, and add 1 so it scales relative to its base size.
            float scaleMultiplier = 1f + (Mathf.Sin(Time.time * pulseSpeed) * pulseMagnitude);
            
            // Apply the new scale
            transform.localScale = originalScale * scaleMultiplier;
        }
        else
        {
            // Smoothly snap back to normal size when you look away
            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, Time.deltaTime * 10f);
        }

        // CRITICAL: Reset the flag at the end of the frame!
        // If your eyes are still on it next frame, the Raycast will trigger OnLook() and set it to true again.
        isBeingLookedAt = false;
    }
}