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
    public float minScale = 0.1f;

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

    [Header("Eye Interaction")]
    [Tooltip("Use Tobii gaze position to control the beam pitch around the X-axis.")]
    public bool gazePitchEnabled = true;

    [Tooltip("Camera used to normalize screen gaze position. Defaults to Camera.main when empty.")]
    public Camera gazeCamera;

    [Tooltip("Pitch angle when looking at the bottom of the screen.")]
    public float minGazePitch = -35f;

    [Tooltip("Pitch angle when looking at the top of the screen.")]
    public float maxGazePitch = 35f;

    [Tooltip("How quickly the beam pitch follows gaze.")]
    public float gazePitchSmoothing = 10f;

    [Tooltip("Invert vertical gaze mapping if the pitch feels backwards.")]
    public bool invertGazeY = false;

    [Header("Dev UI Buttons")]
    [Tooltip("Button that activates Rotate mode")]
    public Button rotateModeButton;

    [Tooltip("Button that activates Move mode")]
    public Button moveModeButton;

    [Header("Button Colors")]
    public Color activeColor   = new Color(0.2f, 0.6f, 1f);    // blue  — active mode
    public Color inactiveColor = new Color(0.25f, 0.25f, 0.25f); // dark grey — inactive

    [Header("Debug")]
    [Tooltip("Print received values to console")]
    public bool verboseDebug = true;

    // ── internals ────────────────────────────────────────────────────────────
    private enum DevMode { Rotate, Move }
    private DevMode _devMode = DevMode.Rotate;

    private Thread    receiveThread;
    private UdpClient client;
    private float     receivedValue = 0f;
    private readonly object lockObject = new object();
    private volatile bool   isRunning  = false;

    private Vector3 _baseWorldPos;
    private Quaternion _baseRotation;
    private float _zRotationDegrees;
    private float _currentGazePitch;

    void Start()
    {
        isRunning = true;

        if (targetCapsule == null)
            targetCapsule = transform;

        _baseWorldPos = GetBaseWorldPos();
        _baseRotation = targetCapsule.rotation;

        if (gazePitchEnabled)
        {
            TobiiAPI.SubscribeGazePointData();
        }

        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
        }

        // Wire up buttons
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

                float parsed;
                bool ok = float.TryParse(text, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out parsed);

                if (ok && !float.IsNaN(parsed) && parsed >= 0f && parsed <= 1f)
                {
                    lock (lockObject) { receivedValue = parsed; }
                    if (verboseDebug) Debug.Log($"EEG: {parsed:F4}");
                }
            }
            catch (SocketException) { }
            catch (System.ObjectDisposedException) { break; }
            catch (System.Exception e) { Debug.LogWarning($"Beam_scale: {e.Message}"); }
        }
    }

    void Update()
    {
        if (devControlsEnabled) HandleArrowKeys();
        if (gazePitchEnabled) HandleGazePitch();
        ApplyTransformFromState();
        HandleScaling();
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
        ColorBlock cb = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.2f);
        cb.pressedColor     = Color.Lerp(color, Color.black, 0.2f);
        btn.colors          = cb;
    }

    // ── Arrow keys ────────────────────────────────────────────────────────────
    private void HandleArrowKeys()
    {
        float input = 0f;
        // 1 = Rotate mode, 2 = Move mode
        if (Keyboard.current.digit1Key.wasPressedThisFrame) SetMode(DevMode.Rotate);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SetMode(DevMode.Move);

        if (Keyboard.current.leftArrowKey.isPressed)  input =  1f;
        if (Keyboard.current.rightArrowKey.isPressed) input = -1f;
        if (input == 0f) return;

        if (_devMode == DevMode.Rotate)
        {
            _zRotationDegrees += input * rotationSpeed * Time.deltaTime;
        }
        else
        {
            _baseWorldPos += Vector3.right * input * moveSpeed * Time.deltaTime;
        }
    }

    private void HandleGazePitch()
    {
        if (gazeCamera == null)
        {
            gazeCamera = Camera.main;
            if (gazeCamera == null)
                return;
        }

        GazePoint gazePoint = TobiiAPI.GetGazePoint();
        if (!gazePoint.IsValid)
            return;

        Rect pixelRect = gazeCamera.pixelRect;
        float normalizedY = Mathf.Clamp01(
            Mathf.InverseLerp(pixelRect.yMin, pixelRect.yMax, gazePoint.Screen.y));
        if (invertGazeY)
            normalizedY = 1f - normalizedY;

        float targetPitch = Mathf.Lerp(minGazePitch, maxGazePitch, normalizedY);
        _currentGazePitch = Mathf.LerpAngle(_currentGazePitch, targetPitch, Time.deltaTime * gazePitchSmoothing);
    }

    private void ApplyTransformFromState()
    {
        Quaternion zRotation = Quaternion.AngleAxis(_zRotationDegrees, Vector3.forward);
        Quaternion localPitch = Quaternion.AngleAxis(_currentGazePitch, Vector3.right);
        targetCapsule.rotation = zRotation * _baseRotation * localPitch;
        RepositionToBase();
    }

    // ── Y-only scaling anchored to the base ──────────────────────────────────
    private void HandleScaling()
    {
        float rawValue;
        lock (lockObject) { rawValue = receivedValue; }

        float targetY   = Mathf.Lerp(minScale, maxScale, rawValue);
        Vector3 current = targetCapsule.localScale;
        float newY      = Mathf.Lerp(current.y, targetY, Time.deltaTime * smoothing);

        targetCapsule.localScale = new Vector3(current.x, newY, current.z);
        targetCapsule.position   = _baseWorldPos + targetCapsule.up * newY;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Vector3 GetBaseWorldPos() =>
        targetCapsule.position - targetCapsule.up * targetCapsule.localScale.y;

    private void RepositionToBase() =>
        targetCapsule.position = _baseWorldPos + targetCapsule.up * targetCapsule.localScale.y;

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
