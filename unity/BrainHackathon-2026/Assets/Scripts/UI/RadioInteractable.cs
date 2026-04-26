using UnityEngine;

public class RadioInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("Radio Visual Settings")]
    [Tooltip("How fast the radio thumps to the 'music'.")]
    public float pulseSpeed = 12f;

    [Tooltip("How much bigger it gets when it thumps (e.g., 0.15 = 15% bigger).")]
    public float pulseMagnitude = 0.15f;

    [Header("Radio Audio Settings")]
    [Tooltip("The volume when actively looking at the radio.")]
    public float maxVolume = 1.0f;
    
    [Tooltip("The quiet background volume when looking away.")]
    public float minVolume = 0.2f;
    
    [Tooltip("How fast the volume fades up and down.")]
    public float volumeFadeSpeed = 5f;

    private Vector3 originalScale;
    private bool isBeingLookedAt = false;
    private AudioSource audioSource;

    void Start()
    {
        originalScale = transform.localScale;
        
        // Grab the AudioSource attached to this same object
        audioSource = GetComponent<AudioSource>();
        
        // Start the radio at the quiet volume
        if (audioSource != null)
        {
            audioSource.volume = minVolume;
        }
        else
        {
            Debug.LogWarning($"No AudioSource found on {gameObject.name}! Please add one.");
        }
    }

    public void OnLookEnter()
    {
        isBeingLookedAt = true;
    }

    public void OnLookStay()
    {
        // Still blank! Math happens in Update.
    }

    public void OnLookExit()
    {
        isBeingLookedAt = false;
    }

    void Update()
    {
        if (isBeingLookedAt)
        {
            // 1. The Speaker Thump Math
            float thumpWave = Mathf.Abs(Mathf.Sin(Time.time * pulseSpeed));
            float scaleMultiplier = 1f + (thumpWave * pulseMagnitude);
            transform.localScale = originalScale * scaleMultiplier;

            // 2. Smoothly fade the volume UP to maxVolume
            if (audioSource != null)
            {
                audioSource.volume = Mathf.Lerp(audioSource.volume, maxVolume, Time.deltaTime * volumeFadeSpeed);
            }
        }
        else
        {
            // 3. Smoothly relax back to normal size when you look away
            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, Time.deltaTime * 10f);

            // 4. Smoothly fade the volume DOWN to minVolume
            if (audioSource != null)
            {
                audioSource.volume = Mathf.Lerp(audioSource.volume, minVolume, Time.deltaTime * volumeFadeSpeed);
            }
        }
    }
}