using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TrailLineRendererObserver
/// Draws trajectory of a target Transform using LineRenderer.
/// You can observe any object by dragging it into the inspector.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
[DisallowMultipleComponent]
public class TrailLineRendererObserver : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Transform to observe and draw trajectory for")]
    public Transform target;

    [Header("Sampling")]
    [Tooltip("Add a new point when moved this distance (meters).")]
    public float distanceStep = 0.05f;

    [Tooltip("Ignore tiny movement (anti jitter).")]
    public float minDistanceEpsilon = 0.001f;

    [Header("Appearance")]
    [Tooltip("Trail line width (meters).")]
    public float trailWidth = 0.02f;

    [Header("Space")]
    [Tooltip("Use world space for trajectory (recommended).")]
    public bool useWorldSpace = true;

    [Header("Limits")]
    [Tooltip("Maximum number of points kept in the trail.")]
    public int maxPoints = 2000;

    [Header("Debug (read-only)")]
    [SerializeField] private int currentPointCount;

    private LineRenderer lr;
    private readonly List<Vector3> points = new();
    private Vector3 lastPos;
    private bool initialized = false;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = useWorldSpace;
        lr.startWidth = trailWidth;
        lr.endWidth = trailWidth;
    }

    void Start()
    {
        TryInit();
    }

    void FixedUpdate()
    {
        if (!initialized)
        {
            TryInit();
            return;
        }

        Vector3 pos = GetPos();
        float dist = Vector3.Distance(pos, lastPos);

        if (dist >= distanceStep && dist > minDistanceEpsilon)
        {
            AddPoint(pos);
            lastPos = pos;
        }
    }

    private void TryInit()
    {
        if (target == null) return;

        lastPos = GetPos();
        AddPoint(lastPos);
        initialized = true;
    }

    private Vector3 GetPos()
    {
        if (useWorldSpace)
            return target.position;
        else
            return target.localPosition;
    }

    private void AddPoint(Vector3 p)
    {
        points.Add(p);

        if (points.Count > maxPoints)
            points.RemoveAt(0);

        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());

        currentPointCount = points.Count;
    }

    [ContextMenu("Clear Trail")]
    public void ClearTrail()
    {
        points.Clear();
        lr.positionCount = 0;
        initialized = false;
    }
}
