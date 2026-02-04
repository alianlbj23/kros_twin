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

        [Header("Target Control (User)")]
        [Tooltip("Target joint angle in degrees (user zeroed)")]
        public float targetAngleDeg = 0f;

        [Header("Drive Axis")]
        [Tooltip("Which drive axis is used by this joint")]
        public DriveAxis driveAxis = DriveAxis.X;

        [Header("Zeroing")]
        [Tooltip("Offset added to user target before sending to drive.target")]
        public float zeroOffsetDeg = 0f;

        [Header("Runtime Info (Read Only)")]
        [SerializeField] private float currentRawDeg;
        [SerializeField] private float currentUserDeg;

        public float CurrentRawDeg => currentRawDeg;
        public float CurrentUserDeg => currentUserDeg;

        /// <summary>
        /// Update current joint angle from jointPosition (radians to degrees).
        /// Uses axis to pick the correct DOF when possible.
        /// </summary>
        public void UpdateCurrentAngle()
        {
            if (articulation == null) return;

            int dof = articulation.jointPosition.dofCount;
            if (dof <= 0) return;

            // Best-effort mapping:
            // - Many revolute joints are 1-DOF => index 0 is correct.
            // - If multi-DOF, axis->index mapping is not guaranteed by Unity,
            //   but this heuristic is still better than always 0.
            int index = 0;
            if (dof >= 3)
            {
                index = (int)driveAxis; // X=0,Y=1,Z=2 (heuristic)
            }
            else if (dof == 2)
            {
                index = Mathf.Clamp((int)driveAxis, 0, 1);
            }

            currentRawDeg = articulation.jointPosition[index] * Mathf.Rad2Deg;
            currentUserDeg = currentRawDeg - zeroOffsetDeg;
        }

        /// <summary>
        /// Convert user target into drive target by applying zero offset.
        /// </summary>
        public float GetDriveTargetDeg()
        {
            return targetAngleDeg + zeroOffsetDeg;
        }

        /// <summary>
        /// Set current pose as user zero (i.e., make currentUserDeg become 0).
        /// </summary>
        public void ZeroHere()
        {
            // After UpdateCurrentAngle:
            // want currentUserDeg = 0 => zeroOffsetDeg = currentRawDeg
            zeroOffsetDeg = currentRawDeg;
        }
    }

    public enum DriveAxis { X, Y, Z }

    [Header("Controlled Joints")]
    public List<Joint> joints = new List<Joint>();

    [Header("Global Drive Parameters")]
    public float stiffness = 10000f;
    public float damping = 100f;
    public float forceLimit = 1000f;

    [Header("Motion Settings")]
    [Tooltip("If greater than zero, joints move gradually toward target angles")]
    public float speedDegPerSec = 0f;

    private void FixedUpdate()
    {
        DriveAllJoints(Time.fixedDeltaTime);
    }

    private void DriveAllJoints(float deltaTime)
    {
        foreach (var joint in joints)
        {
            if (joint == null) continue;
            if (joint.articulation == null) continue;

            joint.UpdateCurrentAngle();

            float userTarget = joint.targetAngleDeg;

            if (speedDegPerSec > 0f)
            {
                userTarget = Mathf.MoveTowardsAngle(
                    joint.CurrentUserDeg,
                    joint.targetAngleDeg,
                    speedDegPerSec * deltaTime
                );
            }

            float driveTarget = userTarget + joint.zeroOffsetDeg;
            ApplyDrive(joint.articulation, joint.driveAxis, driveTarget);
        }
    }

    private void ApplyDrive(ArticulationBody articulation, DriveAxis axis, float targetAngleDeg)
    {
        ArticulationDrive drive = axis switch
        {
            DriveAxis.Y => articulation.yDrive,
            DriveAxis.Z => articulation.zDrive,
            _ => articulation.xDrive
        };

        drive.stiffness = stiffness;
        drive.damping = damping;
        drive.forceLimit = forceLimit;
        drive.target = targetAngleDeg;

        switch (axis)
        {
            case DriveAxis.Y: articulation.yDrive = drive; break;
            case DriveAxis.Z: articulation.zDrive = drive; break;
            default: articulation.xDrive = drive; break;
        }
    }

    /// <summary>
    /// Read current pose and set it as "user zero" (targetAngleDeg becomes 0 at this pose).
    /// </summary>
    [ContextMenu("Zero All Joints Here (Make Current Pose = 0)")]
    public void ZeroAllJointsHere()
    {
        foreach (var joint in joints)
        {
            if (joint == null) continue;
            if (joint.articulation == null) continue;

            joint.UpdateCurrentAngle();
            joint.ZeroHere();
            joint.targetAngleDeg = 0f;
        }
    }

    /// <summary>
    /// Read current user angles and assign them as user targets (keeps pose).
    /// </summary>
    [ContextMenu("Read Current User Angles As Target")]
    public void ReadCurrentUserAnglesAsTarget()
    {
        foreach (var joint in joints)
        {
            if (joint == null) continue;
            if (joint.articulation == null) continue;

            joint.UpdateCurrentAngle();
            joint.targetAngleDeg = joint.CurrentUserDeg;
        }
    }
}
