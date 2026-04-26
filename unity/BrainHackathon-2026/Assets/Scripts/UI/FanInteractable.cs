using UnityEngine;

public class FanInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("Defense Settings")]
    [Tooltip("Drag the child GameObject that has the Wind Collider here.")]
    public GameObject windZone;

    [Header("UDC/Network Control")]
    public SAPCReceiver networkReceiver;
    public float maxSpeed = 1.0f;
    public float speedSmoothing = 5f;

    private Animator anim;
    private bool isBeingLookedAt = false;

    void Start()
    {
        anim = GetComponent<Animator>();
        
        // Ensure the wind zone starts turned OFF so it doesn't kill bugs passively
        if (windZone != null) windZone.SetActive(false);
    }

    public void OnLookEnter()
    {
        isBeingLookedAt = true;
        anim.SetTrigger("TurnOn"); 
        
        // Turn ON the deadly wind!
        if (windZone != null) windZone.SetActive(true);
    }

    public void OnLookStay() { }

    public void OnLookExit()
    {
        isBeingLookedAt = false;
        
        if (anim != null) anim.speed = 1f;
        anim.SetTrigger("TurnOff"); 
        
        // Turn OFF the deadly wind!
        if (windZone != null) windZone.SetActive(false);
    }

    void Update()
    {
        if (anim == null) return;

        if (isBeingLookedAt)
        {
            float targetSpeed = maxSpeed;

            if (networkReceiver != null && networkReceiver.isActiveAndEnabled)
            {
                targetSpeed = Mathf.Lerp(0f, maxSpeed, networkReceiver.CurrentValue);
            }

            anim.speed = Mathf.Lerp(anim.speed, targetSpeed, Time.deltaTime * speedSmoothing);
        }
    }
}