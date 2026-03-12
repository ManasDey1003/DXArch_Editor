// using UnityEngine;
// using System;
// using System.IO;
// using System.Threading.Tasks;
// using System.Collections.Generic;
// using GLTFast;
// using UnityEngine.Android;
// using SimpleFileBrowser;
// // using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
// using UnityEngine.UI;


// public class GLBLoader : MonoBehaviour
// {
//     // [Header("Spawner Integration")]
//     // [Tooltip("The ObjectSpawner that will spawn the loaded model")]
//     // public ObjectSpawner objectSpawner;

//     /* [Tooltip("Replace existing prefabs in spawner (false = add to list)")]
//     public bool replaceSpawnerPrefabs = true; */

//     [Header("Mesh Filtration")]
//     [Tooltip("Filter out small meshes on import")]
//     public bool filterMeshesOnImport = true;

//     [Tooltip("Minimum bounding box size for meshes (world units)")]
//     public Vector3 minimumMeshSize = new Vector3(10f, 10f, 10f);

//     [Header("Optimization Settings")]
//     [Tooltip("Combine meshes with same material for better performance")]
//     public bool combineMeshes = true;

//     [Tooltip("Maximum vertices per combined mesh (Unity limit is 65535)")]
//     public int maxVerticesPerMesh = 65000;

//     [Tooltip("Maximum world-unit radius for grouping meshes into a single spatial cluster")]
//     public float spatialClusterRadius = 5f;

//     [Header("Texture Simplification")]
//     [Tooltip("Replace textures with average colors based on mesh size")]
//     public bool simplifyTexturesBySize = true;

//     [Tooltip("Meshes LARGER than this size will have textures replaced with average colors (world units - diagonal of bounds)")]
//     public float largeMeshThreshold = 2.0f;

//     [Tooltip("Apply texture simplification BEFORE combining meshes")]
//     public bool simplifyBeforeCombining = true;

//     [Header("UI Settings")]
//     [Tooltip("Show file picker on start (useful for testing)")]
//     public bool showPickerOnStart = false;

//     [Tooltip("Loading indicator object to toggle during loading")]
//     public GameObject loadingIndicator;
//     public Image loadingFillImage;

//     [Tooltip("Current loading progress (0-100)")]
//     public int loadingPercentage = 0;

//     [Header("Loading Animation")]
//     [Tooltip("Speed of loading bar lerp animation")]
//     public float lerpSpeed = 5f;

//     [Header("Editor Optimization")]
//     [Tooltip("Model to optimize in editor (use context menu 'Optimize Model')")]
//     public GameObject model;

//     [Header("Material Transparency")]
//     [Tooltip("Slider to control global transparency (assign in inspector)")]
//     public Slider transparencySlider;
//     public Toggle WireframeToggle;

//     private List<Material> trackedMaterials = new List<Material>();

//     public string currentModelPath;
//     private static GameObject currentSpawnedModel;
//     private float targetFillAmount = 0f;
//     private float currentFillAmount = 0f;

//     // -------------------------------------------------------------------------
//     //  Unity lifecycle
//     // -------------------------------------------------------------------------

//     void Start()
//     {
//         HideLoading();
//         if (showPickerOnStart)
//             ShowFilePicker();

//         if (transparencySlider != null)
//         {
//             transparencySlider.value = 1f;
//             transparencySlider.onValueChanged.AddListener(OnTransparencyChanged);
//         }
//     }

//     void Update()
//     {
//         if (loadingFillImage != null && Mathf.Abs(currentFillAmount - targetFillAmount) > 0.001f)
//         {
//             currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, Time.deltaTime * lerpSpeed);
//             loadingFillImage.fillAmount = currentFillAmount;
//         }
//     }

//     // -------------------------------------------------------------------------
//     //  Transparency / Wireframe
//     // -------------------------------------------------------------------------

//     public void OnTransparencyChanged(float alpha) => UpdateAllMaterialTransparency(alpha);

//     public void UpdateAllMaterialTransparency(float alpha)
//     {
//         alpha = Mathf.Clamp01(alpha);
//         foreach (Material mat in trackedMaterials)
//         {
//             if (mat == null) continue;
//             mat.SetFloat("_Alpha", alpha);
//             mat.renderQueue = (alpha >= 1.0f) ? 2000 : 3000;
//         }
//     }

//     public void RegisterMaterialsForTransparency(GameObject model)
//     {
//         trackedMaterials.Clear();
//         MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>();
//         foreach (MeshRenderer renderer in renderers)
//         {
//             foreach (Material mat in renderer.sharedMaterials)
//             {
//                 if (mat != null && !trackedMaterials.Contains(mat))
//                 {
//                     SetupMaterialForTransparency(mat);
//                     trackedMaterials.Add(mat);
//                 }
//             }
//         }
//         Debug.Log($"Registered {trackedMaterials.Count} materials for transparency control");
//     }

//     private void SetupMaterialForTransparency(Material mat)
//     {
//         mat.SetFloat("_Surface", 1);
//         mat.SetFloat("_Blend", 0);
//         mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
//         mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
//         mat.SetInt("_ZWrite", 0);
//         mat.renderQueue = 3000;
//         mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
//         mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
//     }

//     public void SetWireframe(bool enabled)
//     {
//         float wireframeValue = enabled ? 1f : 0f;
//         foreach (Material mat in trackedMaterials)
//         {
//             if (mat == null) continue;
//             mat.SetFloat("_Wireframe", wireframeValue);
//         }
//         Debug.Log($"Wireframe set to {enabled} for {trackedMaterials.Count} materials");
//     }

//     // -------------------------------------------------------------------------
//     //  Loading indicator
//     // -------------------------------------------------------------------------

//     public void ShowLoading()
//     {
//         loadingIndicator?.SetActive(true);
//         currentFillAmount = 0f;
//         targetFillAmount = 0f;
//         if (loadingFillImage != null)
//             loadingFillImage.fillAmount = 0f;
//     }

//     public void HideLoading() => loadingIndicator?.SetActive(false);

//     public void SetLoadingPercentage(int percentage)
//     {
//         loadingPercentage = Mathf.Clamp(percentage, 0, 100);
//         targetFillAmount = loadingPercentage / 100f;
//     }

//     // -------------------------------------------------------------------------
//     //  Mesh Filtration (NEW)
//     // -------------------------------------------------------------------------

//     /// <summary>
//     /// Filters out small meshes from the imported model based on bounding box size.
//     /// This is called immediately after import, before any optimization.
//     /// </summary>
//     private void FilterSmallMeshes(GameObject model)
//     {
//         if (!filterMeshesOnImport)
//         {
//             Debug.Log("Mesh filtration disabled, skipping");
//             return;
//         }

//         Debug.Log($"=== FILTERING SMALL MESHES ON IMPORT ===");
//         Debug.Log($"Minimum size threshold: {minimumMeshSize}");

//         List<GameObject> objectsToRemove = new List<GameObject>();
//         int totalScanned = 0;
//         int filteredCount = 0;

//         // Get all mesh filters in the hierarchy
//         MeshFilter[] meshFilters = model.GetComponentsInChildren<MeshFilter>();
//         Debug.Log($"Found {meshFilters.Length} meshes to scan");

//         foreach (MeshFilter meshFilter in meshFilters)
//         {
//             if (meshFilter == null || meshFilter.sharedMesh == null)
//                 continue;

//             totalScanned++;

//             // Get the mesh bounds
//             Bounds bounds = meshFilter.sharedMesh.bounds;

//             // Account for object's scale
//             Transform transform = meshFilter.transform;
//             Vector3 scaledSize = new Vector3(
//                 bounds.size.x * Mathf.Abs(transform.lossyScale.x),
//                 bounds.size.y * Mathf.Abs(transform.lossyScale.y),
//                 bounds.size.z * Mathf.Abs(transform.lossyScale.z)
//             );

//             // Check if any dimension is below the threshold
//             if (scaledSize.x < minimumMeshSize.x &&
//                 scaledSize.y < minimumMeshSize.y &&
//                 scaledSize.z < minimumMeshSize.z)
//             {
//                 filteredCount++;
//                 string path = GetGameObjectPath(meshFilter.transform);
//                 Debug.Log($"  Filtering: {path} - Size: {scaledSize:F2}");

//                 if (!objectsToRemove.Contains(meshFilter.gameObject))
//                 {
//                     objectsToRemove.Add(meshFilter.gameObject);
//                 }
//             }
//         }

//         // Remove filtered objects
//         foreach (GameObject obj in objectsToRemove)
//         {
//             DestroyImmediate(obj);
//         }

//         Debug.Log($"=== MESH FILTRATION COMPLETE ===");
//         Debug.Log($"Scanned: {totalScanned} meshes");
//         Debug.Log($"Filtered: {filteredCount} meshes");
//         Debug.Log($"Remaining: {totalScanned - filteredCount} meshes");
//     }

//     /// <summary>
//     /// Gets the full hierarchy path of a GameObject for debugging.
//     /// </summary>
//     private string GetGameObjectPath(Transform transform)
//     {
//         string path = transform.name;
//         while (transform.parent != null)
//         {
//             transform = transform.parent;
//             path = transform.name + "/" + path;
//         }
//         return path;
//     }

