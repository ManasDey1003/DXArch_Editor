using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LayerItemUI : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI _layerNameText;
    [SerializeField] private TextMeshProUGUI _objectCountText;
    [SerializeField] private Toggle _visibilityToggle;
    [SerializeField] private Button _selectButton;
    [SerializeField] private Button _deleteButton;
    [SerializeField] private Image _backgroundImage;

    private Layer _layer;
    private int _layerIndex;
    private LayerPanelUI _panelUI;

    public void Initialize(Layer layer, int layerIndex, LayerPanelUI panelUI)
    {
        _layer = layer;
        _layerIndex = layerIndex;
        _panelUI = panelUI;

        UpdateUI();
        SetupListeners();
    }

    private void UpdateUI()
    {
        if (_layerNameText != null)
            _layerNameText.text = _layer.name;

        if (_objectCountText != null)
            _objectCountText.text = $"({_layer.objects.Count})";

        if (_visibilityToggle != null)
            _visibilityToggle.isOn = _layer.isVisible;
    }

    private void SetupListeners()
    {
        if (_visibilityToggle != null)
        {
            _visibilityToggle.onValueChanged.AddListener(OnVisibilityToggled);
        }

        if (_selectButton != null)
        {
            _selectButton.onClick.AddListener(OnSelectClicked);
        }

        if (_deleteButton != null)
        {
            _deleteButton.onClick.AddListener(OnDeleteClicked);
        }
    }

    private void OnVisibilityToggled(bool isVisible)
    {
        _panelUI.OnVisibilityToggled(_layerIndex, isVisible);
    }

    private void OnSelectClicked()
    {
        _panelUI.OnLayerSelected(_layerIndex);
    }

    private void OnDeleteClicked()
    {
        _panelUI.OnDeleteLayer(_layerIndex);
    }

    public void SetSelected(bool isSelected)
    {
        if (_backgroundImage != null)
        {
            _backgroundImage.color = isSelected ?
                new Color(0.3f, 0.5f, 0.8f, 0.5f) :
                new Color(1f, 1f, 1f, 0.1f);
        }
    }
}