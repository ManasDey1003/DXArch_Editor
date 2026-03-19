using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class FrustumCullingManager : MonoBehaviour
{
    public static FrustumCullingManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] ComputeShader _computeShader;

    [Header("Settings")]
    [SerializeField] bool _enabled = true;
    [SerializeField] float _updateInterval = 0.05f;
    [SerializeField] bool _debug = false;

    [Header("Dynamic Bounds")]
    [SerializeField] bool _updateBoundsEveryFrame = true;
    [Tooltip("Only update bounds when objects have moved (optimization)")]
    [SerializeField] bool _onlyUpdateIfMoved = false;

    [Header("Angular Size Culling")]
    [SerializeField] bool _enableAngularSizeCulling = true;
    [Tooltip("Minimum screen-space area in pixels to render an object")]
    [SerializeField] float _minPixelArea = 5f;
    [Tooltip("Use fast approximation (sphere) vs accurate bbox projection")]
    [SerializeField] bool _useFastApproximation = true;
    [Tooltip("Exclude objects on these layers from angular size culling")]
    [SerializeField] LayerMask _angularCullExcludeLayers = 0;

    private Camera _cam;
    private List<MeshRenderer> _renderers = new();
    private HashSet<MeshRenderer> _frustumVisibleSet = new();
    private int _count = 0;
    private bool _ready = false;
    private bool _pending = false;
    private float _lastUpdate = 0f;
    private int _kernel;
    private int _visible, _culled, _angularCulled;

    private GraphicsBuffer _aabbMinBuf;
    private GraphicsBuffer _aabbMaxBuf;
    private GraphicsBuffer _visibilityBuf;

    private Vector3[] _aabbMin;
    private Vector3[] _aabbMax;

    // Track previous positions for movement detection
    private Vector3[] _lastPositions;
    private Quaternion[] _lastRotations;
    private Vector3[] _lastScales;

    // Cache for angular size calculations
    private float _cachedFOV;
    private float _cachedScreenHeight;
    private Matrix4x4 _cachedViewProjection;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnEnable() => RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    void OnDisable() { RenderPipelineManager.endCameraRendering -= OnEndCameraRendering; ReleaseBuffers(); }

    public void RegisterRenderers(List<MeshRenderer> renderers)
    {
        if (renderers == null || renderers.Count == 0) { Debug.LogWarning("[FrustumCulling] No renderers."); return; }
        if (_computeShader == null) { Debug.LogError("[FrustumCulling] No compute shader."); return; }

        _cam = Camera.main;
        if (_cam == null) { Debug.LogError("[FrustumCulling] No main camera."); return; }

        _renderers = renderers;
        _count = renderers.Count;
        _kernel = _computeShader.FindKernel("CSMain");

        if (_kernel < 0) { Debug.LogError("[FrustumCulling] CSMain kernel not found."); return; }

        InitializeArrays();
        BakeAABBs();
        AllocateBuffers();
        _ready = true;

        Debug.Log($"[FrustumCulling] Registered {_count} renderers. Kernel={_kernel}");
    }

    public bool IsFrustumVisible(MeshRenderer renderer)
    {
        return _frustumVisibleSet.Contains(renderer);
    }

    public HashSet<MeshRenderer> GetFrustumVisibleRenderers()
    {
        return _frustumVisibleSet;
    }

    private void InitializeArrays()
    {
        _aabbMin = new Vector3[_count];
        _aabbMax = new Vector3[_count];

        if (_onlyUpdateIfMoved)
        {
            _lastPositions = new Vector3[_count];
            _lastRotations = new Quaternion[_count];
            _lastScales = new Vector3[_count];
        }
    }

    private void BakeAABBs()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_renderers[i] == null) continue;

            Bounds b = _renderers[i].bounds;
            _aabbMin[i] = b.min;
            _aabbMax[i] = b.max;

            if (_onlyUpdateIfMoved)
            {
                Transform t = _renderers[i].transform;
                _lastPositions[i] = t.position;
                _lastRotations[i] = t.rotation;
                _lastScales[i] = t.lossyScale;
            }
        }
    }

    private void UpdateBounds()
    {
        bool anyUpdated = false;

        for (int i = 0; i < _count; i++)
        {
            if (_renderers[i] == null) continue;

            // Check if object has moved (optimization)
            if (_onlyUpdateIfMoved)
            {
                Transform t = _renderers[i].transform;
                bool hasMoved = t.position != _lastPositions[i] ||
                               t.rotation != _lastRotations[i] ||
                               t.lossyScale != _lastScales[i];

                if (!hasMoved) continue;

                _lastPositions[i] = t.position;
                _lastRotations[i] = t.rotation;
                _lastScales[i] = t.lossyScale;
            }

            // Update bounds
            Bounds b = _renderers[i].bounds;
            _aabbMin[i] = b.min;
            _aabbMax[i] = b.max;
            anyUpdated = true;
        }

        // Upload to GPU if any bounds changed
        if (anyUpdated)
        {
            _aabbMinBuf.SetData(_aabbMin);
            _aabbMaxBuf.SetData(_aabbMax);
        }
    }

    private void AllocateBuffers()
    {
        ReleaseBuffers();
        _aabbMinBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, sizeof(float) * 3);
        _aabbMaxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, sizeof(float) * 3);
        _visibilityBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, sizeof(int));
        _aabbMinBuf.SetData(_aabbMin);
        _aabbMaxBuf.SetData(_aabbMax);
    }

    private void ReleaseBuffers()
    {
        _aabbMinBuf?.Release(); _aabbMinBuf = null;
        _aabbMaxBuf?.Release(); _aabbMaxBuf = null;
        _visibilityBuf?.Release(); _visibilityBuf = null;
        _ready = false;
    }

    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (!_enabled || !_ready || _pending) return;
        if (cam != _cam) return;
        if (cam.cameraType != CameraType.Game) return;
        if (Time.time - _lastUpdate < _updateInterval) return;
        _lastUpdate = Time.time;

        // Update bounds if enabled
        if (_updateBoundsEveryFrame)
        {
            UpdateBounds();
        }

        // Cache camera properties for angular size calculations
        if (_enableAngularSizeCulling)
        {
            _cachedFOV = _cam.fieldOfView * Mathf.Deg2Rad;
            _cachedScreenHeight = Screen.height;
            _cachedViewProjection = _cam.projectionMatrix * _cam.worldToCameraMatrix;
        }

        Dispatch();
    }

    private void Dispatch()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_cam);
        Vector4[] packed = new Vector4[6];
        for (int i = 0; i < 6; i++)
            packed[i] = new Vector4(planes[i].normal.x, planes[i].normal.y,
                                    planes[i].normal.z, planes[i].distance);

        _computeShader.SetVectorArray("_FrustumPlanes", packed);
        _computeShader.SetInt("_Count", _count);

        _computeShader.SetBuffer(_kernel, "_AABBMin", _aabbMinBuf);
        _computeShader.SetBuffer(_kernel, "_AABBMax", _aabbMaxBuf);
        _computeShader.SetBuffer(_kernel, "_Visibility", _visibilityBuf);

        int groups = Mathf.CeilToInt(_count / 64f);
        _computeShader.Dispatch(_kernel, groups, 1, 1);

        _pending = true;
        AsyncGPUReadback.Request(_visibilityBuf, OnReadback);
    }

    private void OnReadback(AsyncGPUReadbackRequest req)
    {
        _pending = false;
        if (req.hasError) { Debug.LogWarning("[FrustumCulling] Readback error."); return; }

        NativeArray<int> results = req.GetData<int>();
        _visible = 0;
        _culled = 0;
        _angularCulled = 0;
        _frustumVisibleSet.Clear();

        Vector3 camPos = _cam.transform.position;

        for (int i = 0; i < _count; i++)
        {
            if (i >= _renderers.Count || _renderers[i] == null) continue;

            bool frustumVisible = results[i] == 1;

            if (!frustumVisible)
            {
                _renderers[i].enabled = false;
                _culled++;
                continue;
            }

            // Angular size culling
            bool angularVisible = true;
            if (_enableAngularSizeCulling)
            {
                // Check if this object should be excluded from angular culling
                int objectLayer = _renderers[i].gameObject.layer;
                bool isExcluded = (_angularCullExcludeLayers.value & (1 << objectLayer)) != 0;

                if (!isExcluded)
                {
                    float screenArea = CalculateScreenSpaceArea(_renderers[i].bounds, camPos);
                    angularVisible = screenArea >= _minPixelArea;

                    if (!angularVisible)
                    {
                        _angularCulled++;
                    }
                }
            }
            bool occlusionActive = false;
            // NOTE: You may need to ensure your OcclusionCullingManager has a public property 
            // or just check if it's active in the hierarchy.
            if (OcclusionCullingManager.Instance != null && OcclusionCullingManager.Instance.gameObject.activeInHierarchy)
            {
                occlusionActive = true;
            }
            if (frustumVisible && angularVisible)
            {
                _frustumVisibleSet.Add(_renderers[i]);
                _visible++;

                // ONLY enable the renderer if Occlusion culling is NOT running
                // If occlusion is running, we leave it to OcclusionCullingManager to turn it on
                if (!occlusionActive)
                {
                    _renderers[i].enabled = true;
                }
            }
            else
            {
                // Frustum can always safely turn things OFF if they are behind the camera
                _renderers[i].enabled = false;
                _culled++; // Or angular culled
            }


        }

        if (_debug)
            Debug.Log($"[FrustumCulling] Visible:{_visible}  FrustumCulled:{_culled}  AngularCulled:{_angularCulled}");
    }

    /// <summary>
    /// Calculate screen-space area in pixels
    /// </summary>
    private float CalculateScreenSpaceArea(Bounds bounds, Vector3 camPos)
    {
        if (_useFastApproximation)
        {
            return CalculateScreenSpaceAreaFast(bounds, camPos);
        }
        else
        {
            return CalculateScreenSpaceAreaAccurate(bounds, camPos);
        }
    }

    /// <summary>
    /// Fast approximation using bounding sphere
    /// </summary>
    private float CalculateScreenSpaceAreaFast(Bounds bounds, Vector3 camPos)
    {
        Vector3 center = bounds.center;
        float radius = bounds.extents.magnitude;
        float distance = Vector3.Distance(camPos, center);

        // Avoid division by zero
        if (distance < 0.01f) return float.MaxValue;

        // Calculate screen-space radius using perspective projection
        // radius_screen = (radius_world * screen_height) / (2 * distance * tan(fov/2))
        float screenRadius = (radius * _cachedScreenHeight) / (2f * distance * Mathf.Tan(_cachedFOV / 2f));

        // Return area (π * r²)
        return screenRadius * screenRadius * Mathf.PI;
    }

    /// <summary>
    /// Accurate calculation using bounding box projection
    /// </summary>
    private float CalculateScreenSpaceAreaAccurate(Bounds bounds, Vector3 camPos)
    {
        // Check if camera is inside bounds - if so, object is definitely visible
        if (bounds.Contains(camPos))
        {
            return float.MaxValue;
        }

        // Get 8 corners of bounding box
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] corners = new Vector3[8];
        corners[0] = center + new Vector3(-extents.x, -extents.y, -extents.z);
        corners[1] = center + new Vector3(-extents.x, -extents.y, extents.z);
        corners[2] = center + new Vector3(-extents.x, extents.y, -extents.z);
        corners[3] = center + new Vector3(-extents.x, extents.y, extents.z);
        corners[4] = center + new Vector3(extents.x, -extents.y, -extents.z);
        corners[5] = center + new Vector3(extents.x, -extents.y, extents.z);
        corners[6] = center + new Vector3(extents.x, extents.y, -extents.z);
        corners[7] = center + new Vector3(extents.x, extents.y, extents.z);

        // Project to screen space
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        int cornersInFront = 0;

        for (int i = 0; i < 8; i++)
        {
            Vector4 clipPos = _cachedViewProjection * new Vector4(corners[i].x, corners[i].y, corners[i].z, 1f);

            // Check if behind camera
            if (clipPos.w <= 0) continue;

            cornersInFront++;

            // Convert to NDC
            Vector3 ndc = new Vector3(clipPos.x / clipPos.w, clipPos.y / clipPos.w, clipPos.z / clipPos.w);

            // Convert to screen space
            Vector2 screenPos = new Vector2(
                (ndc.x + 1f) * 0.5f * Screen.width,
                (ndc.y + 1f) * 0.5f * Screen.height
            );

            min.x = Mathf.Min(min.x, screenPos.x);
            min.y = Mathf.Min(min.y, screenPos.y);
            max.x = Mathf.Max(max.x, screenPos.x);
            max.y = Mathf.Max(max.y, screenPos.y);
        }

        // If no corners in front but frustum test passed, object likely wraps around camera
        // This happens with large objects like ground planes
        if (cornersInFront == 0)
        {
            return float.MaxValue; // Assume large on screen
        }

        // Calculate area
        float width = max.x - min.x;
        float height = max.y - min.y;

        return width * height;
    }

    void OnDrawGizmosSelected()
    {
        if (!_debug || _renderers == null || _cam == null) return;

        Vector3 camPos = _cam.transform.position;

        // Draw bounding boxes
        foreach (var mr in _renderers)
        {
            if (mr == null) continue;

            bool isExcluded = (_angularCullExcludeLayers.value & (1 << mr.gameObject.layer)) != 0;

            if (mr.enabled)
            {
                // Green for visible, yellow for excluded from angular culling
                Gizmos.color = isExcluded ? Color.yellow : Color.green;
                Gizmos.DrawWireCube(mr.bounds.center, mr.bounds.size);

                // Draw screen size info
                if (_enableAngularSizeCulling && !isExcluded)
                {
                    float screenArea = CalculateScreenSpaceArea(mr.bounds, camPos);

#if UNITY_EDITOR
                    UnityEditor.Handles.Label(mr.bounds.center, $"{screenArea:F1}px²");
#endif
                }
            }
            else
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(mr.bounds.center, mr.bounds.size);
            }
        }
    }
}