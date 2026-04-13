using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using AlligUtils;



#if UNITY_EDITOR
using UnityEditor;
#endif

public class ModelManager : MonoBehaviour
{
    public static ModelManager Instance;

    [SerializeField] GLBModelImporter _importer;
    [SerializeField] MeshFilteration _meshFilteration;
    [SerializeField] GameObject _currentModel;
    // [SerializeField] OctreeHierarchyBuilder _octreeBuilder;
    [SerializeField] TestExport _testExport;


    // ── Depth color toggle ──────────────────────────────────────────────────
    [SerializeField] bool _depthColorMode = false;

    // ── Serialized settings ─────────────────────────────────────────────────
    [SerializeField] bool _loadModelOnStart = false;
    [SerializeField] bool _shouldFilterMesh = true;
    [SerializeField] bool _combineMeshesAndTextures = true;
    // [SerializeField] bool _optimizeForOcclusionCulling = true;
    [SerializeField] private bool _enableOcclusionCulling = true;

    private MeshCombinationAndTextureComp.ModelOptimizer _optimizer;
    private string _originalModelName;

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

            // Store the original model name when it's first set
            if (_currentModel != null && string.IsNullOrEmpty(_originalModelName))
            {
                _originalModelName = _currentModel.name;
            }

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

    public bool AddMeshCollider;

    private async void CombineMeshFunc()
    {
        // Create once and persist on the class
        _optimizer = new MeshCombinationAndTextureComp.ModelOptimizer();

        // Pass the inspector toggle into the optimizer
        _optimizer.combineMeshes = _combineMeshesAndTextures;
        _optimizer.simplifyTexturesBySize = false; // Disable texture simplification by size

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

        // if (_optimizeForOcclusionCulling)
        // {
        //     // Build octree hierarchy
        //     if (_octreeBuilder != null)
        //     {
        //         _octreeBuilder.RootModel = _currentModel;
        //         _octreeBuilder.BuildOctreeHierarchy();

        //         Debug.Log("[ModelManager] Octree built.");
        //     }
        //     else
        //     {
        //         Debug.LogWarning("[ModelManager] OctreeHierarchyBuilder reference is missing.");
        //     }
        // }

        // Register renderers with culling systems
        List<MeshRenderer> renderers = _optimizer?.GetCombinedRenderers();
        if (renderers != null && renderers.Count > 0)
        {
            // Register with frustum culling
            if (FrustumCullingManager.Instance != null)
            {
                FrustumCullingManager.Instance.RegisterRenderers(renderers);
                Debug.Log($"[ModelManager] Registered {renderers.Count} renderers with FrustumCullingManager.");
            }

            // Register with occlusion culling
            if (_enableOcclusionCulling && OcclusionCullingManager.Instance != null)
            {
                OcclusionCullingManager.Instance.RegisterRenderers(renderers);
                Debug.Log($"[ModelManager] Registered {renderers.Count} renderers with OcclusionCullingManager.");
            }

            if (AddMeshCollider)
            {
                // Ensure the renderer's GameObject is on the correct layer for occlusion culling
                foreach (var renderer in renderers)
                {
                    renderer.gameObject.layer = LayerMask.NameToLayer("Pickable");
                    renderer.gameObject.AddComponent<MeshCollider>();
                    Outline outline = renderer.gameObject.AddComponent<Outline>();
                    outline.OutlineMode = Outline.Mode.OutlineAll;
                    outline.OutlineColor = Color.red;
                    outline.OutlineWidth = 5f;
                    outline.enabled = false; // Start with outline disabled
                }


            }
        }
    }

    // ── Export functionality ────────────────────────────────────────────────

    [ContextMenu("Export Model")]
    public void ExportModel()
    {
        if (_currentModel == null)
        {
            Debug.LogError("[ModelManager] No model loaded to export.");
            return;
        }

        if (_testExport == null)
        {
            Debug.LogError("[ModelManager] TestExport reference is missing.");
            return;
        }

        string exportPath = OpenSaveFileDialog();

        if (string.IsNullOrEmpty(exportPath))
        {
            Debug.Log("[ModelManager] Export cancelled by user.");
            return;
        }

        _testExport.exportRoot = new GameObject[] { _currentModel.transform.GetChildWithName("CombinedMeshes").gameObject ?? _currentModel };
        _testExport.path = exportPath;

        // Use chunked export instead
        _testExport.ExportInChunks();

        Debug.Log($"[ModelManager] Initiated chunked export of '{_currentModel.name}'");
    }

