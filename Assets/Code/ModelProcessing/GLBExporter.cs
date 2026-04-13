using GLTFast;
using UnityEngine;
using GLTFast.Export;
using GLTFast.Logging;
using System.Collections.Generic;

public class TestExport : MonoBehaviour
{
    // Made public so ModelManager can set these
    public GameObject[] exportRoot;
    public string path;

    // Temporary clones so original scene is untouched
    private List<GameObject> tempObjects = new();

    [ContextMenu("Export POSITION ONLY GLB")]
    public async void AdvancedExport()
    {
        if (exportRoot == null || exportRoot.Length == 0)
        {
            Debug.LogError("[TestExport] No export root objects assigned.");
            return;
        }

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[TestExport] Export path is not set.");
            return;
        }

        var logger = new CollectingLogger();

        tempObjects.Clear();

        // --------------------------------------------------
        // CREATE CLEAN EXPORT COPIES
        // --------------------------------------------------
        foreach (var root in exportRoot)
        {
            if (root == null) continue;

            var clone = Instantiate(root);
            clone.name = root.name;
            PrepareMeshes(clone);

            tempObjects.Add(clone);
        }

        // --------------------------------------------------
        // EXPORT SETTINGS
        // --------------------------------------------------
        var exportSettings = new ExportSettings
        {
            Format = GltfFormat.Binary,
            FileConflictResolution = FileConflictResolution.Overwrite,

            // Mesh only
            ComponentMask = ComponentType.Mesh,

            // No compression
            Compression = Compression.Uncompressed
        };

        var goSettings = new GameObjectExportSettings
        {
            OnlyActiveInHierarchy = false,
            DisabledComponents = false
        };

        var export = new GameObjectExport(
            exportSettings,
            goSettings
        );

        export.AddScene(tempObjects.ToArray());

        bool success = await export.SaveToFileAndDispose(path);

        // Cleanup temp objects
        foreach (var obj in tempObjects)
            DestroyImmediate(obj);

        tempObjects.Clear();

        if (!success)
        {
            Debug.LogError("[TestExport] GLB export failed");
            logger.LogAll();
        }
        else
        {
            Debug.Log($"[TestExport] POSITION ONLY GLB exported successfully to: {path}");
        }
    }

    // --------------------------------------------------
    // STRIP NON-GEOMETRY DATA
    // --------------------------------------------------
    void PrepareMeshes(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var filters = root.GetComponentsInChildren<MeshFilter>(true);

        for (int i = 0; i < filters.Length; i++)
        {
            var mesh = filters[i].sharedMesh;
            if (mesh == null) continue;

            Mesh cleanMesh = Instantiate(mesh);

            // ✅ KEEP ONLY POSITIONS + INDICES
            cleanMesh.normals = null;
            cleanMesh.tangents = null;
            cleanMesh.colors = null;

            cleanMesh.uv = null;
            cleanMesh.uv2 = null;
            cleanMesh.uv3 = null;
            cleanMesh.uv4 = null;

            cleanMesh.RecalculateBounds();

            filters[i].sharedMesh = cleanMesh;
        }

        // ✅ REMOVE MATERIALS COMPLETELY
        foreach (var r in renderers)
        {
            r.sharedMaterials = new Material[0];
        }
    }





    /// <summary>
    /// Export model in chunks to avoid MemoryStream limits
    /// </summary>
    public async void ExportInChunks(int maxChunkSizeBytes = 400 * 1024 * 1024)
    {
        if (exportRoot == null || exportRoot.Length == 0)
        {
            Debug.LogError("[TestExport] No export root objects assigned.");
            return;
        }

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[TestExport] Export path is not set.");
            return;
        }

        // Collect all mesh renderers from all roots
        List<(GameObject obj, int estimatedSize)> allObjects = new();

        foreach (var root in exportRoot)
        {
            if (root == null) continue;

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.sharedMesh != null)
                {
                    // Estimate: 12 bytes per vertex (positions) + 4 bytes per index
                    int vertexBytes = filter.sharedMesh.vertexCount * 12;
                    int indexBytes = filter.sharedMesh.triangles.Length * 4;
                    int estimated = vertexBytes + indexBytes;

                    allObjects.Add((renderer.gameObject, estimated));
                }
            }
        }

        if (allObjects.Count == 0)
        {
            Debug.LogError("[TestExport] No meshes found to export.");
            return;
        }

        // Group into chunks
        List<List<GameObject>> chunks = new();
        List<GameObject> currentChunk = new();
        int currentChunkSize = 0;

        foreach (var (obj, size) in allObjects)
        {
            if (currentChunkSize + size > maxChunkSizeBytes && currentChunk.Count > 0)
            {
                chunks.Add(new List<GameObject>(currentChunk));
                currentChunk.Clear();
                currentChunkSize = 0;
            }

            currentChunk.Add(obj);
            currentChunkSize += size;
        }

        if (currentChunk.Count > 0)
            chunks.Add(currentChunk);

        // Export each chunk
        string basePath = System.IO.Path.GetDirectoryName(path);
        string baseFilename = System.IO.Path.GetFileNameWithoutExtension(path);

        Debug.Log($"[TestExport] Exporting {allObjects.Count} objects in {chunks.Count} chunks...");

        for (int i = 0; i < chunks.Count; i++)
        {
            string chunkPath = chunks.Count == 1
                ? path
                : System.IO.Path.Combine(basePath, $"{baseFilename}_part{i + 1}.glb");

            await ExportChunk(chunks[i], chunkPath, i + 1, chunks.Count);
        }

        Debug.Log($"[TestExport] Export complete: {chunks.Count} file(s) created.");
    }

    private async System.Threading.Tasks.Task ExportChunk(List<GameObject> objects, string chunkPath, int chunkNum, int totalChunks)
    {
        tempObjects.Clear();

        // Create temporary parent for this chunk
        GameObject chunkRoot = new GameObject($"Chunk_{chunkNum}");

        foreach (var obj in objects)
        {
            var clone = Instantiate(obj);
            clone.transform.SetParent(chunkRoot.transform);
            clone.transform.position = obj.transform.position;
            clone.transform.rotation = obj.transform.rotation;
            clone.transform.localScale = obj.transform.lossyScale;
        }

        PrepareMeshes(chunkRoot);
        tempObjects.Add(chunkRoot);

        var exportSettings = new ExportSettings
        {
            Format = GltfFormat.Binary,
            FileConflictResolution = FileConflictResolution.Overwrite,
            ComponentMask = ComponentType.Mesh,
            Compression = Compression.Uncompressed
        };

        var goSettings = new GameObjectExportSettings
        {
            OnlyActiveInHierarchy = false,
            DisabledComponents = false
        };

        var export = new GameObjectExport(exportSettings, goSettings);
        export.AddScene(tempObjects.ToArray());

        bool success = await export.SaveToFileAndDispose(chunkPath);

        foreach (var obj in tempObjects)
            DestroyImmediate(obj);

        tempObjects.Clear();

        if (success)
            Debug.Log($"[TestExport] Chunk {chunkNum}/{totalChunks} exported: {chunkPath}");
        else
            Debug.LogError($"[TestExport] Chunk {chunkNum}/{totalChunks} FAILED");
    }
}