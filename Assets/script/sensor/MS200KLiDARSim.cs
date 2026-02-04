using System;
using UnityEngine;

/// <summary>
/// MS200K-like 2D spinning LiDAR simulator (Physics.Raycast),
/// producing ROS sensor_msgs/LaserScan–compatible data.
///
/// =======================
/// Public Parameters Policy
/// =======================
///
/// This component intentionally exposes ONLY hardware-level specifications,
/// i.e. values that would normally be found in a LiDAR datasheet or set by
/// the device firmware. All ROS LaserScan fields are derived from these specs.
///
/// -----------------------
/// Exposed (Hardware Specs)
/// -----------------------
///
/// 1) FOV (Field of View, degrees)
///    - Physical angular coverage of one full scan.
///    - Example: 360° for MS200K.
///    - Affects:
///        angle_min, angle_max, angle_increment
///
/// 2) Scan Rate (Hz)
///    - Mechanical rotation frequency of the LiDAR.
///    - Example: 10 Hz = 10 full rotations per second.
///    - Affects:
///        scan_time = 1 / scanRateHz
///        time_increment (together with measurement rate)
///
/// 3) Measurement Rate (points per second)
///    - Total number of distance samples produced per second.
///    - Example: 4500 pts/s.
///    - Affects:
///        points per scan = measurementRateHz / scanRateHz
///        angle_increment
///        time_increment
///
/// 4) Min / Max Range (meters)
///    - Physical sensing limits of the LiDAR.
///    - Points outside this range are considered invalid.
///    - Affects:
///        range_min, range_max
///        validity of each range measurement
///
/// 5) Resolution (degrees) [Optional / Informational]
///    - Nominal horizontal angular resolution at a given scan rate.
///    - Often listed in datasheets (e.g. 0.8° @ 10 Hz).
///    - Used for reference or cross-checking derived point count.
///    - Does NOT directly override measurement rate unless explicitly chosen.
///
/// 6) Rotation Direction (CW / CCW)
///    - Physical rotation direction of the LiDAR head.
///    - Determines point ordering in the ranges[] array.
///    - CW means angles decrease in ROS frame as index increases.
///    - CCW follows standard ROS LaserScan convention.
///
/// 7) angleOffsetDeg (degrees)
///    - Fixed yaw offset to align the LiDAR’s internal 0° direction with
///      the robot / Unity forward direction.
///    - Represents SENSOR EXTRINSIC (mounting orientation), not scan behavior.
///
///    - Effect:
///        • Applies a constant rotation to ALL rays.
///        • Does NOT affect FOV, scan rate, resolution, or point count.
///
///    - ROS note:
///        • Equivalent to a fixed yaw between LiDAR frame and base frame.
///        • Could be modeled as a static TF; applied here for simplicity.
///
///    - Examples:
///        • LiDAR mounted 90° clockwise → angleOffsetDeg = -90
///        • LiDAR facing backward     → angleOffsetDeg = 180
/// -----------------------
/// Derived (Not User-Set)
/// -----------------------
///
/// The following ROS LaserScan fields are ALWAYS computed internally
/// to guarantee consistency and correctness:
///
///   angle_min
///   angle_max
///   angle_increment
///   scan_time
///   time_increment
///   number of points per scan
///
/// Users should NOT attempt to tune these manually.
///
/// -----------------------
/// Coordinate & Timing Notes
/// -----------------------
///
/// - header.stamp represents the time of the FIRST laser ray in the scan.
/// - Timestamp is taken from SimClock (simulation time starting at 0)
///   if available; otherwise Unity Time.timeAsDouble is used.
/// - No measurement noise is added in this simulator.
///
/// This design ensures:
/// - Deterministic behavior
/// - ROS-compliant LaserScan output
/// - Clear separation between hardware specs and derived scan parameters
/// </summary>
[DisallowMultipleComponent]
public class MS200KLiDARSim : MonoBehaviour
{
    public enum RotationDirection
    {
        CounterClockwise = 0, // CCW
        Clockwise = 1         // CW (MS200K spec commonly states clockwise)
    }

