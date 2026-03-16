using System.Collections.Generic;
using UnityEngine;

public class ModelManager : MonoBehaviour
{
    public static ModelManager Instance;

    [SerializeField] GLBModelImporter _importer;
    [SerializeField] MeshFilteration _meshFilteration;
    [SerializeField] GameObject _currentModel;
    [SerializeField] OctreeHierarchyBuilder _octreeBuilder;


    // ── Depth color toggle ──────────────────────────────────────────────────
    [SerializeField] bool _depthColorMode = false;

    // ── Serialized settings ─────────────────────────────────────────────────
    [SerializeField] bool _loadModelOnStart = false;
    [SerializeField] bool _shouldFilterMesh = true;
    [SerializeField] bool _combineMeshesAndTextures = true;
    [SerializeField] bool _optimizeForOcclusionCulling = true;
    
    private MeshCombinationAndTextureComp.ModelOptimizer _optimizer;

    // Persisted so SetDepthColorMode() can be called on it later
    public bool DepthColorMode
    {
        get => _depthColorMode;
        set
        {
            _depthColorMode = value;
            _optimizer?.SetDepthColorMode(_depthColorMode);
        }
    }

    // ── Current model ───────────────────────────────────────────────────────
    public GameObject CurrentModel
    {
        get => _currentModel;
        set
        {
            _currentModel = value;

            if (_shouldFilterMesh && _currentModel != null)
                _currentModel = _meshFilteration.FilterSmallMeshes(_currentModel);

            if (_currentModel != null)
            {
                // Always call this so we can extract the renderers for occlusion
                // and apply depth colors, even if combination is false.
                CombineMeshFunc();
            }
        }
    }

    private async void CombineMeshFunc()
    {
        // Create once and persist on the class
        _optimizer = new MeshCombinationAndTextureComp.ModelOptimizer();

        // Pass the inspector toggle into the optimizer
        _optimizer.combineMeshes = _combineMeshesAndTextures;

        await _optimizer.ApplyOptimizationsAsync(_currentModel, progress =>
        {
            Debug.Log($"Optimization progress: {progress * 100f:F2}%");
        });

        // Re-apply depth mode in case it was set before the model finished loading
        _optimizer.SetDepthColorMode(_depthColorMode);

        RebuildHierarchy();

    }
    
    private void RebuildHierarchy()
    {
        if (_currentModel == null) return;
        
        if (_optimizeForOcclusionCulling)
        {
            // Build octree hierarchy
            if (_octreeBuilder != null)
            {
                _octreeBuilder.RootModel = _currentModel;
                _octreeBuilder.BuildOctreeHierarchy();
                
                // Octree builder will auto-register with FrustumCullingManager
                // if _autoRegisterCulling is enabled in its inspector
                Debug.Log("[ModelManager] Octree built. Frustum culling registration handled by OctreeHierarchyBuilder.");
            }
            else
            {
                Debug.LogWarning("[ModelManager] OctreeHierarchyBuilder reference is missing.");
            }
        }
        else
        {
            // No octree - register optimizer renderers directly with frustum culling
            if (FrustumCullingManager.Instance != null)
            {
                List<MeshRenderer> renderers = _optimizer?.GetCombinedRenderers();
                if (renderers != null && renderers.Count > 0)
                {
                    FrustumCullingManager.Instance.RegisterRenderers(renderers);
                    Debug.Log($"[ModelManager] Registered {renderers.Count} renderers with FrustumCullingManager (no octree).");
                }
            }
            else
            {
                Debug.LogWarning("[ModelManager] FrustumCullingManager Instance is missing.");
            }
        }
    }

    // ── Unity lifecycle ─────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _importer = GetComponent<GLBModelImporter>();
        _meshFilteration = GetComponent<MeshFilteration>();
        _octreeBuilder = GetComponent<OctreeHierarchyBuilder>();
    }

    void Start()
    {
        if (_loadModelOnStart)
            _importer.OpenFileBrowser();
    }
}