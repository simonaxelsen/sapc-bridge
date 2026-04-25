using UnityEngine;

public class GazeBeautyBreakable : MonoBehaviour
{
    public enum EffectMode
    {
        Fireworks,
        Butterflies,
        Random
    }

    [Header("Look Timing")]
    public float minLookTime = 2f;
    public float maxLookTime = 3f;

    [Header("Effect Choice")]
    public EffectMode effectMode = EffectMode.Random;
    public GameObject fireworksPrefab;
    public GameObject butterflyPrefab;

    [Header("Optional")]
    public bool hideOriginalObject = true;
    public float spawnedEffectLifetime = 6f;

    [Header("Optional Visual Feedback")]
    public Renderer targetRenderer;
    public Color normalColor = Color.white;
    public Color lookedAtColor = Color.cyan;

    private float lookTimer;
    private float requiredLookTime;
    private bool isLooking;
    private bool hasTriggered;

    private void Start()
    {
        requiredLookTime = Random.Range(minLookTime, maxLookTime);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        if (targetRenderer != null && targetRenderer.material.HasProperty("_Color"))
            targetRenderer.material.color = normalColor;
    }

    public void StartLooking()
    {
        if (hasTriggered)
            return;

        isLooking = true;

        if (targetRenderer != null && targetRenderer.material.HasProperty("_Color"))
            targetRenderer.material.color = lookedAtColor;
    }

    public void KeepLooking()
    {
        if (hasTriggered || !isLooking)
            return;

        lookTimer += Time.deltaTime;

        if (lookTimer >= requiredLookTime)
        {
            TriggerBeautyEffect();
        }
    }

    public void StopLooking()
    {
        if (hasTriggered)
            return;

        isLooking = false;
        lookTimer = 0f;

        if (targetRenderer != null && targetRenderer.material.HasProperty("_Color"))
            targetRenderer.material.color = normalColor;
    }

    private void TriggerBeautyEffect()
    {
        if (hasTriggered)
            return;

        hasTriggered = true;

        GameObject prefabToSpawn = null;

        switch (effectMode)
        {
            case EffectMode.Fireworks:
                prefabToSpawn = fireworksPrefab;
                break;

            case EffectMode.Butterflies:
                prefabToSpawn = butterflyPrefab;
                break;

            case EffectMode.Random:
                prefabToSpawn = Random.value > 0.5f ? fireworksPrefab : butterflyPrefab;
                break;
        }

        if (prefabToSpawn != null)
        {
            GameObject effect = Instantiate(
                prefabToSpawn,
                transform.position,
                Quaternion.identity
            );

            Destroy(effect, spawnedEffectLifetime);
        }

        if (hideOriginalObject)
        {
            HideObject();
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void HideObject()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Collider[] colliders = GetComponentsInChildren<Collider>();

        foreach (Renderer r in renderers)
            r.enabled = false;

        foreach (Collider c in colliders)
            c.enabled = false;
    }

    public float GetLookProgress()
    {
        if (requiredLookTime <= 0f)
            return 0f;

        return Mathf.Clamp01(lookTimer / requiredLookTime);
    }
}