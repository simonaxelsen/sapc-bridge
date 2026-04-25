using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Tobii.Gaming;

public class Beam_scale : MonoBehaviour
{
    [Header("Network")]
    public int port = 1000;

    [Header("Capsule Control")]
    [Tooltip("The 3D capsule to scale (leave empty to use this GameObject)")]
    public Transform targetCapsule;

    [Tooltip("Minimum Y scale when value is 0.0")]
    public float minScale = 1f;

    [Tooltip("Maximum Y scale when value is 1.0")]
    public float maxScale = 2.0f;

    [Tooltip("Visual smoothing (higher = faster)")]
    public float smoothing = 8f;

    [Header("Dev Controls")]
    [Tooltip("Enable dev controls in Play mode")]
    public bool devControlsEnabled = true;

    [Tooltip("Degrees per second when rotating")]
    public float rotationSpeed = 90f;

    [Tooltip("Units per second when moving")]
    public float moveSpeed = 3f;

    // ── Gaze Move ─────────────────────────────────────────────────────────────
    [Header("Gaze Horizontal Movement")]
    [Tooltip("Move the beam left/right by looking left/right.")]
    public bool gazeMoveEnabled = true;

    [Tooltip("Maximum units per second the beam travels when gaze is at the screen edge.")]
    public float gazeMoveSpeed = 4f;

    [Tooltip("Normalised half-width of the centre dead zone (0 = no dead zone, 0.2 = 20% each side).")]
    [Range(0f, 0.49f)]
    public float gazeMoveDeadZone = 0.15f;

    [Tooltip("Clamp beam X position so it can't leave this world-space range.")]
    public float gazeMoveMinX = -5f;
    public float gazeMoveMaxX =  5f;

    [Tooltip("Invert horizontal gaze mapping if the direction feels backwards.")]
    public bool invertGazeX = false;

    // ── Gaze Pitch ────────────────────────────────────────────────────────────
    [Header("Eye Pitch Interaction")]
    [Tooltip("Use Tobii gaze position to control the beam pitch around the X-axis.")]
    public bool gazePitchEnabled = true;

    [Tooltip("Camera used to normalise screen gaze position. Defaults to Camera.main when empty.")]
    public Camera gazeCamera;

    [Tooltip("Pitch angle when looking at the bottom of the screen.")]
    public float minGazePitch = -35f;

    [Tooltip("Pitch angle when looking at the top of the screen.")]
    public float maxGazePitch = 35f;

    [Tooltip("How quickly the beam pitch follows gaze.")]
    public float gazePitchSmoothing = 10f;

    [Tooltip("Invert vertical gaze mapping if the pitch feels backwards.")]
    public bool invertGazeY = false;

    // ── Dev UI Buttons ────────────────────────────────────────────────────────
    [Header("Dev UI Buttons")]
    [Tooltip("Button that activates Rotate mode")]
    public Button rotateModeButton;

    [Tooltip("Button that activates Move mode")]
    public Button moveModeButton;

    [Header("Button Colors")]
    public Color activeColor   = new Color(0.2f, 0.6f, 1f);
    public Color inactiveColor = new Color(0.25f, 0.25f, 0.25f);

    [Header("Debug")]
    [Tooltip("Print received values to console")]
    public bool verboseDebug = true;

    // ── internals ─────────────────────────────────────────────────────────────
    private enum DevMode { Rotate, Move }
    private DevMode _devMode = DevMode.Rotate;

    private Thread    receiveThread;
    private UdpClient client;
    private float     receivedValue = 0f;
    private readonly object lockObject = new object();
    private volatile bool   isRunning  = false;

    private Vector3   _baseWorldPos;
    private Transform topObject;

    private float _currentGazePitch = 0f;

    // Tracks cumulative rotation so we can clamp to ±90° (180° total arc)
    private float _currentAngle = 0f;

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        isRunning = true;

        if (targetCapsule == null)
            targetCapsule = transform;

        _baseWorldPos = GetBaseWorldPos();
        topObject     = GameObject.FindWithTag("top")?.transform;

