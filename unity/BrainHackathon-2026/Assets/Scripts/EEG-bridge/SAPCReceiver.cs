using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// =============================================================================
// SAPCReceiver.cs
// =============================================================================
// Receives a continuous control value (0.0 to 1.0) over UDP and maps it
// to the scale of the GameObject this script is attached to.
//
// Attach this script to a sphere in Unity and send UDP values to this port.
// Sender and receiver ports must match.
//
// Part of the SAPC (Stroke Adaptive Playback Control) project
//   → Real-time biofeedback for stroke rehabilitation.
// =============================================================================

public class SAPCReceiver : MonoBehaviour
{
    [Header("Network")]
    public int port = 1000;

    [Header("Debug")]
    [Tooltip("Print received values to console")]
    public bool verboseDebug = true;

    [Tooltip("Log frequency in Hz for received values (set <= 0 to log every packet)")]
    public float debugLogRateHz = 10.0f;

    [Tooltip("Also warn when a packet cannot be parsed")]
    public bool logInvalidPackets = true;

    [Header("Packet parsing")]
    [Tooltip("For float-array packets, choose this index. -1 = auto-select likely control value.")]
    public int floatValueIndex = -1;

    [Tooltip("Auto mode: treat values near 0/1 as edge flags and prefer values inside this margin.")]
    public float autoEdgeEpsilon = 0.01f;

    /// <summary>
    /// Current normalized SAPC signal value (0.0 to 1.0).
    /// </summary>
    public float CurrentValue { get; private set; } = 0.5f;

    private Thread receiveThread;
    private UdpClient client;

    private float sapcValue = 0.5f;
    private readonly object lockObject = new object();
    private volatile bool isRunning = false;

    private float latestReceivedValue = 0.5f;
    private bool hasNewValue = false;
    private bool hasInvalidPacket = false;
    private string latestInvalidPacket = "";
    private float nextLogTime = 0f;

