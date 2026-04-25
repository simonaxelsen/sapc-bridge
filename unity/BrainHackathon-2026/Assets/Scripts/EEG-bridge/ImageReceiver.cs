using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class ImageReceiver : MonoBehaviour
{
    [Header("Network")]
    public int port = 1000;

    [Header("Image Control")]
    [Tooltip("The UI Image to scale (leave empty to use this GameObject's Image)")]
    public Image targetImage;

    [Tooltip("Minimum scale when value is 0.0")]
    public float minScale = 0.1f;

    [Tooltip("Maximum scale when value is 1.0")]
    public float maxScale = 2.0f;

    [Tooltip("Visual smoothing (higher = faster)")]
    public float smoothing = 8f;

    [Header("Debug")]
    [Tooltip("Print received values to console")]
    public bool verboseDebug = true;

    private Thread receiveThread;
    private UdpClient client;
    private float receivedValue = 0f;
    private readonly object lockObject = new object();
    private volatile bool isRunning = false;

    void Start()
    {
        isRunning = true;

        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetImage == null)
        {
            Debug.LogError("ImageReceiver: No UI Image found! Add an Image component or assign one.");
            return;
        }

        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 500;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ImageReceiver: Could not open UDP port {port} — {e.Message}");
            return;
        }

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log($"ImageReceiver: Listening on UDP:{port}");
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
                bool ok = float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed);

                if (ok && !float.IsNaN(parsed) && parsed >= 0f && parsed <= 1f)
                {
                    lock (lockObject)
                    {
                        receivedValue = parsed;
                    }

                    if (verboseDebug)
                        Debug.Log($"EEG: {parsed:F4}");
                }
            }
            catch (SocketException) { }
            catch (System.ObjectDisposedException) { break; }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ImageReceiver: {e.Message}");
            }
        }
    }

    void Update()
    {
        float rawValue;
        lock (lockObject) { rawValue = receivedValue; }

        float visualScale = Mathf.Lerp(minScale, maxScale, rawValue);
        Vector3 targetScale = new Vector3(visualScale, visualScale, 1f);

        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * smoothing);
    }

    void OnApplicationQuit() => Cleanup();
    void OnDisable() => Cleanup();

    private void Cleanup()
    {
        isRunning = false;
        if (client != null) { client.Close(); client = null; }
        if (receiveThread != null && receiveThread.IsAlive) { receiveThread.Join(1000); receiveThread = null; }
    }
}