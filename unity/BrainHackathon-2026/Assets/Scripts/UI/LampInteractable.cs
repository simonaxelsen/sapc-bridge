using UnityEngine;

public class LampInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("Lamp Settings")]
    [Tooltip("Drag the child GameObject's Sprite Renderer here.")]
    public SpriteRenderer childSprite;
    
    [Tooltip("How fast the light ramps up and down.")]
    public float fadeSpeed = 8f;

    [Header("UDC/Network Control")]
    [Tooltip("Drag the GameObject holding the SAPCReceiver script here.")]
    public SAPCReceiver networkReceiver;

    // The colors we are blending between
    private Color offColor;
    private Color onColor = Color.white; 
    
    private bool isBeingLookedAt = false;

    void Start()
    {
        ColorUtility.TryParseHtmlString("#B2B2B2", out offColor);
        
        if (childSprite != null)
        {
            childSprite.color = offColor;
        }
        else
        {
            Debug.LogError($"Hey! You forgot to assign the Child Sprite on {gameObject.name}");
        }
    }

    public void OnLookEnter()
    {
        isBeingLookedAt = true;
    }

    public void OnLookStay() { }

    public void OnLookExit()
    {
        isBeingLookedAt = false;
    }

    void Update()
    {
        if (childSprite == null) return;

        // 1. We start by assuming the lamp should be OFF
        Color targetColor = offColor;

        // 2. ONLY override the target color if the lamp is turned ON (being looked at)
        if (isBeingLookedAt)
        {
            // Check if the receiver exists AND is actually turned on/active in the scene!
            if (networkReceiver != null && networkReceiver.isActiveAndEnabled)
            {
                targetColor = Color.Lerp(offColor, onColor, networkReceiver.CurrentValue);
            }
            else
            {
                // Safety fallback: If it's unassigned OR disabled, just act like a normal lamp
                targetColor = onColor;
            }
        }

        // 3. Smoothly blend the current color towards our calculated target color
        childSprite.color = Color.Lerp(childSprite.color, targetColor, Time.deltaTime * fadeSpeed);
    }
}