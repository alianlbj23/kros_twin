using UnityEngine;

public class ArticulationWheelRPMController : MonoBehaviour
{
    public enum DriveAxis { X, Y, Z }

    [Header("Wheels (order: FL, FR, RL, RR)")]
    public ArticulationBody[] wheels;

    [Header("Input (from CarCFrontWheelReceiver)")]
    [Tooltip("Receiver that provides float[] array. Expect at least 1 or 2 elements.")]
    public CarCFrontWheelReceiver receiver;

    [Tooltip("If true, auto-subscribe to receiver in OnEnable().")]
    public bool subscribeReceiver = true;

    [Header("Control")]
    [Tooltip("Which local axis of the wheel joint is the rotation axis (matches the revolute axis you set in the joint).")]
    public DriveAxis axis = DriveAxis.X;

    [Tooltip("Invert direction without flipping parts. If your wheel spins opposite, toggle this.")]
    public bool invertDirection = false;

    [Tooltip("Apply targetRPM continuously each FixedUpdate.")]
    public bool enableMotor = true;

    [Header("Drive Settings")]
    [Tooltip("Max torque/force the drive can apply. Increase if wheel can't reach RPM.")]
    public float forceLimit = 1000f;

    [Tooltip("Damping for the drive. Increase if wheel oscillates.")]
    public float damping = 50f;

    [Tooltip("Stiffness for the drive. Usually keep 0 for velocity control style.")]
    public float stiffness = 0f;

    [Header("Debug (read-only)")]
    [SerializeField] private float[] currentRPMs; // per-wheel RPMs
    [SerializeField] private float targetFrontRPM; // from array[0]
    [SerializeField] private float targetRearRPM;  // from array[1]

    // Updated from receiver (thread-safe enough if receiver updates in Update() and this uses FixedUpdate)
    private volatile bool _hasTargets = false;

    void Reset()
    {
        wheels = new[] { GetComponent<ArticulationBody>() };
    }

    void OnEnable()
    {
        if (!subscribeReceiver || receiver == null) return;
        receiver.OnCarCArray += OnCarCArray;
    }

    void OnDisable()
    {
        if (!subscribeReceiver || receiver == null) return;
        receiver.OnCarCArray -= OnCarCArray;
    }

    private void OnCarCArray(float[] v)
    {
        if (v == null || v.Length == 0) return;

        // Only read first 2 elements
        targetFrontRPM = v[0];

        if (v.Length >= 2)
        {
            targetRearRPM = v[1];
        }

        // If v.Length == 1 -> keep previous targetRearRPM
        _hasTargets = true;
    }

    void FixedUpdate()
    {
        if (wheels == null || wheels.Length == 0) return;

        // Read RPMs for debug (one per wheel)
        if (currentRPMs == null || currentRPMs.Length != wheels.Length)
            currentRPMs = new float[wheels.Length];

        for (int i = 0; i < wheels.Length; i++)
            currentRPMs[i] = (wheels[i] != null) ? GetWheelRPM(wheels[i]) : 0f;

        if (!enableMotor) return;

        float dir = invertDirection ? -1f : 1f;

        // Decide targets (receiver-provided)
        float frontRPM = targetFrontRPM;
        float rearRPM  = targetRearRPM;

        // RPM -> deg/s
        float frontDegPerSec = frontRPM * 6f * dir;
        float rearDegPerSec  = rearRPM  * 6f * dir;

        // Apply:
        // wheels[0], wheels[1] = front two
        // wheels[2], wheels[3] = rear two
        ApplyToWheelIndex(0, frontDegPerSec);
        ApplyToWheelIndex(1, frontDegPerSec);
        ApplyToWheelIndex(2, rearDegPerSec);
        ApplyToWheelIndex(3, rearDegPerSec);
    }

    private void ApplyToWheelIndex(int idx, float targetDegPerSec)
    {
        if (wheels == null) return;
        if (idx < 0 || idx >= wheels.Length) return;

        var wheel = wheels[idx];
        if (wheel == null) return;

        ApplyTargetVelocity(wheel, targetDegPerSec);
    }

    private void ApplyTargetVelocity(ArticulationBody wheel, float targetDegPerSec)
    {
        switch (axis)
        {
            case DriveAxis.X:
            {
                var d = wheel.xDrive;
                d.stiffness = stiffness;
                d.damping = damping;
                d.forceLimit = forceLimit;
                d.targetVelocity = targetDegPerSec;
                wheel.xDrive = d;
                break;
            }
            case DriveAxis.Y:
            {
                var d = wheel.yDrive;
                d.stiffness = stiffness;
                d.damping = damping;
                d.forceLimit = forceLimit;
                d.targetVelocity = targetDegPerSec;
                wheel.yDrive = d;
                break;
            }
            case DriveAxis.Z:
            {
                var d = wheel.zDrive;
                d.stiffness = stiffness;
                d.damping = damping;
                d.forceLimit = forceLimit;
                d.targetVelocity = targetDegPerSec;
                wheel.zDrive = d;
                break;
            }
        }
    }

    private float GetWheelRPM(ArticulationBody wheel)
    {
        Vector3 axisWorld = axis switch
        {
            DriveAxis.X => wheel.transform.right,
            DriveAxis.Y => wheel.transform.up,
            DriveAxis.Z => wheel.transform.forward,
            _ => wheel.transform.right
        };

        float omega = Vector3.Dot(wheel.angularVelocity, axisWorld); // rad/s
        float rpm = omega * 60f / (2f * Mathf.PI);

        if (invertDirection) rpm = -rpm;
        return rpm;
    }

    public float[] CurrentRPMs => currentRPMs;
}