    [Header("Hardware-like Specs (Public)")]
    [Tooltip("Field of View in degrees. MS200K is 360.")]
    public float fovDeg = 360.0f;

    [Tooltip("Rotational speed (scan rate) in Hz. Example: 10.")]
    public float scanRateHz = 10.0f;

    [Tooltip("Measurement rate in points per second. Example: 4500.")]
    public int measurementRateHz = 4500;

    [Tooltip("Minimum valid range in meters.")]
    public float minRangeMeters = 0.03f;

    [Tooltip("Maximum valid range in meters.")]
    public float maxRangeMeters = 12.0f;

    [Tooltip("Horizontal angular resolution in degrees at the given scan rate (optional input). Example: 0.8 deg @ 10Hz.")]
    public float resolutionDeg = 0.8f;

    [Tooltip("Device rotation direction / point order.")]
    public RotationDirection rotationDirection = RotationDirection.Clockwise;

    [Header("Raycast")]
    [Tooltip("Which layers are considered obstacles.")]
    public LayerMask obstacleLayers = ~0;

    [Tooltip("Start raycast a bit away from origin to avoid self-hit (meters).")]
    public float rayStartOffset = 0.01f;

    [Tooltip("If true, ignore trigger colliders.")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("ROS Output")]
    [Tooltip("frame_id for LaserScan. If empty, uses GameObject name.")]
    public string frameId = "ms200k";

    [Tooltip("If true, use SimClock.Instance.Now for stamp; otherwise fall back to Time.timeAsDouble.")]
    public bool useSimClockIfAvailable = true;

    [Tooltip("Angle offset in degrees to align LiDAR forward axis. 0 means transform.forward is 0-degree reference.")]
    public float angleOffsetDeg = 0.0f;

    // ------------------------
    // ROS-like message structs
    // ------------------------
    [Serializable]
    public struct RosTime
    {
        public int sec;
        public uint nanosec;

        public static RosTime FromSec(double timeSec)
        {
            double t = timeSec;
            int sec = (int)Math.Floor(t);
            double frac = t - sec;

            if (frac < 0)
            {
                sec -= 1;
                frac = t - sec;
            }

            uint nanosec = (uint)Math.Round(frac * 1e9);
            if (nanosec >= 1000000000u)
            {
                sec += 1;
                nanosec -= 1000000000u;
            }

            return new RosTime { sec = sec, nanosec = nanosec };
        }
    }

    [Serializable]
    public struct Header
    {
        public RosTime stamp;
        public string frame_id;
    }

    [Serializable]
    public struct LaserScanMsg
    {
        public Header header;

        public float angle_min;
        public float angle_max;
        public float angle_increment;

        public float time_increment;
        public float scan_time;

        public float range_min;
        public float range_max;

        public float[] ranges;
        public float[] intensities;
    }

    public event Action<LaserScanMsg> OnScanReady;

    private double _nextScanTimeSec = 0.0;
    private LaserScanMsg _lastScan;
    private bool _hasScan;

    private void OnValidate()
    {
        fovDeg = Mathf.Clamp(fovDeg, 1.0f, 360.0f);
        scanRateHz = Mathf.Max(0.1f, scanRateHz);
        measurementRateHz = Mathf.Max(1, measurementRateHz);

        minRangeMeters = Mathf.Max(0.0f, minRangeMeters);
        maxRangeMeters = Mathf.Max(minRangeMeters, maxRangeMeters);

        resolutionDeg = Mathf.Max(0.0001f, resolutionDeg);
    }

    private void Update()
    {
        double now = NowSec();
        if (now >= _nextScanTimeSec)
        {
            float scanTime = 1.0f / scanRateHz;
            double scanStart = now; // best-effort "first ray time"

            _nextScanTimeSec = now + scanTime;
            ProduceFullScan(scanStart, scanTime);
        }
    }

    public bool TryGetLaserScan(out LaserScanMsg msg)
    {
        msg = _lastScan;
        return _hasScan;
    }

