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
}