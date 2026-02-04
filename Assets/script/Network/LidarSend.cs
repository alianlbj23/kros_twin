using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends LiDAR scan data over WebSocket as binary frames.
/// Depends on WsClientSharp and MS200KLiDARSim (new version with LaserScanMsg).
///
/// Binary protocol (little-endian):
/// Header:
///   uint32 magic        ('LIDR')
///   uint16 version
///   uint32 pointCount
///   int32  stamp_sec
///   uint32 stamp_nanosec
///   float32 angleMin
///   float32 angleMax
///   float32 angleIncrement
///   float32 timeIncrement
///   float32 scanTime
///   float32 rangeMin
///   float32 rangeMax
/// Payload:
///   float32 ranges[pointCount]
///
/// Notes:
/// - We send ROS-like stamp (sec + nanosec) instead of float timestamp.
/// - Optionally replace +Infinity ranges with rangeMax for compatibility.
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
    public ushort version = 2;

    [Tooltip("If true, replace +Infinity with rangeMax in the outgoing payload.")]
    public bool replaceInfinityWithRangeMax = true;

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

    private void HandleScanReady(MS200KLiDARSim.LaserScanMsg scan)
    {
        if (!sendOnScanReady) return;
        if (wsClient == null || !wsClient.IsConnected) return;
        if (scan.ranges == null || scan.ranges.Length == 0) return;

        int n = scan.ranges.Length;

        // Pre-allocate
        // Header bytes:
        // magic(4) + version(2) + pointCount(4)
        // + stamp_sec(4) + stamp_nanosec(4)
        // + 7 floats (28 bytes) = 4*7
        // Total header = 4+2+4+4+4+28 = 46 bytes
        // Payload = n * 4
        int headerBytes = 46;
        var buffer = new List<byte>(headerBytes + n * 4);

        // Write header
        AppendUInt32(buffer, magic);
        AppendUInt16(buffer, version);
        AppendUInt32(buffer, (uint)n);

        // ROS-like stamp
        AppendInt32(buffer, scan.header.stamp.sec);
        AppendUInt32(buffer, scan.header.stamp.nanosec);

        // LaserScan metadata
        AppendFloat(buffer, scan.angle_min);
        AppendFloat(buffer, scan.angle_max);
        AppendFloat(buffer, scan.angle_increment);
        AppendFloat(buffer, scan.time_increment);
        AppendFloat(buffer, scan.scan_time);
        AppendFloat(buffer, scan.range_min);
        AppendFloat(buffer, scan.range_max);

        // Payload: ranges
        float rangeMax = scan.range_max;

        for (int i = 0; i < n; i++)
        {
            float r = scan.ranges[i];

            if (replaceInfinityWithRangeMax && !float.IsFinite(r))
            {
                // Convert +Infinity/NaN to rangeMax (some receivers can't handle Inf)
                r = rangeMax;
            }

            AppendFloat(buffer, r);
        }

        wsClient.SendBinary(buffer.ToArray());
    }

    // ---------------------------
    // Little-endian writers
    // ---------------------------

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

    private static void AppendInt32(List<byte> buffer, int value)
    {
        unchecked
        {
            buffer.Add((byte)(value & 0xFF));
            buffer.Add((byte)((value >> 8) & 0xFF));
            buffer.Add((byte)((value >> 16) & 0xFF));
            buffer.Add((byte)((value >> 24) & 0xFF));
        }
    }

    private static void AppendFloat(List<byte> buffer, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        buffer.AddRange(bytes);
    }
}
