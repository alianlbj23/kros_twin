using System;
using UnityEngine;

/// <summary>
/// Bidirectional WebSocket channel for /car_C_front_wheel.
/// - Receives binary float32[] (little-endian) from ROS and exposes latest values.
/// - Can send float32[] back to ROS using the same binary format.
///
/// Requires WsClientSharp on the same GameObject (websocket-sharp).
/// </summary>
[DisallowMultipleComponent]
public class CarFrontWheelWsChannel : MonoBehaviour
{
    [Header("Ws Client")]
    public WsClientSharp wsClient;

    [Header("Receive State")]
    [Tooltip("Latest received data from /car_C_front_wheel (float32[]).")]
    public float[] latestData = Array.Empty<float>();

    [Tooltip("Set true when a new frame arrives. Auto-cleared in LateUpdate.")]
    public bool hasNewFrame;

    [Header("Send Options")]
    [Tooltip("If true, sends data once on Start for quick testing.")]
    public bool sendTestOnStart;
    public float[] testPayload = new float[] { 1f, 2f, 3f };

    private readonly object _dataLock = new object();

    private void Awake()
    {
        if (wsClient == null) wsClient = GetComponent<WsClientSharp>();
        if (wsClient == null)
        {
            Debug.LogError("[CarFrontWheelWsChannel] Missing WsClientSharp component.");
            enabled = false;
            return;
        }

        wsClient.OnBinaryMessage += HandleBinary;
    }

    private void Start()
    {
        if (sendTestOnStart)
        {
            SendFloatArray(testPayload);
        }
    }

    private void LateUpdate()
    {
        // Reset the flag so consumers can poll it per-frame.
        hasNewFrame = false;
    }

    private void OnDestroy()
    {
        if (wsClient != null)
        {
            wsClient.OnBinaryMessage -= HandleBinary;
        }
    }

    private void HandleBinary(byte[] data)
    {
        if (data == null || data.Length < 4 || (data.Length % 4) != 0) return;

        int count = data.Length / 4;
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * 4;
            uint u = (uint)(data[offset]
                            | (data[offset + 1] << 8)
                            | (data[offset + 2] << 16)
                            | (data[offset + 3] << 24));
            values[i] = BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
        }

        lock (_dataLock)
        {
            latestData = values;
            hasNewFrame = true;
        }
    }

    public void SendFloatArray(float[] values)
    {
        if (wsClient == null || values == null || values.Length == 0) return;

        var payload = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            byte[] bytes = BitConverter.GetBytes(values[i]);
            // Ensure little-endian on the wire
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, payload, i * 4, 4);
        }

        wsClient.SendBinary(payload);
    }
}