    private double NowSec()
    {
        if (useSimClockIfAvailable && SimClock.Instance != null)
            return SimClock.Instance.Now;

        return Time.timeAsDouble;
    }

    private void ProduceFullScan(double scanStartTimeSec, float scanTime)
    {
        // -----------------------------
        // Derive N (points per scan)
        // -----------------------------
        // Prefer using measurement rate & scan rate (most fundamental).
        int nFromRates = Mathf.Max(1, Mathf.RoundToInt(measurementRateHz / scanRateHz));

        // Also compute N from resolution if user provided a meaningful value.
        int nFromResolution = Mathf.Max(1, Mathf.RoundToInt(fovDeg / resolutionDeg));

        // Choose a consistent N:
        // - If resolutionDeg matches the rates, both are close.
        // - If they differ, prioritize measurementRateHz/scanRateHz (because it defines time_increment too).
        int n = nFromRates;

        // -----------------------------
        // Derive ROS angles
        // -----------------------------
        float fovRad = fovDeg * Mathf.Deg2Rad;

        // Use a symmetric angle range around 0 for stability in visualization:
        // angle_min = -FOV/2, angle_max = +FOV/2 - increment
        float angleMin = -0.5f * fovRad;

        // angle_increment: prefer from N (consistent with sample count)
        float angleIncrement = (n <= 1) ? 0.0f : (fovRad / n);

        // angle_max must correspond to the last sample index (N-1)
        float angleMax = angleMin + angleIncrement * Mathf.Max(0, n - 1);

        // -----------------------------
        // Derive ROS time metadata
        // -----------------------------
        // time_increment: time between adjacent measurements
        float timeIncrement = (n <= 0) ? 0.0f : (scanTime / n);

        // -----------------------------
        // Raycast + fill ranges
        // -----------------------------
        float[] ranges = new float[n];
        float[] intensities = new float[n]; // keep, but zeros are fine

        Vector3 origin = transform.position;
        Vector3 up = Vector3.up;
        Vector3 baseDir = transform.forward;

        // Rotation direction:
        // - LaserScan angle increases CCW as index increases (typical convention).
        // - If device is clockwise, we flip the effective angle sign so points come out in device order.
        float sign = (rotationDirection == RotationDirection.Clockwise) ? -1.0f : 1.0f;

        float offsetRad = angleOffsetDeg * Mathf.Deg2Rad;

        for (int i = 0; i < n; i++)
        {
            // ROS angle for sample i (monotonic)
            float rosAngle = angleMin + i * angleIncrement;

            // Device effective angle (apply CW/CCW and offset)
            float effectiveAngleRad = sign * rosAngle + offsetRad;

            float deg = effectiveAngleRad * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.AngleAxis(deg, up);
            Vector3 dir = rot * baseDir;

            Vector3 rayOrigin = origin + dir * rayStartOffset;

            bool hit = Physics.Raycast(
                rayOrigin, dir,
                out RaycastHit hitInfo,
                maxRangeMeters,
                obstacleLayers,
                triggerInteraction);

            if (hit)
            {
                float measured = hitInfo.distance + rayStartOffset;
                measured = Mathf.Clamp(measured, minRangeMeters, maxRangeMeters);
                ranges[i] = measured;
            }
            else
            {
                ranges[i] = float.PositiveInfinity;
            }

            intensities[i] = 0.0f;
        }

        _lastScan = new LaserScanMsg
        {
            header = new Header
            {
                stamp = RosTime.FromSec(scanStartTimeSec),
                frame_id = string.IsNullOrEmpty(frameId) ? gameObject.name : frameId
            },

            angle_min = angleMin,
            angle_max = angleMax,
            angle_increment = angleIncrement,

            time_increment = timeIncrement,
            scan_time = scanTime,

            range_min = minRangeMeters,
            range_max = maxRangeMeters,

            ranges = ranges,
            intensities = intensities
        };

        _hasScan = true;
        OnScanReady?.Invoke(_lastScan);
    }
}
