using UnityEngine;
using UnityEngine.Networking;
using SimpleFileBrowser;
using System.Collections;
using System.IO;

/// <summary>
/// Unity script to import GLB models using SimpleFileBrowser asset
/// Requires: SimpleFileBrowser and a GLB loader (e.g., GLTFast or TriLib)
/// </summary>
public class GLBModelImporter : MonoBehaviour
{
    [Header("Import Settings")]
    [Tooltip("Parent transform for the imported model")]
    public Transform modelParent;

    [Tooltip("Scale factor to apply to imported model")]
    public float importScale = 1.0f;

    [Tooltip("Auto-center the imported model")]
    public bool autoCenterModel = true;

    [Header("UI Feedback (Optional)")]
    [Tooltip("Optional loading text UI element")]
    public UnityEngine.UI.Text loadingText;

    [Tooltip("Optional progress bar UI element")]
    public UnityEngine.UI.Slider progressBar;

    public GameObject currentModel;

    void Start()
    {
        // Set file browser properties
        FileBrowser.SetFilters(true, new FileBrowser.Filter("GLB Models", ".glb"), new FileBrowser.Filter("GLTF Models", ".gltf"));
        FileBrowser.SetDefaultFilter(".glb");
        FileBrowser.SetExcludedExtensions(".lnk", ".tmp", ".zip", ".rar", ".exe");

        // If no parent is set, use this transform
        if (modelParent == null)
            modelParent = transform;
    }

    /// <summary>
    /// Opens the file browser to select a GLB file
    /// Call this from a UI button or other trigger
    /// </summary>
    [ContextMenu("Import GLB Model")]
    public void OpenFileBrowser()
    {
        StartCoroutine(ShowLoadDialogCoroutine());
    }

    IEnumerator ShowLoadDialogCoroutine()
    {
        // Show file browser
        yield return FileBrowser.WaitForLoadDialog(
            FileBrowser.PickMode.Files,
            false, // allowMultiSelection
            null, // initialPath
            null, // initialFilename
            "Select GLB Model",
            "Load"
        );

        // Check if user selected a file
        if (FileBrowser.Success)
        {
            string filePath = FileBrowser.Result[0];
            Debug.Log("Selected file: " + filePath);

            // Load the model using async if GLTFast is available
            // #if USING_GLTFAST
            LoadGLBModelAsync(filePath);
            // #else
            // StartCoroutine(LoadGLBModelCoroutine(filePath));
            // #endif
        }
        else
        {
            Debug.Log("File selection cancelled");
            UpdateLoadingText("");
        }
    }

    // #if USING_GLTFAST
    /// <summary>
    /// Loads a GLB model asynchronously using GLTFast
    /// To enable: Add USING_GLTFAST to Project Settings > Player > Scripting Define Symbols
    /// </summary>

