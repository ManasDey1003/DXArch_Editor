using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class LayerPanelUI : MonoBehaviour
{
    public static LayerPanelUI Instance;
    
    [Header("References")]
    [SerializeField] private LayerManager _layerManager;
    [SerializeField] private Transform _layerListContainer;
    [SerializeField] private GameObject _layerItemPrefab;
    
    [Header("Buttons")]
    [SerializeField] private Button _createLayerButton;
    
    [Header("Settings")]
    [SerializeField] private Color _selectedLayerColor = new Color(0.3f, 0.5f, 0.8f);
    [SerializeField] private Color _normalLayerColor = Color.white;
    
    private List<GameObject> _layerUIItems = new List<GameObject>();
    private int _selectedLayerIndex = -1;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        if (_layerManager == null)
            _layerManager = LayerManager.Instance;
        
        if (_createLayerButton != null)
            _createLayerButton.onClick.AddListener(OnCreateLayerClicked);
        
        RefreshLayerList();
    }

    private void OnCreateLayerClicked()
    {
        _layerManager.CreateLayerFromSelection();
        RefreshLayerList();
    }

    public void RefreshLayerList()
    {
        // Clear existing UI items
        foreach (var item in _layerUIItems)
        {
            if (item != null)
                Destroy(item);
        }
        _layerUIItems.Clear();

        // Create UI for each layer
        for (int i = 0; i < _layerManager.Layers.Count; i++)
        {
            CreateLayerUIItem(i);
        }
    }

    private void CreateLayerUIItem(int layerIndex)
    {
        Layer layer = _layerManager.Layers[layerIndex];
        
        GameObject layerItem = Instantiate(_layerItemPrefab, _layerListContainer);
        _layerUIItems.Add(layerItem);
        
        // Get components
        LayerItemUI itemUI = layerItem.GetComponent<LayerItemUI>();
        if (itemUI != null)
        {
            itemUI.Initialize(layer, layerIndex, this);
        }
    }

    public void OnVisibilityToggled(int layerIndex, bool isVisible)
    {
        _layerManager.SetLayerVisibility(layerIndex, isVisible);
    }

    public void OnLayerSelected(int layerIndex)
    {
        _selectedLayerIndex = layerIndex;
        _layerManager.SelectLayer(layerIndex);
        UpdateLayerSelection();
    }

    public void OnDeleteLayer(int layerIndex)
    {
        _layerManager.DeleteLayer(layerIndex);
        RefreshLayerList();
    }

    private void UpdateLayerSelection()
    {
        for (int i = 0; i < _layerUIItems.Count; i++)
        {
            LayerItemUI itemUI = _layerUIItems[i].GetComponent<LayerItemUI>();
            if (itemUI != null)
            {
                itemUI.SetSelected(i == _selectedLayerIndex);
            }
        }
    }

    // Public method to be called when selection changes externally
    public void OnSelectionChanged()
    {
        RefreshLayerList();
    }
}