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
    private Texture2D _debugTexture; // CPU-side texture for visualization
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

    // Debug info
    private int _lastRenderedCount = 0;
    private int _lastSkippedCount = 0;

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

        // Create debug texture
        _debugTexture = new Texture2D(_idBufferResolution, _idBufferResolution, TextureFormat.RGBA32, false);
        _debugTexture.filterMode = FilterMode.Point;

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
    }

    void Update()
    {
        if (!_enabled || _registeredRenderers.Count == 0) return;

        if (Time.time - _lastUpdateTime < _updateInterval) return;

        if (_readbackPending) return;

        _lastUpdateTime = Time.time;
        RenderIDBuffer();
    }

    private void CreateIDRenderTexture()
    {
        if (_idRenderTexture != null)
            _idRenderTexture.Release();

        _idRenderTexture = new RenderTexture(
            _idBufferResolution,
            _idBufferResolution,
            24,
            RenderTextureFormat.RInt
        );

        _idRenderTexture.name = "ObjectID_RenderTexture";
        _idRenderTexture.filterMode = FilterMode.Point;
        bool created = _idRenderTexture.Create();

        Debug.Log($"[OcclusionCulling] RenderTexture created: {created}, IsCreated: {_idRenderTexture.IsCreated()}");
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
        if (_mainCamera == null || _idRenderTexture == null)
        {
            Debug.LogError("[OcclusionCulling] Camera or RenderTexture is null!");
            return;
        }

        _idCommandBuffer.Clear();

        _idCommandBuffer.SetRenderTarget(_idRenderTexture);
        _idCommandBuffer.ClearRenderTarget(true, true, Color.black);

        _idCommandBuffer.SetViewProjectionMatrices(
            _mainCamera.worldToCameraMatrix,
            _mainCamera.projectionMatrix
        );

        int renderedCount = 0;
        int skippedCount = 0;
        int nullMeshCount = 0;
        int frustumCulledCount = 0;

        foreach (var renderer in _registeredRenderers)
        {
            if (renderer == null)
            {
                skippedCount++;
                continue;
            }

            // Check frustum visibility
            if (FrustumCullingManager.Instance != null)
            {
                if (!FrustumCullingManager.Instance.IsFrustumVisible(renderer))
                {
                    frustumCulledCount++;
                    continue;
                }
            }

            int objectID = _rendererToID[renderer];
            _propertyBlock.SetInt(ObjectIDProperty, objectID);

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                nullMeshCount++;
                continue;
            }

            _idCommandBuffer.DrawMesh(
                meshFilter.sharedMesh,
                renderer.transform.localToWorldMatrix,
                _idMaterial,
                0,
                0,
                _propertyBlock
            );
            renderedCount++;
        }

        _lastRenderedCount = renderedCount;
        _lastSkippedCount = frustumCulledCount;

        Graphics.ExecuteCommandBuffer(_idCommandBuffer);

        Debug.Log($"[OcclusionCulling] DrawMesh called {renderedCount} times. Frustum culled: {frustumCulledCount}, Null meshes: {nullMeshCount}");

        _readbackPending = true;
        AsyncGPUReadback.Request(_idRenderTexture, 0, OnReadbackComplete);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _readbackPending = false;

        if (request.hasError)
        {
            Debug.LogError("[OcclusionCulling] Readback error!");
            return;
        }

        NativeArray<int> idData = request.GetData<int>();
        
        // Count non-zero pixels for debugging
        int nonZeroPixels = 0;
        int maxID = 0;
        for (int i = 0; i < idData.Length; i++)
        {
            if (idData[i] > 0)
            {
                nonZeroPixels++;
                maxID = Mathf.Max(maxID, idData[i]);
            }
        }

        Debug.Log($"[OcclusionCulling] Readback complete. Non-zero pixels: {nonZeroPixels}/{idData.Length}, Max ID found: {maxID}");

        // Update debug texture
        if (_debug)
        {
            UpdateDebugTexture(idData);
        }

        ProcessIDBuffer(idData);
    }

    private void UpdateDebugTexture(NativeArray<int> idData)
    {
        Color32[] pixels = new Color32[_idBufferResolution * _idBufferResolution];
        
        for (int i = 0; i < idData.Length; i++)
        {
            int id = idData[i];
            
            if (id == 0)
            {
                pixels[i] = new Color32(0, 0, 0, 255); // Black for background
            }
            else
            {
                // Convert ID to a visible color using HSV
                float hue = (id % 360) / 360f;
                Color color = Color.HSVToRGB(hue, 1f, 1f);
                pixels[i] = color;
            }
        }
        
        _debugTexture.SetPixels32(pixels);
        _debugTexture.Apply();
    }

    private void ProcessIDBuffer(NativeArray<int> idData)
    {
        _visibleIDs.Clear();

        for (int i = 0; i < idData.Length; i++)
        {
            int id = idData[i];
            if (id > 0)
            {
                _visibleIDs.Add(id);
            }
        }

        ApplyOcclusionCulling();
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

            bool isFrustumVisible = true;
            if (FrustumCullingManager.Instance != null)
            {
                isFrustumVisible = FrustumCullingManager.Instance.IsFrustumVisible(renderer);
            }

            bool finalVisibility = isVisibleInIDBuffer && isFrustumVisible;

            if (_useHysteresis)
            {
                if (!finalVisibility)
                {
                    _invisibleFrameCount[objectID]++;
                    if (_invisibleFrameCount[objectID] >= _framesBeforeCulling)
                    {
                        renderer.enabled = false;
                        _occlusionCulled++;
                    }
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
                renderer.enabled = finalVisibility;
                if (finalVisibility)
                    _visibleAfterOcclusion++;
                else
                    _occlusionCulled++;
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

        if (_debugTexture != null)
        {
            Destroy(_debugTexture);
            _debugTexture = null;
        }
    }

    void OnGUI()
    {
        if (!_debug) return;

        // Stats panel
        GUILayout.BeginArea(new Rect(10, 10, 350, 300));
        GUILayout.Box("=== Occlusion Culling Debug ===");
        GUILayout.Label($"Total Registered: {_totalObjects}");
        GUILayout.Label($"Last Render Call: {_lastRenderedCount} objects");
        GUILayout.Label($"Frustum Culled: {_lastSkippedCount}");
        GUILayout.Label($"Visible IDs Found: {_visibleIDs.Count}");
        GUILayout.Label($"Visible After Occlusion: {_visibleAfterOcclusion}");
        GUILayout.Label($"Occlusion Culled: {_occlusionCulled}");
        GUILayout.Label($"Readback Pending: {_readbackPending}");
        GUILayout.Label($"");
        GUILayout.Label($"Camera: {(_mainCamera != null ? _mainCamera.name : "NULL")}");
        GUILayout.Label($"RenderTexture: {(_idRenderTexture != null && _idRenderTexture.IsCreated() ? "OK" : "FAILED")}");
        GUILayout.Label($"Material: {(_idMaterial != null ? "OK" : "NULL")}");
        GUILayout.EndArea();

        // Show debug texture
        if (_debugTexture != null)
        {
            int size = 256;
            GUI.Box(new Rect(Screen.width - size - 12, 10, size + 4, size + 24), "ID Buffer Visualization");
            GUI.DrawTexture(new Rect(Screen.width - size - 10, 32, size, size), _debugTexture);
        }
    }
}