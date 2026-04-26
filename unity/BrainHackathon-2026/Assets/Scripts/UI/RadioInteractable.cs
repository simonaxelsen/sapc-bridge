using UnityEngine;

public class RadioInteractable : MonoBehaviour, IGazeInteractable
{
    [Header("Radio Visual Settings")]
    public float pulseSpeed = 12f;
    public float pulseMagnitude = 0.15f;

    [Header("Radio Audio Settings")]
    public float maxVolume = 1.0f;
    public float minVolume = 0.2f;
    public float volumeFadeSpeed = 5f;

    [Header("Defense Settings")]
    [Tooltip("Drag the child GameObject with the massive Dance Zone Collider here.")]
    public GameObject danceZone; // <--- NEW VARIABLE

    private Vector3 originalScale;
    private bool isBeingLookedAt = false;
    private AudioSource audioSource;

    void Start()
    {
        originalScale = transform.localScale;
        audioSource = GetComponent<AudioSource>();
        
        if (audioSource != null)
        {
            audioSource.volume = minVolume;
        }
        else
        {
            Debug.LogWarning($"No AudioSource found on {gameObject.name}! Please add one.");
        }

        // Ensure the dance zone starts turned OFF so the radio isn't passively defending
        if (danceZone != null) danceZone.SetActive(false);
    }

    public void OnLookEnter()
    {
        isBeingLookedAt = true;
        
        // Turn ON the heavy bass zone!
        if (danceZone != null) danceZone.SetActive(true);
    }

    public void OnLookStay() { }

    public void OnLookExit()
    {
        isBeingLookedAt = false;
        
        // Turn OFF the heavy bass zone!
        if (danceZone != null) danceZone.SetActive(false);
    }

    void Update()
    {
        if (isBeingLookedAt)
        {
            float thumpWave = Mathf.Abs(Mathf.Sin(Time.time * pulseSpeed));
            float scaleMultiplier = 1f + (thumpWave * pulseMagnitude);
            transform.localScale = originalScale * scaleMultiplier;

            if (audioSource != null)
            {
                audioSource.volume = Mathf.Lerp(audioSource.volume, maxVolume, Time.deltaTime * volumeFadeSpeed);
            }
        }
        else
        {
            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, Time.deltaTime * 10f);

            if (audioSource != null)
            {
                audioSource.volume = Mathf.Lerp(audioSource.volume, minVolume, Time.deltaTime * volumeFadeSpeed);
            }
        }
    }
}