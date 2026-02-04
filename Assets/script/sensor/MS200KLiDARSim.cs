using System;
using UnityEngine;

/// <summary>
/// Simulates a 2D 360-degree spinning LiDAR (e.g., Orbbec MS200K) using Physics.Raycast.
/// Output is a full scan (ranges array) at a given scan rate (Hz).
///
/// Coordinate convention:
/// - Default: Y is up (Unity typical), scan happens on the XZ plane.
/// - Angles increase clockwise when looking from +Y depending on axis; adjust angleOffsetDeg if needed.
/// </summary>
[DisallowMultipleComponent]
public class MS200KLiDARSim : MonoBehaviour
{
    [Header("MS200K-like Specs (default)")]
    [Tooltip("Minimum valid range in meters.")]
    public float minRangeMeters = 0.03f;

    [Tooltip("Maximum range in meters (90% reflectance typical).")]
    public float maxRangeMeters = 12.0f;

    [Tooltip("Total measurement rate (points per second). MS200K max is around 4500 Hz.")]
    public int measurementRateHz = 4500;

    [Tooltip("Scan rate in rotations per second (Hz). Typical 7~15 Hz. Default 10 Hz.")]
    public float scanRateHz = 10.0f;

    [Header("Scan Geometry")]
    [Tooltip("Start angle of scan in degrees. Usually 0..360. 0 points to +Z by default (configurable).")]
    public float startAngleDeg = 0.0f;

    [Tooltip("End angle of scan in degrees. Use 360 for full circle.")]
    public float endAngleDeg = 360.0f;

    [Tooltip("Angle offset applied to the scan direction. Use this to match your LiDAR forward direction.")]
    public float angleOffsetDeg = 0.0f;

    [Tooltip("If true, forces pointsPerScan to a fixed number instead of measurementRateHz/scanRateHz.")]
    public bool overridePointsPerScan = false;

    [Tooltip("Number of rays per full scan (one rotation). If overridePointsPerScan is false, it is computed.")]
    public int pointsPerScan = 450;

    [Header("Raycast")]
    [Tooltip("Which layers are considered obstacles.")]
    public LayerMask obstacleLayers = ~0;

    [Tooltip("Start raycast a bit away from the origin to avoid self-hits (meters).")]
    public float rayStartOffset = 0.01f;