//     // -------------------------------------------------------------------------
//     //  Editor optimization
//     // -------------------------------------------------------------------------

//     [ContextMenu("Optimize Model")]
//     public void OptimizeModelInEditor()
//     {
//         if (model == null)
//         {
//             Debug.LogError("No model assigned! Please assign a GameObject to the 'Model' field.");
//             return;
//         }

//         Debug.Log($"=== STARTING EDITOR OPTIMIZATION FOR: {model.name} ===");

//         // Apply mesh filtration first
//         FilterSmallMeshes(model);

//         var optimizer = new ModelOptimizer();
//         optimizer.combineMeshes = combineMeshes;
//         optimizer.maxVerticesPerMesh = maxVerticesPerMesh;
//         optimizer.spatialClusterRadius = spatialClusterRadius;
//         optimizer.simplifyTexturesBySize = simplifyTexturesBySize;
//         optimizer.largeMeshThreshold = largeMeshThreshold;
//         optimizer.simplifyBeforeCombining = simplifyBeforeCombining;

//         optimizer.ApplyOptimizations(model);

//         Debug.Log($"=== OPTIMIZATION COMPLETE FOR: {model.name} ===");
//         Debug.Log("You can now save this as a prefab for use in your AR application!");
//     }

//     // -------------------------------------------------------------------------
//     //  Android permissions
//     // -------------------------------------------------------------------------

//     private void RequestAndroidPermissions()
//     {
//         Debug.Log("=== REQUESTING ANDROID PERMISSIONS ===");

//         if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
//         {
//             Debug.Log("Requesting read permission...");
//             Permission.RequestUserPermission(Permission.ExternalStorageRead);
//         }
//         else
//         {
//             Debug.Log("Read permission already granted");
//         }

//         if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
//         {
//             Debug.Log("Requesting write permission...");
//             Permission.RequestUserPermission(Permission.ExternalStorageWrite);
//         }
//         else
//         {
//             Debug.Log("Write permission already granted");
//         }
//     }

//     // -------------------------------------------------------------------------
//     //  File picker
//     // -------------------------------------------------------------------------

//     public void ShowFilePicker()
//     {
//         Debug.Log("=== SHOW FILE PICKER CALLED ===");

// #if UNITY_ANDROID
//         RequestAndroidPermissions();
// #endif

//         FileBrowser.SetFilters(true, new FileBrowser.Filter("3D Models", ".glb", ".gltf"));
//         FileBrowser.SetDefaultFilter(".glb");

//         Debug.Log("Opening file browser dialog...");

//         FileBrowser.ShowLoadDialog(
//             onSuccess: (paths) =>
//             {
//                 Debug.Log($"File browser success! Paths count: {paths.Length}");
//                 OnFileSelected(paths[0]);
//             },
//             onCancel: () => Debug.Log("File picker cancelled by user"),
//             pickMode: FileBrowser.PickMode.Files,
//             allowMultiSelection: false,
//             initialPath: null,
//             initialFilename: null,
//             title: "Select GLB Model",
//             loadButtonText: "Load"
//         );

//         Debug.Log("FileBrowser.ShowLoadDialog called");
//     }

//     private void OnFileSelected(string path)
//     {
//         Debug.Log($"=== FILE SELECTED ===");
//         Debug.Log($"Raw path: {path}");

//         ShowLoading();
//         SetLoadingPercentage(0);

// #if UNITY_ANDROID
//         if (path.StartsWith("content://"))
//         {
//             Debug.Log("Android content URI detected, copying file...");
//             StartCoroutine(HandleAndroidContentUri(path));
//         }
//         else
//         {
//             Debug.Log("Using path directly");
//             SetupSpawnerPrefab(path);
//         }
// #else
//         SetupSpawnerPrefab(path);
// #endif
//     }


//     public void ShowBinFilePicker(string glbPath)
//     {
//         Debug.Log("=== REQUESTING .BIN FILE ===");

// #if UNITY_ANDROID
//         RequestAndroidPermissions();
// #endif

//         FileBrowser.SetFilters(true, new FileBrowser.Filter("Binary Data", ".bin"));
//         FileBrowser.SetDefaultFilter(".bin");

//         FileBrowser.ShowLoadDialog(
//             onSuccess: (paths) =>
//             {
//                 Debug.Log($"BIN file selected: {paths[0]}");
//                 OnBinFileSelected(paths[0], glbPath);
//             },
//             onCancel: () =>
//             {
//                 Debug.Log("BIN file picker cancelled - continuing without LOD");
//                 // Continue without LOD data
//             },
//             pickMode: FileBrowser.PickMode.Files,
//             allowMultiSelection: false,
//             initialPath: null,
//             initialFilename: null,
//             title: "Select .bin file for LOD data",
//             loadButtonText: "Load"
//         );
//     }

//     private void OnBinFileSelected(string binPath, string glbPath)
//     {
//         Debug.Log($"=== BIN FILE SELECTED ===");
//         Debug.Log($"BIN path: {binPath}");
//         Debug.Log($"Associated GLB path: {glbPath}");

// #if UNITY_ANDROID
//         if (binPath.StartsWith("content://"))
//         {
//             Debug.Log("Android content URI detected for BIN, copying file...");
//             StartCoroutine(HandleAndroidContentUriForBin(binPath, glbPath));
//         }
//         else
//         {
//             Debug.Log("Using BIN path directly");
//             // Store the bin path somewhere accessible
//             PlayerPrefs.SetString("LastBinPath", binPath);
//         }
// #else
//     PlayerPrefs.SetString("LastBinPath", binPath);
// #endif

//     }

//     private System.Collections.IEnumerator HandleAndroidContentUri(string contentUri)
//     {
//         Debug.Log("Copying file from Android content URI...");
//         SetLoadingPercentage(10);

//         byte[] fileData = null;
//         try
//         {
//             fileData = FileBrowserHelpers.ReadBytesFromFile(contentUri);
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"Failed to read file: {e.Message}");
//             HideLoading();
//             yield break;
//         }

//         if (fileData == null || fileData.Length == 0)
//         {
//             Debug.LogError("File data is empty or null");
//             HideLoading();
//             yield break;
//         }

//         Debug.Log($"Read {fileData.Length} bytes from content URI");
//         SetLoadingPercentage(20);

//         string fileName = $"glbModel.glb";
//         string cachePath = Path.Combine(Application.persistentDataPath, fileName);

//         try
//         {
//             File.WriteAllBytes(cachePath, fileData);
//             Debug.Log($"File copied to: {cachePath}");
//             SetupSpawnerPrefab(cachePath);
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"Failed to write file to cache: {e.Message}");
//             HideLoading();
//         }
//     }
//     private System.Collections.IEnumerator HandleAndroidContentUriForBin(string contentUri, string associatedGlbPath)
//     {
//         Debug.Log("Copying BIN file from Android content URI...");

//         byte[] fileData = null;
//         try
//         {
//             fileData = FileBrowserHelpers.ReadBytesFromFile(contentUri);
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"Failed to read BIN file: {e.Message}");
//             yield break;
//         }

//         if (fileData == null || fileData.Length == 0)
//         {
//             Debug.LogError("BIN file data is empty or null");
//             yield break;
//         }

//         Debug.Log($"Read {fileData.Length} bytes from BIN content URI");

//         // Use the same naming convention as the GLB file
//         string fileName = Path.GetFileNameWithoutExtension(associatedGlbPath) + ".bin";
//         string cachePath = Path.Combine(Application.persistentDataPath, fileName);

//         try
//         {
//             File.WriteAllBytes(cachePath, fileData);
//             Debug.Log($"BIN file copied to: {cachePath}");

//             // Store the path for later use
//             PlayerPrefs.SetString("LastBinPath", cachePath);
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"Failed to write BIN file to cache: {e.Message}");
//         }
//     }


//     // -------------------------------------------------------------------------
//     //  Spawner setup
//     // -------------------------------------------------------------------------

//     private void SetupSpawnerPrefab(string path)
//     {
//         Debug.Log($"=== SETUP SPAWNER PREFAB ===");
//         Debug.Log($"Path: {path}");
//         Debug.Log($"File exists: {File.Exists(path)}");

//         if (!File.Exists(path))
//         {
//             Debug.LogError($"File not found at: {path}");
//             HideLoading();
//             return;
//         }

//         currentModelPath = path;
//         Debug.Log($"Model path stored: {currentModelPath}");

//         if (objectSpawner != null)
//         {
//             Debug.Log("ObjectSpawner found, creating spawner prefab...");

//             var spawnerPrefab = new GameObject("GLBSpawnerPrefab");
//             var loader = spawnerPrefab.AddComponent<GLBSpawnerPrefab>();
//             loader.glbPath = currentModelPath;
//             loader.filterMeshesOnImport = filterMeshesOnImport;
//             loader.minimumMeshSize = minimumMeshSize;
//             loader.combineMeshes = combineMeshes;
//             loader.maxVerticesPerMesh = maxVerticesPerMesh;
//             loader.spatialClusterRadius = spatialClusterRadius;
//             loader.simplifyTexturesBySize = simplifyTexturesBySize;
//             loader.largeMeshThreshold = largeMeshThreshold;
//             loader.simplifyBeforeCombining = simplifyBeforeCombining;
//             loader.glbLoader = this;