    void Start()
    {
        isRunning = true;
        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 500;
        }
        catch (System.Exception e)
        {
            Debug.LogError("SAPCReceiver: could not open UDP port " + port + " — " + e.Message);
            return;
        }

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("SAPCReceiver: listening on UDP :" + port);
    }

    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string packetForDebug;
                float parsed;
                bool ok = TryParseIncomingPacket(data, out parsed, out packetForDebug);

                if (ok && !float.IsNaN(parsed))
                {
                    if (parsed < 0f) parsed = 0f;
                    if (parsed > 1f) parsed = 1f;

                    lock (lockObject)
                    {
                        sapcValue = parsed;
                        latestReceivedValue = parsed;
                        hasNewValue = true;
                    }
                }
                else
                {
                    lock (lockObject)
                    {
                        latestInvalidPacket = packetForDebug;
                        hasInvalidPacket = true;
                    }
                }
            }
            catch (SocketException)
            {
                // Receive timeout — normal, keep looping
            }
            catch (System.ObjectDisposedException)
            {
                break;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SAPCReceiver: " + e.Message);
            }
        }
    }

    private bool TryParseIncomingPacket(byte[] data, out float parsed, out string packetForDebug)
    {
        parsed = 0f;
        packetForDebug = "<empty>";

        if (data == null || data.Length == 0)
            return false;

        string text = Encoding.UTF8.GetString(data).Trim();
        packetForDebug = text;

        if (TryParseFloatText(text, out parsed))
            return true;

        if (data.Length % 4 == 0)
        {
            float parsedArrayValue;
            string parsedArrayDebug;
            if (TryParseFloatArrayPacket(data, out parsedArrayValue, out parsedArrayDebug))
            {
                parsed = parsedArrayValue;
                packetForDebug = parsedArrayDebug;
                return true;
            }
        }

        if (data.Length == 4)
        {
            float littleEndianValue = System.BitConverter.ToSingle(data, 0);

            byte[] reversed = new byte[] { data[3], data[2], data[1], data[0] };
            float bigEndianValue = System.BitConverter.ToSingle(reversed, 0);

            bool littleFinite = !float.IsNaN(littleEndianValue) && !float.IsInfinity(littleEndianValue);
            bool bigFinite = !float.IsNaN(bigEndianValue) && !float.IsInfinity(bigEndianValue);

            if (littleFinite && littleEndianValue >= 0f && littleEndianValue <= 1f)
            {
                parsed = littleEndianValue;
                packetForDebug = "<binary-f32-le>";
                return true;
            }

            if (bigFinite && bigEndianValue >= 0f && bigEndianValue <= 1f)
            {
                parsed = bigEndianValue;
                packetForDebug = "<binary-f32-be>";
                return true;
            }

            if (littleFinite)
            {
                parsed = littleEndianValue;
                packetForDebug = "<binary-f32-le>";
                return true;
            }

            if (bigFinite)
            {
                parsed = bigEndianValue;
                packetForDebug = "<binary-f32-be>";
                return true;
            }
        }

        packetForDebug = "<hex:" + System.BitConverter.ToString(data) + ">";
        return false;
    }

    private bool TryParseFloatArrayPacket(byte[] data, out float parsed, out string packetForDebug)
    {
        parsed = 0f;
        packetForDebug = "<binary-f32-array-invalid>";

        int count = data.Length / 4;
        if (count <= 0)
            return false;

        float[] values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = System.BitConverter.ToSingle(data, i * 4);

        int index = SelectControlValueIndex(values);
        if (index < 0)
            return false;

        parsed = values[index];
        packetForDebug = "<binary-f32-array-le count=" + count + " idx=" + index + " val="
            + parsed.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + ">";
        return true;
    }

    private int SelectControlValueIndex(float[] values)
    {
        if (values == null || values.Length == 0)
            return -1;

        if (floatValueIndex >= 0 && floatValueIndex < values.Length)
        {
            if (IsFinite(values[floatValueIndex]))
                return floatValueIndex;
        }

        // Auto mode: prefer normalized values that are not near 0/1 (often status flags).
        float edge = Mathf.Clamp01(autoEdgeEpsilon);
        float lower = edge;
        float upper = 1f - edge;

        for (int i = values.Length - 1; i >= 0; i--)
        {
            float value = values[i];
            if (IsFinite(value) && value > lower && value < upper)
                return i;
        }

        // Then allow any normalized value.
        for (int i = values.Length - 1; i >= 0; i--)
        {
            float value = values[i];
            if (IsFinite(value) && value >= 0f && value <= 1f)
                return i;
        }

        for (int i = values.Length - 1; i >= 0; i--)
        {
            if (IsFinite(values[i]))
                return i;
        }

        return -1;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool TryParseFloatText(string text, out float parsed)
    {
        bool ok = float.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed
        );

        if (ok)
            return true;

        return float.TryParse(
            text.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed
        );
    }

    void Update()
    {
        float target;
        float receivedForLog = 0f;
        bool shouldLogValue = false;
        bool shouldLogInvalid = false;
        string invalidText = "";

        lock (lockObject) { target = sapcValue; }
        
        CurrentValue = target;

        lock (lockObject)
        {
            if (hasNewValue)
            {
                receivedForLog = latestReceivedValue;
                shouldLogValue = true;
                hasNewValue = false;
            }

            if (hasInvalidPacket)
            {
                invalidText = latestInvalidPacket;
                shouldLogInvalid = true;
                hasInvalidPacket = false;
            }
        }

        if (verboseDebug && shouldLogValue)
        {
            if (debugLogRateHz <= 0f || Time.unscaledTime >= nextLogTime)
            {
                Debug.Log($"EEG: {receivedForLog:F4}");
                nextLogTime = Time.unscaledTime + (debugLogRateHz > 0f ? 1f / debugLogRateHz : 0f);
            }
        }

        if (logInvalidPackets && shouldLogInvalid)
        {
            Debug.LogWarning($"SAPCReceiver: could not parse packet '{invalidText}'");
        }
    }

    void OnApplicationQuit() { Cleanup(); }
    void OnDisable() { Cleanup(); }

    private void Cleanup()
    {
        isRunning = false;

        if (client != null)
        {
            try { client.Close(); } catch { }
            client = null;
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            receiveThread = null;
        }
    }
}
