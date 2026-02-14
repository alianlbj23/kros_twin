using System;
using System.Collections.Concurrent;
using UnityEngine;

public class CmdVelReceiver : MonoBehaviour
{
    public WsClientSharp ws;

    [Serializable]
    public struct Twist
    {
        public Vector3 linear;   // x,y,z
        public Vector3 angular;  // x,y,z
    }

    public event Action<Twist> OnCmdVel;

    private readonly ConcurrentQueue<Twist> _queue = new();

    private const int FloatSize = 4;
    private const int TwistFloatCount = 6;
    private const int TwistByteSize = TwistFloatCount * FloatSize; // 24 bytes

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
        if (data == null) return;

        // Expect exactly 6 floats (linear xyz + angular xyz)
        if (data.Length != TwistByteSize) return;

        float lx = ReadFloatLE(data, 0);
        float ly = ReadFloatLE(data, 4);
        float lz = ReadFloatLE(data, 8);

        float ax = ReadFloatLE(data, 12);
        float ay = ReadFloatLE(data, 16);
        float az = ReadFloatLE(data, 20);

        var twist = new Twist
        {
            linear = new Vector3(lx, ly, lz),
            angular = new Vector3(ax, ay, az),
        };

        _queue.Enqueue(twist);
    }

    void Update()
    {
        bool has = false;
        Twist latest = default;

        while (_queue.TryDequeue(out var v))
        {
            latest = v;
            has = true;
        }

        if (has)
            OnCmdVel?.Invoke(latest);
    }

    private static float ReadFloatLE(byte[] data, int offset)
    {
        // Python side uses struct.pack('<6f', ...) => little-endian
        if (BitConverter.IsLittleEndian)
            return BitConverter.ToSingle(data, offset);

        // Big-endian machine fallback
        var tmp = new byte[4];
        Buffer.BlockCopy(data, offset, tmp, 0, 4);
        Array.Reverse(tmp);
        return BitConverter.ToSingle(tmp, 0);
    }
}