//             Debug.Log($"GLBSpawnerPrefab created with path: {loader.glbPath}");

//             spawnerPrefab.transform.SetParent(transform);
//             spawnerPrefab.SetActive(false);

//             Debug.Log("=== PRELOADING MODEL ===");
//             loader.PreloadModel();

//             Debug.Log("Spawner prefab parented and deactivated");

//             if (replaceSpawnerPrefabs)
//             {
//                 Debug.Log($"Clearing existing prefabs (count: {objectSpawner.objectPrefabs.Count})");
//                 objectSpawner.objectPrefabs.Clear();
//                 objectSpawner.objectPrefabs.Add(spawnerPrefab);
//                 objectSpawner.spawnOptionIndex = 0;
//                 Debug.Log("Replaced spawner prefabs with loaded model");
//             }
//             else
//             {
//                 Debug.Log($"Adding to existing prefabs (current count: {objectSpawner.objectPrefabs.Count})");
//                 objectSpawner.objectPrefabs.Add(spawnerPrefab);
//                 objectSpawner.spawnOptionIndex = objectSpawner.objectPrefabs.Count - 1;
//                 Debug.Log($"Added model to spawner. Total prefabs: {objectSpawner.objectPrefabs.Count}");
//             }

//             Debug.Log($"Final spawn option index: {objectSpawner.spawnOptionIndex}");
//             Debug.Log("Model is ready!");
//         }
//         else
//         {
//             Debug.LogError("ObjectSpawner is NULL! Assign it in the inspector to enable spawning.");
//             HideLoading();
//         }

//         Debug.Log("=== END SETUP SPAWNER PREFAB ===");
//     }

//     public static void SetCurrentSpawnedModel(GameObject model) => currentSpawnedModel = model;
//     public static GameObject GetCurrentSpawnedModel() => currentSpawnedModel;

//     // -------------------------------------------------------------------------
//     //  Clear
//     // -------------------------------------------------------------------------

//     public void ClearLoadedModel()
//     {
//         currentModelPath = null;
//         currentSpawnedModel = null;
//         if (objectSpawner != null)
//         {
//             objectSpawner.objectPrefabs.RemoveAll(prefab =>
//                 prefab != null && prefab.GetComponent<GLBSpawnerPrefab>() != null);
//             Debug.Log("Cleared loaded model from spawner");
//         }
//     }

//     // =========================================================================
//     //  ModelOptimizer
//     // =========================================================================

//     public class ModelOptimizer
//     {
//         public bool combineMeshes = true;
//         public int maxVerticesPerMesh = 65000;

//         /// <summary>Maximum world-unit radius for grouping meshes into one spatial cluster.</summary>
//         public float spatialClusterRadius = 5f;

//         public bool simplifyTexturesBySize = true;
//         public float largeMeshThreshold = 2.0f;
//         public bool simplifyBeforeCombining = true;

//         private Dictionary<Texture2D, Color> textureAverageCache = new Dictionary<Texture2D, Color>();
//         private Dictionary<MaterialKey, Material> simplifiedMaterialCache = new Dictionary<MaterialKey, Material>();

//         private struct MaterialKey
//         {
//             public Shader shader;
//             public Color color;

//             public MaterialKey(Shader shader, Color color)
//             {
//                 this.shader = shader;
//                 this.color = color;
//             }

//             public override bool Equals(object obj)
//             {
//                 if (!(obj is MaterialKey)) return false;
//                 MaterialKey other = (MaterialKey)obj;
//                 return shader == other.shader &&
//                        Mathf.Approximately(color.r, other.color.r) &&
//                        Mathf.Approximately(color.g, other.color.g) &&
//                        Mathf.Approximately(color.b, other.color.b) &&
//                        Mathf.Approximately(color.a, other.color.a);
//             }

//             public override int GetHashCode() => shader.GetHashCode() ^ color.GetHashCode();
//         }

//         // ------------------------------------------------------------------
//         //  Public entry points
//         // ------------------------------------------------------------------

//         public void ApplyOptimizations(GameObject model)
//         {
//             if (simplifyTexturesBySize && simplifyBeforeCombining)
//             {
//                 Debug.Log("=== SIMPLIFYING TEXTURES (BEFORE COMBINING) ===");
//                 SimplifyTexturesByMeshSize(model);
//             }

//             if (combineMeshes)
//             {
//                 Debug.Log("=== STARTING MESH COMBINING ===");
//                 CombineMeshesByMaterial(model);
//                 Debug.Log("=== MESH COMBINING COMPLETE ===");
//             }

//             if (simplifyTexturesBySize && !simplifyBeforeCombining)
//             {
//                 Debug.Log("=== SIMPLIFYING TEXTURES (AFTER COMBINING) ===");
//                 SimplifyTexturesByMeshSize(model);
//             }

//             for (int i = 0; i < model.transform.childCount; i++)
//             {
//                 var child = model.transform.GetChild(i);
//                 Debug.Log($"  Final Child {i}: {child.name}");
//                 var renderers = child.GetComponentsInChildren<Renderer>();
//                 Debug.Log($"    Renderers: {renderers.Length}");
//             }
//         }

//         public async Task ApplyOptimizationsAsync(GameObject model, Action<float> onProgress = null)
//         {
//             float currentProgress = 0f;
//             int totalSteps = 0;

//             bool doSimplifyBefore = simplifyTexturesBySize && simplifyBeforeCombining;
//             bool doCombine = combineMeshes;
//             bool doSimplifyAfter = simplifyTexturesBySize && !simplifyBeforeCombining;

//             if (doSimplifyBefore) totalSteps++;
//             if (doCombine) totalSteps += 3;
//             if (doSimplifyAfter) totalSteps++;

//             if (totalSteps == 0)
//             {
//                 onProgress?.Invoke(1f);
//                 return;
//             }

//             float progressPerUnit = 1f / totalSteps;

//             if (doSimplifyBefore)
//             {
//                 Debug.Log("=== SIMPLIFYING TEXTURES (BEFORE COMBINING) ===");
//                 await SimplifyTexturesByMeshSizeAsync(model, p =>
//                     onProgress?.Invoke(currentProgress + (p * progressPerUnit)));
//                 currentProgress += progressPerUnit;
//             }

//             if (doCombine)
//             {
//                 Debug.Log("=== STARTING MESH COMBINING ===");
//                 float combineWeight = progressPerUnit * 3;
//                 await CombineMeshesByMaterialAsync(model, p =>
//                     onProgress?.Invoke(currentProgress + (p * combineWeight)));
//                 currentProgress += combineWeight;
//                 Debug.Log("=== MESH COMBINING COMPLETE ===");
//             }

//             if (doSimplifyAfter)
//             {
//                 Debug.Log("=== SIMPLIFYING TEXTURES (AFTER COMBINING) ===");
//                 await SimplifyTexturesByMeshSizeAsync(model, p =>
//                     onProgress?.Invoke(currentProgress + (p * progressPerUnit)));
//             }

//             onProgress?.Invoke(1f);
//         }

//         // ------------------------------------------------------------------
//         //  Texture simplification
//         // ------------------------------------------------------------------

//         private void SimplifyTexturesByMeshSize(GameObject targetModel)
//         {
//             MeshRenderer[] renderers = targetModel.GetComponentsInChildren<MeshRenderer>();
//             int simplifiedCount = 0, skippedCount = 0, sharedMaterialCount = 0;

//             foreach (MeshRenderer renderer in renderers)
//             {
//                 float meshSize = renderer.bounds.size.magnitude;
//                 Debug.Log($"Mesh: {renderer.name}, Size: {meshSize:F2}, Threshold: {largeMeshThreshold:F2}");

//                 if (meshSize >= largeMeshThreshold)
//                 {
//                     Debug.Log($"  → LARGE mesh - simplifying textures");
//                     Material[] materials = renderer.sharedMaterials;
//                     bool materialsChanged = false;

//                     for (int i = 0; i < materials.Length; i++)
//                     {
//                         Material mat = materials[i];
//                         if (mat == null) continue;

//                         Material simplifiedMat = GetOrCreateSimplifiedMaterial(mat);
//                         if (simplifiedMat != null && simplifiedMat != mat)
//                         {
//                             materials[i] = simplifiedMat;
//                             materialsChanged = true;
//                             simplifiedCount++;

//                             int cacheCount = 0;
//                             bool isReused = false;
//                             foreach (var cachedMat in simplifiedMaterialCache.Values)
//                             {
//                                 if (cachedMat == simplifiedMat)
//                                 {
//                                     cacheCount++;
//                                     if (cacheCount > 1) { isReused = true; break; }
//                                 }
//                             }
//                             if (isReused) sharedMaterialCount++;
//                         }
//                     }

