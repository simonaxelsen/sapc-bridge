using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class BeamController : MonoBehaviour
{
    [Header("Signal Source")]
    [Tooltip("Reference to the SAPCReceiver handling UDP input.")]
    public SAPCReceiver receiver;

    [Header("Beam Graphics")]
    public LineRenderer beamLine;
    public Transform firePoint;
    public float minBeamWidth = 0.05f;
    public float maxBeamWidth = 0.4f;
    public float minBeamLength = 2f;
    public float maxBeamLength = 20f;
    public Material beamMaterial;

    [Header("Targets")]
    public List<Transform> targets;
    public float targetHitRadius = 1.5f;

    [Header("Effects")]
    public bool enableHitEffect = true;
    public Color lowPowerColor = Color.blue;
    public Color highPowerColor = Color.magenta;
    public float smoothing = 8f;

    [Header("Debug")]
    public bool verboseDebug = true;

    private float visualIntensity = 0f;

    void Start()
    {
        SetupBeam();
    }

    void SetupBeam()
    {
        if (beamLine == null)
        {
            GameObject go = new GameObject("BeamLine");
            go.transform.parent = transform;
            beamLine = go.AddComponent<LineRenderer>();
            beamLine.startWidth = minBeamWidth;
            beamLine.endWidth = minBeamWidth;
            beamLine.positionCount = 2;
            beamLine.material = beamMaterial != null ? beamMaterial : new Material(Shader.Find("Sprites/Default"));
            beamLine.startColor = lowPowerColor;
            beamLine.endColor = lowPowerColor;
        }

        if (firePoint == null)
        {
            firePoint = transform;
        }

        var allTargets = FindObjectsOfType<BeamTarget>();
        targets = new List<Transform>();
        foreach (var t in allTargets) targets.Add(t.transform);
    }

    void Update()
    {
        float rawSignal = 0f;
        if (receiver != null)
        {
            rawSignal = receiver.CurrentValue;
        }

        // Smooth the visual representation 
        visualIntensity = Mathf.Lerp(visualIntensity, rawSignal, Time.deltaTime * smoothing);

        UpdateBeam(visualIntensity);
        CheckHits(visualIntensity);
    }

    void UpdateBeam(float intensity)
    {
        if (beamLine == null) return;

        float width = Mathf.Lerp(minBeamWidth, maxBeamWidth, intensity);
        float length = Mathf.Lerp(minBeamLength, maxBeamLength, intensity);

        beamLine.startWidth = width;
        beamLine.endWidth = width;

        Vector3 endPos = firePoint.forward * length;
        beamLine.SetPosition(0, firePoint.position);
        beamLine.SetPosition(1, firePoint.position + endPos);

        Color beamColor = Color.Lerp(lowPowerColor, highPowerColor, intensity);
        beamLine.startColor = beamColor;
        beamLine.endColor = beamColor;
    }

    void CheckHits(float intensity)
    {
        if (intensity < 0.1f || !enableHitEffect) return;

        Vector3 beamOrigin = firePoint.position;
        Vector3 beamDir = firePoint.forward;
        float beamLength = Mathf.Lerp(minBeamLength, maxBeamLength, intensity);

        foreach (var target in targets)
        {
            if (target == null) continue;

            Vector3 toTarget = target.position - beamOrigin;
            float distAlongBeam = Vector3.Dot(toTarget, beamDir);

            if (distAlongBeam < 0 || distAlongBeam > beamLength) continue;

            float perpDist = (toTarget - beamDir * distAlongBeam).magnitude;

            if (perpDist < targetHitRadius)
            {
                var beamTarget = target.GetComponent<BeamTarget>();
                beamTarget?.Hit(intensity);
            }
        }
    }
}
