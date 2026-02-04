using System;
using UnityEngine;

/// <summary>
/// Simulates a 2D 360-degree spinning LiDAR (e.g., Orbbec MS200K) using Physics.Raycast.
/// Produces ROS LaserScan-like data and a getter for /scan topic payload.
///
/// Notes:
/// - header.stamp is the time of the FIRST ray in the scan.
/// - This script uses SimClock (0-based sim time) if available.
/// - Angles are ROS-style metadata (radians).
/// </summary>
[DisallowMultipleComponent]
public class MS200KLiDARSim : MonoBehaviour
{
    [Header("MS200K-like Specs")]
    [Tooltip("Minimum valid range in meters.")]
    public float minRangeMeters = 0.03f;

    [Tooltip("Maximum valid range in meters.")]
    public float maxRangeMeters = 12.0f;

    [Tooltip("Total measurement rate (points per second).")]
    public int measurementRateHz = 4500;

    [Tooltip("Scan rate in rotations per second (Hz).")]
    public float scanRateHz = 10.0f;

    [Header("Scan Geometry (ROS convention)")]
    [Tooltip("ROS angle_min (radians). Typical: -PI.")]
    public float rosAngleMin = -Mathf.PI;

    [Tooltip("ROS angle_max (radians). Typical: +PI.")]
    public float rosAngleMax = Mathf.PI;

    [Tooltip("Angle offset applied to ray directions (degrees). Use to align your LiDAR forward axis.")]
    public float angleOffsetDeg = 0.0f;

    [Tooltip("If true, forces pointsPerScan to a fixed number instead of measurementRateHz/scanRateHz.")]
    public bool overridePointsPerScan = false;

    [Tooltip("Number of rays per scan (one rotation). If overridePointsPerScan is false, it is computed.")]
    public int pointsPerScan = 450;

    [Tooltip("If true, simulate MS200K clockwise point order (device polar direction is clockwise).")]
    public bool ms200kClockwise = true;

    [Header("Raycast")]
    [Tooltip("Which layers are considered obstacles.")]
    public LayerMask obstacleLayers = ~0;

    [Tooltip("Start raycast a bit away from origin to avoid self-hit (meters).")]
    public float rayStartOffset = 0.01f;

