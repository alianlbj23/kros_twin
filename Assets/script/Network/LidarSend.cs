using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends LiDAR scan data over WebSocket as binary frames.
/// Depends on WsClientSharp and MS200KLiDARSim.
/// </summary>
[DisallowMultipleComponent]
public class LidarSend : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("WebSocket client used to send binary data.")]
    public WsClientSharp wsClient;

    [Tooltip("LiDAR simulator providing scan data.")]
    public MS200KLiDARSim lidar;

    [Header("Send Options")]
    public bool sendOnScanReady = true;

    [Header("Binary Format")]
    [Tooltip("4-byte magic value at header start. Default is 'LIDR'.")]
    public uint magic = 0x4C494452; // 'LIDR'

    [Tooltip("Binary protocol version.")]
    public ushort version = 1;

    private void OnEnable()
    {
        if (wsClient == null) wsClient = GetComponent<WsClientSharp>();
        if (lidar == null) lidar = GetComponent<MS200KLiDARSim>();

        if (lidar != null)
        {
            lidar.OnScanReady += HandleScanReady;
        }
    }

    private void OnDisable()
    {
        if (lidar != null)
        {
            lidar.OnScanReady -= HandleScanReady;
        }
    }

    private void HandleScanReady(float timestamp, float[] anglesDeg, float[] ranges, bool[] hits)
    {
        if (!sendOnScanReady) return;
        if (wsClient == null || !wsClient.IsConnected) return;
        if (ranges == null || ranges.Length == 0) return;

        // Build header
        int n = ranges.Length;
        float angleMin = 0.0f;
        float angleMax = 0.0f;
        float angleInc = 0.0f;
        float rangeMin = 0.0f;
        float rangeMax = 0.0f;

        if (lidar != null && lidar.TryGetLaserScanData(out var scan))
        {
            angleMin = scan.angle_min;
            angleMax = scan.angle_max;
            angleInc = scan.angle_increment;
            rangeMin = scan.range_min;
            rangeMax = scan.range_max;
        }

        var buffer = new List<byte>(64 + n * 4);

        // Header layout (little-endian):
        // uint32 magic
        // uint16 version
        // uint16 flags
        // uint32 pointCount
        // float32 timestamp
        // float32 angleMin
        // float32 angleMax
        // float32 angleIncrement
        // float32 rangeMin
        // float32 rangeMax
        AppendUInt32(buffer, magic);
        AppendUInt16(buffer, version);
        AppendUInt16(buffer, 0);
        AppendUInt32(buffer, (uint)n);
        AppendFloat(buffer, timestamp);
        AppendFloat(buffer, angleMin);
        AppendFloat(buffer, angleMax);
        AppendFloat(buffer, angleInc);
        AppendFloat(buffer, rangeMin);
        AppendFloat(buffer, rangeMax);

        // Payload: ranges
        for (int i = 0; i < n; i++)
        {
            AppendFloat(buffer, ranges[i]);
        }

        wsClient.SendBinary(buffer.ToArray());
    }

    private static void AppendUInt16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
    }

    private static void AppendUInt32(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
        buffer.Add((byte)((value >> 16) & 0xFF));
        buffer.Add((byte)((value >> 24) & 0xFF));
    }

    private static void AppendFloat(List<byte> buffer, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        buffer.AddRange(bytes);
    }
}
