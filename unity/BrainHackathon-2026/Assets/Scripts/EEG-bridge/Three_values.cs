using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// Receives SAPC / CZ / GLOBAL (0-1) over UDP from unicorn_udp_bridge.py.
/// Packet format: "SAPC=0.500 | CZ=0.342 | GLOBAL=0.471"
/// </summary>
public class Three_values : MonoBehaviour
{
    [Header("Network")]
    public int port = 1000;

    [Header("Smoothing")]
    [Tooltip("EMA smoothing factor (0 = none, 0.95 = very smooth).")]
    [Range(0f, 0.999f)]
    public float smoothFactor = 0.92f;

    [Header("Debug")]
    public bool verboseDebug = true;
    public float debugLogRateHz = 10f;

    // ── Public outputs (0-1) ─────────────────────────────────────────────────
    public float SAPC_01   { get; private set; } = 0.5f;
    public float CZ_01     { get; private set; } = 0.5f;
    public float GLOBAL_01 { get; private set; } = 0.5f;

    // ── Internals ─────────────────────────────────────────────────────────────
    private Thread receiveThread;
    private UdpClient client;
    private readonly object lockObject = new object();
    private volatile bool isRunning = false;

    private float _tSAPC, _tCZ, _tGLOBAL;
    private bool _hasNew = false;
    private int _packetsReceived = 0;
    private int _packetsRejected = 0;

    private float _nextLogTime = 0f;

    // ========================================================================
    void Start()
    {
        isRunning = true;

        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 500;
        }
        catch (Exception e)
        {
            Debug.LogError($"Three_values: Could not open UDP port {port} — {e.Message}");
            enabled = false;
            return;
        }

        receiveThread = new Thread(ReceiveData) { IsBackground = true };
        receiveThread.Start();

        Debug.Log($"Three_values: Listening on UDP:{port}");
    }

    // ========================================================================
    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref anyIP);
                string text = Encoding.UTF8.GetString(data).Trim();

                if (TryParseThreeValues(text, out float s, out float c, out float g))
                {
                    lock (lockObject)
                    {
                        _tSAPC    = s;
                        _tCZ      = c;
                        _tGLOBAL  = g;
                        _hasNew   = true;
                        _packetsReceived++;
                    }
                }
                else
                {
                    lock (lockObject) { _packetsRejected++; }
                    if (verboseDebug)
                        Debug.LogWarning($"Three_values: unexpected packet: '{text}'");
                }
            }
            catch (SocketException) { /* timeout — normal */ }
            catch (ObjectDisposedException) { break; }
            catch (Exception e) { Debug.LogWarning($"Three_values recv: {e.Message}"); }
        }
    }

    // ========================================================================
    // Parser for: "SAPC=0.500 | CZ=0.342 | GLOBAL=0.471"
    // Also accepts European comma decimals: "SAPC=0,500 | CZ=0,342 | GLOBAL=0,471"
    // Returns true only when ALL THREE keys are successfully parsed.
    // ========================================================================
    private bool TryParseThreeValues(string text, out float sapc, out float cz, out float global)
    {
        sapc = cz = global = 0f;

        if (string.IsNullOrWhiteSpace(text)) return false;

        // Normalise: comma decimal → dot decimal, collapse whitespace
        string normalised = text.Replace(',', '.').Trim();

        string[] parts = normalised.Split('|');
        if (parts.Length < 3) return false;

        bool gotSAPC = false, gotCZ = false, gotGLOBAL = false;

        foreach (string part in parts)
        {
            string[] kv = part.Trim().Split('=');
            if (kv.Length != 2) continue;

            string key    = kv[0].Trim().ToUpperInvariant();
            string valStr = kv[1].Trim();

            if (!float.TryParse(valStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float val))
                continue;

            switch (key)
            {
                case "SAPC":
                    sapc    = Mathf.Clamp01(val);
                    gotSAPC = true;
                    break;
                case "CZ":
                    cz    = Mathf.Clamp01(val);
                    gotCZ = true;
                    break;
                case "GLOBAL":
                    global    = Mathf.Clamp01(val);
                    gotGLOBAL = true;
                    break;
            }
        }

        // FIX: only succeed when all three keys were actually found
        return gotSAPC && gotCZ && gotGLOBAL;
    }

    // ========================================================================
    void Update()
    {
        bool gotNew = false;
        float inSAPC = 0f, inCZ = 0f, inGLOBAL = 0f;

        lock (lockObject)
        {
            if (_hasNew)
            {
                inSAPC    = _tSAPC;
                inCZ      = _tCZ;
                inGLOBAL  = _tGLOBAL;
                _hasNew   = false;
                gotNew    = true;
            }
        }

        if (gotNew)
        {
            float a = 1f - smoothFactor;
            SAPC_01   = Mathf.Lerp(SAPC_01,   inSAPC,   a);
            CZ_01     = Mathf.Lerp(CZ_01,     inCZ,     a);
            GLOBAL_01 = Mathf.Lerp(GLOBAL_01, inGLOBAL, a);
        }

        if (verboseDebug && Time.unscaledTime >= _nextLogTime)
        {
            lock (lockObject)
            {
                Debug.Log($"Three_values | SAPC={SAPC_01:F3}  CZ={CZ_01:F3}  GLOBAL={GLOBAL_01:F3}" +
                          $"  [rx={_packetsReceived} bad={_packetsRejected}]");
            }
            _nextLogTime = Time.unscaledTime + (debugLogRateHz > 0f ? 1f / debugLogRateHz : 1f);
        }
    }

    // ========================================================================
    void OnApplicationQuit() => Cleanup();
    void OnDisable()         => Cleanup();

    private void Cleanup()
    {
        isRunning = false;
        try { client?.Close(); } catch { }
        client = null;
        if (receiveThread is { IsAlive: true })
        {
            receiveThread.Join(1000);
            receiveThread = null;
        }
    }
}