    private string OpenSaveFileDialog()
    {
#if UNITY_EDITOR
        string defaultName = !string.IsNullOrEmpty(_originalModelName)
            ? _originalModelName
            : "exported_model";

        // Remove any file extension from the default name
        defaultName = System.IO.Path.GetFileNameWithoutExtension(defaultName);

        string path = EditorUtility.SaveFilePanel(
            "Export Model as GLB",
            "",
            defaultName + ".glb",
            "glb"
        );

        return path;
#else
        // For runtime builds, you'll need a runtime file browser solution
        Debug.LogWarning("[ModelManager] File browser only works in editor. Exporting to default path.");
        string defaultName = !string.IsNullOrEmpty(_originalModelName) 
            ? _originalModelName 
            : "exported_model";
        return Application.persistentDataPath + "/" + defaultName + ".glb";
#endif
    }

    // Public method to be called from UI or other scripts
    public void ExportModelToPath(string path)
    {
        if (_currentModel == null)
        {
            Debug.LogError("[ModelManager] No model loaded to export.");
            return;
        }

        if (_testExport == null)
        {
            Debug.LogError("[ModelManager] TestExport reference is missing.");
            return;
        }

        _testExport.exportRoot = new GameObject[] { _currentModel };
        _testExport.path = path;
        _testExport.AdvancedExport();

        Debug.Log($"[ModelManager] Exported '{_currentModel.name}' to: {path}");
    }


    /// <summary>
    /// Coroutine that processes the model (filtration + optimization) and exports it when complete
    /// </summary>
    /// <param name="exportPath">Path where the model should be exported</param>
    public IEnumerator ProcessAndExportModel(string exportPath)
    {
        if (_currentModel == null)
        {
            Debug.LogError("[ModelManager] No model loaded to process and export.");
            yield break;
        }

        Debug.Log("[ModelManager] Starting model processing and export...");

        // Step 1: Filter mesh if enabled
        // if (_shouldFilterMesh)
        {
            _currentModel = _meshFilteration.FilterSmallMeshes(_currentModel);
            Debug.Log("[ModelManager] Mesh filtration complete.");
        }

        // Step 2: Combine meshes and textures
        _optimizer = new MeshCombinationAndTextureComp.ModelOptimizer();
        _optimizer.combineMeshes = true;

        // Flag to track async completion
        bool optimizationComplete = false;

        // Start the async optimization
        var optimizationTask = _optimizer.ApplyOptimizationsAsync(_currentModel, progress =>
        {
            Debug.Log($"Optimization progress: {progress * 100f:F2}%");
        });

        // Continue the task and set flag when done
        optimizationTask.ContinueWith(task =>
        {
            optimizationComplete = true;
        });

        // Step 3: Wait for optimization to complete
        while (!optimizationComplete)
        {
            yield return null; // Wait one frame
        }

        Debug.Log("[ModelManager] Mesh optimization complete.");

        // Re-apply depth mode in case it was set before optimization
        _optimizer.SetDepthColorMode(false);

        // Rebuild hierarchy (frustum culling, occlusion culling, etc.)
        RebuildHierarchy();

        Debug.Log("[ModelManager] Hierarchy rebuilt.");

        // Step 4: Export the model
        if (_testExport == null)
        {
            Debug.LogError("[ModelManager] TestExport reference is missing. Cannot export.");
            yield break;
        }

        _testExport.exportRoot = new GameObject[] { _currentModel.transform.GetChildWithName("CombinedMeshes").gameObject ?? _currentModel };
        _testExport.path = exportPath;
        // _testExport.AdvancedExport();

        // _testExport.exportRoot = new GameObject[] { _currentModel.transform.GetChildWithName("CombinedMeshes").gameObject ?? _currentModel };
        _testExport.ExportInChunks();

        Debug.Log($"[ModelManager] Export complete: '{_currentModel.name}' saved to {exportPath}");
    }

    /// <summary>
    /// Convenience method to process and export with file dialog
    /// </summary>


    [ContextMenu("Process and Export with Dialog")]
    public void ProcessAndExportWithDialog()
    {
        string exportPath = OpenSaveFileDialog();

        if (string.IsNullOrEmpty(exportPath))
        {
            Debug.Log("[ModelManager] Export cancelled by user.");
            return;
        }

        StartCoroutine(ProcessAndExportModel(exportPath));
    }


    // ── Unity lifecycle ─────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _importer = GetComponent<GLBModelImporter>();
        _meshFilteration = GetComponent<MeshFilteration>();
        // _octreeBuilder = GetComponent<OctreeHierarchyBuilder>();
        _testExport = GetComponent<TestExport>();
    }

    void Start()
    {
        if (_loadModelOnStart)
            _importer.OpenFileBrowser();
    }
}