    async void LoadGLBModelAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError("File does not exist: " + filePath);
            UpdateLoadingText("Error: File not found");
            return;
        }

        UpdateLoadingText("Loading model...");
        UpdateProgress(0.1f);

        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        try
        {
            var gltf = new GLTFast.GltfImport();

            // Configure import settings
            var importSettings = new GLTFast.ImportSettings
            {
                GenerateMipMaps = true,
                AnisotropicFilterLevel = 3
            };

            UpdateProgress(0.3f);

            bool success = await gltf.Load(filePath, importSettings);
            UpdateProgress(0.6f);

            if (success)
            {
                currentModel = new GameObject(Path.GetFileNameWithoutExtension(filePath));
                currentModel.transform.SetParent(modelParent);

                // Configure instantiation to include materials
                // var instantiator = new GLTFast.InstantiationSettings
                // {
                //     Mask = GLTFast.ComponentType.All
                // };

                bool instantiated = await gltf.InstantiateMainSceneAsync(currentModel.transform);
                UpdateProgress(0.9f);

                if (instantiated)
                {
                    currentModel.transform.localScale = Vector3.one * importScale;

                    if (autoCenterModel)
                    {
                        CenterModel(currentModel);
                    }

                    UpdateProgress(1.0f);
                    UpdateLoadingText("Model loaded successfully!");
                    Debug.Log($"GLB model loaded with {gltf.MaterialCount} materials");

                    await System.Threading.Tasks.Task.Delay(2000);
                    UpdateLoadingText("");
                    UpdateProgress(0f);

                    ModelManager.Instance.CurrentModel = currentModel;
                }
                else
                {
                    Debug.LogError("Failed to instantiate GLB model");
                    UpdateLoadingText("Error: Failed to instantiate model");
                }
            }
            else
            {
                Debug.LogError("Failed to load GLB file");
                UpdateLoadingText("Error: Failed to load model");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading GLB: {e.Message}\n{e.StackTrace}");
            UpdateLoadingText($"Error: {e.Message}");
        }
    }

    /// <summary>
    /// Coroutine version for loading GLB (fallback if not using GLTFast)
    /// Creates a placeholder cube. To use real GLB loading:
    /// 1. Install GLTFast package from Package Manager
    /// 2. Add USING_GLTFAST to Project Settings > Player > Scripting Define Symbols
    /// 3. Restart Unity
    /// </summary>
    IEnumerator LoadGLBModelCoroutine(string filePath)
    {
        // Validate file
        if (!File.Exists(filePath))
        {
            Debug.LogError("File does not exist: " + filePath);
            UpdateLoadingText("Error: File not found");
            yield break;
        }

        UpdateLoadingText("Loading model...");
        UpdateProgress(0.1f);

        // Destroy previous model if exists
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        UpdateProgress(0.3f);

        // Read file bytes
        byte[] modelData = null;
        try
        {
            modelData = File.ReadAllBytes(filePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error reading file: {e.Message}");
            UpdateLoadingText($"Error: {e.Message}");
            yield break;
        }

        UpdateProgress(0.5f);

        Debug.Log($"GLB file loaded: {modelData.Length} bytes");
        UpdateLoadingText("Model data loaded. Parsing...");

        yield return new WaitForSeconds(0.5f); // Simulate processing time

        // PLACEHOLDER: Create a cube as demonstration
        // To load actual GLB files, enable USING_GLTFAST (see instructions above)
        currentModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        currentModel.name = Path.GetFileNameWithoutExtension(filePath) + " (Placeholder)";
        currentModel.transform.SetParent(modelParent);
        currentModel.transform.localScale = Vector3.one * importScale;

        if (autoCenterModel)
        {
            CenterModel(currentModel);
        }

        UpdateProgress(1.0f);
        UpdateLoadingText("Placeholder loaded. Enable USING_GLTFAST for real GLB support.");

        Debug.LogWarning("PLACEHOLDER MODE: Using cube instead of GLB.\n" +
                        "To enable real GLB loading:\n" +
                        "1. Install GLTFast: Window > Package Manager > + > Add package from git URL\n" +
                        "   Enter: https://github.com/atteneder/glTFast.git\n" +
                        "2. Add 'USING_GLTFAST' to: Edit > Project Settings > Player > Scripting Define Symbols\n" +
                        "3. Restart Unity");

        yield return new WaitForSeconds(3f);
        UpdateLoadingText("");
        UpdateProgress(0f);
    }

    /// <summary>
    /// Centers the model based on its renderer bounds
    /// </summary>
    void CenterModel(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        Vector3 center = bounds.center;
        model.transform.position -= center - modelParent.position;
    }

    /// <summary>
    /// Updates loading text UI
    /// </summary>
    void UpdateLoadingText(string message)
    {
        if (loadingText != null)
        {
            loadingText.text = message;
        }
    }

    /// <summary>
    /// Updates progress bar UI
    /// </summary>
    void UpdateProgress(float progress)
    {
        if (progressBar != null)
        {
            progressBar.value = progress;
        }
    }

    /// <summary>
    /// Clears the currently loaded model
    /// </summary>
    public void ClearModel()
    {
        if (currentModel != null)
        {
            Destroy(currentModel);
            currentModel = null;
            Debug.Log("Model cleared");
        }
    }
}