//                     if (materialsChanged)
//                         renderer.sharedMaterials = materials;
//                 }
//                 else
//                 {
//                     Debug.Log($"  → Small mesh - keeping detailed texture");
//                     skippedCount++;
//                 }
//             }

//             Debug.Log($"Texture simplification complete: {simplifiedCount} simplified, {skippedCount} kept, {sharedMaterialCount} reused");
//         }

//         private async Task SimplifyTexturesByMeshSizeAsync(GameObject targetModel, Action<float> onProgress = null)
//         {
//             MeshRenderer[] renderers = targetModel.GetComponentsInChildren<MeshRenderer>();
//             int simplifiedCount = 0, skippedCount = 0;
//             var stopwatch = System.Diagnostics.Stopwatch.StartNew();

//             for (int k = 0; k < renderers.Length; k++)
//             {
//                 onProgress?.Invoke((float)k / renderers.Length);

//                 if (stopwatch.ElapsedMilliseconds > 10)
//                 {
//                     await Task.Yield();
//                     stopwatch.Restart();
//                 }

//                 MeshRenderer renderer = renderers[k];
//                 if (renderer == null) continue;

//                 float meshSize = renderer.bounds.size.magnitude;

//                 if (meshSize >= largeMeshThreshold)
//                 {
//                     Material[] materials = renderer.sharedMaterials;
//                     bool materialsChanged = false;

//                     for (int i = 0; i < materials.Length; i++)
//                     {
//                         Material mat = materials[i];
//                         if (mat == null) continue;

//                         Material simplifiedMat = GetOrCreateSimplifiedMaterial(mat);
//                         if (simplifiedMat != null && simplifiedMat != mat)
//                         {
//                             materials[i] = simplifiedMat;
//                             materialsChanged = true;
//                             simplifiedCount++;
//                         }
//                     }

//                     if (materialsChanged)
//                         renderer.sharedMaterials = materials;
//                 }
//                 else
//                 {
//                     skippedCount++;
//                 }
//             }

//             onProgress?.Invoke(1f);
//             Debug.Log($"Texture simplification complete (Async): {simplifiedCount} simplified, {skippedCount} skipped.");
//         }

//         // ------------------------------------------------------------------
//         //  Material helpers
//         // ------------------------------------------------------------------

//         private float ColorDifference(Color a, Color b)
//         {
//             float dr = (a.r - b.r) * 0.299f;
//             float dg = (a.g - b.g) * 0.587f;
//             float db = (a.b - b.b) * 0.114f;
//             float da = (a.a - b.a) * 0.5f;
//             return Mathf.Sqrt(dr * dr + dg * dg + db * db + da * da);
//         }

//         private Material FindSimilarCachedMaterial(Shader shader, Color targetColor, float threshold = 0.05f)
//         {
//             foreach (var kvp in simplifiedMaterialCache)
//             {
//                 if (kvp.Key.shader != shader) continue;
//                 if (ColorDifference(kvp.Key.color, targetColor) <= threshold)
//                 {
//                     Debug.Log($"    ✓ Color merge: new {targetColor} within {threshold * 100:F0}% of cached {kvp.Key.color} → reusing");
//                     return kvp.Value;
//                 }
//             }
//             return null;
//         }

//         private Material GetOrCreateSimplifiedMaterial(Material originalMaterial)
//         {
//             if (originalMaterial == null) return null;

//             Shader customShader = Shader.Find("Custom/TransparentWithDepth");
//             if (customShader == null)
//             {
//                 Debug.LogError("    Custom/TransparentWithDepth shader not found!");
//                 return originalMaterial;
//             }

//             Texture mainTex = originalMaterial.mainTexture;

//             if (mainTex == null)
//             {
//                 Debug.Log($"    No main texture on {originalMaterial.name}, using existing color");
//                 Color existingColor = Color.white;
//                 if (originalMaterial.HasProperty("_BaseColor"))
//                     existingColor = originalMaterial.GetColor("_BaseColor");
//                 else if (originalMaterial.HasProperty("_Color"))
//                     existingColor = originalMaterial.GetColor("_Color");
//                 else
//                     existingColor = originalMaterial.color;

//                 MaterialKey key1 = new MaterialKey(customShader, existingColor);
//                 if (simplifiedMaterialCache.ContainsKey(key1))
//                 {
//                     Debug.Log($"    ✓ Reusing existing material (no texture, exact match)");
//                     return simplifiedMaterialCache[key1];
//                 }

//                 Material similar1 = FindSimilarCachedMaterial(customShader, existingColor);
//                 if (similar1 != null) return similar1;

//                 Material newMat1 = new Material(customShader);
//                 string hexColor1 = ColorUtility.ToHtmlStringRGBA(existingColor);
//                 newMat1.name = $"SimplifiedCustom_NoTexture_{hexColor1}";
//                 newMat1.SetColor("_Color", existingColor);
//                 newMat1.SetFloat("_Alpha", 1.0f);
//                 simplifiedMaterialCache[key1] = newMat1;
//                 Debug.Log($"    ✓ Created material (no texture) color: {existingColor}");
//                 return newMat1;
//             }

//             if (!(mainTex is Texture2D texture2D))
//             {
//                 Debug.LogWarning($"    Texture is not Texture2D: {mainTex.GetType().Name}");
//                 Color fallbackColor = new Color(0.5f, 0.5f, 0.5f, 1f);

//                 Material similarFallback = FindSimilarCachedMaterial(customShader, fallbackColor);
//                 if (similarFallback != null) return similarFallback;

//                 Material fallbackMat = new Material(customShader);
//                 fallbackMat.name = $"SimplifiedCustom_UnreadableTexture_{originalMaterial.GetHashCode()}";
//                 fallbackMat.SetColor("_Color", fallbackColor);
//                 fallbackMat.SetFloat("_Alpha", 1.0f);
//                 simplifiedMaterialCache[new MaterialKey(customShader, fallbackColor)] = fallbackMat;
//                 return fallbackMat;
//             }

//             Color averageColor;
//             if (textureAverageCache.ContainsKey(texture2D))
//             {
//                 averageColor = textureAverageCache[texture2D];
//                 Debug.Log($"    Using cached average color for {texture2D.name}");
//             }
//             else
//             {
//                 averageColor = GetAverageColorFromTexture(texture2D);
//                 if (averageColor == Color.clear)
//                 {
//                     Debug.LogWarning($"    Could not get average color, using default gray");
//                     averageColor = new Color(0.5f, 0.5f, 0.5f, 1f);
//                 }
//                 textureAverageCache[texture2D] = averageColor;
//             }

//             MaterialKey key = new MaterialKey(customShader, averageColor);
//             if (simplifiedMaterialCache.ContainsKey(key))
//             {
//                 Debug.Log($"    ✓ Reusing existing simplified material (exact match)");
//                 return simplifiedMaterialCache[key];
//             }

//             Material similar = FindSimilarCachedMaterial(customShader, averageColor);
//             if (similar != null) return similar;

//             Material newMat = new Material(customShader);
//             var hexColor = $"#{ColorUtility.ToHtmlStringRGBA(averageColor)}";
//             newMat.name = $"SimplifiedCustom_Color_{hexColor}";
//             newMat.SetColor("_Color", averageColor);
//             newMat.SetFloat("_Alpha", 1.0f);
//             simplifiedMaterialCache[key] = newMat;

//             Debug.Log($"    ✓ Created simplified material {newMat.name} (color: {averageColor})");
//             return newMat;
//         }

//         private Color GetAverageColorFromTexture(Texture2D texture)
//         {
//             if (texture == null) return Color.clear;

//             try
//             {
//                 Color[] pixels = texture.GetPixels();
//                 float r = 0, g = 0, b = 0, a = 0;
//                 foreach (Color pixel in pixels) { r += pixel.r; g += pixel.g; b += pixel.b; a += pixel.a; }
//                 int count = pixels.Length;
//                 Color avgColor = new Color(r / count, g / count, b / count, a / count);
//                 Debug.Log($"    Calculated average color from {texture.name}: {avgColor}");
//                 return avgColor;
//             }
//             catch (Exception e)
//             {
//                 Debug.LogWarning($"    Texture {texture.name} not readable, attempting to copy: {e.Message}");
//                 try
//                 {
//                     return GetAverageColorFromUnreadableTexture(texture);
//                 }
//                 catch (Exception ex)
//                 {
//                     Debug.LogError($"    Failed to read texture {texture.name}: {ex.Message}");
//                     return new Color(0.5f, 0.5f, 0.5f, 1f);
//                 }
//             }
//         }

//         private Color GetAverageColorFromUnreadableTexture(Texture2D texture)
//         {
//             try
//             {
//                 int sampleSize = Mathf.Min(256, texture.width, texture.height);
//                 RenderTexture rt = RenderTexture.GetTemporary(sampleSize, sampleSize, 0,
//                     RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
//                 RenderTexture previous = RenderTexture.active;

//                 Graphics.Blit(texture, rt);
//                 RenderTexture.active = rt;

//                 Texture2D readableTexture = new Texture2D(sampleSize, sampleSize, TextureFormat.ARGB32, false);
//                 readableTexture.ReadPixels(new Rect(0, 0, sampleSize, sampleSize), 0, 0);
//                 readableTexture.Apply();

