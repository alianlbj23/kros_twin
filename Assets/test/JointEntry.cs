using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JointEntry drives multiple ArticulationBody joints using position control.
/// All joints share the same stiffness, damping, and force limit.
/// </summary>
[DisallowMultipleComponent]
public class JointEntry : MonoBehaviour
{
    [Serializable]
    public class Joint
    {
        [Header("Joint Reference")]
        [Tooltip("ArticulationBody to be controlled")]
        public ArticulationBody articulation;

        [Header("Target Control")]
        [Tooltip("Target joint angle in degrees")]
        public float targetAngleDeg = 0f;

        [Header("Drive Axis")]
        [Tooltip("Which drive axis is used by this joint")]
        public DriveAxis driveAxis = DriveAxis.X;

        [Header("Runtime Info (Read Only)")]
        [SerializeField]
        private float currentAngleDeg;

        public float CurrentAngleDeg => currentAngleDeg;

        /// <summary>
        /// Update current joint angle from jointPosition (radians to degrees).
        /// </summary>
        public void UpdateCurrentAngle()
        {
            if (articulation == null) return;
            if (articulation.jointPosition.dofCount == 0) return;

            currentAngleDeg = articulation.jointPosition[0] * Mathf.Rad2Deg;
        }
    }

    public enum DriveAxis
    {
        X,
        Y,
        Z
    }

    [Header("Controlled Joints")]
    [Tooltip("List of joints to be driven")]
    public List<Joint> joints = new List<Joint>();

    [Header("Global Drive Parameters")]
    [Tooltip("Joint stiffness for position control")]
    public float stiffness = 10000f;

    [Tooltip("Joint damping")]
    public float damping = 100f;

    [Tooltip("Maximum force or torque applied by the joint")]
    public float forceLimit = 1000f;

    [Header("Motion Settings")]
    [Tooltip("If greater than zero, joints move gradually toward target angles")]
    public float speedDegPerSec = 0f;

    private void FixedUpdate()
    {
        DriveAllJoints(Time.fixedDeltaTime);
    }

    /// <summary>
    /// Drive all joints toward their target angles.
    /// </summary>
    private void DriveAllJoints(float deltaTime)
    {
        foreach (var joint in joints)
        {
            if (joint == null) continue;
            if (joint.articulation == null) continue;

            joint.UpdateCurrentAngle();

            float target = joint.targetAngleDeg;

            if (speedDegPerSec > 0f)
            {
                target = Mathf.MoveTowardsAngle(
                    joint.CurrentAngleDeg,
                    joint.targetAngleDeg,
                    speedDegPerSec * deltaTime
                );
            }

            ApplyDrive(joint.articulation, joint.driveAxis, target);
        }
    }

    /// <summary>
    /// Apply drive parameters and target angle to the specified articulation joint.
    /// </summary>
    private void ApplyDrive(
        ArticulationBody articulation,
        DriveAxis axis,
        float targetAngleDeg
    )
    {
        ArticulationDrive drive;

        switch (axis)
        {
            case DriveAxis.Y:
                drive = articulation.yDrive;
                break;
            case DriveAxis.Z:
                drive = articulation.zDrive;
                break;
            default:
                drive = articulation.xDrive;
                break;
        }

        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        drive.target = targetAngleDeg;

        switch (axis)
        {
            case DriveAxis.Y:
                articulation.yDrive = drive;
                break;
            case DriveAxis.Z:
                articulation.zDrive = drive;
                break;
            default:
                articulation.xDrive = drive;
                break;
        }
    }

    /// <summary>
    /// Read current joint angles and assign them as target angles.
    /// Useful for calibration and zeroing.
    /// </summary>
    [ContextMenu("Read Current Angles As Target")]
    public void ReadCurrentAnglesAsTarget()
    {
        foreach (var joint in joints)
        {
            if (joint == null) continue;
            if (joint.articulation == null) continue;

            joint.UpdateCurrentAngle();
            joint.targetAngleDeg = joint.CurrentAngleDeg;
        }
    }
}
