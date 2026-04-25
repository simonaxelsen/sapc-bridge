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

    [Header("Packet Parsing")]
    [Tooltip("For float-array packets, choose this index. -1 = auto-select likely control value.")]
    public int floatValueIndex = -1;

    [Tooltip("Auto mode: treat values near 0/1 as edge flags and prefer values inside this margin.")]
    public float autoEdgeEpsilon = 0.01f;

    [Header("Capsule Control")]
    [Tooltip("The 3D capsule to scale (leave empty to use this GameObject)")]
    public Transform targetCapsule;

    [Tooltip("Minimum Y scale when value is 0.0")]
    public float minScale = 1f;

    [Tooltip("Maximum Y scale when value is 1.0")]
    public float maxScale = 2.0f;

    [Tooltip("Visual smoothing (higher = faster)")]
    public float smoothing = 8f;

    [Header("EEG Scaling")]
    [Tooltip("Amplify EEG values to make beam scale changes more dramatic")]
    public float eegAmplification = 3f;
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

    [Tooltip("Log frequency in Hz for received values (set <= 0 to log every packet)")]
    public float debugLogRateHz = 10f;

    [Tooltip("Also warn when a packet cannot be parsed")]
    public bool logInvalidPackets = true;

    // ── Public read-only EEG value ────────────────────────────────────────────
    /// <summary>Current normalised EEG value (0.0 – 1.0).</summary>
    public float CurrentEEGValue { get; private set; } = 0f;

    // ── internals ─────────────────────────────────────────────────────────────
    private enum DevMode { Rotate, Move }
    private DevMode _devMode = DevMode.Rotate;

    private Thread    receiveThread;
    private UdpClient client;
    private readonly object lockObject = new object();
    private volatile bool   isRunning  = false;

    // Thread → Update hand-off
    private float  _latestReceivedValue  = 0f;
    private bool   _hasNewValue          = false;
    private bool   _hasInvalidPacket     = false;
    private string _latestInvalidPacket  = "";
    private float  _nextLogTime          = 0f;

    // Scaling state
    private float _sapcValue = 0f;

    private Vector3   _baseWorldPos;
    private Transform topObject;

    private float _currentGazePitch = 0f;
    private float _currentAngle     = 0f;

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

    // ── UDP receive thread ────────────────────────────────────────────────────

    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);

                float parsed;
                string packetForDebug;
                bool ok = TryParseIncomingPacket(data, out parsed, out packetForDebug);

                if (ok && !float.IsNaN(parsed))
                {
                    parsed = Mathf.Clamp01(parsed);
                    lock (lockObject)
                    {
                        _sapcValue             = parsed;
                        _latestReceivedValue   = parsed;
                        _hasNewValue           = true;
                    }
                }
                else
                {
                    lock (lockObject)
                    {
                        _latestInvalidPacket = packetForDebug;
                        _hasInvalidPacket    = true;
                    }
                }
            }
            catch (SocketException)                { /* receive timeout — normal */ }
            catch (System.ObjectDisposedException) { break; }
            catch (System.Exception e)             { Debug.LogWarning($"Beam_scale: {e.Message}"); }
        }
    }

    // ── Packet parsing (ported from SAPCReceiver) ─────────────────────────────

    private bool TryParseIncomingPacket(byte[] data, out float parsed, out string packetForDebug)
    {
        parsed         = 0f;
        packetForDebug = "<empty>";

        if (data == null || data.Length == 0)
            return false;

        string text = Encoding.UTF8.GetString(data).Trim();
        packetForDebug = text;

        // 1. Plain text float
        if (TryParseFloatText(text, out parsed))
            return true;

        // 2. Binary float array (length multiple of 4)
        if (data.Length % 4 == 0)
        {
            float  arrayVal;
            string arrayDebug;
            if (TryParseFloatArrayPacket(data, out arrayVal, out arrayDebug))
            {
                parsed         = arrayVal;
                packetForDebug = arrayDebug;
                return true;
            }
        }

        // 3. Raw 4-byte float (little- or big-endian)
        if (data.Length == 4)
        {
            float le = System.BitConverter.ToSingle(data, 0);
            byte[] rev = new byte[] { data[3], data[2], data[1], data[0] };
            float be = System.BitConverter.ToSingle(rev, 0);

            bool leFinite = IsFinite(le);
            bool beFinite = IsFinite(be);

            if (leFinite && le >= 0f && le <= 1f) { parsed = le; packetForDebug = "<binary-f32-le>"; return true; }
            if (beFinite && be >= 0f && be <= 1f) { parsed = be; packetForDebug = "<binary-f32-be>"; return true; }
            if (leFinite) { parsed = le; packetForDebug = "<binary-f32-le>"; return true; }
            if (beFinite) { parsed = be; packetForDebug = "<binary-f32-be>"; return true; }
        }

        packetForDebug = "<hex:" + System.BitConverter.ToString(data) + ">";
        return false;
    }

    private bool TryParseFloatArrayPacket(byte[] data, out float parsed, out string packetForDebug)
    {
        parsed         = 0f;
        packetForDebug = "<binary-f32-array-invalid>";

        int count = data.Length / 4;
        if (count <= 0) return false;

        float[] values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = System.BitConverter.ToSingle(data, i * 4);

        int index = SelectControlValueIndex(values);
        if (index < 0) return false;

        parsed         = values[index];
        packetForDebug = $"<binary-f32-array-le count={count} idx={index} val={parsed.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}>";
        return true;
    }

    private int SelectControlValueIndex(float[] values)
    {
        if (values == null || values.Length == 0) return -1;

        // Honour explicit index
        if (floatValueIndex >= 0 && floatValueIndex < values.Length && IsFinite(values[floatValueIndex]))
            return floatValueIndex;

        float edge  = Mathf.Clamp01(autoEdgeEpsilon);
        float lower = edge;
        float upper = 1f - edge;

        // Prefer interior normalised values (avoid edge flags)
        for (int i = values.Length - 1; i >= 0; i--)
            if (IsFinite(values[i]) && values[i] > lower && values[i] < upper) return i;

        // Any normalised value
        for (int i = values.Length - 1; i >= 0; i--)
            if (IsFinite(values[i]) && values[i] >= 0f && values[i] <= 1f) return i;

        // Any finite value
        for (int i = values.Length - 1; i >= 0; i--)
            if (IsFinite(values[i])) return i;

        return -1;
    }

    private static bool TryParseFloatText(string text, out float parsed)
    {
        if (float.TryParse(text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed))
            return true;

        return float.TryParse(text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed);
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    // ── Unity Update ──────────────────────────────────────────────────────────

    void Update()
    {
        EnsureGazeCamera();
        FlushThreadData();

        if (devControlsEnabled) HandleArrowKeys();
        if (gazeMoveEnabled)    HandleGazeMove();
        if (gazePitchEnabled)   HandleGazePitch();

        // FIX: scale first, then apply rotation + reposition
        HandleScaling();
        ApplyTransformFromState();
    }

    /// <summary>
    /// Safely pulls data written by the receive thread into the main thread,
    /// then handles debug logging.
    /// </summary>
    private void FlushThreadData()
    {
        float  receivedForLog  = 0f;
        bool   shouldLogValue  = false;
        bool   shouldLogInvalid = false;
        string invalidText     = "";

        lock (lockObject)
        {
            if (_hasNewValue)
            {
                receivedForLog = _latestReceivedValue;
                shouldLogValue = true;
                _hasNewValue   = false;
            }
            if (_hasInvalidPacket)
            {
                invalidText       = _latestInvalidPacket;
                shouldLogInvalid  = true;
                _hasInvalidPacket = false;
            }
        }

        if (verboseDebug && shouldLogValue)
        {
            if (debugLogRateHz <= 0f || Time.unscaledTime >= _nextLogTime)
            {
                Debug.Log($"EEG: {receivedForLog:F4}");
                _nextLogTime = Time.unscaledTime + (debugLogRateHz > 0f ? 1f / debugLogRateHz : 0f);
            }
        }

        if (logInvalidPackets && shouldLogInvalid)
            Debug.LogWarning($"Beam_scale: could not parse packet '{invalidText}'");
    }

    // ── Scaling ───────────────────────────────────────────────────────────────

    private void HandleScaling()
    {
        float rawValue;
        lock (lockObject) { rawValue = _sapcValue; }

        CurrentEEGValue = rawValue;

        // Use EEG value (0-1) directly as the Y scale multiplier
        Vector3 current = targetCapsule.localScale;
        Vector3 targetScale = new Vector3(current.x, rawValue, current.z);
        Vector3 newScale = Vector3.Lerp(current, targetScale, Time.deltaTime * smoothing * 2f);

        targetCapsule.localScale = newScale;
    }

    // ── Gaze horizontal movement ──────────────────────────────────────────────

    private void HandleGazeMove()
    {
        var gazePoint = TobiiAPI.GetGazePoint();
        if (!gazePoint.IsValid) return;

        Rect pixelRect = gazeCamera != null
            ? gazeCamera.pixelRect
            : new Rect(0f, 0f, Screen.width, Screen.height);

        float normX = Mathf.InverseLerp(pixelRect.xMin, pixelRect.xMax, gazePoint.Screen.x) * 2f - 1f;
        if (invertGazeX) normX = -normX;

        float velocity = ApplyDeadZone(normX, gazeMoveDeadZone);

        if (_devMode == DevMode.Rotate)
        {
            float delta = velocity * rotationSpeed * Time.deltaTime;
            float newAngle = _currentAngle + delta;
            
            // Check if rotation is allowed (top object stays at or above y=2)
            if (CanRotateZ(newAngle))
            {
                _currentAngle = Mathf.Clamp(newAngle, -180f, 180f);
            }
        }
        else
        {
            _baseWorldPos.x += velocity * gazeMoveSpeed * Time.deltaTime;
            _baseWorldPos.x  = Mathf.Clamp(_baseWorldPos.x, gazeMoveMinX, gazeMoveMaxX);
        }
    }

    private static float ApplyDeadZone(float value, float deadZone)
    {
        if (deadZone <= 0f) return value;
        float abs = Mathf.Abs(value);
        if (abs <= deadZone) return 0f;
        return Mathf.Sign(value) * (abs - deadZone) / (1f - deadZone);
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

        Quaternion manualRotation = Quaternion.AngleAxis(_currentAngle, Vector3.forward);

        if (gazePitchEnabled)
        {
            Quaternion gazeRotation = Quaternion.AngleAxis(_currentGazePitch, Vector3.right);
            targetCapsule.rotation  = manualRotation * gazeRotation;
        }
        else
        {
            targetCapsule.rotation = manualRotation;
        }

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
            float deltaAngle = input * rotationSpeed * Time.deltaTime;
            float newAngle = _currentAngle + deltaAngle;
            
            // Check if rotation is allowed (top object stays at or above y=2)
            if (CanRotateZ(newAngle))
            {
                _currentAngle = Mathf.Clamp(newAngle, -180f, 180f);
            }
        }
        else
        {
            _baseWorldPos += Vector3.right * input * moveSpeed * Time.deltaTime;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureGazeCamera()
    {
        if (gazeCamera == null)
            gazeCamera = Camera.main;
    }

    private Vector3 GetBaseWorldPos() =>
        targetCapsule.position - targetCapsule.up * targetCapsule.localScale.y;

    private void RepositionToBase()
    {
        targetCapsule.position = _baseWorldPos + targetCapsule.up * targetCapsule.localScale.y;
    }

    /// <summary>
    /// Check if rotating to the given angle would keep the top object at or above y=2.
    /// Pitch rotation is allowed freely (not constrained by this check).
    /// </summary>
    private bool CanRotateZ(float targetAngle)
    {
        if (topObject == null) return true;

        // Simulate the Z-axis rotation
        Quaternion testZRotation = Quaternion.AngleAxis(targetAngle, Vector3.forward);
        
        // Apply both Z (manual) and X (gaze pitch) rotations
        Quaternion testRotation = gazePitchEnabled
            ? testZRotation * Quaternion.AngleAxis(_currentGazePitch, Vector3.right)
            : testZRotation;

        // Temporarily apply the test rotation
        Quaternion originalRotation = targetCapsule.rotation;
        targetCapsule.rotation = testRotation;
        targetCapsule.position = _baseWorldPos + targetCapsule.up * (targetCapsule.localScale.y / 2f);
        
        // Check the top object's Y position
        float topY = topObject.position.y;
        
        // Restore original transform
        targetCapsule.rotation = originalRotation;
        RepositionToBase();
        
        // Allow rotation only if top object stays at or above y=2
        return topY >= 2f;
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
        if (client != null)        { try { client.Close(); } catch { } client = null; }
        if (receiveThread != null && receiveThread.IsAlive) { receiveThread.Join(1000); receiveThread = null; }
    }
}
