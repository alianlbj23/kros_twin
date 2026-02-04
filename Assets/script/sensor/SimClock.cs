using UnityEngine;

/// <summary>
/// Simulation clock for sensor timestamps.
/// - Time starts from 0 at game start
/// - Monotonic increasing
/// - Single source of truth for all sensors
///
/// Use this clock to generate ROS header.stamp equivalents.
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