//                 RenderTexture.active = previous;
//                 RenderTexture.ReleaseTemporary(rt);

//                 Color[] pixels = readableTexture.GetPixels();
//                 float r = 0, g = 0, b = 0, a = 0;
//                 foreach (Color pixel in pixels) { r += pixel.r; g += pixel.g; b += pixel.b; a += pixel.a; }
//                 int count = pixels.Length;
//                 Color avgColor = new Color(r / count, g / count, b / count, a / count);

//                 UnityEngine.Object.DestroyImmediate(readableTexture);

//                 Debug.Log($"    Calculated average from unreadable texture: {avgColor}");
//                 return avgColor;
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"    RenderTexture fallback failed for {texture.name}: {e.Message}");
//                 return new Color(0.5f, 0.5f, 0.5f, 1f);
//             }
//         }

//         // ------------------------------------------------------------------
//         //  Spatial clustering
//         // ------------------------------------------------------------------

//         /// <summary>
//         /// Groups CombineInstances into spatial clusters so that only nearby
//         /// meshes are combined together. This keeps the weighted pivot accurate
//         /// and LOD distances meaningful.
//         /// </summary>
//         private List<List<CombineInstance>> ClusterByProximity(List<CombineInstance> combines, float maxRadius)
//         {
//             var remaining = new List<CombineInstance>(combines);
//             var clusters = new List<List<CombineInstance>>();

//             while (remaining.Count > 0)
//             {
//                 // Seed a new cluster with the first remaining mesh
//                 var cluster = new List<CombineInstance>();
//                 CombineInstance seed = remaining[0];
//                 remaining.RemoveAt(0);
//                 cluster.Add(seed);

//                 // Cluster center starts at the seed's world-space bounds center
//                 Vector3 clusterCenter = seed.transform.MultiplyPoint3x4(seed.mesh.bounds.center);

//                 // Greedily absorb any remaining mesh whose center is within the radius
//                 for (int i = remaining.Count - 1; i >= 0; i--)
//                 {
//                     CombineInstance ci = remaining[i];
//                     if (ci.mesh == null) { remaining.RemoveAt(i); continue; }

//                     Vector3 meshCenter = ci.transform.MultiplyPoint3x4(ci.mesh.bounds.center);

//                     if (Vector3.Distance(meshCenter, clusterCenter) <= maxRadius)
//                     {
//                         cluster.Add(ci);
//                         remaining.RemoveAt(i);

//                         // Recompute cluster center as unweighted average of all members
//                         clusterCenter = Vector3.zero;
//                         foreach (var c in cluster)
//                             clusterCenter += c.transform.MultiplyPoint3x4(c.mesh.bounds.center);
//                         clusterCenter /= cluster.Count;
//                     }
//                 }

//                 clusters.Add(cluster);
//             }

//             Debug.Log($"  Proximity clustering: {combines.Count} meshes → {clusters.Count} clusters (radius: {maxRadius})");
//             return clusters;
//         }

//         // ------------------------------------------------------------------
//         //  Mesh combining (sync)
//         // ------------------------------------------------------------------

//         private void CombineMeshesByMaterial(GameObject targetModel)
//         {
//             MeshFilter[] meshFilters = targetModel.GetComponentsInChildren<MeshFilter>();
//             Debug.Log($"Found {meshFilters.Length} mesh filters to process");

//             if (meshFilters.Length == 0)
//             {
//                 Debug.LogWarning("No meshes found to combine!");
//                 return;
//             }

//             Dictionary<Material, List<CombineInstance>> materialGroups =
//                 new Dictionary<Material, List<CombineInstance>>();

//             GameObject combinedParent = new GameObject("CombinedMeshes");
//             combinedParent.transform.SetParent(targetModel.transform);
//             combinedParent.transform.localPosition = Vector3.zero;
//             combinedParent.transform.localRotation = Quaternion.identity;
//             combinedParent.transform.localScale = Vector3.one;

//             Matrix4x4 combinedParentWorldToLocal = combinedParent.transform.worldToLocalMatrix;

//             for (int i = 0; i < meshFilters.Length; i++)
//             {
//                 MeshFilter mf = meshFilters[i];
//                 MeshRenderer mr = mf.GetComponent<MeshRenderer>();
//                 if (mr == null || mf.sharedMesh == null) continue;

//                 Mesh mesh = mf.sharedMesh;
//                 Material[] materials = mr.sharedMaterials;

//                 for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
//                 {
//                     Material mat = subMeshIndex < materials.Length ? materials[subMeshIndex] : null;
//                     if (mat == null)
//                     {
//                         Debug.LogWarning($"No material for submesh {subMeshIndex} on {mf.name}");
//                         continue;
//                     }

//                     Mesh submesh = ExtractSubmesh(mesh, subMeshIndex);
//                     if (submesh == null || submesh.vertexCount == 0) continue;

//                     if (!materialGroups.ContainsKey(mat))
//                         materialGroups[mat] = new List<CombineInstance>();

//                     CombineInstance ci = new CombineInstance();
//                     ci.mesh = submesh;
//                     ci.transform = combinedParentWorldToLocal * mf.transform.localToWorldMatrix;
//                     materialGroups[mat].Add(ci);
//                 }
//             }

//             Debug.Log($"Grouped into {materialGroups.Count} material groups");

//             foreach (MeshFilter mf in meshFilters)
//                 mf.gameObject.SetActive(false);

//             int groupIndex = 0;
//             foreach (var kvp in materialGroups)
//             {
//                 Material material = kvp.Key;
//                 List<CombineInstance> combines = kvp.Value;

//                 Debug.Log($"Processing material group {groupIndex}: {material.name} with {combines.Count} mesh parts");

//                 // --- Spatial clustering ---
//                 List<List<CombineInstance>> spatialClusters = ClusterByProximity(combines, spatialClusterRadius);

//                 for (int clusterIdx = 0; clusterIdx < spatialClusters.Count; clusterIdx++)
//                 {
//                     List<CombineInstance> clusterMeshes = spatialClusters[clusterIdx];

//                     int currentVertexCount = 0;
//                     int batchIndex = 0;
//                     List<CombineInstance> currentBatch = new List<CombineInstance>();

//                     foreach (var ci in clusterMeshes)
//                     {
//                         if (ci.mesh == null) continue;

//                         if (currentVertexCount + ci.mesh.vertexCount > maxVerticesPerMesh && currentBatch.Count > 0)
//                         {
//                             string batchName = $"{material.name}_Cluster{clusterIdx}_Batch{batchIndex}";
//                             CreateCombinedMesh(combinedParent, material, currentBatch, batchName);
//                             currentBatch.Clear();
//                             currentVertexCount = 0;
//                             batchIndex++;
//                         }

//                         currentBatch.Add(ci);
//                         currentVertexCount += ci.mesh.vertexCount;
//                     }

//                     if (currentBatch.Count > 0)
//                     {
//                         string finalName = (spatialClusters.Count == 1 && batchIndex == 0)
//                             ? material.name
//                             : $"{material.name}_Cluster{clusterIdx}_Batch{batchIndex}";
//                         CreateCombinedMesh(combinedParent, material, currentBatch, finalName);
//                     }
//                 }

//                 groupIndex++;
//             }

//             Debug.Log($"Created combined meshes under: {combinedParent.name}");
//         }

//         // ------------------------------------------------------------------
//         //  Mesh combining (async)
//         // ------------------------------------------------------------------

//         private async Task CombineMeshesByMaterialAsync(GameObject targetModel, Action<float> onProgress = null)
//         {
//             MeshFilter[] meshFilters = targetModel.GetComponentsInChildren<MeshFilter>();
//             if (meshFilters.Length == 0)
//             {
//                 onProgress?.Invoke(1f);
//                 return;
//             }

//             Dictionary<Material, List<CombineInstance>> materialGroups =
//                 new Dictionary<Material, List<CombineInstance>>();

//             GameObject combinedParent = new GameObject("CombinedMeshes");
//             combinedParent.transform.SetParent(targetModel.transform);
//             combinedParent.transform.localPosition = Vector3.zero;
//             combinedParent.transform.localRotation = Quaternion.identity;
//             combinedParent.transform.localScale = Vector3.one;

//             Matrix4x4 combinedParentWorldToLocal = combinedParent.transform.worldToLocalMatrix;
//             var stopwatch = System.Diagnostics.Stopwatch.StartNew();

//             float phase1Weight = 0.3f;

//             // Phase 1 – group by material
//             for (int i = 0; i < meshFilters.Length; i++)
//             {
//                 onProgress?.Invoke((float)i / meshFilters.Length * phase1Weight);

//                 if (stopwatch.ElapsedMilliseconds > 10)
//                 {
//                     await Task.Yield();
//                     stopwatch.Restart();
//                 }

//                 MeshFilter mf = meshFilters[i];
//                 MeshRenderer mr = mf.GetComponent<MeshRenderer>();
//                 if (mr == null || mf.sharedMesh == null) continue;

