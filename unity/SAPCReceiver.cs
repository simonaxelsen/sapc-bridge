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
// Attach this script to a sphere in Unity and run a UDP sender on port 5005
// (see python/udp_sender.py in this repo).
//
// Part of the SAPC (Stroke Adaptive Playback Control) project
//   → Real-time biofeedback for stroke rehabilitation.
// =============================================================================

public class SAPCReceiver : MonoBehaviour
{
    [Header("Network")]
    public int port = 5005;

    [Header("Sphere scaling")]
    [Tooltip("Minimum sphere scale when control value is 0.0")]
    public float minScale = 0.5f;

    [Tooltip("Maximum sphere scale when control value is 1.0")]
    public float maxScale = 3.0f;

    [Tooltip("Visual smoothing speed. Higher = more reactive, lower = smoother.")]
    public float smoothingSpeed = 8.0f;

    private Thread receiveThread;
    private UdpClient client;

    private float sapcValue = 0.5f;
    private readonly object lockObject = new object();
    private volatile bool isRunning = false;

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
                string text = Encoding.UTF8.GetString(data);

                float parsed;
                bool ok = float.TryParse(
                    text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parsed
                );

                if (ok && !float.IsNaN(parsed) && parsed >= 0f && parsed <= 1f)
                {
                    lock (lockObject) { sapcValue = parsed; }
                }
            }
            catch (SocketException)
            {
                // Receive timeout — normal, keep looping
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SAPCReceiver: " + e.Message);
            }
        }
    }

    void Update()
    {
        float target;
        lock (lockObject) { target = sapcValue; }

        float visualScale = Mathf.Lerp(minScale, maxScale, target);
        Vector3 targetScale = new Vector3(visualScale, visualScale, visualScale);

        // Extra visual smoothing to hide sender/render rate mismatch
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            Time.deltaTime * smoothingSpeed
        );
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
