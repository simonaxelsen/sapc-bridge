using UnityEngine;
using Tobii.Gaming;

public class GazeDwellBreaker : MonoBehaviour
{
    [Header("Camera")]
    public Camera mainCamera;

    [Header("Gaze Settings")]
    public float rayDistance = 100f;
    public LayerMask gazeLayerMask = ~0;

    [Header("Debug")]
    public bool drawDebugRay = true;
    public bool printDebugInfo = false;
    public bool invertY = false;

    private GazeBeautyBreakable currentBreakable;

    private void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        TobiiAPI.SubscribeGazePointData();
    }

    private void Update()
    {
        GazePoint gazePoint = TobiiAPI.GetGazePoint();

        if (!gazePoint.IsValid)
        {
            ClearCurrentTarget();
            return;
        }

        Vector2 screenPosition = gazePoint.Screen;

        if (invertY)
            screenPosition.y = Screen.height - screenPosition.y;

        Ray gazeRay = mainCamera.ScreenPointToRay(screenPosition);

        if (drawDebugRay)
            Debug.DrawRay(gazeRay.origin, gazeRay.direction * rayDistance, Color.cyan);

        if (Physics.Raycast(gazeRay, out RaycastHit hit, rayDistance, gazeLayerMask))
        {
            GazeBeautyBreakable breakable =
                hit.collider.GetComponentInParent<GazeBeautyBreakable>();

            if (breakable != null)
            {
                SetCurrentTarget(breakable);

                if (printDebugInfo)
                    Debug.Log("Looking at: " + breakable.name);

                return;
            }
        }

        ClearCurrentTarget();
    }

    private void SetCurrentTarget(GazeBeautyBreakable newTarget)
    {
        if (currentBreakable == newTarget)
        {
            currentBreakable.KeepLooking();
            return;
        }

        if (currentBreakable != null)
            currentBreakable.StopLooking();

        currentBreakable = newTarget;
        currentBreakable.StartLooking();
    }

    private void ClearCurrentTarget()
    {
        if (currentBreakable != null)
        {
            currentBreakable.StopLooking();
            currentBreakable = null;
        }
    }
}