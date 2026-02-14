using UnityEngine;

public class CmdDriver : MonoBehaviour
{
    public enum DriveAxis { X, Y, Z }

    [Header("Wheels (order: FL, FR, RL, RR)")]
    public ArticulationBody[] wheels;

    [Header("Input (from CmdVelReceiver)")]
    public CmdVelReceiver cmdVelReceiver;
    [Tooltip("If true, auto-subscribe to CmdVelReceiver in OnEnable().")]
    public bool subscribeReceiver = true;

    [Header("Kinematics (required)")]
    [Tooltip("Wheel radius R (meters).")]
    public float wheelRadius = 0.10f;

    [Tooltip("Wheel separation W (meters). Distance between left/right wheel contact centers.")]
    public float wheelSeparation = 0.40f;

    [Header("cmd_vel Mapping")]
    [Tooltip("Use linear.x as forward velocity (m/s).")]
    public bool useLinearX = true;

    [Tooltip("Use angular.z as yaw rate (rad/s).")]
    public bool useAngularZ = true;

    [Tooltip("If true, interpret cmd_vel as m/s & rad/s (ROS standard).")]
    public bool rosUnits = true;

    [Header("Limits (recommended)")]
    [Tooltip("Clamp wheel RPM to +/- maxRPM. Set <= 0 to disable.")]
    public float maxRPM = 0f;

    [Tooltip("Clamp input linear speed (m/s). Set <= 0 to disable.")]
    public float maxLinearSpeed = 0f;

    [Tooltip("Clamp input angular speed (rad/s). Set <= 0 to disable.")]
    public float maxAngularSpeed = 0f;

    [Header("Control")]
    [Tooltip("Which local axis of the wheel joint is the rotation axis (matches the revolute axis you set in the joint).")]
    public DriveAxis axis = DriveAxis.X;

    [Tooltip("Invert direction without flipping parts. If wheels spin opposite, toggle this.")]
    public bool invertDirection = false;

    [Tooltip("Invert ONLY left side (useful if left/right joint axes are opposite).")]
    public bool invertLeftOnly = false;

    [Tooltip("Invert ONLY right side (useful if left/right joint axes are opposite).")]
    public bool invertRightOnly = false;

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
    [SerializeField] private float[] currentRPMs;     // per-wheel measured RPM
    [SerializeField] private float cmdV;              // linear.x (m/s)
    [SerializeField] private float cmdW;              // angular.z (rad/s)
    [SerializeField] private float targetLeftRPM;     // computed
    [SerializeField] private float targetRightRPM;    // computed
    [SerializeField] private float targetLeftDegPerSec;
    [SerializeField] private float targetRightDegPerSec;

    private volatile bool _hasCmd = false;

    void Reset()
    {
        wheels = new[] { GetComponent<ArticulationBody>() };
    }

    void OnEnable()
    {
        if (!subscribeReceiver || cmdVelReceiver == null) return;
        cmdVelReceiver.OnCmdVel += OnCmdVel;
    }

    void OnDisable()
    {
        if (!subscribeReceiver || cmdVelReceiver == null) return;
        cmdVelReceiver.OnCmdVel -= OnCmdVel;
    }

    private void OnCmdVel(CmdVelReceiver.Twist t)
    {
        // ROS convention: ground vehicle uses v = linear.x, w = angular.z
        cmdV = useLinearX ? t.linear.x : 0f;
        cmdW = useAngularZ ? t.angular.z : 0f;

        // Optional clamps
        if (maxLinearSpeed > 0f)  cmdV = Mathf.Clamp(cmdV, -maxLinearSpeed,  maxLinearSpeed);
        if (maxAngularSpeed > 0f) cmdW = Mathf.Clamp(cmdW, -maxAngularSpeed, maxAngularSpeed);

        _hasCmd = true;
    }

    void FixedUpdate()
    {
        if (wheels == null || wheels.Length == 0) return;

        // Debug measured RPMs
        if (currentRPMs == null || currentRPMs.Length != wheels.Length)
            currentRPMs = new float[wheels.Length];

        for (int i = 0; i < wheels.Length; i++)
            currentRPMs[i] = (wheels[i] != null) ? GetWheelRPM(wheels[i]) : 0f;

        if (!enableMotor) return;
        if (!_hasCmd) return;

        // Validate kinematics parameters
        if (wheelRadius <= 0f || wheelSeparation <= 0f) return;

        // Differential drive mapping:
        // v_left  = v - w * W/2
        // v_right = v + w * W/2
        float vLeft  = cmdV - cmdW * (wheelSeparation * 0.5f);
        float vRight = cmdV + cmdW * (wheelSeparation * 0.5f);

        // wheel angular speed (rad/s)
        float wLeft  = vLeft  / wheelRadius;
        float wRight = vRight / wheelRadius;

        // rad/s -> RPM
        targetLeftRPM  = wLeft  * 60f / (2f * Mathf.PI);
        targetRightRPM = wRight * 60f / (2f * Mathf.PI);

        // Optional RPM clamp
        if (maxRPM > 0f)
        {
            targetLeftRPM  = Mathf.Clamp(targetLeftRPM,  -maxRPM, maxRPM);
            targetRightRPM = Mathf.Clamp(targetRightRPM, -maxRPM, maxRPM);
        }

        // Apply global invert
        if (invertDirection)
        {
            targetLeftRPM  = -targetLeftRPM;
            targetRightRPM = -targetRightRPM;
        }

        // Side-specific invert (useful when joint axis differs)
        float leftSign  = invertLeftOnly  ? -1f : 1f;
        float rightSign = invertRightOnly ? -1f : 1f;

        // RPM -> deg/s  (1 RPM = 360 deg / 60 s = 6 deg/s)
        targetLeftDegPerSec  = targetLeftRPM  * 6f * leftSign;
        targetRightDegPerSec = targetRightRPM * 6f * rightSign;

        // Apply to wheels:
        // FL(0) & RL(2) = left
        // FR(1) & RR(3) = right
        ApplyToWheelIndex(0, targetLeftDegPerSec);   // FL
        ApplyToWheelIndex(2, targetLeftDegPerSec);   // RL
        ApplyToWheelIndex(1, targetRightDegPerSec);  // FR
        ApplyToWheelIndex(3, targetRightDegPerSec);  // RR
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

        // NOTE: This is only for display; do not apply side-invert here, or you'll confuse debug.
        if (invertDirection) rpm = -rpm;
        return rpm;
    }

    public float[] CurrentRPMs => currentRPMs;
}
