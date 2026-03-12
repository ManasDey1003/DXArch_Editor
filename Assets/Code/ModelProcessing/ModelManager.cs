using UnityEngine;

public class ModelManager : MonoBehaviour
{
    public static ModelManager Instance;

    [SerializeField] GLBModelImporter _importer;
    [SerializeField] MeshFilteration _meshFilteration;
    [SerializeField] GameObject _currentModel;

    // Persisted so SetDepthColorMode() can be called on it later
    private MeshCombinationAndTextureComp.ModelOptimizer _optimizer;

    // ── Depth color toggle ──────────────────────────────────────────────────
    [SerializeField] bool _depthColorMode = false;
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

            if (shouldFilterMesh && _currentModel != null)
                _currentModel = _meshFilteration.FilterSmallMeshes(_currentModel);

            if (CombineMeshesAndTextures && _currentModel != null)
                CombineMeshFunc();
        }
    }

    private async void CombineMeshFunc()
    {
        // Create once and persist on the class
        _optimizer = new MeshCombinationAndTextureComp.ModelOptimizer();

        await _optimizer.ApplyOptimizationsAsync(_currentModel, progress =>
        {
            Debug.Log($"Optimization progress: {progress * 100f:F2}%");
        });

        // Re-apply depth mode in case it was set before the model finished loading
        _optimizer.SetDepthColorMode(_depthColorMode);


        // Wire up occlusion culling after combining is done
        if (OcclusionCullingManager.Instance != null)
            OcclusionCullingManager.Instance.RegisterRenderers(
                _optimizer.GetCombinedRenderers());
    }

    // ── Serialized settings ─────────────────────────────────────────────────
    [SerializeField] bool loadModelOnStart = false;
    [SerializeField] bool shouldFilterMesh = true;
    [SerializeField] bool CombineMeshesAndTextures = true;

    // ── Unity lifecycle ─────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _importer = GetComponent<GLBModelImporter>();
        _meshFilteration = GetComponent<MeshFilteration>();
    }

    void Start()
    {
        if (loadModelOnStart)
            _importer.OpenFileBrowser();
    }
}