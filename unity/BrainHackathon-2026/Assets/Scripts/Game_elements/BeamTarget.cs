using UnityEngine;

public class BeamTarget : MonoBehaviour
{
    [Header("Target Behavior")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public bool destroyOnDeath = true;
    public float deathDelay = 0.5f;

    [Header("Visual Feedback")]
    public float hitFlashTime = 0.1f;
    public Color hitColor = Color.red;
    public Vector3 hitScaleBump = new Vector3(1.3f, 1.3f, 1.3f);

    [Header("Particle Effects")]
    public GameObject hitParticlePrefab;
    public Transform particleSpawnPoint;

    private Renderer rend;
    private Color originalColor;
    private Vector3 originalScale;
    private float lastHitTime;

    void Start()
    {
        rend = GetComponent<Renderer>();
        originalColor = rend != null ? rend.material.color : Color.white;
        originalScale = transform.localScale;
        currentHealth = maxHealth;
    }

    public void Hit(float intensity)
    {
        float damage = intensity * 50f;
        currentHealth -= damage;
        lastHitTime = Time.time;

        VisualFlash();
        SpawnParticles();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void VisualFlash()
    {
        if (rend != null)
        {
            rend.material.color = hitColor;
            Invoke(nameof(ResetColor), hitFlashTime);
        }

        transform.localScale = Vector3.Scale(originalScale, hitScaleBump);
        Invoke(nameof(ResetScale), hitFlashTime);
    }

    void ResetColor()
    {
        if (rend != null) rend.material.color = originalColor;
    }

    void ResetScale()
    {
        transform.localScale = originalScale;
    }

    void SpawnParticles()
    {
        if (hitParticlePrefab != null)
        {
            Vector3 spawnPos = particleSpawnPoint != null ? particleSpawnPoint.position : transform.position;
            GameObject p = Instantiate(hitParticlePrefab, spawnPos, Quaternion.identity);
            Destroy(p, 2f);
        }
    }

    void Die()
    {
        if (destroyOnDeath)
        {
            Destroy(gameObject, deathDelay);
        }

        if (rend != null)
        {
            rend.material.color = Color.black;
        }
        transform.localScale = Vector3.zero;
    }
}