    [Tooltip("If true, ignore trigger colliders.")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Debug Visualization")]
    [Tooltip("Draw LiDAR rays for debugging.")]
    public bool drawRays = false;

    [Tooltip("Only draw rays that hit something.")]
    public bool drawHitsOnly = false;

    [Tooltip("When no hit, draw ray at max range.")]
    public bool drawMissAtMaxRange = true;

    [Tooltip("Ray color when hit.")]
    public Color hitRayColor = Color.green;

    [Tooltip("Ray color when miss.")]
    public Color missRayColor = Color.red;

    [Tooltip("Ray duration in seconds (0 draws only for one frame).")]
    public float rayDrawDuration = 0.0f;

    [Header("Noise (optional)")]
    [Tooltip("Gaussian noise standard deviation (meters). Set 0 to disable.")]
    public float rangeNoiseStdDev = 0.0f;

    [Tooltip("Random seed. Use a fixed seed for reproducible noise.")]
    public int noiseSeed = 0;

    [Header("ROS LaserScan Output")]
    [Tooltip("frame_id for LaserScan-like data. If empty, uses GameObject name.")]
    public string frameId = "";

    /// <summary>
    /// LaserScan-like data (ROS sensor_msgs/LaserScan) without stamp.
    /// All angles are in radians.
    /// </summary>
    public struct LaserScanData
    {
        public string frame_id;
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
    /// - timestamp: Time.time (seconds)
    /// - anglesDeg: angle per point (degrees)
    /// - ranges: distance per point (meters). If no hit, returns maxRangeMeters.
    /// - hitFlags: true if hit something, false if no hit.
    /// </summary>
    public event Action<float, float[], float[], bool[]> OnScanReady;

    private System.Random _rng;
    private float _nextScanTime;
    private LaserScanData _lastScan;
    private bool _hasScan;

    private void Awake()
    {
        _rng = (noiseSeed == 0) ? new System.Random() : new System.Random(noiseSeed);
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

        // Avoid degenerate ranges
        if (Mathf.Approximately(startAngleDeg, endAngleDeg))
        {
            endAngleDeg = startAngleDeg + 360.0f;
        }
    }

    private void Update()
    {
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + (1.0f / scanRateHz);
            ProduceFullScan();
        }
    }

    private void ProduceFullScan()
    {
        // Determine points per scan
        int n = overridePointsPerScan ? pointsPerScan : Mathf.Max(1, Mathf.RoundToInt(measurementRateHz / scanRateHz));

        float totalAngle = endAngleDeg - startAngleDeg;
        if (totalAngle <= 0.0f) totalAngle += 360.0f;

        float[] angles = new float[n];
        float[] ranges = new float[n];
        bool[] hits = new bool[n];

        Vector3 origin = transform.position;
        Vector3 up = Vector3.up;

        // Define "0 deg" direction: by default we use +Z as forward reference.
        // If your LiDAR forward should be transform.forward, set baseDir = transform.forward instead.
        Vector3 baseDir = Vector3.forward;

        int drawEvery = Mathf.Max(1, n / 9);

        for (int i = 0; i < n; i++)
        {
            float t = (n == 1) ? 0.0f : (float)i / (n - 1);
            float angle = startAngleDeg + t * totalAngle + angleOffsetDeg;

            angles[i] = NormalizeAngleDeg(angle);

            // Rotate baseDir around Y axis (scan on XZ plane)
            Quaternion rot = Quaternion.AngleAxis(angle, up);
            Vector3 dir = rot * baseDir;

            // Start slightly away from origin to avoid self-collisions
            Vector3 rayOrigin = origin + dir * rayStartOffset;

            float measured = maxRangeMeters;
            bool hit = Physics.Raycast(rayOrigin, dir, out RaycastHit hitInfo, maxRangeMeters, obstacleLayers, triggerInteraction);

            if (hit)
            {
                measured = hitInfo.distance + rayStartOffset; // compensate offset
                measured = Mathf.Max(minRangeMeters, measured);
                measured = Mathf.Min(maxRangeMeters, measured);
            }

            // Add optional Gaussian noise
            if (rangeNoiseStdDev > 0.0f)
            {
                measured += (float)(Gaussian01(_rng) * rangeNoiseStdDev);
                measured = Mathf.Clamp(measured, minRangeMeters, maxRangeMeters);
            }

            ranges[i] = measured;
            hits[i] = hit;

            if (drawRays && (i % drawEvery == 0))
            {
                float drawDistance = hit ? measured : (drawMissAtMaxRange ? maxRangeMeters : measured);
                if (!drawHitsOnly || hit)
                {
                    Debug.DrawRay(rayOrigin, dir * drawDistance, hit ? hitRayColor : missRayColor, rayDrawDuration);
                }
            }

        }

        OnScanReady?.Invoke(Time.time, angles, ranges, hits);

        // Build LaserScan-like data (ROS) without stamp
        float angleMinRad = Mathf.Deg2Rad * NormalizeAngleDeg(startAngleDeg + angleOffsetDeg);
        float angleMaxRad = Mathf.Deg2Rad * NormalizeAngleDeg(startAngleDeg + totalAngle + angleOffsetDeg);
        float angleIncrementRad = (n <= 1) ? 0.0f : (Mathf.Deg2Rad * totalAngle) / (n - 1);

        _lastScan = new LaserScanData
        {
            frame_id = string.IsNullOrEmpty(frameId) ? gameObject.name : frameId,
            angle_min = angleMinRad,
            angle_max = angleMaxRad,
            angle_increment = angleIncrementRad,
            time_increment = 1.0f / Mathf.Max(1, measurementRateHz),
            scan_time = 1.0f / Mathf.Max(0.1f, scanRateHz),
            range_min = minRangeMeters,
            range_max = maxRangeMeters,
            ranges = (float[])ranges.Clone(),
            intensities = new float[n]
        };
        _hasScan = true;
    }

    /// <summary>
    /// Getter for LaserScan-like data without stamp. Returns false if no scan yet.
    /// </summary>
    public bool TryGetLaserScanData(out LaserScanData data)
    {
        data = _lastScan;
        return _hasScan;
    }

    private static float NormalizeAngleDeg(float deg)
    {
        deg %= 360.0f;
        if (deg < 0.0f) deg += 360.0f;
        return deg;
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
