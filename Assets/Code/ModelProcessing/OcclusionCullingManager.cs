using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class OcclusionCullingManager : MonoBehaviour
{
    public static OcclusionCullingManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] ComputeShader _computeShader;

    [Header("Settings")]
    [SerializeField] bool _enabled = true;
    [SerializeField] float _updateInterval = 0.05f; // run every 50ms, not every frame
    [SerializeField] bool _debug = false;

    // State
    private Camera _cam;
    private List<MeshRenderer> _renderers = new();
    private int _count = 0;
    private bool _ready = false;
    private bool _pending = false;
    private float _lastUpdate = 0f;

    // GPU Buffers
    private GraphicsBuffer _aabbMinBuf;
    private GraphicsBuffer _aabbMaxBuf;
    private GraphicsBuffer _visibilityBuf;

    // CPU arrays — baked once at register time
    private Vector3[] _aabbMin;
    private Vector3[] _aabbMax;

    private int _kernel;

    // Stats
    private int _visible, _culled;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        ReleaseBuffers();
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void RegisterRenderers(List<MeshRenderer> renderers)
    {
        if (renderers == null || renderers.Count == 0)
        {
            Debug.LogWarning("[Occlusion] No renderers to register.");
            return;
        }

        if (_computeShader == null)
        {
            Debug.LogError("[Occlusion] Compute shader not assigned.");
            return;
        }

        _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[Occlusion] No main camera.");
            return;
        }

        _renderers = renderers;
        _count = renderers.Count;
        _kernel = _computeShader.FindKernel("CSMain");

        BakeAABBs();
        AllocateBuffers();

        _ready = true;
        Debug.Log($"[Occlusion] Registered {_count} renderers.");
    }

    public void SetEnabled(bool value)
    {
        _enabled = value;
        if (!value)
            foreach (var mr in _renderers)
                if (mr != null) mr.enabled = true;
    }

    // ── AABB Baking ───────────────────────────────────────────────────────

    private void BakeAABBs()
    {
        _aabbMin = new Vector3[_count];
        _aabbMax = new Vector3[_count];

        for (int i = 0; i < _count; i++)
        {
            if (_renderers[i] == null) continue;
            Bounds b = _renderers[i].bounds;
            _aabbMin[i] = b.min;
            _aabbMax[i] = b.max;
        }
    }

    // ── Buffer Management ─────────────────────────────────────────────────

    private void AllocateBuffers()
    {
        ReleaseBuffers();
        int stride3 = sizeof(float) * 3;
        int stride1 = sizeof(int);
        _aabbMinBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, stride3);
        _aabbMaxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, stride3);
        _visibilityBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, stride1);
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

    // ── Per Frame ─────────────────────────────────────────────────────────

    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (!_enabled || !_ready || _pending) return;
        if (cam != _cam) return;
        if (cam.cameraType != CameraType.Game) return;
        if (Time.time - _lastUpdate < _updateInterval) return;

        _lastUpdate = Time.time;
        Dispatch();
    }

    private void Dispatch()
    {
        // Depth texture — populated by your DepthWrite pass
        var depthTex = Shader.GetGlobalTexture("_CameraDepthTexture");
        if (depthTex == null)
        {
            if (_debug) Debug.LogWarning("[Occlusion] _CameraDepthTexture not ready.");
            return;
        }

        // View projection matrix
        Matrix4x4 vp = _cam.projectionMatrix * _cam.worldToCameraMatrix;

        // Frustum planes
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_cam);
        Vector4[] planePacked = new Vector4[6];
        for (int i = 0; i < 6; i++)
            planePacked[i] = new Vector4(
                planes[i].normal.x,
                planes[i].normal.y,
                planes[i].normal.z,
                planes[i].distance);

        // Set compute params
        _computeShader.SetMatrix("_VP", vp);
        _computeShader.SetVectorArray("_FrustumPlanes", planePacked);
        _computeShader.SetVector("_ScreenSize",
            new Vector2(_cam.pixelWidth, _cam.pixelHeight));
        _computeShader.SetInt("_Count", _count);
        _computeShader.SetInt("_IsReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);
        _computeShader.SetInt("_UVStartsAtTop", SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        _computeShader.SetTexture(_kernel, "_CameraDepth", depthTex);
        _computeShader.SetBuffer(_kernel, "_AABBMin", _aabbMinBuf);
        _computeShader.SetBuffer(_kernel, "_AABBMax", _aabbMaxBuf);
        _computeShader.SetBuffer(_kernel, "_Visibility", _visibilityBuf);

        // Dispatch
        int groups = Mathf.CeilToInt(_count / 64f);
        _computeShader.Dispatch(_kernel, groups, 1, 1);

        // Request async readback — completes next frame, no stall
        _pending = true;
        AsyncGPUReadback.Request(_visibilityBuf, OnReadback);
        if (_debug)
        {
            RenderTexture rt = depthTex as RenderTexture;
            if (rt != null)
                Debug.Log($"[Occlusion] Depth texture: {rt.width}x{rt.height} format:{rt.format}");
            else
                Debug.Log($"[Occlusion] Depth texture type: {depthTex.GetType().Name}");
        }
    }

    private void OnReadback(AsyncGPUReadbackRequest req)
    {
        _pending = false;

        if (req.hasError)
        {
            Debug.LogWarning("[Occlusion] Readback failed.");
            return;
        }

        NativeArray<int> results = req.GetData<int>();
        _visible = 0;
        _culled = 0;

        for (int i = 0; i < _count; i++)
        {
            if (i >= _renderers.Count || _renderers[i] == null) continue;
            bool vis = results[i] == 1;
            _renderers[i].enabled = vis;
            if (vis) _visible++; else _culled++;
        }

        if (_debug)
            Debug.Log($"[Occlusion] Visible:{_visible} Culled:{_culled} Total:{_count}");
    }

    // ── Gizmos ────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!_debug || _renderers == null) return;
        foreach (var mr in _renderers)
        {
            if (mr == null) continue;
            Gizmos.color = mr.enabled ? Color.green : Color.red;
            Gizmos.DrawWireCube(mr.bounds.center, mr.bounds.size);
        }
    }
}