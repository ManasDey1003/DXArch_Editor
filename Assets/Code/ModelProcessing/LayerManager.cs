using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[Serializable]
public class Layer
{
    public string name;
    public GameObject layerObject; // The parent GameObject in hierarchy
    public List<GameObject> objects = new List<GameObject>();
    public bool isVisible = true;
    public bool isLocked = false;

    public Layer(string layerName, GameObject layerGameObject)
    {
        name = layerName;
        layerObject = layerGameObject;
    }
}

public class LayerManager : MonoBehaviour
{
    public static LayerManager Instance;

    [SerializeField] private List<Layer> _layers = new List<Layer>();
    [SerializeField] private int _layerCounter = 0;
    [SerializeField] private GameObject _layersContainer; // Optional: parent container for all layers

    private SelectionHandler _selectionHandler;
    private ModelManager _modelManager;

    public List<Layer> Layers => _layers;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        _selectionHandler = SelectionHandler.Instance;
        _modelManager = GetComponent<ModelManager>();

        // Create or find layers container
        if (_layersContainer == null)
        {
            _layersContainer = GameObject.Find("LayersContainer");
            if (_layersContainer == null)
            {
                _layersContainer = new GameObject("LayersContainer");
                if (_modelManager != null && _modelManager.CurrentModel != null)
                {
                    _layersContainer.transform.SetParent(_modelManager.CurrentModel.transform, false);
                }
            }
        }
    }

    /// <summary>
    /// Creates a new layer from currently selected objects
    /// </summary>
    public void CreateLayerFromSelection()
    {
        // Validate selection
        if (_selectionHandler == null ||
            _selectionHandler.CurrentSelectedObjects == null ||
            _selectionHandler.CurrentSelectedObjects.Count == 0)
        {
            Debug.LogWarning("[LayerManager] No objects selected to create layer.");
            return;
        }

        // Create new layer with unique name
        _layerCounter++;
        string layerName = $"Layer_{_layerCounter}";

        // Create GameObject in hierarchy
        GameObject layerGameObject = new GameObject(layerName);

        // Parent to layers container or current model
        if (_layersContainer != null)
        {
            layerGameObject.transform.SetParent(_layersContainer.transform, false);
        }
        else if (_modelManager != null && _modelManager.CurrentModel != null)
        {
            layerGameObject.transform.SetParent(_modelManager.CurrentModel.transform, false);
        }

        // Calculate average position for layer placement
        Vector3 averagePosition = Vector3.zero;
        foreach (var obj in _selectionHandler.CurrentSelectedObjects)
        {
            if (obj != null)
            {
                averagePosition += obj.transform.position;
            }
        }
        averagePosition /= _selectionHandler.CurrentSelectedObjects.Count;
        layerGameObject.transform.position = averagePosition;

        // Create layer data
        Layer newLayer = new Layer(layerName, layerGameObject);

        // Move selected objects under the layer GameObject and add to list
        int movedCount = 0;
        foreach (var obj in _selectionHandler.CurrentSelectedObjects)
        {
            if (obj != null)
            {
                obj.transform.SetParent(layerGameObject.transform, true);
                newLayer.objects.Add(obj);
                movedCount++;
            }
        }

        // Add layer to the list
        _layers.Add(newLayer);

        Debug.Log($"[LayerManager] Created '{newLayer.name}' with {movedCount} objects in hierarchy.");

        // Refresh UI
        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();

        SyncLayersContextMenu(); // Ensure layer data is synced with hierarchy after creation    
    }

    /// <summary>
    /// Creates a new layer with custom name from selected objects
    /// </summary>
    public void CreateLayerFromSelection(string customName)
    {
        if (_selectionHandler == null ||
            _selectionHandler.CurrentSelectedObjects == null ||
            _selectionHandler.CurrentSelectedObjects.Count == 0)
        {
            Debug.LogWarning("[LayerManager] No objects selected to create layer.");
            return;
        }

        // Create GameObject in hierarchy
        GameObject layerGameObject = new GameObject(customName);

        if (_layersContainer != null)
        {
            layerGameObject.transform.SetParent(_layersContainer.transform, false);
        }
        else if (_modelManager != null && _modelManager.CurrentModel != null)
        {
            layerGameObject.transform.SetParent(_modelManager.CurrentModel.transform, false);
        }

        // Calculate average position
        Vector3 averagePosition = Vector3.zero;
        foreach (var obj in _selectionHandler.CurrentSelectedObjects)
        {
            if (obj != null)
            {
                averagePosition += obj.transform.position;
            }
        }
        averagePosition /= _selectionHandler.CurrentSelectedObjects.Count;
        layerGameObject.transform.position = averagePosition;

        Layer newLayer = new Layer(customName, layerGameObject);

        foreach (var obj in _selectionHandler.CurrentSelectedObjects)
        {
            if (obj != null)
            {
                obj.transform.SetParent(layerGameObject.transform, true);
                newLayer.objects.Add(obj);
            }
        }

        _layers.Add(newLayer);

        Debug.Log($"[LayerManager] Created '{newLayer.name}' with {newLayer.objects.Count} objects.");

        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();
    }

    /// <summary>
    /// Add objects to an existing layer
    /// </summary>
    public void AddSelectionToLayer(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= _layers.Count)
        {
            Debug.LogError($"[LayerManager] Invalid layer index: {layerIndex}");
            return;
        }

        if (_selectionHandler == null ||
            _selectionHandler.CurrentSelectedObjects == null ||
            _selectionHandler.CurrentSelectedObjects.Count == 0)
        {
            Debug.LogWarning("[LayerManager] No objects selected to add to layer.");
            return;
        }

        Layer targetLayer = _layers[layerIndex];
        int addedCount = 0;

        foreach (var obj in _selectionHandler.CurrentSelectedObjects)
        {
            if (obj != null && !targetLayer.objects.Contains(obj))
            {
                // Move to layer hierarchy
                obj.transform.SetParent(targetLayer.layerObject.transform, true);
                targetLayer.objects.Add(obj);
                addedCount++;
            }
        }

        Debug.Log($"[LayerManager] Added {addedCount} objects to '{targetLayer.name}'.");

        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();
    }

    /// <summary>
    /// Remove objects from a layer (moves them back to root or parent)
    /// </summary>
    public void RemoveObjectsFromLayer(int layerIndex, List<GameObject> objectsToRemove)
    {
        if (layerIndex < 0 || layerIndex >= _layers.Count)
        {
            Debug.LogError($"[LayerManager] Invalid layer index: {layerIndex}");
            return;
        }

        Layer targetLayer = _layers[layerIndex];

        foreach (var obj in objectsToRemove)
        {
            if (obj != null && targetLayer.objects.Contains(obj))
            {
                // Move back to layers container or model root
                if (_layersContainer != null)
                {
                    obj.transform.SetParent(_layersContainer.transform, true);
                }
                else if (_modelManager != null && _modelManager.CurrentModel != null)
                {
                    obj.transform.SetParent(_modelManager.CurrentModel.transform, true);
                }
                else
                {
                    obj.transform.SetParent(null, true);
                }

                targetLayer.objects.Remove(obj);
            }
        }

        Debug.Log($"[LayerManager] Removed objects from '{targetLayer.name}'.");

        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();
    }

    /// <summary>
    /// Delete a layer and all its objects from the hierarchy
    /// </summary>
    public void DeleteLayer(int layerIndex, bool destroyObjects = false)
    {
        if (layerIndex < 0 || layerIndex >= _layers.Count)
        {
            Debug.LogError($"[LayerManager] Invalid layer index: {layerIndex}");
            return;
        }

        Layer layer = _layers[layerIndex];
        string layerName = layer.name;

        if (destroyObjects)
        {
            // Destroy the layer GameObject and all children
            if (layer.layerObject != null)
            {
                Destroy(layer.layerObject);
            }
        }
        else
        {
            // Move children back to parent before destroying layer object
            if (layer.layerObject != null)
            {
                Transform parent = _layersContainer != null ? _layersContainer.transform :
                                   (_modelManager != null && _modelManager.CurrentModel != null ?
                                    _modelManager.CurrentModel.transform : null);

                while (layer.layerObject.transform.childCount > 0)
                {
                    layer.layerObject.transform.GetChild(0).SetParent(parent, true);
                }

                Destroy(layer.layerObject);
            }
        }

        _layers.RemoveAt(layerIndex);

        Debug.Log($"[LayerManager] Deleted layer '{layerName}'.");

        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();
    }

    /// <summary>
    /// Toggle visibility of layer GameObject and all children
    /// </summary>
    public void SetLayerVisibility(int layerIndex, bool visible)
    {
        if (layerIndex < 0 || layerIndex >= _layers.Count)
        {
            Debug.LogError($"[LayerManager] Invalid layer index: {layerIndex}");
            return;
        }

        Layer layer = _layers[layerIndex];
        layer.isVisible = visible;

        if (layer.layerObject != null)
        {
            layer.layerObject.SetActive(visible);
        }

        Debug.Log($"[LayerManager] Set '{layer.name}' visibility to {visible}.");
    }

    /// <summary>
    /// Rename a layer
    /// </summary>
    public void RenameLayer(int layerIndex, string newName)
    {
        if (layerIndex < 0 || layerIndex >= _layers.Count)
        {
            Debug.LogError($"[LayerManager] Invalid layer index: {layerIndex}");
            return;
        }

        Layer layer = _layers[layerIndex];
        layer.name = newName;

        if (layer.layerObject != null)
        {
            layer.layerObject.name = newName;
        }

        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();
    }

    /// <summary>
    /// Get layer by name
    /// </summary>
    public Layer GetLayerByName(string layerName)
    {
        return _layers.Find(layer => layer.name == layerName);
    }

    /// <summary>
    /// Select all objects in a layer
    /// </summary>
    public void SelectLayer(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= _layers.Count)
        {
            Debug.LogError($"[LayerManager] Invalid layer index: {layerIndex}");
            return;
        }

        Layer layer = _layers[layerIndex];

        // Clear current selection
        MeshSelection.ClearAllObjects();

        // Filter out null objects
        List<GameObject> validObjects = layer.objects.FindAll(obj => obj != null);

        // Add all layer objects to selection
        if (validObjects.Count > 0)
        {
            MeshSelection.AddObjects(validObjects, false);
            SelectionHandler.Instance.CurrentSelectedObjects = validObjects;
            Debug.Log($"[LayerManager] Selected {validObjects.Count} objects in '{layer.name}'.");
        }
    }

    /// <summary>
    /// Sync layer data with hierarchy (call this if hierarchy is manually changed)
    /// </summary>
    public void SyncLayersWithHierarchy()
    {
        foreach (var layer in _layers)
        {
            if (layer.layerObject != null)
            {
                // Clear and rebuild object list from hierarchy
                layer.objects.Clear();

                foreach (Transform child in layer.layerObject.transform)
                {
                    layer.objects.Add(child.gameObject);
                }
            }
        }

        if (LayerPanelUI.Instance != null)
            LayerPanelUI.Instance.RefreshLayerList();

        Debug.Log("[LayerManager] Synced layers with hierarchy.");
    }

    /// <summary>
    /// Context menu helper for testing
    /// </summary>
    [ContextMenu("Create Layer from Selection")]
    private void CreateLayerFromSelectionContextMenu()
    {
        CreateLayerFromSelection();
    }

    [ContextMenu("Sync Layers with Hierarchy")]
    private void SyncLayersContextMenu()
    {
        SyncLayersWithHierarchy();
    }
}