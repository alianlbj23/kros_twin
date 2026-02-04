using System;
using UnityEngine;

#if USING_URP
using UnityEngine.Rendering.Universal;
#endif

/// <summary>
/// Attach this to a parent Camera.
/// All child Cameras will be kept in sync with the parent Camera settings.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class CameraChildSync : MonoBehaviour
{
    public enum ChildSearchMode
    {
        DirectChildrenOnly,
        IncludeAllDescendants
    }

    [Header("Search")]
    [SerializeField] private ChildSearchMode searchMode = ChildSearchMode.IncludeAllDescendants;

    [Header("When to sync")]
    [SerializeField] private bool syncOnEnable = true;
    [SerializeField] private bool syncOnValidate = true;      // Editor changes
    [SerializeField] private bool syncEveryFrame = false;     // Runtime continuous
    [SerializeField] private float syncIntervalSeconds = 0f;  // 0 = every frame when syncEveryFrame is true

    [Header("What to sync")]
    [SerializeField] private bool syncTransform = false;      // Usually false for multi-sensor rigs
    [SerializeField] private bool syncCameraBasics = true;
    [SerializeField] private bool syncPhysicalCamera = true;
    [SerializeField] private bool syncOutput = true;
    [SerializeField] private bool syncCulling = true;

    [Tooltip("If true, child cameras will also copy parent targetTexture. " +
             "For multi-sensor rigs, you often want different RenderTextures per child, so keep this false.")]
    [SerializeField] private bool copyTargetTexture = false;

#if USING_URP
    [Header("URP (optional)")]
    [SerializeField] private bool syncUrpAdditionalData = true;

    [Tooltip("Not recommended for sensor cameras. Stack can contaminate output.")]
    [SerializeField] private bool copyUrpCameraStack = false;
#endif

    private Camera _parentCam;
    private float _nextSyncTime;

    private void Awake()
    {
        _parentCam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        if (_parentCam == null) _parentCam = GetComponent<Camera>();
        if (syncOnEnable) SyncNow();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!syncOnValidate) return;
        if (_parentCam == null) _parentCam = GetComponent<Camera>();
        // Avoid syncing in play mode if user doesn't want it.
        if (!Application.isPlaying) SyncNow();
    }
#endif

    private void Update()
    {
        if (!syncEveryFrame) return;

        if (syncIntervalSeconds <= 0f)
        {
            SyncNow();
            return;
        }

        if (Time.unscaledTime >= _nextSyncTime)
        {
            _nextSyncTime = Time.unscaledTime + syncIntervalSeconds;
            SyncNow();
        }
    }

    [ContextMenu("Sync Now")]
    public void SyncNow()
    {
        if (_parentCam == null) return;

        var children = GetChildCameras();
        foreach (var childCam in children)
        {
            if (childCam == null) continue;
            if (childCam == _parentCam) continue;

            if (syncTransform)
            {
                childCam.transform.position = _parentCam.transform.position;
                childCam.transform.rotation = _parentCam.transform.rotation;
                childCam.transform.localScale = _parentCam.transform.localScale;
            }

            if (syncCameraBasics) CopyCameraBasics(_parentCam, childCam);
            if (syncCulling) CopyCulling(_parentCam, childCam);
            if (syncOutput) CopyOutput(_parentCam, childCam);
            if (syncPhysicalCamera) CopyPhysicalCamera(_parentCam, childCam);

#if USING_URP
            if (syncUrpAdditionalData) CopyUrpAdditional(_parentCam, childCam);
#endif
        }
    }

    private Camera[] GetChildCameras()
    {
        if (searchMode == ChildSearchMode.DirectChildrenOnly)
        {
            // Direct children only
            Transform t = transform;
            var cams = new System.Collections.Generic.List<Camera>();
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i).GetComponent<Camera>();
                if (c != null) cams.Add(c);
            }
            return cams.ToArray();
        }

        // Include all descendants
        return GetComponentsInChildren<Camera>(includeInactive: true);
    }

    private static void CopyCameraBasics(Camera src, Camera dst)
    {
        // Projection & frustum
        dst.orthographic = src.orthographic;
        dst.orthographicSize = src.orthographicSize;
        dst.fieldOfView = src.fieldOfView;
        dst.focalLength = src.focalLength; // meaningful when physical camera is enabled
        dst.usePhysicalProperties = src.usePhysicalProperties;

        // Viewport & ordering
        dst.rect = src.rect;
        dst.depth = src.depth;

        // Clear & background
        dst.clearFlags = src.clearFlags;
        dst.backgroundColor = src.backgroundColor;

        // Misc
        dst.allowMSAA = src.allowMSAA;
        dst.allowHDR = src.allowHDR;
        dst.renderingPath = src.renderingPath;
        dst.stereoTargetEye = src.stereoTargetEye;
    }

    private void CopyOutput(Camera src, Camera dst)
    {
        dst.nearClipPlane = src.nearClipPlane;
        dst.farClipPlane = src.farClipPlane;

        // If you are using sensor cameras, you usually want separate RenderTextures per camera.
        if (copyTargetTexture)
        {
            dst.targetTexture = src.targetTexture;
        }
    }

    private static void CopyCulling(Camera src, Camera dst)
    {
        dst.cullingMask = src.cullingMask;
        dst.eventMask = src.eventMask;
        dst.layerCullSpherical = src.layerCullSpherical;

        dst.useOcclusionCulling = src.useOcclusionCulling;

        // Note: per-layer distances are global in QualitySettings in most pipelines,
        // but camera has layerCullDistances.
        dst.layerCullDistances = src.layerCullDistances;
    }

    private static void CopyPhysicalCamera(Camera src, Camera dst)
    {
        // Only relevant when physical camera is enabled
        dst.usePhysicalProperties = src.usePhysicalProperties;

        dst.sensorSize = src.sensorSize;
        dst.lensShift = src.lensShift;
        dst.gateFit = src.gateFit;

        dst.focalLength = src.focalLength;
        dst.aperture = src.aperture;
        dst.focusDistance = src.focusDistance;

        dst.bladeCount = src.bladeCount;
        dst.curvature = src.curvature;
        dst.barrelClipping = src.barrelClipping;
        dst.anamorphism = src.anamorphism;
    }

#if USING_URP
    private void CopyUrpAdditional(Camera src, Camera dst)
    {
        var srcData = src.GetComponent<UniversalAdditionalCameraData>();
        var dstData = dst.GetComponent<UniversalAdditionalCameraData>();

        if (srcData == null || dstData == null) return;

        dstData.renderType = srcData.renderType;
        dstData.renderShadows = srcData.renderShadows;
        dstData.requiresColorTexture = srcData.requiresColorTexture;
        dstData.requiresDepthTexture = srcData.requiresDepthTexture;

        dstData.stopNaN = srcData.stopNaN;
        dstData.dithering = srcData.dithering;
        dstData.clearDepth = srcData.clearDepth;

        // Post-processing should usually be OFF for sensor cameras
        dstData.renderPostProcessing = srcData.renderPostProcessing;

        if (copyUrpCameraStack)
        {
            // Copy stack references (not recommended for sensor cameras).
            // Note: overlay cameras are references; be careful with shared usage.
            dstData.cameraStack.Clear();
            foreach (var cam in srcData.cameraStack)
            {
                if (cam != null) dstData.cameraStack.Add(cam);
            }
        }
    }
#endif
}
