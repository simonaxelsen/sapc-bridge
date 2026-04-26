using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GazeBeautyBreakable : MonoBehaviour
{
    public enum EffectMode
    {
        FireworksOnly,
        ButterfliesOnly,
        RandomOneType,
        MixedEffects
    }

    [Header("Look Timing")]
    public float minLookTime = 2f;
    public float maxLookTime = 3f;

    [Header("Effect Choice")]
    public EffectMode effectMode = EffectMode.MixedEffects;

    [Tooltip("Assign one or more firework / particle prefabs here.")]
    public GameObject[] fireworksPrefabs;

    [Tooltip("Assign one or more butterfly burst prefabs here.")]
    public GameObject[] butterflyPrefabs;

    [Tooltip("Optional extra magical effects, sparkles, smoke, portals, etc.")]
    public GameObject[] extraEffectPrefabs;

    [Header("Optional GPU Burst")]
    [Tooltip("Optional compute-shader burst that plays when this object triggers.")]
    public GpuPrefabBurstSpawner destroyBurst;

    [Header("Spawn Amount")]
    public int instantBurstCount = 3;
    public int overTimeSpawnCount = 10;
    public float spawnDuration = 2.5f;

    [Header("Spawn Area")]
    public float spawnRadius = 1.2f;
    public float upwardOffset = 0.5f;
    public float randomHeight = 1.5f;

    [Header("Random Transform")]
    public bool randomRotation = true;
    public bool randomScale = true;
    public float minScale = 0.7f;
    public float maxScale = 1.4f;

    [Header("Lifetime")]
    public bool hideOriginalObject = true;
    public bool destroyOriginalAfterEffects = false;
    public float spawnedEffectLifetime = 8f;

    [Header("Optional Visual Feedback")]
    public Renderer targetRenderer;
    public Color normalColor = Color.white;
    public Color lookedAtColor = Color.cyan;

    [SerializeField, HideInInspector] private GameObject fireworksPrefab;
    [SerializeField, HideInInspector] private GameObject butterflyPrefab;

    private float lookTimer;
    private float requiredLookTime;
    private bool isLooking;
    private bool hasTriggered;

    private void Start()
    {
        requiredLookTime = Random.Range(minLookTime, maxLookTime);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        if (destroyBurst == null)
            destroyBurst = GetComponent<GpuPrefabBurstSpawner>();

        SetFeedbackColor(normalColor);
    }

    public void StartLooking()
    {
        if (hasTriggered)
            return;

        isLooking = true;

        SetFeedbackColor(lookedAtColor);
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

        SetFeedbackColor(normalColor);
    }

    public bool TriggerFromBeamHit()
    {
        return TriggerBeautyEffect();
    }

    private bool TriggerBeautyEffect()
    {
        if (hasTriggered)
            return true;

        List<GameObject> candidatePrefabs = GetCandidatePrefabs();
        bool hasCpuEffects = candidatePrefabs.Count > 0;
        bool hasGpuBurst = destroyBurst != null;
        if (!hasCpuEffects && !hasGpuBurst)
        {
            Debug.LogWarning(name + " has no beauty effect prefabs assigned.");
            return false;
        }

        hasTriggered = true;

        if (destroyBurst != null)
        {
            destroyBurst.Play(transform.position, transform.rotation);
        }

        if (hideOriginalObject)
        {
            HideOriginalObject();
        }

        if (hasCpuEffects)
        {
            StartCoroutine(SpawnEffectsSequence(candidatePrefabs));
            return true;
        }

        if (destroyOriginalAfterEffects)
        {
            Destroy(gameObject, spawnedEffectLifetime);
        }

        return true;
    }

    private IEnumerator SpawnEffectsSequence(List<GameObject> candidatePrefabs)
    {
        // Immediate burst
        for (int i = 0; i < instantBurstCount; i++)
        {
            SpawnOneEffect(candidatePrefabs);
        }

        // Effects over time
        float delay = overTimeSpawnCount > 0 ? spawnDuration / overTimeSpawnCount : 0f;

        for (int i = 0; i < overTimeSpawnCount; i++)
        {
            yield return new WaitForSeconds(delay);
            SpawnOneEffect(candidatePrefabs);
        }

        if (destroyOriginalAfterEffects)
        {
            Destroy(gameObject, spawnedEffectLifetime);
        }
    }

    private List<GameObject> GetCandidatePrefabs()
    {
        List<GameObject> candidates = new List<GameObject>();

        switch (effectMode)
        {
            case EffectMode.FireworksOnly:
                AddPrefabs(candidates, fireworksPrefabs);
                AddPrefab(candidates, fireworksPrefab);
                break;

            case EffectMode.ButterfliesOnly:
                AddPrefabs(candidates, butterflyPrefabs);
                AddPrefab(candidates, butterflyPrefab);
                break;

            case EffectMode.RandomOneType:
                bool chooseFireworks = Random.value > 0.5f;

                if (chooseFireworks)
                {
                    AddPrefabs(candidates, fireworksPrefabs);
                    AddPrefab(candidates, fireworksPrefab);
                }
                else
                {
                    AddPrefabs(candidates, butterflyPrefabs);
                    AddPrefab(candidates, butterflyPrefab);
                }

                break;

            case EffectMode.MixedEffects:
                AddPrefabs(candidates, fireworksPrefabs);
                AddPrefabs(candidates, butterflyPrefabs);
                AddPrefabs(candidates, extraEffectPrefabs);
                AddPrefab(candidates, fireworksPrefab);
                AddPrefab(candidates, butterflyPrefab);
                break;
        }

        return candidates;
    }

    private void AddPrefabs(List<GameObject> list, GameObject[] prefabs)
    {
        if (prefabs == null)
            return;

        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null)
                list.Add(prefab);
        }
    }

    private void AddPrefab(List<GameObject> list, GameObject prefab)
    {
        if (prefab != null)
            list.Add(prefab);
    }

    private void SpawnOneEffect(List<GameObject> candidatePrefabs)
    {
        GameObject prefab = candidatePrefabs[Random.Range(0, candidatePrefabs.Count)];

        Vector3 randomOffset = Random.insideUnitSphere * spawnRadius;
        randomOffset.y = Mathf.Abs(randomOffset.y) + upwardOffset + Random.Range(0f, randomHeight);

        Vector3 spawnPosition = transform.position + randomOffset;

        Quaternion spawnRotation = randomRotation ? Random.rotation : Quaternion.identity;

        GameObject spawnedEffect = Instantiate(
            prefab,
            spawnPosition,
            spawnRotation
        );

        if (randomScale)
        {
            float scale = Random.Range(minScale, maxScale);
            spawnedEffect.transform.localScale *= scale;
        }

        PlayParticleSystems(spawnedEffect);

        Destroy(spawnedEffect, spawnedEffectLifetime);
    }

    private void PlayParticleSystems(GameObject effectObject)
    {
        ParticleSystem[] particleSystems = effectObject.GetComponentsInChildren<ParticleSystem>();

        foreach (ParticleSystem ps in particleSystems)
        {
            ps.Play();
        }
    }

    private void HideOriginalObject()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Collider[] colliders = GetComponentsInChildren<Collider>();

        foreach (Renderer r in renderers)
            r.enabled = false;

        foreach (Collider c in colliders)
            c.enabled = false;
    }

    private void SetFeedbackColor(Color color)
    {
        if (targetRenderer == null)
            return;

        Material material = targetRenderer.material;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
            return;
        }

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    public float GetLookProgress()
    {
        if (requiredLookTime <= 0f)
            return 0f;

        return Mathf.Clamp01(lookTimer / requiredLookTime);
    }
}