    [Tooltip("If true, ignore trigger colliders.")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Noise (optional)")]
    [Tooltip("Gaussian noise standard deviation (meters). Set 0 to disable.")]
    public float rangeNoiseStdDev = 0.0f;

    [Tooltip("Random seed. 0 means non-deterministic.")]
    public int noiseSeed = 0;

    [Header("ROS Output")]
    [Tooltip("frame_id for LaserScan. If empty, uses GameObject name.")]
    public string frameId = "ms200k";

    [Tooltip("If true, use SimClock.Instance.Now for stamp; otherwise fall back to Time.timeAsDouble.")]
    public bool useSimClockIfAvailable = true;

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
            // timeSec is expected >= 0 in sim, but handle negative gracefully.
            double t = timeSec;
            int sec = (int)Math.Floor(t);
            double frac = t - sec;
            if (frac < 0)
            {
                // If negative fractional due to negative time, normalize.
                sec -= 1;
                frac = (t - sec);
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

    /// <summary>
    /// Fired when a full scan is produced.
    /// </summary>
    public event Action<LaserScanMsg> OnScanReady;

    private System.Random _rng;
    private double _nextScanTimeSec;
    private LaserScanMsg _lastScan;
    private bool _hasScan;

    private void Awake()
    {
        _rng = (noiseSeed == 0) ? new System.Random() : new System.Random(noiseSeed);
        _nextScanTimeSec = 0.0;
        _hasScan = false;
    }

    private void OnValidate()
    {
        minRangeMeters = Mathf.Max(0.0f, minRangeMeters);
        maxRangeMeters = Mathf.Max(minRangeMeters, maxRangeMeters);

        scanRateHz = Mathf.Max(0.1f, scanRateHz);
        measurementRateHz = Mathf.Max(1, measurementRateHz);

        if (!overridePointsPerScan)
        {
            pointsPerScan = Mathf.Max(1, Mathf.RoundToInt(measurementRateHz / scanRateHz));
        }
        else
        {
            pointsPerScan = Mathf.Max(1, pointsPerScan);
        }

        if (Mathf.Approximately(rosAngleMin, rosAngleMax))
        {
            rosAngleMin = -Mathf.PI;
            rosAngleMax = Mathf.PI;
        }
    }

    private void Update()
    {
        double now = NowSec();
        if (now >= _nextScanTimeSec)
        {
            float scanTime = 1.0f / Mathf.Max(0.1f, scanRateHz);
            double scanStartTime = now; // first-ray time (best effort)

            _nextScanTimeSec = now + scanTime;
            ProduceFullScan(scanStartTime, scanTime);
        }
    }

    /// <summary>
    /// Getter: return the latest /scan LaserScan message.
    /// Returns false if no scan has been produced yet.
    /// </summary>
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
        int n = overridePointsPerScan
            ? Mathf.Max(1, pointsPerScan)
            : Mathf.Max(1, Mathf.RoundToInt(measurementRateHz / scanRateHz));

        // ROS angle metadata (radians)
        float angleMin = rosAngleMin;
        float angleMax = rosAngleMax;

        // Common LaserScan convention:
        // angle(i) = angle_min + i * angle_increment
        float angleIncrement = (n <= 1) ? 0.0f : (angleMax - angleMin) / (n - 1);

        // time_increment: time between measurements
        // Often used: scan_time / (N - 1) for consistency with first..last sample span
        float timeIncrement = (n <= 1) ? 0.0f : (scanTime / (n - 1));

        float[] rangesForRos = new float[n];
        float[] intensities = new float[n];

        Vector3 origin = transform.position;
        Vector3 up = Vector3.up;
        Vector3 baseDir = transform.forward;

        // If device scans clockwise, invert sign so point order matches clockwise.
        float sign = ms200kClockwise ? -1.0f : 1.0f;

        // offset in radians
        float offsetRad = angleOffsetDeg * Mathf.Deg2Rad;

        for (int i = 0; i < n; i++)
        {
            float rosAngle = angleMin + i * angleIncrement;  // radians
            float effectiveAngleRad = sign * rosAngle + offsetRad;

            // convert radians -> degrees for Unity rotation
            float deg = effectiveAngleRad * Mathf.Rad2Deg;

            Quaternion rot = Quaternion.AngleAxis(deg, up);
            Vector3 dir = rot * baseDir;

            Vector3 rayOrigin = origin + dir * rayStartOffset;

            float measured = maxRangeMeters;
            bool hit = Physics.Raycast(rayOrigin, dir, out RaycastHit hitInfo, maxRangeMeters, obstacleLayers, triggerInteraction);

            if (hit)
            {
                measured = hitInfo.distance + rayStartOffset;
                measured = Mathf.Clamp(measured, minRangeMeters, maxRangeMeters);
            }
            else
            {
                measured = float.PositiveInfinity; // common ROS practice for "no return"
            }

            if (rangeNoiseStdDev > 0.0f && float.IsFinite(measured))
            {
                measured += (float)(Gaussian01(_rng) * rangeNoiseStdDev);
                measured = Mathf.Clamp(measured, minRangeMeters, maxRangeMeters);
            }

            rangesForRos[i] = measured;

            // If you don't have intensity model, leave 0.
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

            ranges = rangesForRos,
            intensities = intensities
        };

        _hasScan = true;
        OnScanReady?.Invoke(_lastScan);
    }

    // Standard normal N(0,1) using Box-Muller transform
    private static double Gaussian01(System.Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(); // (0,1]
        double u2 = 1.0 - rng.NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        return r * Math.Cos(theta);
    }
}
