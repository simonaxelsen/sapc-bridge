using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class SliderReceiver : MonoBehaviour
{
    [Header("Network")]
    public int port = 1000;

    [Header("Debug")]
    [Tooltip("Print received values to console")]
    public bool verboseDebug = true;

    [Header("Slider")]
    [Tooltip("The UI Slider to control (leave empty to find one automatically)")]
    public Slider targetSlider;

    [Tooltip("Minimum value for the slider")]
    public float minValue = 0f;

    [Tooltip("Maximum value for the slider")]
    public float maxValue = 1f;

    private Thread receiveThread;
    private UdpClient client;
    private float receivedValue = 0.5f;
    private readonly object lockObject = new object();
    private volatile bool isRunning = false;

    void Start()
    {
        isRunning = true;

        if (targetSlider == null)
        {
            targetSlider = FindObjectOfType<Slider>();
            if (targetSlider == null)
            {
                Debug.LogError("SliderReceiver: No UI Slider found! Please assign one in the Inspector.");
                return;
            }
        }

        try
        {
            client = new UdpClient(port);
            client.Client.ReceiveTimeout = 500;
        }
        catch (System.Exception e)
        {
            Debug.LogError("SliderReceiver: Could not open UDP port " + port + " — " + e.Message);
            return;
        }

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("SliderReceiver: Listening on UDP:" + port);
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
                    lock (lockObject)
                    {
                        receivedValue = parsed;
                    }

                    if (verboseDebug)
                        Debug.Log($"EEG: {parsed:F4}");
                }
            }
            catch (SocketException)
            {
            }
            catch (System.ObjectDisposedException)
            {
                break;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SliderReceiver: " + e.Message);
            }
        }
    }

    void Update()
    {
        float rawValue;
        lock (lockObject)
        {
            rawValue = receivedValue;
        }

        float mappedValue = Mathf.Lerp(minValue, maxValue, rawValue);

        if (targetSlider != null)
        {
            targetSlider.value = mappedValue;
        }
    }

    void OnApplicationQuit()
    {
        Cleanup();
    }

    void OnDisable()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        isRunning = false;

        if (client != null)
        {
            try
            {
                client.Close();
            }
            catch
            {
            }
            client = null;
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(1000);
            receiveThread = null;
        }
    }
}