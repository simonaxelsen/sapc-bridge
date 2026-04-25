using UnityEngine;

public class FanInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("UDC/Network Control")]
    [Tooltip("Drag the GameObject holding the SAPCReceiver script here.")]
    public SAPCReceiver networkReceiver;

    [Tooltip("The normal animation speed when UDC is fully at 1.0")]
    public float maxSpeed = 1.0f;

    [Tooltip("How smoothly the fan adjusts to new network speeds.")]
    public float speedSmoothing = 5f;

    private Animator anim;
    private bool isBeingLookedAt = false;

    void Start()
    {
        anim = GetComponent<Animator>();
    }

    public void OnLookEnter()
    {
        isBeingLookedAt = true;
        anim.SetTrigger("TurnOn"); 
    }

    public void OnLookStay() 
    { 
        // Required by interface
    }

    public void OnLookExit()
    {
        isBeingLookedAt = false;
        
        // CRITICAL: Reset the master speed dial back to 1. 
        // This ensures your wind-down animation plays at its normal, expected pace!
        if (anim != null) anim.speed = 1f;
        
        anim.SetTrigger("TurnOff"); 
    }

    void Update()
    {
        if (anim == null) return;

        // ONLY override the animation speed if the fan is actively on
        if (isBeingLookedAt)
        {
            // 1. We start by assuming it should play at max speed
            float targetSpeed = maxSpeed;

            // 2. Check if the network receiver is assigned AND actively running
            if (networkReceiver != null && networkReceiver.isActiveAndEnabled)
            {
                // Map the UDC value (0.0 to 1.0) to an animation speed (0.0 to maxSpeed)
                targetSpeed = Mathf.Lerp(0f, maxSpeed, networkReceiver.CurrentValue);
            }

            // 3. Smoothly adjust the Animator's playback speed to match the target
            anim.speed = Mathf.Lerp(anim.speed, targetSpeed, Time.deltaTime * speedSmoothing);
        }
    }
}