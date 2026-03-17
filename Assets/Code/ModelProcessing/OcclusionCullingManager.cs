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

    private Camera _cam;
    private List<MeshRenderer> _renderers = new();
    private HashSet<MeshRenderer> _frustumVisibleSet = new();
    private int _count = 0;
    private bool _ready = false;
    private bool _pending = false;
    private float _lastUpdate = 0f;
    private int _kernel;
    private int _visible, _culled;

    private GraphicsBuffer _aabbMinBuf;
    private GraphicsBuffer _aabbMaxBuf;
    private GraphicsBuffer _visibilityBuf;

    private Vector3[] _aabbMin;
    private Vector3[] _aabbMax;
    
    // Track previous positions for movement detection
    private Vector3[] _lastPositions;
    private Quaternion[] _lastRotations;
    private Vector3[] _lastScales;

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
        _visible = 0; _culled = 0;
        _frustumVisibleSet.Clear();

        for (int i = 0; i < _count; i++)
        {
            if (i >= _renderers.Count || _renderers[i] == null) continue;
            bool vis = results[i] == 1;
            
            if (vis)
            {
                _renderers[i].enabled = true;
                _frustumVisibleSet.Add(_renderers[i]);
                _visible++;
            }
            else
            {
                _renderers[i].enabled = false;
                _culled++;
            }
        }

        if (_debug)
            Debug.Log($"[FrustumCulling] Visible:{_visible}  Culled:{_culled}  Total:{_count}");
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
