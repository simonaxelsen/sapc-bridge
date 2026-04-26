using UnityEngine;

public class LampInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("Lamp Settings")]
    public SpriteRenderer childSprite;
    public float fadeSpeed = 8f;

    [Header("Defense Settings")]
    [Tooltip("Drag the child GameObject with the massive Attract Collider here.")]
    public GameObject attractZone; // <--- NEW VARIABLE

    [Header("UDC/Network Control")]
    public SAPCReceiver networkReceiver;

    private Color offColor;
    private Color onColor = Color.white; 
    private bool isBeingLookedAt = false;

    void Start()
    {
        ColorUtility.TryParseHtmlString("#B2B2B2", out offColor);
        if (childSprite != null) childSprite.color = offColor;
        
        // Ensure the attract zone starts turned OFF
        if (attractZone != null) attractZone.SetActive(false);
    }

    public void OnLookEnter()
    {
        isBeingLookedAt = true;
        // Turn ON the light zone!
        if (attractZone != null) attractZone.SetActive(true); 
    }

    public void OnLookStay() { }

    public void OnLookExit()
    {
        isBeingLookedAt = false;
        // Turn OFF the light zone!
        if (attractZone != null) attractZone.SetActive(false); 
    }

    void Update()
    {
        if (childSprite == null) return;
        Color targetColor = offColor;

        if (isBeingLookedAt)
        {
            if (networkReceiver != null && networkReceiver.isActiveAndEnabled)
            {
                targetColor = Color.Lerp(offColor, onColor, networkReceiver.CurrentValue);
            }
            else
            {
                targetColor = onColor;
            }
        }
        childSprite.color = Color.Lerp(childSprite.color, targetColor, Time.deltaTime * fadeSpeed);
    }
}