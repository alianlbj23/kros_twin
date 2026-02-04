using UnityEngine;

/// <summary>
/// Simulation clock used as the unified time source for all simulated sensors.
///
/// Design goals:
/// 1. Time starts from 0 when the simulation begins (game start or reset).
/// 2. Time is strictly monotonic (never goes backward).
/// 3. Acts as a single source of truth for timestamps across all sensors
///    (LiDAR, camera, IMU, odometry, etc.).
///
/// This clock represents "simulation time", NOT wall-clock time.
/// It is conceptually equivalent to ROS /clock when use_sim_time is enabled.
///
/// Typical usage:
/// - Sensor data (e.g., LaserScan, Image, Imu) should use this clock
///   to populate header.stamp.
/// - The stamp corresponds to the time at which the measurement
///   is considered to have been taken in the simulated world.
///
/// Time scaling:
/// - If driven by Time.deltaTime, the clock follows Unity's Time.timeScale,
///   allowing the entire simulation (including sensor timestamps) to run
///   faster or slower while remaining temporally consistent.
/// - If driven by Time.unscaledDeltaTime, the clock follows real elapsed time
///   regardless of simulation speed.
///
/// Why this matters:
/// - SLAM, sensor fusion, and state estimation algorithms rely on
///   correct temporal relationships between measurements.
/// - Using a unified simulation clock prevents inconsistencies caused by
///   mixing wall-clock time, Unity frame time, and sensor-specific timers.
///
/// In short:
/// This clock defines "when" things happen in the simulated world,
/// independent of real-world time.
/// </summary>
public class SimClock : MonoBehaviour
{
    public static SimClock Instance { get; private set; }

    [Header("Clock Behavior")]
    [Tooltip("If true, clock is affected by Time.timeScale (pause/slow motion will stop or slow the clock).")]
    public bool useScaledTime = true;

    // Internal simulation time (seconds since start)
    private double simTimeSec = 0.0;

    /// <summary>
    /// Current simulation time in seconds since game start.
    /// This is the value sensors should use for header.stamp.
    /// </summary>
    public double Now => simTimeSec;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[SimClock] Multiple instances detected. Destroying duplicate.");
            Destroy(this);
            return;
        }

        Instance = this;
        simTimeSec = 0.0;
    }

    private void Update()
    {
        double delta =
            useScaledTime
                ? Time.deltaTime          // affected by Time.timeScale
                : Time.unscaledDeltaTime; // real elapsed time

        simTimeSec += delta;
    }

    /// <summary>
    /// Reset simulation time back to zero.
    /// Useful for restarting experiments.
    /// </summary>
    public void ResetClock()
    {
        simTimeSec = 0.0;
    }
}
