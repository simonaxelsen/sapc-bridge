using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Tobii.Gaming;

public class TobiiSignal : MonoBehaviour
{
    [Header("Dot Settings")]
    public float dotSize = 28f;
    public float trailDotSize = 18f;
    public int trailLength = 25;
    public float trailSpacing = 0.02f;

    [Header("Smoothing")]
    public bool smoothMovement = true;
    public float smoothSpeed = 15f;

    [Header("Debug")]
    public bool printGazeToConsole = false;
    public bool invertY = false;

    private Canvas canvas;
    private RectTransform canvasRect;
    private Image mainDot;
    private readonly List<Image> trailDots = new List<Image>();
    private readonly Queue<Vector2> trailPositions = new Queue<Vector2>();

    private Vector2 currentSmoothedPosition;
    private float trailTimer;

    private void Start()
    {
        TobiiAPI.SubscribeGazePointData();

        CreateCanvas();
        CreateMainDot();
        CreateTrailDots();
    }

    private void Update()
    {
        GazePoint gazePoint = TobiiAPI.GetGazePoint();

        if (!gazePoint.IsValid)
        {
            mainDot.enabled = false;

            foreach (Image dot in trailDots)
                dot.enabled = false;

            return;
        }

        mainDot.enabled = true;

        Vector2 screenPosition = gazePoint.Screen;

        if (invertY)
        {
            screenPosition.y = Screen.height - screenPosition.y;
        }

        if (smoothMovement)
        {
            currentSmoothedPosition = Vector2.Lerp(
                currentSmoothedPosition,
                screenPosition,
                smoothSpeed * Time.deltaTime
            );
        }
        else
        {
            currentSmoothedPosition = screenPosition;
        }

        MoveImageToScreenPosition(mainDot.rectTransform, currentSmoothedPosition);

        UpdateTrail(currentSmoothedPosition);

        if (printGazeToConsole)
        {
            Debug.Log($"Tobii gaze: {screenPosition}");
        }
    }

    private void CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Tobii Signal Canvas");

        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        canvasRect = canvas.GetComponent<RectTransform>();
    }

    private void CreateMainDot()
    {
        GameObject dotObject = new GameObject("Tobii Gaze Dot");
        dotObject.transform.SetParent(canvas.transform, false);

        mainDot = dotObject.AddComponent<Image>();
        mainDot.sprite = CreateCircleSprite(64);
        mainDot.color = new Color(0f, 0.75f, 1f, 0.95f);

        RectTransform rect = mainDot.rectTransform;
        rect.sizeDelta = new Vector2(dotSize, dotSize);
    }

    private void CreateTrailDots()
    {
        for (int i = 0; i < trailLength; i++)
        {
            GameObject trailObject = new GameObject($"Tobii Trail Dot {i}");
            trailObject.transform.SetParent(canvas.transform, false);

            Image trailDot = trailObject.AddComponent<Image>();
            trailDot.sprite = CreateCircleSprite(64);

            float alpha = Mathf.Lerp(0.5f, 0.02f, (float)i / trailLength);
            trailDot.color = new Color(0f, 0.75f, 1f, alpha);

            RectTransform rect = trailDot.rectTransform;
            rect.sizeDelta = new Vector2(trailDotSize, trailDotSize);

            trailDots.Add(trailDot);
        }
    }

    private void UpdateTrail(Vector2 screenPosition)
    {
        trailTimer += Time.deltaTime;

        if (trailTimer < trailSpacing)
            return;

        trailTimer = 0f;

        trailPositions.Enqueue(screenPosition);

        while (trailPositions.Count > trailLength)
        {
            trailPositions.Dequeue();
        }

        Vector2[] positions = trailPositions.ToArray();

        for (int i = 0; i < trailDots.Count; i++)
        {
            int positionIndex = positions.Length - 1 - i;

            if (positionIndex < 0)
            {
                trailDots[i].enabled = false;
                continue;
            }

            trailDots[i].enabled = true;
            MoveImageToScreenPosition(trailDots[i].rectTransform, positions[positionIndex]);
        }
    }

    private void MoveImageToScreenPosition(RectTransform rectTransform, Vector2 screenPosition)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPosition,
            null,
            out Vector2 localPoint
        );

        rectTransform.anchoredPosition = localPoint;
    }

    private Sprite CreateCircleSprite(int size)
    {
        Texture2D texture = new Texture2D(size, size);
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = distance <= radius ? 1f : 0f;

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f)
        );
    }
}