        if (gazeMoveEnabled || gazePitchEnabled)
            TobiiAPI.SubscribeGazePointData();

        if (rotateModeButton != null) rotateModeButton.onClick.AddListener(() => SetMode(DevMode.Rotate));
        if (moveModeButton   != null) moveModeButton.onClick.AddListener(  () => SetMode(DevMode.Move));
        RefreshButtonColors();

        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 500;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Beam_scale: Could not open UDP port {port} — {e.Message}");
            return;
        }

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log($"Beam_scale: Listening on UDP:{port}");
    }

    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data);

                if (float.TryParse(text,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float parsed)
                    && !float.IsNaN(parsed) && parsed >= 0f && parsed <= 1f)
                {
                    lock (lockObject) { receivedValue = parsed; }
                    if (verboseDebug) Debug.Log($"EEG: {parsed:F4}");
                }
            }
            catch (SocketException)                { }
            catch (System.ObjectDisposedException) { break; }
            catch (System.Exception e)             { Debug.LogWarning($"Beam_scale: {e.Message}"); }
        }
    }

    void Update()
    {
        EnsureGazeCamera();

        if (devControlsEnabled) HandleArrowKeys();

        if (gazeMoveEnabled)  HandleGazeMove();
        if (gazePitchEnabled) HandleGazePitch();

        ApplyTransformFromState();
        HandleScaling();
    }

    // ── Gaze horizontal movement ──────────────────────────────────────────────

    private void HandleGazeMove()
    {
        var gazePoint = TobiiAPI.GetGazePoint();
        if (!gazePoint.IsValid) return;

        Rect pixelRect = gazeCamera != null
            ? gazeCamera.pixelRect
            : new Rect(0f, 0f, Screen.width, Screen.height);

        // Normalise X to -1 ... +1 (left edge = -1, right edge = +1)
        float normX = Mathf.InverseLerp(pixelRect.xMin, pixelRect.xMax, gazePoint.Screen.x) * 2f - 1f;
        if (invertGazeX) normX = -normX;

        float velocity = ApplyDeadZone(normX, gazeMoveDeadZone);

        if (_devMode == DevMode.Rotate)
        {
            float delta = velocity * rotationSpeed * Time.deltaTime;
            _currentAngle = Mathf.Clamp(_currentAngle + delta, -180f, 180f);
        }
        else
        {
            _baseWorldPos.x += velocity * gazeMoveSpeed * Time.deltaTime;
            _baseWorldPos.x  = Mathf.Clamp(_baseWorldPos.x, gazeMoveMinX, gazeMoveMaxX);
        }
    }

    /// <summary>
    /// Remaps a –1…+1 value so the centre band [–deadZone, +deadZone] returns 0,
    /// and the outer range scales smoothly to –1…+1 at the edges.
    /// </summary>
    private static float ApplyDeadZone(float value, float deadZone)
    {
        if (deadZone <= 0f) return value;

        float abs = Mathf.Abs(value);
        if (abs <= deadZone) return 0f;

        float rescaled = (abs - deadZone) / (1f - deadZone);
        return Mathf.Sign(value) * rescaled;
    }

    // ── Gaze pitch ────────────────────────────────────────────────────────────

    private void HandleGazePitch()
    {
        var gazePoint = TobiiAPI.GetGazePoint();
        if (!gazePoint.IsValid) return;

        float normalizedY = (gazePoint.Screen.y / gazeCamera.pixelHeight) * 2f - 1f;
        if (invertGazeY) normalizedY = -normalizedY;

        float targetPitch = Mathf.Lerp(minGazePitch, maxGazePitch, (normalizedY + 1f) / 2f);
        _currentGazePitch = Mathf.Lerp(_currentGazePitch, targetPitch, Time.deltaTime * gazePitchSmoothing);
    }

    private void ApplyTransformFromState()
    {
        if (targetCapsule == null) return;

        // Build final rotation: manual rotation on Z-axis, gaze pitch on X-axis
        Quaternion manualRotation = Quaternion.AngleAxis(_currentAngle, Vector3.forward);
        
        if (gazePitchEnabled)
        {
            Quaternion gazeRotation = Quaternion.AngleAxis(_currentGazePitch, Vector3.right);
            targetCapsule.rotation = manualRotation * gazeRotation;
        }
        else
        {
            targetCapsule.rotation = manualRotation;
        }
        
        // Reposition to maintain base position
        RepositionToBase();
    }

    // ── Mode switching ────────────────────────────────────────────────────────

    private void SetMode(DevMode mode)
    {
        _devMode = mode;
        RefreshButtonColors();
        Debug.Log($"Beam_scale dev mode: {_devMode}");
    }

    private void RefreshButtonColors()
    {
        SetButtonColor(rotateModeButton, _devMode == DevMode.Rotate ? activeColor : inactiveColor);
        SetButtonColor(moveModeButton,   _devMode == DevMode.Move   ? activeColor : inactiveColor);
    }

    private void SetButtonColor(Button btn, Color color)
    {
        if (btn == null) return;
        ColorBlock cb       = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
        cb.pressedColor     = Color.Lerp(color, Color.black, 0.2f);
        btn.colors          = cb;
    }

    // ── Arrow keys ────────────────────────────────────────────────────────────

    private void HandleArrowKeys()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) SetMode(DevMode.Rotate);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SetMode(DevMode.Move);

        float input = 0f;
        if (Keyboard.current.leftArrowKey.isPressed)  input =  1f;
        if (Keyboard.current.rightArrowKey.isPressed) input = -1f;
        if (input == 0f) return;

        if (_devMode == DevMode.Rotate)
        {
            float delta = input * rotationSpeed * Time.deltaTime;

            // Clamp to ±180° limit
            float newAngle = Mathf.Clamp(_currentAngle + delta, -180f, 180f);
            float clampedDelta = newAngle - _currentAngle;

            if (Mathf.Abs(clampedDelta) > 0.0001f)
            {
                _currentAngle = newAngle;
            }
        }
        else
        {
            _baseWorldPos += Vector3.right * input * moveSpeed * Time.deltaTime;
        }
    }

    // ── Scaling ───────────────────────────────────────────────────────────────

    private void HandleScaling()
    {
        float rawValue;
        lock (lockObject) { rawValue = receivedValue; }

        float targetY   = Mathf.Lerp(minScale, maxScale, rawValue);
        Vector3 current = targetCapsule.localScale;
        float newY      = Mathf.Lerp(current.y, targetY, Time.deltaTime * smoothing);

        targetCapsule.localScale = new Vector3(current.x, newY, current.z);
        targetCapsule.position   = _baseWorldPos + targetCapsule.up * (newY / 2f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureGazeCamera()
    {
        if (gazeCamera == null)
            gazeCamera = Camera.main;
    }

    private Vector3 GetBaseWorldPos() =>
        targetCapsule.position - targetCapsule.up * targetCapsule.localScale.y;

    private void RepositionToBase() =>
        targetCapsule.position = _baseWorldPos + targetCapsule.up * targetCapsule.localScale.y;

    private bool CanRotate(float rotationDelta)
    {
        if (topObject == null) return true;

        // Test rotation by checking if topObject would go below ground
        float testAngle = _currentAngle + rotationDelta;
        Quaternion testRotation = Quaternion.AngleAxis(testAngle, Vector3.forward);
        Vector3 testPos = _baseWorldPos + testRotation * (targetCapsule.position - _baseWorldPos);
        
        return topObject.position.y >= 0.01f;
    }

    void OnDrawGizmosSelected()
    {
        if (targetCapsule == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(GetBaseWorldPos(), 0.1f);
    }

    void OnApplicationQuit() => Cleanup();
    void OnDisable()         => Cleanup();

    private void Cleanup()
    {
        isRunning = false;
        if (client != null) { client.Close(); client = null; }
        if (receiveThread != null && receiveThread.IsAlive) { receiveThread.Join(1000); receiveThread = null; }
    }
}
