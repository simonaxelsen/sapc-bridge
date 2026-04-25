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
// Also exposes raw EEG channel values for C3, Cz, and C4 from the
// Unicorn Hybrid Black headset (g.tec), which streams 17 floats per packet:
//   [0]=Fz  [1]=C3  [2]=Cz  [3]=C4  [4]=Pz  [5]=PO7  [6]=Oz  [7]=PO8
//   [8–10]=Accel  [11–13]=Gyro  [14]=Battery  [15]=Counter  [16]=Validation
//
// Part of the SAPC (Stroke Adaptive Playback Control) project
//   → Real-time biofeedback for stroke rehabilitation.
// =============================================================================

public class Three_2 : MonoBehaviour
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

    [Header("Unicorn EEG Channel Indices")]
    [Tooltip("Float-array index for C3 in the Unicorn packet (default 1)")]
    public int channelIndexC3 = 1;

    [Tooltip("Float-array index for Cz in the Unicorn packet (default 2)")]
    public int channelIndexCz = 2;

    [Tooltip("Float-array index for C4 in the Unicorn packet (default 3)")]
    public int channelIndexC4 = 3;

    // -------------------------------------------------------------------------
    // Public read-only properties
    // -------------------------------------------------------------------------

    /// <summary>Current normalized SAPC signal value (0.0 to 1.0).</summary>
    public float CurrentValue { get; private set; } = 0.5f;

    /// <summary>Raw EEG value for channel C3 in µV. NaN if not yet received.</summary>
    public float C3 { get; private set; } = float.NaN;

    /// <summary>Raw EEG value for channel Cz in µV. NaN if not yet received.</summary>
    public float Cz { get; private set; } = float.NaN;

    /// <summary>Raw EEG value for channel C4 in µV. NaN if not yet received.</summary>
    public float C4 { get; private set; } = float.NaN;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private Thread receiveThread;
    private UdpClient client;

    private float sapcValue = 0.5f;
    private readonly object lockObject = new object();
    private volatile bool isRunning = false;

    private float latestReceivedValue = 0.5f;
    private float latestC3 = float.NaN;
    private float latestCz = float.NaN;
    private float latestC4 = float.NaN;

    private bool hasNewValue = false;
    private bool hasInvalidPacket = false;
    private string latestInvalidPacket = "";
    private float nextLogTime = 0f;

    // -------------------------------------------------------------------------

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
                float c3 = float.NaN, cz = float.NaN, c4 = float.NaN;

                bool ok = TryParseIncomingPacket(data, out parsed, out packetForDebug,
                                                 out c3, out cz, out c4);

                if (ok && !float.IsNaN(parsed))
                {
                    if (parsed < 0f) parsed = 0f;
                    if (parsed > 1f) parsed = 1f;

                    lock (lockObject)
                    {
                        sapcValue             = parsed;
                        latestReceivedValue   = parsed;
                        latestC3              = c3;
                        latestCz              = cz;
                        latestC4              = c4;
                        hasNewValue           = true;
                    }
                }
                else
                {
                    lock (lockObject)
                    {
                        latestInvalidPacket = packetForDebug;
                        hasInvalidPacket    = true;
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

    // -------------------------------------------------------------------------
    // Packet parsing
    // -------------------------------------------------------------------------

    private bool TryParseIncomingPacket(byte[] data, out float parsed, out string packetForDebug,
                                        out float c3, out float cz, out float c4)
    {
        parsed         = 0f;
        packetForDebug = "<empty>";
        c3 = cz = c4  = float.NaN;

        if (data == null || data.Length == 0)
            return false;

        string text = Encoding.UTF8.GetString(data).Trim();
        packetForDebug = text;

        // Plain text float — no EEG channel data available in this format
        if (TryParseFloatText(text, out parsed))
            return true;

        // Float array (Unicorn and similar)
        if (data.Length % 4 == 0)
        {
            string arrayDebug;
            if (TryParseFloatArrayPacket(data, out parsed, out arrayDebug, out c3, out cz, out c4))
            {
                packetForDebug = arrayDebug;
                return true;
            }
        }

        // Single raw binary float
        if (data.Length == 4)
        {
            float littleEndianValue = System.BitConverter.ToSingle(data, 0);
            byte[] reversed         = new byte[] { data[3], data[2], data[1], data[0] };
            float bigEndianValue    = System.BitConverter.ToSingle(reversed, 0);

            bool littleFinite = IsFinite(littleEndianValue);
            bool bigFinite    = IsFinite(bigEndianValue);

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
            if (littleFinite) { parsed = littleEndianValue; packetForDebug = "<binary-f32-le>"; return true; }
            if (bigFinite)    { parsed = bigEndianValue;    packetForDebug = "<binary-f32-be>"; return true; }
        }

        packetForDebug = "<hex:" + System.BitConverter.ToString(data) + ">";
        return false;
    }

    private bool TryParseFloatArrayPacket(byte[] data, out float parsed, out string packetForDebug,
                                          out float c3, out float cz, out float c4)
    {
        parsed         = 0f;
        packetForDebug = "<binary-f32-array-invalid>";
        c3 = cz = c4  = float.NaN;

        int count = data.Length / 4;
        if (count <= 0)
            return false;

        float[] values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = System.BitConverter.ToSingle(data, i * 4);

        // Extract EEG channels when the packet is large enough (Unicorn = 17 floats)
        if (count > channelIndexC3 && IsFinite(values[channelIndexC3])) c3 = values[channelIndexC3];
        if (count > channelIndexCz && IsFinite(values[channelIndexCz])) cz = values[channelIndexCz];
        if (count > channelIndexC4 && IsFinite(values[channelIndexC4])) c4 = values[channelIndexC4];

        int index = SelectControlValueIndex(values);
        if (index < 0)
            return false;

        parsed = values[index];
        packetForDebug = string.Format(
            "<unicorn count={0} ctrl_idx={1} ctrl={2:F4} C3={3:F2} Cz={4:F2} C4={5:F2}>",
            count, index, parsed,
            float.IsNaN(c3) ? 0f : c3,
            float.IsNaN(cz) ? 0f : cz,
            float.IsNaN(c4) ? 0f : c4);

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

        float edge  = Mathf.Clamp01(autoEdgeEpsilon);
        float lower = edge;
        float upper = 1f - edge;

        for (int i = values.Length - 1; i >= 0; i--)
        {
            float v = values[i];
            if (IsFinite(v) && v > lower && v < upper)
                return i;
        }
        for (int i = values.Length - 1; i >= 0; i--)
        {
            float v = values[i];
            if (IsFinite(v) && v >= 0f && v <= 1f)
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
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool TryParseFloatText(string text, out float parsed)
    {
        bool ok = float.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed);
        if (ok) return true;

        return float.TryParse(
            text.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed);
    }

    // -------------------------------------------------------------------------
    // Unity Update — runs on the main thread
    // -------------------------------------------------------------------------

    void Update()
    {
        float target;
        float receivedForLog = 0f;
        float c3 = float.NaN, cz = float.NaN, c4 = float.NaN;
        bool shouldLogValue   = false;
        bool shouldLogInvalid = false;
        string invalidText    = "";

        lock (lockObject)
        {
            target = sapcValue;
        }

        CurrentValue = target;

        lock (lockObject)
        {
            if (hasNewValue)
            {
                receivedForLog = latestReceivedValue;
                c3             = latestC3;
                cz             = latestCz;
                c4             = latestC4;
                shouldLogValue = true;
                hasNewValue    = false;
            }

            if (hasInvalidPacket)
            {
                invalidText       = latestInvalidPacket;
                shouldLogInvalid  = true;
                hasInvalidPacket  = false;
            }
        }

        // Commit EEG values to public properties (main thread only)
        if (!float.IsNaN(c3)) C3 = c3;
        if (!float.IsNaN(cz)) Cz = cz;
        if (!float.IsNaN(c4)) C4 = c4;

        if (verboseDebug && shouldLogValue)
        {
            if (debugLogRateHz <= 0f || Time.unscaledTime >= nextLogTime)
            {
                Debug.Log($"EEG ctrl={receivedForLog:F4}  C3={C3:F2}µV  Cz={Cz:F2}µV  C4={C4:F2}µV");
                nextLogTime = Time.unscaledTime + (debugLogRateHz > 0f ? 1f / debugLogRateHz : 0f);
            }
        }

        if (logInvalidPackets && shouldLogInvalid)
        {
            Debug.LogWarning($"SAPCReceiver: could not parse packet '{invalidText}'");
        }
    }

    // -------------------------------------------------------------------------

    void OnApplicationQuit() { Cleanup(); }
    void OnDisable()         { Cleanup(); }

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