//                 Mesh mesh = mf.sharedMesh;
//                 Material[] materials = mr.sharedMaterials;

//                 for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
//                 {
//                     Material mat = subMeshIndex < materials.Length ? materials[subMeshIndex] : null;
//                     if (mat == null) continue;

//                     Mesh submesh = ExtractSubmesh(mesh, subMeshIndex);
//                     if (submesh == null || submesh.vertexCount == 0) continue;

//                     if (!materialGroups.ContainsKey(mat))
//                         materialGroups[mat] = new List<CombineInstance>();

//                     CombineInstance ci = new CombineInstance();
//                     ci.mesh = submesh;
//                     ci.transform = combinedParentWorldToLocal * mf.transform.localToWorldMatrix;
//                     materialGroups[mat].Add(ci);
//                 }
//             }

//             foreach (MeshFilter mf in meshFilters)
//                 mf.gameObject.SetActive(false);

//             // Phase 2 – cluster spatially and combine
//             int groupIndex = 0;
//             int totalGroups = materialGroups.Count;

//             foreach (var kvp in materialGroups)
//             {
//                 Material material = kvp.Key;
//                 List<CombineInstance> combines = kvp.Value;

//                 float groupProgressStart = phase1Weight + ((float)groupIndex / totalGroups * (1f - phase1Weight));
//                 float groupProgressEnd = phase1Weight + ((float)(groupIndex + 1) / totalGroups * (1f - phase1Weight));

//                 // Spatial clustering (fast, no yield needed)
//                 List<List<CombineInstance>> spatialClusters = ClusterByProximity(combines, spatialClusterRadius);

//                 for (int clusterIdx = 0; clusterIdx < spatialClusters.Count; clusterIdx++)
//                 {
//                     List<CombineInstance> clusterMeshes = spatialClusters[clusterIdx];

//                     int currentVertexCount = 0;
//                     int batchIndex = 0;
//                     List<CombineInstance> currentBatch = new List<CombineInstance>();

//                     for (int i = 0; i < clusterMeshes.Count; i++)
//                     {
//                         float innerProgress = ((float)clusterIdx / spatialClusters.Count) +
//                                               ((float)i / clusterMeshes.Count / spatialClusters.Count);
//                         onProgress?.Invoke(Mathf.Lerp(groupProgressStart, groupProgressEnd, innerProgress));

//                         if (stopwatch.ElapsedMilliseconds > 10)
//                         {
//                             await Task.Yield();
//                             stopwatch.Restart();
//                         }

//                         CombineInstance ci = clusterMeshes[i];
//                         if (ci.mesh == null) continue;

//                         if (currentVertexCount + ci.mesh.vertexCount > maxVerticesPerMesh && currentBatch.Count > 0)
//                         {
//                             string batchName = $"{material.name}_Cluster{clusterIdx}_Batch{batchIndex}";
//                             CreateCombinedMesh(combinedParent, material, currentBatch, batchName);
//                             currentBatch.Clear();
//                             currentVertexCount = 0;
//                             batchIndex++;
//                         }

//                         currentBatch.Add(ci);
//                         currentVertexCount += ci.mesh.vertexCount;
//                     }

//                     if (currentBatch.Count > 0)
//                     {
//                         string finalName = (spatialClusters.Count == 1 && batchIndex == 0)
//                             ? material.name
//                             : $"{material.name}_Cluster{clusterIdx}_Batch{batchIndex}";
//                         CreateCombinedMesh(combinedParent, material, currentBatch, finalName);
//                     }
//                 }

//                 groupIndex++;
//             }

//             onProgress?.Invoke(1f);
//         }

//         // ------------------------------------------------------------------
//         //  Submesh extraction
//         // ------------------------------------------------------------------

//         private Mesh ExtractSubmesh(Mesh sourceMesh, int submeshIndex)
//         {
//             if (submeshIndex >= sourceMesh.subMeshCount) return null;

//             int[] triangles = sourceMesh.GetTriangles(submeshIndex);
//             if (triangles.Length == 0) return null;

//             HashSet<int> usedVertices = new HashSet<int>();
//             foreach (int index in triangles)
//                 usedVertices.Add(index);

//             int[] oldToNew = new int[sourceMesh.vertexCount];
//             List<int> newToOld = new List<int>();

//             for (int i = 0; i < sourceMesh.vertexCount; i++)
//             {
//                 if (usedVertices.Contains(i))
//                 {
//                     oldToNew[i] = newToOld.Count;
//                     newToOld.Add(i);
//                 }
//             }

//             Mesh newMesh = new Mesh();
//             newMesh.name = $"{sourceMesh.name}_Submesh{submeshIndex}";

//             Vector3[] sourceVertices = sourceMesh.vertices;
//             Vector3[] newVertices = new Vector3[newToOld.Count];
//             for (int i = 0; i < newToOld.Count; i++)
//                 newVertices[i] = sourceVertices[newToOld[i]];
//             newMesh.vertices = newVertices;

//             if (sourceMesh.normals != null && sourceMesh.normals.Length > 0)
//             {
//                 Vector3[] src = sourceMesh.normals;
//                 Vector3[] dst = new Vector3[newToOld.Count];
//                 for (int i = 0; i < newToOld.Count; i++) dst[i] = src[newToOld[i]];
//                 newMesh.normals = dst;
//             }

//             if (sourceMesh.uv != null && sourceMesh.uv.Length > 0)
//             {
//                 Vector2[] src = sourceMesh.uv;
//                 Vector2[] dst = new Vector2[newToOld.Count];
//                 for (int i = 0; i < newToOld.Count; i++) dst[i] = src[newToOld[i]];
//                 newMesh.uv = dst;
//             }

//             if (sourceMesh.uv2 != null && sourceMesh.uv2.Length > 0)
//             {
//                 Vector2[] src = sourceMesh.uv2;
//                 Vector2[] dst = new Vector2[newToOld.Count];
//                 for (int i = 0; i < newToOld.Count; i++) dst[i] = src[newToOld[i]];
//                 newMesh.uv2 = dst;
//             }

//             if (sourceMesh.tangents != null && sourceMesh.tangents.Length > 0)
//             {
//                 Vector4[] src = sourceMesh.tangents;
//                 Vector4[] dst = new Vector4[newToOld.Count];
//                 for (int i = 0; i < newToOld.Count; i++) dst[i] = src[newToOld[i]];
//                 newMesh.tangents = dst;
//             }

//             if (sourceMesh.colors != null && sourceMesh.colors.Length > 0)
//             {
//                 Color[] src = sourceMesh.colors;
//                 Color[] dst = new Color[newToOld.Count];
//                 for (int i = 0; i < newToOld.Count; i++) dst[i] = src[newToOld[i]];
//                 newMesh.colors = dst;
//             }

//             int[] newTriangles = new int[triangles.Length];
//             for (int i = 0; i < triangles.Length; i++)
//                 newTriangles[i] = oldToNew[triangles[i]];
//             newMesh.triangles = newTriangles;

//             newMesh.RecalculateBounds();
//             return newMesh;
//         }

//         // ------------------------------------------------------------------
//         //  Create combined mesh with weighted pivot
//         // ------------------------------------------------------------------

//         private void CreateCombinedMesh(GameObject parent, Material material,
//             List<CombineInstance> combines, string name)
//         {
//             GameObject combinedObj = new GameObject($"Combined_{name}");
//             combinedObj.transform.SetParent(parent.transform);
//             combinedObj.transform.localPosition = Vector3.zero;
//             combinedObj.transform.localRotation = Quaternion.identity;
//             combinedObj.transform.localScale = Vector3.one;

//             MeshFilter meshFilter = combinedObj.AddComponent<MeshFilter>();
//             MeshRenderer meshRenderer = combinedObj.AddComponent<MeshRenderer>();

//             Mesh combinedMesh = new Mesh();
//             combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
//             combinedMesh.CombineMeshes(combines.ToArray(), true, true);
//             combinedMesh.RecalculateBounds();

//             // --- Weighted pivot: average of each sub-mesh's transformed center,
//             //     weighted by vertex count.  Because meshes are already spatially
//             //     clustered the result is always near the actual geometry.
//             Vector3 weightedCenter = Vector3.zero;
//             float totalWeight = 0f;

//             foreach (var ci in combines)
//             {
//                 if (ci.mesh == null) continue;
//                 Vector3 meshLocalCenter = ci.mesh.bounds.center;
//                 Vector3 meshWorldCenter = ci.transform.MultiplyPoint3x4(meshLocalCenter);
//                 float weight = ci.mesh.vertexCount;
//                 weightedCenter += meshWorldCenter * weight;
//                 totalWeight += weight;
//             }

//             Vector3 pivot = (totalWeight > 0f) ? (weightedCenter / totalWeight) : combinedMesh.bounds.center;

//             // Shift all vertices so the pivot sits at the mesh's local origin
//             Vector3[] vertices = combinedMesh.vertices;
//             for (int i = 0; i < vertices.Length; i++)
//                 vertices[i] -= pivot;

//             combinedMesh.vertices = vertices;
//             combinedMesh.RecalculateBounds();   // bounds.center should now be ~(0,0,0)
//             combinedMesh.Optimize();

