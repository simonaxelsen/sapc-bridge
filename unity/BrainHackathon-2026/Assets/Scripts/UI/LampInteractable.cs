using UnityEngine;

public class LampInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("Lamp Settings")]
    [Tooltip("Drag the child GameObject's Sprite Renderer here.")]
    public SpriteRenderer childSprite;
    
    [Tooltip("How fast the light ramps up and down.")]
    public float fadeSpeed = 8f;

    // The colors we are blending between
    private Color offColor;
    private Color onColor = Color.white; // Color.white is full 1,1,1,1
    
    private bool isBeingLookedAt = false;

    void Start()
    {
        // 1. Convert your hex code B2B2B2 into a Unity Color format
        ColorUtility.TryParseHtmlString("#B2B2B2", out offColor);
        
        // 2. Make sure we actually assigned the sprite renderer in the Inspector
        if (childSprite != null)
        {
            // 3. Force it to start exactly at the B2B2B2 off state
            childSprite.color = offColor;
        }
        else
        {
            Debug.LogError($"Hey! You forgot to assign the Child Sprite on {gameObject.name}");
        }
    }

    public void OnLookEnter()
    {
        // Tell the script we are looking at it so the Update loop can start ramping to White
        isBeingLookedAt = true;
    }

    public void OnLookStay()
    {
        // We leave this blank! The ramping needs to happen in Update() 
        // so it has access to Time.deltaTime for a smooth fade.
    }

    public void OnLookExit()
    {
        // Tell the script we looked away so the Update loop starts ramping to B2B2B2
        isBeingLookedAt = false;
    }

    void Update()
    {
        // Safety check to prevent errors
        if (childSprite == null) return;

        // Figure out which color we are trying to transition to based on where the mouse is
        Color targetColor = isBeingLookedAt ? onColor : offColor;

        // Color.Lerp smoothly blends the current color towards the target color over time
        childSprite.color = Color.Lerp(childSprite.color, targetColor, Time.deltaTime * fadeSpeed);
    }
}