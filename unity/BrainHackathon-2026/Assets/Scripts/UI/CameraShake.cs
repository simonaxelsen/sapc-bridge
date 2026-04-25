using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public float duration = 0.1f;
    public float magnitude = 0.2f;

    private Vector3 originalPos;

    void Start()
    {
        originalPos = transform.localPosition;
    }

    public void Shake()
    {
        StopAllCoroutines();
        StartCoroutine(DoShake());
    }

    private System.Collections.IEnumerator DoShake()
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            transform.localPosition = originalPos + (Vector3)Random.insideUnitCircle * magnitude;
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = originalPos;
    }
}