//             // Move the GameObject so the mesh appears in the same world position
//             combinedObj.transform.localPosition = pivot;

//             meshFilter.mesh = combinedMesh;
//             meshRenderer.material = material;

//             Debug.Log($"Created combined mesh: {name} | verts: {combinedMesh.vertexCount} | " +
//                       $"tris: {combinedMesh.triangles.Length / 3} | weighted pivot: {pivot}");
//         }
//     }

//     // =========================================================================
//     //  GLBSpawnerPrefab
//     // =========================================================================

//     public class GLBSpawnerPrefab : MonoBehaviour
//     {
//         public string glbPath;
//         public bool filterMeshesOnImport = true;
//         public Vector3 minimumMeshSize = new Vector3(10f, 10f, 10f);
//         public bool combineMeshes = true;
//         public int maxVerticesPerMesh = 200000;
//         public float spatialClusterRadius = 5f;
//         public bool simplifyTexturesBySize = true;
//         public float largeMeshThreshold = 2.0f;
//         public bool simplifyBeforeCombining = true;
//         public GLBLoader glbLoader;

//         private GameObject preloadedModel;
//         private bool isModelLoaded = false;
//         private bool isFirstModel = false;

//         // ------------------------------------------------------------------
//         //  Pre-optimization detection helpers
//         // ------------------------------------------------------------------

//         /// <summary>
//         /// Checks if the model is already optimized by looking for:
//         /// 1. A "CombinedMeshes" parent object
//         /// 2. Child objects with names starting with "Combined_"
//         /// </summary>
//         private bool IsModelPreOptimized(GameObject model)
//         {
//             // Look for CombinedMeshes parent
//             Transform combinedMeshesParent = model.transform.Find("CombinedMeshes");
//             if (combinedMeshesParent == null)
//             {
//                 Debug.Log("No 'CombinedMeshes' parent found - model is not pre-optimized");
//                 return false;
//             }

//             // Check if it has children with "Combined_" prefix
//             int combinedChildCount = 0;
//             foreach (Transform child in combinedMeshesParent)
//             {
//                 if (child.name.StartsWith("Combined_"))
//                 {
//                     combinedChildCount++;
//                 }
//             }

//             bool isPreOptimized = combinedChildCount > 0;
//             Debug.Log($"Found {combinedChildCount} combined meshes - model is {(isPreOptimized ? "PRE-OPTIMIZED" : "NOT pre-optimized")}");
//             return isPreOptimized;
//         }

//         /// <summary>
//         /// Applies the custom shader and extracts colors from object names.
//         /// Expected name format: "Combined_MaterialName_ColorHEX" or "Combined_MaterialName_Cluster0_Batch0"
//         /// where ColorHEX might be embedded like "SimplifiedCustom_Color_12345678"
//         /// </summary>
//         private void ApplyShaderAndColorsFromNames(GameObject model)
//         {
//             Shader customShader = Shader.Find("Custom/TransparentWithDepth");
//             if (customShader == null)
//             {
//                 Debug.LogError("Custom/TransparentWithDepth shader not found! Cannot apply pre-optimized materials.");
//                 return;
//             }

//             Transform combinedMeshesParent = model.transform.Find("CombinedMeshes");
//             if (combinedMeshesParent == null)
//             {
//                 Debug.LogWarning("CombinedMeshes parent not found");
//                 return;
//             }

//             int processedCount = 0;
//             foreach (Transform child in combinedMeshesParent)
//             {
//                 MeshRenderer renderer = child.GetComponent<MeshRenderer>();
//                 if (renderer == null) continue;

//                 // Try to extract color from the object name or material name
//                 Color extractedColor = ExtractColorFromName(child.name);

//                 // If we couldn't extract from the object name, try the material name
//                 if (extractedColor == Color.clear && renderer.sharedMaterial != null)
//                 {
//                     extractedColor = ExtractColorFromName(renderer.sharedMaterial.name);
//                 }

//                 // Fallback to a default gray if we couldn't extract a color
//                 if (extractedColor == Color.clear)
//                 {
//                     extractedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
//                     Debug.LogWarning($"Could not extract color from '{child.name}', using default gray");
//                 }

//                 // Create new material with custom shader
//                 Material newMat = new Material(customShader);
//                 newMat.name = $"PreOptimized_{child.name}";
//                 newMat.SetColor("_Color", extractedColor);
//                 newMat.SetFloat("_Alpha", 1.0f);

//                 renderer.sharedMaterial = newMat;
//                 processedCount++;

//                 Debug.Log($"Applied custom shader to '{child.name}' with color: {extractedColor}");
//             }

//             Debug.Log($"Applied shader and colors to {processedCount} pre-optimized meshes");
//         }

//         /// <summary>
//         /// Attempts to extract a color from various name formats:
//         /// - Hash code format: "SimplifiedCustom_Color_123456789"
//         /// - Hex format: "Material_RRGGBBAA" or "Material_#RRGGBBAA"
//         /// </summary>
//         private Color ExtractColorFromName(string name)
//         {
//             if (string.IsNullOrEmpty(name))
//                 return Color.clear;

//             // Method 1: Look for hash code after "Color_"
//             int colorIndex = name.IndexOf("Color_");
//             if (colorIndex >= 0)
//             {
//                 string afterColor = name.Substring(colorIndex + 6); // Skip "Color_"

//                 // Extract the hash code (numbers before the next underscore or end)
//                 string hashString = "";
//                 foreach (char c in afterColor)
//                 {
//                     if (char.IsDigit(c) || c == '-')
//                         hashString += c;
//                     else
//                         break;
//                 }

//                 if (!string.IsNullOrEmpty(hashString))
//                 {
//                     Debug.Log($"Found hash code in name: {hashString}");
//                     // We can't reliably convert a hash back to a color, so we'll use a fallback
//                     // In practice, you might want to store the actual hex color in the name instead
//                     return Color.clear; // Signal that we need to use a different method
//                 }
//             }

//             // Method 2: Look for hex color format (6 or 8 hex digits)
//             // Pattern: RRGGBB or RRGGBBAA (with or without # prefix)
//             System.Text.RegularExpressions.Match match =
//                 System.Text.RegularExpressions.Regex.Match(name, @"#?([0-9A-Fa-f]{6,8})");

//             if (match.Success)
//             {
//                 string hexColor = match.Groups[1].Value;
//                 return ParseHexColor(hexColor);
//             }

//             // Method 3: Look for individual RGB values in the name (less common)
//             // Pattern like "R255G128B64" or "R_255_G_128_B_64"
//             var rgbMatch = System.Text.RegularExpressions.Regex.Match(
//                 name, @"R_?(\d+)_?G_?(\d+)_?B_?(\d+)",
//                 System.Text.RegularExpressions.RegexOptions.IgnoreCase);

//             if (rgbMatch.Success)
//             {
//                 float r = int.Parse(rgbMatch.Groups[1].Value) / 255f;
//                 float g = int.Parse(rgbMatch.Groups[2].Value) / 255f;
//                 float b = int.Parse(rgbMatch.Groups[3].Value) / 255f;
//                 return new Color(r, g, b, 1f);
//             }

//             return Color.clear;
//         }

//         /// <summary>
//         /// Parses a hex color string (RRGGBB or RRGGBBAA) into a Color
//         /// </summary>
//         private Color ParseHexColor(string hex)
//         {
//             hex = hex.TrimStart('#');

//             if (hex.Length == 6)
//             {
//                 // RRGGBB format
//                 byte r = Convert.ToByte(hex.Substring(0, 2), 16);
//                 byte g = Convert.ToByte(hex.Substring(2, 2), 16);
//                 byte b = Convert.ToByte(hex.Substring(4, 2), 16);
//                 return new Color32(r, g, b, 255);
//             }
//             else if (hex.Length == 8)
//             {
//                 // RRGGBBAA format
//                 byte r = Convert.ToByte(hex.Substring(0, 2), 16);
//                 byte g = Convert.ToByte(hex.Substring(2, 2), 16);
//                 byte b = Convert.ToByte(hex.Substring(4, 2), 16);
//                 byte a = Convert.ToByte(hex.Substring(6, 2), 16);
//                 return new Color32(r, g, b, a);
//             }

//             Debug.LogWarning($"Invalid hex color format: {hex}");
//             return Color.clear;
//         }

//         // ------------------------------------------------------------------
//         //  Preload
//         // ------------------------------------------------------------------

//         public async void PreloadModel()
//         {
//             if (isModelLoaded)
//             {
//                 Debug.Log("Model already preloaded, skipping");
//                 return;
//             }

//             if (string.IsNullOrEmpty(glbPath))
//             {
//                 Debug.LogError("GLB path not set in GLBSpawnerPrefab!");
//                 if (glbLoader != null) glbLoader.HideLoading();
//                 return;
//             }

//             Debug.Log($"Preloading GLB from: {glbPath}");
//             Debug.Log($"File exists: {File.Exists(glbPath)}");

