using System;
using System.Collections.Concurrent;
using UnityEngine;

public class CarCFrontWheelReceiver : MonoBehaviour
{
    public WsClientSharp ws;

    // 最新一筆資料（主執行緒用）
    private float[] _latest;
    private readonly ConcurrentQueue<float[]> _queue = new();

    void OnEnable()
    {
        ws.OnBinaryMessage += OnBinary;
    }

    void OnDisable()
    {
        ws.OnBinaryMessage -= OnBinary;
    }

    private void OnBinary(byte[] data)
    {
        // Decode float32 little-endian
        if (data.Length % 4 != 0) return;

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
        // 主執行緒安全處理
        while (_queue.TryDequeue(out var arr))
        {
            _latest = arr;
            HandleCarC(arr);
        }
    }

    private void HandleCarC(float[] v)
    {
        // v == /car_C_front_wheel
        // e.g. v[0]=left, v[1]=right ...
        Debug.Log($"car_C_front_wheel: {string.Join(", ", v)}");

        // 👉 在這裡呼叫你的 CarController / ArticulationBody
    }
}
