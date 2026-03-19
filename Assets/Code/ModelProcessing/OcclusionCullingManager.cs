using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class OcclusionCullingManager : MonoBehaviour
{
    public static OcclusionCullingManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Shader _objectIDShader;
    [SerializeField] private Camera _mainCamera;

    [Header("Settings")]
    [SerializeField] private bool _enabled = true;
    [SerializeField] private int _idBufferResolution = 256;
    [SerializeField] private float _updateInterval = 0.1f;
    [SerializeField] private bool _debug = false;

    [Header("Hysteresis (Prevent Flickering)")]
    [SerializeField] private bool _useHysteresis = true;
    [SerializeField] private int _framesBeforeCulling = 2;

    [Header("Statistics")]
    [SerializeField] private int _totalObjects = 0;
    [SerializeField] private int _visibleAfterOcclusion = 0;
    [SerializeField] private int _occlusionCulled = 0;

    // ID rendering
    private RenderTexture _idRenderTexture;
    private Material _idMaterial;
    private CommandBuffer _idCommandBuffer;

    // Object tracking
    private List<MeshRenderer> _registeredRenderers = new List<MeshRenderer>();
    private Dictionary<MeshRenderer, int> _rendererToID = new Dictionary<MeshRenderer, int>();
    private Dictionary<int, MeshRenderer> _idToRenderer = new Dictionary<int, MeshRenderer>();

    // Visibility tracking
    private HashSet<int> _visibleIDs = new HashSet<int>();
    private Dictionary<int, int> _invisibleFrameCount = new Dictionary<int, int>();

    // Async readback
    private bool _readbackPending = false;
    private float _lastUpdateTime = 0f;

    // Material property block for setting IDs
    private MaterialPropertyBlock _propertyBlock;
    private static readonly int ObjectIDProperty = Shader.PropertyToID("_ObjectID");

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void OnEnable()
    {
        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_objectIDShader == null)
        {
            Debug.LogError("[OcclusionCulling] ObjectID shader not assigned!");
            return;
        }

        _idMaterial = new Material(_objectIDShader);
        _propertyBlock = new MaterialPropertyBlock();

        CreateIDRenderTexture();
        CreateCommandBuffer();
    }

    void OnDisable()
    {
        ReleaseResources();
    }

    void OnDestroy()
    {
        ReleaseResources();
    }

    public void RegisterRenderers(List<MeshRenderer> renderers)
    {
        if (renderers == null || renderers.Count == 0)
        {
            Debug.LogWarning("[OcclusionCulling] No renderers to register.");
            return;
        }

        _registeredRenderers.Clear();
        _rendererToID.Clear();
        _idToRenderer.Clear();
        _invisibleFrameCount.Clear();

        int idCounter = 1; // Start from 1 (0 = background)

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            _registeredRenderers.Add(renderer);
            _rendererToID[renderer] = idCounter;
            _idToRenderer[idCounter] = renderer;
            _invisibleFrameCount[idCounter] = 0;

            idCounter++;
        }

        _totalObjects = _registeredRenderers.Count;

        Debug.Log($"[OcclusionCulling] Registered {_totalObjects} renderers. Max ID: {idCounter - 1}");

        // Validate ID count
        if (idCounter > 65535)
        {
            Debug.LogError("[OcclusionCulling] Too many objects! Limit is 65,535. Consider using R32 format.");
        }
    }

    void Update()
    {
        if (!_enabled || _registeredRenderers.Count == 0) return;

        // Check if it's time to update
        if (Time.time - _lastUpdateTime < _updateInterval) return;

        // Don't start new readback if one is pending
        if (_readbackPending) return;

        _lastUpdateTime = Time.time;
        RenderIDBuffer();
    }

    private void CreateIDRenderTexture()
    {
        if (_idRenderTexture != null)
            _idRenderTexture.Release();

        // Use RInt for 32-bit integer (can store large IDs)
        _idRenderTexture = new RenderTexture(
            _idBufferResolution,
            _idBufferResolution,
            24, // Depth buffer bits
            RenderTextureFormat.RInt
        );

        _idRenderTexture.name = "ObjectID_RenderTexture";
        _idRenderTexture.filterMode = FilterMode.Point;
        _idRenderTexture.Create();
    }

    private void CreateCommandBuffer()
    {
        _idCommandBuffer = new CommandBuffer
        {
            name = "ObjectID Rendering"
        };
    }

    private void RenderIDBuffer()
    {
        if (_mainCamera == null || _idRenderTexture == null) return;

        _idCommandBuffer.Clear();

        // Set render target
        _idCommandBuffer.SetRenderTarget(_idRenderTexture);
        _idCommandBuffer.ClearRenderTarget(true, true, Color.black);

        // Setup camera matrices
        _idCommandBuffer.SetViewProjectionMatrices(
            _mainCamera.worldToCameraMatrix,
            _mainCamera.projectionMatrix
        );

        // Render each object with its unique ID
        foreach (var renderer in _registeredRenderers)
        {
            // 1. We ONLY check for null. 
            // REMOVED: !renderer.enabled
            if (renderer == null) continue;

            // 2. We let the Frustum Manager dictate if we should test it for occlusion
            if (FrustumCullingManager.Instance != null)
            {
                // If it's outside the camera view, skip rendering it to the ID buffer entirely
                if (!FrustumCullingManager.Instance.IsFrustumVisible(renderer))
                    continue;
            }

            int objectID = _rendererToID[renderer];

            // Set object ID in material
            _propertyBlock.SetInt(ObjectIDProperty, objectID);

            // Draw the mesh
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                _idCommandBuffer.DrawMesh(
                    meshFilter.sharedMesh,
                    renderer.transform.localToWorldMatrix,
                    _idMaterial,
                    0, // submesh index
                    0, // shader pass
                    _propertyBlock
                );
            }
        }

        // Execute command buffer
        Graphics.ExecuteCommandBuffer(_idCommandBuffer);

        // Request async readback (don't specify texture format for RInt RenderTexture)
        _readbackPending = true;
        AsyncGPUReadback.Request(_idRenderTexture, 0, OnReadbackComplete);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _readbackPending = false;

        if (request.hasError)
        {
            Debug.LogWarning("[OcclusionCulling] Readback error.");
            return;
        }

        // Process the ID buffer
        ProcessIDBuffer(request.GetData<int>());
    }

    private void ProcessIDBuffer(NativeArray<int> idData)
    {
        // Clear previous visible set
        _visibleIDs.Clear();

        // Scan all pixels and collect unique IDs
        for (int i = 0; i < idData.Length; i++)
        {
            int id = idData[i];
            if (id > 0) // 0 is background
            {
                _visibleIDs.Add(id);
            }
        }

        // Apply occlusion culling with optional hysteresis
        ApplyOcclusionCulling();

        if (_debug)
        {
            Debug.Log($"[OcclusionCulling] Visible IDs: {_visibleIDs.Count}/{_totalObjects}  Culled: {_occlusionCulled}");
        }
    }

    private void ApplyOcclusionCulling()
    {
        _visibleAfterOcclusion = 0;
        _occlusionCulled = 0;

        foreach (var kvp in _rendererToID)
        {
            MeshRenderer renderer = kvp.Key;
            int objectID = kvp.Value;

            if (renderer == null) continue;

            bool isVisibleInIDBuffer = _visibleIDs.Contains(objectID);

            // NEW: Ask the Frustum manager if this is even in the camera view!
            bool isFrustumVisible = true;
            if (FrustumCullingManager.Instance != null)
            {
                isFrustumVisible = FrustumCullingManager.Instance.IsFrustumVisible(renderer);
            }

            // It is ONLY visible if it passed the ID check AND the Frustum check
            bool finalVisibility = isVisibleInIDBuffer && isFrustumVisible;

            // Apply your hysteresis logic using finalVisibility instead of just isVisibleInIDBuffer
            if (_useHysteresis)
            {
                if (!finalVisibility)
                {
                    _invisibleFrameCount[objectID]++;
                    if (_invisibleFrameCount[objectID] >= _framesBeforeCulling)
                    {
                        renderer.enabled = false; // Add your tracking from the previous fix here
                        _occlusionCulled++;
                    }
                    // ...
                }
                else
                {
                    _invisibleFrameCount[objectID] = 0;
                    renderer.enabled = true;
                    _visibleAfterOcclusion++;
                }
            }
            else
            {
                // No hysteresis
                renderer.enabled = finalVisibility;
            }
        }
    }
    private void ReleaseResources()
    {
        if (_idRenderTexture != null)
        {
            _idRenderTexture.Release();
            _idRenderTexture = null;
        }

        if (_idCommandBuffer != null)
        {
            _idCommandBuffer.Release();
            _idCommandBuffer = null;
        }

        if (_idMaterial != null)
        {
            Destroy(_idMaterial);
            _idMaterial = null;
        }
    }

    // Debug visualization
    void OnGUI()
    {
        if (!_debug) return;

        GUILayout.BeginArea(new Rect(10, 100, 300, 200));
        GUILayout.Label($"=== Occlusion Culling Stats ===");
        GUILayout.Label($"Total Objects: {_totalObjects}");
        GUILayout.Label($"Visible (After Occlusion): {_visibleAfterOcclusion}");
        GUILayout.Label($"Occlusion Culled: {_occlusionCulled}");
        GUILayout.Label($"Unique Visible IDs: {_visibleIDs.Count}");
        GUILayout.EndArea();

        // Show ID buffer preview
        if (_idRenderTexture != null)
        {
            GUI.DrawTexture(new Rect(Screen.width - 264, 10, 256, 256), _idRenderTexture);
        }
    }
}