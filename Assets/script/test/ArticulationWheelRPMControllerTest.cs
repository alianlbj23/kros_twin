using UnityEngine;

public class ArticulationWheelRPMControllerTest : MonoBehaviour
{
    public enum DriveAxis { X, Y, Z }

    [Header("Wheels (drag ArticulationBody here)")]
    public ArticulationBody[] wheels;

    [Header("Control")]
    [Tooltip("Target wheel speed in RPM. Positive/negative depends on Axis & Invert Direction.")]
    public float targetRPM = 60f;

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

    void Reset()
    {
        wheels = new[] { GetComponent<ArticulationBody>() };
    }

    void FixedUpdate()
    {
        if (wheels == null || wheels.Length == 0) return;

        // Read RPMs for debug (one per wheel)
        if (currentRPMs == null || currentRPMs.Length != wheels.Length)
        {
            currentRPMs = new float[wheels.Length];
        }

        for (int i = 0; i < wheels.Length; i++)
        {
            currentRPMs[i] = (wheels[i] != null) ? GetWheelRPM(wheels[i]) : 0f;
        }

        if (!enableMotor) return;

        float dir = invertDirection ? -1f : 1f;
        float targetDegPerSec = targetRPM * 6f * dir; // RPM -> deg/s

        foreach (var wheel in wheels)
        {
            if (wheel == null) continue;

            // Make sure it's a revolute joint (recommended)
            // wheel.jointType = ArticulationJointType.RevoluteJoint; // 可視需求打開

            ApplyTargetVelocity(wheel, targetDegPerSec);
        }
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

    /// <summary>
    /// Estimate wheel RPM from angularVelocity projected onto the chosen axis.
    /// Keeps sign (direction).
    /// </summary>
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

    // Optional: call this from UI slider
    public void SetRPM(float rpm) => targetRPM = rpm;

    public float[] CurrentRPMs => currentRPMs;
}
