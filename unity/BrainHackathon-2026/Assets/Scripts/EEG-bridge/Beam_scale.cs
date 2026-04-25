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
    private Transform topObject;

    void Start()
    {
        isRunning = true;

        if (targetCapsule == null)
            targetCapsule = transform;

        _baseWorldPos = GetBaseWorldPos();
        topObject = GameObject.FindWithTag("top")?.transform;

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
            float rotation = input * rotationSpeed * Time.deltaTime;
            if (CanRotate(rotation))
            {
                targetCapsule.RotateAround(_baseWorldPos, Vector3.forward, rotation);
                RepositionToBase();
            }
        }
        else
        {
            _baseWorldPos += Vector3.right * input * moveSpeed * Time.deltaTime;
        }
    }

    // ── Y-only scaling anchored to the center point (grows both up and down) ──
    private void HandleScaling()
    {
        float rawValue;
        lock (lockObject) { rawValue = receivedValue; }

        float targetY   = Mathf.Lerp(minScale, maxScale, rawValue);
        Vector3 current = targetCapsule.localScale;
        float newY      = Mathf.Lerp(current.y, targetY, Time.deltaTime * smoothing);

        targetCapsule.localScale = new Vector3(current.x, newY, current.z);
        // Position the center of the beam at the base, so it grows equally up and down
        targetCapsule.position   = _baseWorldPos + targetCapsule.up * (newY / 2f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Vector3 GetBaseWorldPos() =>
        targetCapsule.position - targetCapsule.up * targetCapsule.localScale.y;

    private void RepositionToBase() =>
        targetCapsule.position = _baseWorldPos + targetCapsule.up * targetCapsule.localScale.y;

    // Check if rotation would keep the top object at or above y=0.01
    private bool CanRotate(float rotationDelta)
    {
        if (topObject == null) return true;

        // Simulate the rotation
        Quaternion originalRotation = targetCapsule.rotation;
        targetCapsule.RotateAround(_baseWorldPos, Vector3.forward, rotationDelta);
        
        // Check the top object's Y position after rotation
        float topY = topObject.position.y;
        
        // Restore original rotation
        targetCapsule.rotation = originalRotation;
        
        // Allow rotation only if top object stays at or above y=0.01
        return topY >= 0.01f;
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