//             if (!File.Exists(glbPath))
//             {
//                 Debug.LogError($"File not found at path: {glbPath}");
//                 if (glbLoader != null) glbLoader.HideLoading();
//                 return;
//             }

//             if (glbLoader != null) glbLoader.SetLoadingPercentage(30);

//             GameObject existingModel = GLBLoader.GetCurrentSpawnedModel();
//             isFirstModel = (existingModel == null);

//             try
//             {
//                 Debug.Log("Creating GltfImport instance for preloading...");
//                 var gltf = new GltfImport();

//                 Debug.Log("Loading GLB...");
//                 bool success = await gltf.Load(glbPath);
//                 Debug.Log($"Load result: {success}");

//                 if (glbLoader != null) glbLoader.SetLoadingPercentage(50);

//                 if (success)
//                 {
//                     Debug.Log("Instantiating scene for preload...");

//                     preloadedModel = new GameObject("PreloadedModel");
//                     preloadedModel.transform.SetParent(transform);
//                     preloadedModel.transform.localPosition = Vector3.zero;
//                     preloadedModel.transform.localRotation = Quaternion.identity;
//                     preloadedModel.transform.localScale = Vector3.one;

//                     await gltf.InstantiateMainSceneAsync(preloadedModel.transform);
//                     Debug.Log($"GLB preloaded! Children: {preloadedModel.transform.childCount}");

//                     if (glbLoader != null) glbLoader.SetLoadingPercentage(60);

//                     // APPLY MESH FILTRATION FIRST (before checking if pre-optimized)
//                     if (filterMeshesOnImport)
//                     {
//                         Debug.Log("=== FILTERING MESHES ON IMPORT ===");
//                         glbLoader.FilterSmallMeshes(preloadedModel);
//                     }

//                     if (glbLoader != null) glbLoader.SetLoadingPercentage(70);

//                     // Check if model is already optimized
//                     bool isPreOptimized = IsModelPreOptimized(preloadedModel);

//                     if (isPreOptimized)
//                     {
//                         Debug.Log("=== MODEL IS PRE-OPTIMIZED - APPLYING SHADER AND COLORS FROM NAMES ===");
//                         ApplyShaderAndColorsFromNames(preloadedModel);
//                         if (glbLoader != null) glbLoader.SetLoadingPercentage(100);
//                     }
//                     else
//                     {
//                         Debug.Log("=== MODEL NOT PRE-OPTIMIZED - RUNNING NORMAL OPTIMIZATION ===");
//                         var optimizer = new ModelOptimizer();
//                         optimizer.combineMeshes = combineMeshes;
//                         optimizer.maxVerticesPerMesh = maxVerticesPerMesh;
//                         optimizer.spatialClusterRadius = spatialClusterRadius;
//                         optimizer.simplifyTexturesBySize = simplifyTexturesBySize;
//                         optimizer.largeMeshThreshold = largeMeshThreshold;
//                         optimizer.simplifyBeforeCombining = simplifyBeforeCombining;

//                         await optimizer.ApplyOptimizationsAsync(preloadedModel, (progress) =>
//                         {
//                             if (glbLoader != null)
//                                 glbLoader.SetLoadingPercentage(70 + (int)(progress * 30));
//                         });

//                         var integration = glbLoader.GetComponent<GLBLoaderOcclusionIntegration>();
//                         if (integration != null)
//                         {
//                             integration.RegisterModelForOcclusion(preloadedModel);
//                         }


//                     }

//                     glbLoader.RegisterMaterialsForTransparency(preloadedModel);

//                     preloadedModel.SetActive(false);
//                     isModelLoaded = true;

//                     Debug.Log("=== PRELOAD COMPLETE ===");
//                     transform.localPosition = Vector3.zero;

//                     existingModel = GLBLoader.GetCurrentSpawnedModel();
//                     if (!isFirstModel && existingModel != null)
//                     {
//                         Debug.Log("=== SECOND+ MODEL - AUTO-SPAWNING AT EXISTING MODEL ===");
//                         SpawnAsChildOfExisting(existingModel);

//                         if (preloadedModel != null)
//                         {
//                             Debug.Log("Destroying preloaded model (no longer needed)");
//                             DestroyImmediate(preloadedModel);
//                             preloadedModel = null;
//                         }

//                         gameObject.SetActive(false);
//                         Debug.Log("Spawner prefab deactivated (second+ model complete)");
//                     }

//                     if (glbLoader != null) glbLoader.SetLoadingPercentage(100);
//                 }
//                 else
//                 {
//                     Debug.LogError("Failed to preload GLB model");
//                 }
//             }
//             catch (Exception e)
//             {
//                 Debug.LogError($"Exception in PreloadModel: {e.Message}");
//                 Debug.LogError($"Stack trace: {e.StackTrace}");
//             }
//             finally
//             {
//                 if (glbLoader != null) glbLoader.HideLoading();
//             }
//         }

//         // ------------------------------------------------------------------
//         //  Enable / spawn logic
//         // ------------------------------------------------------------------

//         void OnEnable()
//         {
//             Debug.Log($"=== GLBSpawnerPrefab OnEnable: {gameObject.name} | pos: {transform.position} ===");

//             if (!isModelLoaded)
//             {
//                 Debug.LogWarning("OnEnable called before model is preloaded! Waiting...");
//                 StartCoroutine(WaitForPreloadThenSpawn());
//                 return;
//             }

//             GameObject existingModel = GLBLoader.GetCurrentSpawnedModel();

//             if (existingModel != null)
//             {
//                 Debug.Log("=== MODEL EXISTS - MOVING TO NEW POSITION ===");
//                 MoveExistingModelToNewPosition(existingModel);
//             }
//             else
//             {
//                 Debug.Log("=== FIRST MODEL - SPAWNING NORMALLY ===");
//                 SpawnFirstModel();
//             }

//             transform.localPosition = Vector3.zero;
//         }

//         System.Collections.IEnumerator WaitForPreloadThenSpawn()
//         {
//             while (!isModelLoaded)
//                 yield return null;

//             Debug.Log("Preload complete, now spawning...");

//             GameObject existingModel = GLBLoader.GetCurrentSpawnedModel();

//             if (existingModel != null)
//             {
//                 Debug.Log("=== MODEL EXISTS - MOVING TO NEW POSITION ===");
//                 MoveExistingModelToNewPosition(existingModel);
//             }
//             else
//             {
//                 Debug.Log("=== FIRST MODEL - SPAWNING NORMALLY ===");
//                 SpawnFirstModel();
//             }
//         }

//         void MoveExistingModelToNewPosition(GameObject existingModel)
//         {
//             transform.localPosition = Vector3.zero;
//             Debug.Log($"Moving existing model from {existingModel.transform.position} to {transform.position}");
//             Debug.Log($"Model moved to new position: {transform.position}");
//         }

//         void SpawnFirstModel()
//         {
//             if (!isModelLoaded || preloadedModel == null)
//             {
//                 Debug.LogError("Model not preloaded! Cannot spawn.");
//                 return;
//             }

//             GameObject newModelInstance = Instantiate(preloadedModel);
//             newModelInstance.name = "SpawnedModel_Root";
//             newModelInstance.transform.SetParent(transform);
//             newModelInstance.transform.localPosition = Vector3.zero;
//             newModelInstance.transform.localRotation = Quaternion.identity;
//             newModelInstance.transform.localScale = Vector3.one;
//             newModelInstance.SetActive(true);

//             var LODloader = newModelInstance.AddComponent<RuntimeLODLoader>();

//             // Check if we have a BIN file cached
//             string binPath = PlayerPrefs.GetString("LastBinPath", "");
//             // string binPath = null;
//             if (string.IsNullOrEmpty(binPath) || !File.Exists(binPath))
//             {
//                 // Prompt user to select BIN file
//                 Debug.Log("No BIN file found, prompting user...");
//                 if (glbLoader != null)
//                 {
//                     glbLoader.ShowBinFilePicker(glbPath);
//                 }
//             }
//             else
//             {
//                 LODloader.lodDataPath = binPath;
//                 LODloader.LoadLODsFromFile();
//             }

//             GLBLoader.SetCurrentSpawnedModel(transform.gameObject);

//             Debug.Log($"First model spawned at: {transform.position}");
//             transform.localPosition = Vector3.zero;
//         }

//         void SpawnAsChildOfExisting(GameObject existingModel)
//         {
//             if (!isModelLoaded || preloadedModel == null)
//             {
//                 Debug.LogError("Model not preloaded! Cannot spawn as child.");
//                 return;
//             }

//             Debug.Log($"Spawning model as child of: {existingModel.name}");

//             GameObject newModelInstance = Instantiate(preloadedModel);
//             newModelInstance.name = $"AdditionalModel_{DateTime.Now.Ticks}";
//             newModelInstance.transform.SetParent(existingModel.transform);
//             newModelInstance.transform.localPosition = Vector3.zero;
//             newModelInstance.transform.localRotation = Quaternion.identity;
//             newModelInstance.transform.localScale = Vector3.one;
//             newModelInstance.SetActive(true);

//             Debug.Log($"Additional model spawned as child at local (0,0,0)");
//         }
//     }
// }