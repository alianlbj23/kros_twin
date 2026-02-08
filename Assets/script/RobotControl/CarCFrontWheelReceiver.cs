using System;
using System.Collections.Concurrent;
using UnityEngine;

public class CarCFrontWheelReceiver : MonoBehaviour
{
    public WsClientSharp ws;

    public event Action<float[]> OnCarCArray;

    private readonly ConcurrentQueue<float[]> _queue = new();

    void OnEnable()
    {
        if (ws != null)
            ws.OnBinaryMessage += OnBinary;
    }

    void OnDisable()
    {
        if (ws != null)
            ws.OnBinaryMessage -= OnBinary;
    }

    private void OnBinary(byte[] data)
    {
        if (data == null || data.Length % 4 != 0) return;

        int n = data.Length / 4;
        var arr = new float[n];

        if (BitConverter.IsLittleEndian)
        {
            for (int i = 0; i < n; i++)
                arr[i] = BitConverter.ToSingle(data, i * 4);
        }
        else
        {
            var tmp = new byte[4];
            for (int i = 0; i < n; i++)
            {
                Buffer.BlockCopy(data, i * 4, tmp, 0, 4);
                Array.Reverse(tmp);
                arr[i] = BitConverter.ToSingle(tmp, 0);
            }
        }

        _queue.Enqueue(arr);
    }

    void Update()
    {
        float[] latest = null;
        while (_queue.TryDequeue(out var v))
            latest = v;

        if (latest != null)
        {
            OnCarCArray?.Invoke(latest);
        }
    }
}
