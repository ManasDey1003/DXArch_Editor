using System.Collections.Generic;
using AlligUtils;
using UnityEngine;

public class MeshFilteration : MonoBehaviour
{
    public Vector3 minimumMeshSize = new Vector3(1f, 1f, 1f);
    public GameObject FilterSmallMeshes(GameObject model)
    {

        Debug.Log($"=== FILTERING SMALL MESHES ON IMPORT ===");
        Debug.Log($"Minimum size threshold: {minimumMeshSize}");

        List<GameObject> objectsToRemove = new List<GameObject>();
        int totalScanned = 0;
        int filteredCount = 0;

        // Get all mesh filters in the hierarchy
        MeshFilter[] meshFilters = model.GetComponentsInChildren<MeshFilter>();
        Debug.Log($"Found {meshFilters.Length} meshes to scan");

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            totalScanned++;

            // Get the mesh bounds
            Bounds bounds = meshFilter.sharedMesh.bounds;

            // Account for object's scale
            Transform transform = meshFilter.transform;
            Vector3 scaledSize = new Vector3(
                bounds.size.x * Mathf.Abs(transform.lossyScale.x),
                bounds.size.y * Mathf.Abs(transform.lossyScale.y),
                bounds.size.z * Mathf.Abs(transform.lossyScale.z)
            );

            // Check if any dimension is below the threshold
            if (scaledSize.x < minimumMeshSize.x &&
                scaledSize.y < minimumMeshSize.y &&
                scaledSize.z < minimumMeshSize.z)
            {
                filteredCount++;
                string path = GetGameObjectPath(meshFilter.transform);
                Debug.Log($"  Filtering: {path} - Size: {scaledSize:F2}");

                if (!objectsToRemove.Contains(meshFilter.gameObject))
                {
                    objectsToRemove.Add(meshFilter.gameObject);
                }
            }
        }
        $"Removing {filteredCount} small meshes out of {totalScanned} scanned.".Print();
        // Remove filtered objects
        foreach (GameObject obj in objectsToRemove)
        {
            DestroyImmediate(obj);
        }
        return model;
    }

    private string GetGameObjectPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
