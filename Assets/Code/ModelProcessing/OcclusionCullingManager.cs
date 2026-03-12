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
    [SerializeField] float _updateInterval = 0.05f;
    [SerializeField] bool _debug = false;

    [Header("Debug Visualization")]
    [SerializeField] bool _debugDepthRead = false; // logs what depth values compute reads
    [SerializeField] int _debugObjectIndex = 0;    // which object to debug

    private Camera _cam;
    private List<MeshRenderer> _renderers = new();
    private int _count = 0;
    private bool _ready = false;
    private bool _pending = false;
    private float _lastUpdate = 0f;
    private int _kernel;
    private int _visible, _culled;

    private GraphicsBuffer _aabbMinBuf;
    private GraphicsBuffer _aabbMaxBuf;
    private GraphicsBuffer _visibilityBuf;
    private GraphicsBuffer _debugBuf;      // NEW: reads back intermediate values

    private Vector3[] _aabbMin;
    private Vector3[] _aabbMax;

    // Debug buffer layout per object:
    // [0] uvMinX  [1] uvMinY  [2] uvMaxX  [3] uvMaxY
    // [4] closestNDC
    // [5-13] 9 scene depth samples
    // [14] occludedSampleCount
    // [15] finalResult (0=visible 1=occluded)
    private const int DEBUG_FLOATS_PER_OBJECT = 16;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnEnable() => RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    void OnDisable() { RenderPipelineManager.endCameraRendering -= OnEndCameraRendering; ReleaseBuffers(); }

    public void RegisterRenderers(List<MeshRenderer> renderers)
    {
        if (renderers == null || renderers.Count == 0) { Debug.LogWarning("[Occlusion] No renderers."); return; }
        if (_computeShader == null) { Debug.LogError("[Occlusion] No compute shader."); return; }

        _cam = Camera.main;
        if (_cam == null) { Debug.LogError("[Occlusion] No main camera."); return; }
        _cam.depthTextureMode |= DepthTextureMode.Depth;

        _renderers = renderers;
        _count = renderers.Count;
        _kernel = _computeShader.FindKernel("CSMain");

        if (_kernel < 0) { Debug.LogError("[Occlusion] CSMain kernel not found."); return; }

        BakeAABBs();
        AllocateBuffers();
        _ready = true;

        Debug.Log($"[Occlusion] Registered {_count} renderers. Kernel={_kernel}");
    }

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

    private void AllocateBuffers()
    {
        ReleaseBuffers();
        _aabbMinBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, sizeof(float) * 3);
        _aabbMaxBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, sizeof(float) * 3);
        _visibilityBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count, sizeof(int));
        _debugBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _count * DEBUG_FLOATS_PER_OBJECT, sizeof(float));
        _aabbMinBuf.SetData(_aabbMin);
        _aabbMaxBuf.SetData(_aabbMax);
    }

    private void ReleaseBuffers()
    {
        _aabbMinBuf?.Release(); _aabbMinBuf = null;
        _aabbMaxBuf?.Release(); _aabbMaxBuf = null;
        _visibilityBuf?.Release(); _visibilityBuf = null;
        _debugBuf?.Release(); _debugBuf = null;
        _ready = false;
    }

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
        // Use our captured depth instead of _CameraDepthTexture
        RenderTexture depthTex = DepthCaptureFeature.Instance?.OcclusionDepthTexture;

        if (depthTex == null)
        {
            if (_debug) Debug.LogWarning("[Occlusion] DepthCaptureFeature not ready. " +
                                         "Add DepthCaptureFeature to your URP Renderer.");
            return;
        }

        if (_debug)
            Debug.Log($"[Occlusion] Using depth: {depthTex.width}x{depthTex.height} " +
                      $"fmt:{depthTex.format}");

        Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(_cam.projectionMatrix, true);
        Matrix4x4 vp = gpuProj * _cam.worldToCameraMatrix;

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_cam);
        Vector4[] packed = new Vector4[6];
        for (int i = 0; i < 6; i++)
            packed[i] = new Vector4(planes[i].normal.x, planes[i].normal.y,
                                    planes[i].normal.z, planes[i].distance);

        _computeShader.SetMatrix("_VP", vp);
        _computeShader.SetVectorArray("_FrustumPlanes", packed);
        _computeShader.SetVector("_ScreenSize", new Vector2(_cam.pixelWidth, _cam.pixelHeight));
        _computeShader.SetInt("_Count", _count);
        _computeShader.SetInt("_IsReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);
        _computeShader.SetInt("_UVStartsAtTop", SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        _computeShader.SetInt("_DebugIndex", _debugObjectIndex);

        _computeShader.SetTexture(_kernel, "_CameraDepth", depthTex);
        _computeShader.SetBuffer(_kernel, "_AABBMin", _aabbMinBuf);
        _computeShader.SetBuffer(_kernel, "_AABBMax", _aabbMaxBuf);
        _computeShader.SetBuffer(_kernel, "_Visibility", _visibilityBuf);
        _computeShader.SetBuffer(_kernel, "_DebugOut", _debugBuf);

        int groups = Mathf.CeilToInt(_count / 64f);
        _computeShader.Dispatch(_kernel, groups, 1, 1);

        _pending = true;
        AsyncGPUReadback.Request(_visibilityBuf, OnReadback);
        if (_debugDepthRead)
            AsyncGPUReadback.Request(_debugBuf, OnDebugReadback);

        if (_debugDepthRead)
        {
            // Read one pixel directly on CPU to verify RT contents
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = depthTex;

            Texture2D readback = new Texture2D(depthTex.width, depthTex.height,
                TextureFormat.RFloat, false);
            readback.ReadPixels(new Rect(0, 0, depthTex.width, depthTex.height), 0, 0);
            readback.Apply();

            // Sample center pixel and a few others
            Color center = readback.GetPixel(depthTex.width / 2, depthTex.height / 2);
            Color topLeft = readback.GetPixel(10, 10);
            Color bottomRight = readback.GetPixel(depthTex.width - 10, depthTex.height - 10);

            RenderTexture.active = prev;
            Destroy(readback);

            Debug.Log($"[DepthReadback] RT size: {depthTex.width}x{depthTex.height}\n" +
                      $"  center pixel:      r={center.r:F6}\n" +
                      $"  topLeft pixel:     r={topLeft.r:F6}\n" +
                      $"  bottomRight pixel: r={bottomRight.r:F6}");
        }






    }

    private void OnReadback(AsyncGPUReadbackRequest req)
    {
        _pending = false;
        if (req.hasError) { Debug.LogWarning("[Occlusion] Readback error."); return; }

        NativeArray<int> results = req.GetData<int>();
        _visible = 0; _culled = 0;

        for (int i = 0; i < _count; i++)
        {
            if (i >= _renderers.Count || _renderers[i] == null) continue;
            bool vis = results[i] == 1;
            _renderers[i].enabled = vis;
            if (vis) _visible++; else _culled++;
        }

        if (_debug)
            Debug.Log($"[Occlusion] Visible:{_visible}  Culled:{_culled}  Total:{_count}");
    }

    private void OnDebugReadback(AsyncGPUReadbackRequest req)
    {
        if (req.hasError) return;

        NativeArray<float> data = req.GetData<float>();
        int idx = _debugObjectIndex;
        if (idx < 0 || idx >= _count) return;

        int base_ = idx * DEBUG_FLOATS_PER_OBJECT;

        float uvMinX = data[base_ + 0];
        float uvMinY = data[base_ + 1];
        float uvMaxX = data[base_ + 2];
        float uvMaxY = data[base_ + 3];
        float closestNDC = data[base_ + 4];
        float occCount = data[base_ + 14];
        float finalResult = data[base_ + 15];

        string samples = "";
        for (int s = 0; s < 9; s++)
            samples += $"\n    sample[{s}] sceneDepth={data[base_ + 5 + s]:F4}";

        string rendererName = _renderers[idx] != null ? _renderers[idx].name : "null";

        Debug.Log($"[OcclusionDebug] Object[{idx}] '{rendererName}'\n" +
                  $"  screenRect UV: ({uvMinX:F3},{uvMinY:F3}) -> ({uvMaxX:F3},{uvMaxY:F3})\n" +
                  $"  closestNDC: {closestNDC:F4}\n" +
                  $"  IsReversedZ: {SystemInfo.usesReversedZBuffer}\n" +
                  $"  depthSamples:{samples}\n" +
                  $"  occludedSampleCount: {occCount}\n" +
                  $"  finalResult: {(finalResult > 0.5f ? "OCCLUDED" : "VISIBLE")}");
    }

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