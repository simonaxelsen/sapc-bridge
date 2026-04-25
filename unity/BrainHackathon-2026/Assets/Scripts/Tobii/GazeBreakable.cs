using UnityEngine;

public class GazeBreakable : MonoBehaviour
{
    [Header("Break Settings")]
    public GameObject brokenPrefab;

    [Tooltip("Minimum time the player has to look at the object.")]
    public float minLookTime = 2f;

    [Tooltip("Maximum time the player has to look at the object.")]
    public float maxLookTime = 3f;

    [Header("Explosion")]
    public float explosionForce = 350f;
    public float explosionRadius = 3f;
    public float upwardModifier = 0.4f;

    [Header("Object Life")]
    public bool destroyOriginal = true;
    public float destroyPiecesAfter = 8f;

    [Header("Optional Visual Feedback")]
    public Renderer targetRenderer;
    public Color normalColor = Color.white;
    public Color lookedAtColor = Color.cyan;

    private float lookTimer;
    private float requiredLookTime;
    private bool isLooking;
    private bool hasBroken;

    private void Start()
    {
        requiredLookTime = Random.Range(minLookTime, maxLookTime);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        if (targetRenderer != null)
            targetRenderer.material.color = normalColor;
    }

    public void StartLooking()
    {
        if (hasBroken)
            return;

        isLooking = true;

        if (targetRenderer != null)
            targetRenderer.material.color = lookedAtColor;
    }

    public void KeepLooking()
    {
        if (hasBroken || !isLooking)
            return;

        lookTimer += Time.deltaTime;

        if (lookTimer >= requiredLookTime)
        {
            BreakObject();
        }
    }

    public void StopLooking()
    {
        if (hasBroken)
            return;

        isLooking = false;
        lookTimer = 0f;

        if (targetRenderer != null)
            targetRenderer.material.color = normalColor;
    }

    private void BreakObject()
    {
        if (hasBroken)
            return;

        hasBroken = true;

        if (brokenPrefab != null)
        {
            GameObject brokenObject = Instantiate(
                brokenPrefab,
                transform.position,
                transform.rotation
            );

            brokenObject.transform.localScale = transform.localScale;

            Rigidbody[] rigidbodies = brokenObject.GetComponentsInChildren<Rigidbody>();

            foreach (Rigidbody rb in rigidbodies)
            {
                rb.isKinematic = false;
                rb.useGravity = true;

                rb.AddExplosionForce(
                    explosionForce,
                    transform.position,
                    explosionRadius,
                    upwardModifier,
                    ForceMode.Impulse
                );
            }

            Destroy(brokenObject, destroyPiecesAfter);
        }
        else
        {
            Debug.LogWarning(name + " has no broken prefab assigned.");
        }

        if (destroyOriginal)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public float GetLookProgress()
    {
        if (requiredLookTime <= 0f)
            return 0f;

        return Mathf.Clamp01(lookTimer / requiredLookTime);
    }
}