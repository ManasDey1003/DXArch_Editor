using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class OctreeHierarchyBuilder : MonoBehaviour
{
    public static OctreeHierarchyBuilder Instance { get; private set; }

    [Header("Target")]
    [SerializeField] GameObject _rootObject;
    public GameObject RootModel
    {
        get => _rootObject;
        set => _rootObject = value;
    }

    [Header("Subdivision Settings")]
    [Tooltip("Maximum depth of the octree (typically 6-8 for architecture)")]
    [SerializeField] int _maxDepth = 7;

    [Tooltip("Minimum objects in a node before stopping subdivision")]
    [SerializeField] int _minObjectsPerNode = 10;

    [Tooltip("Minimum node size in world units before stopping subdivision")]
    [SerializeField] float _minNodeSize = 2f;

    [Header("Options")]
    [Tooltip("How to handle objects spanning multiple octants")]
    [SerializeField] AssignmentMode _assignmentMode = AssignmentMode.AssignToSmallestContaining;

    [Tooltip("Preserve original hierarchy structure (creates octree alongside)")]
    [SerializeField] bool _preserveOriginalHierarchy = false;

    [Tooltip("Prefix for octree node names")]
    [SerializeField] string _nodePrefix = "Octree_";

    [Header("Culling Integration")]
    [Tooltip("Automatically register with FrustumCullingManager after build")]
    [SerializeField] bool _autoRegisterCulling = true;

    [Header("AABB Occlusion Culling")]
    [SerializeField] bool _enableAABBOcclusion = true;
    [Tooltip("Minimum screen area percentage (0-1) for an object to be considered an occluder")]
    [SerializeField] float _minOccluderScreenAreaPercent = 0.01f;
    [Tooltip("Depth bias to prevent Z-fighting in occlusion tests")]
    [SerializeField] float _depthBias = 0.01f;

    [Header("Distance-Based Rendering")]
    [Tooltip("Enable forced rendering of nearby objects")]
    [SerializeField] bool _enableDistanceLimiter = true;
    [Tooltip("Objects within this distance always render, regardless of occlusion (in world units)")]
    [SerializeField] float _forceRenderDistance = 10f;
    [Tooltip("Use bounds center for distance check (false = use closest point on bounds)")]
    [SerializeField] bool _useCenter = false;

    [Header("Debug")]
    [SerializeField] bool _showGizmos = true;
    [SerializeField] Color _leafNodeColor = Color.green;
    [SerializeField] Color _internalNodeColor = Color.yellow;
    [SerializeField] bool _debugAABBOcclusion = false;
    [SerializeField] bool _visualizeOccluders = false;
    [SerializeField] bool _visualizeForceRendered = true;

    private OctreeNode _root;
    private List<MeshRenderer> _allRenderers = new();
    private GameObject _octreeRootGameObject;
    private Camera _mainCamera;
    private int _aabbOccludedCount = 0;
    private int _forceRenderedCount = 0;
    private List<AABBOcclusionData> _occlusionData = new();

    public enum AssignmentMode
    {
        AssignToSmallestContaining,
        AssignToCenterOctant,
        DuplicateInOverlapping
    }

    private class AABBOcclusionData
    {
        public MeshRenderer renderer;
        public Bounds bounds;
        public Rect screenRect;
        public float closestDepthNDC;
        public float distanceToCamera;
        public bool isOccluder;
        public bool isOccluded;
        public bool forceRendered;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        _mainCamera = Camera.main;
    }

    public void OnFrustumCullingComplete()
    {
        if (_enableAABBOcclusion && _octreeRootGameObject != null)
        {
            PerformAABBOcclusion();
        }
    }

    [ContextMenu("Build Octree Hierarchy")]
    public void BuildOctreeHierarchy()
    {
        if (_rootObject == null)
        {
            Debug.LogError("[OctreeBuilder] No root object assigned!");
            return;
        }

        Debug.Log($"[OctreeBuilder] Starting octree build for '{_rootObject.name}'...");

        _allRenderers.Clear();
        _allRenderers.AddRange(_rootObject.GetComponentsInChildren<MeshRenderer>());

        if (_allRenderers.Count == 0)
        {
            Debug.LogWarning("[OctreeBuilder] No MeshRenderers found in hierarchy!");
            return;
        }

        Debug.Log($"[OctreeBuilder] Found {_allRenderers.Count} mesh renderers");

        Bounds totalBounds = CalculateTotalBounds();
        Debug.Log($"[OctreeBuilder] Total bounds: center={totalBounds.center}, size={totalBounds.size}");

        _root = new OctreeNode(totalBounds, 0);
        BuildNode(_root, _allRenderers);

        _octreeRootGameObject = CreateHierarchyGameObjects();

        Debug.Log($"[OctreeBuilder] Complete! Created {CountNodes(_root)} octree nodes");
        Debug.Log($"[OctreeBuilder] Leaf nodes: {CountLeafNodes(_root)}, Max depth reached: {GetMaxDepth(_root)}");

        if (_autoRegisterCulling)
        {
            RegisterWithCullingManager();
        }
    }

    [ContextMenu("Perform AABB Occlusion Culling")]
    public void PerformAABBOcclusion()
    {
        if (_mainCamera == null)
        {
            if (_debugAABBOcclusion)
                Debug.LogWarning("[AABBOcclusion] No camera found!");
            return;
        }

        if (_octreeRootGameObject == null)
        {
            if (_debugAABBOcclusion)
                Debug.LogWarning("[AABBOcclusion] Octree not built yet!");
            return;
        }

        HashSet<MeshRenderer> frustumVisibleRenderers = null;
        if (FrustumCullingManager.Instance != null)
        {
            frustumVisibleRenderers = FrustumCullingManager.Instance.GetFrustumVisibleRenderers();
        }

        if (frustumVisibleRenderers == null || frustumVisibleRenderers.Count == 0)
        {
            if (_debugAABBOcclusion)
                Debug.LogWarning("[AABBOcclusion] No frustum-visible renderers to process!");
            return;
        }

        Vector3 cameraPosition = _mainCamera.transform.position;

        // Build occlusion data ONLY for frustum-visible renderers
        _occlusionData.Clear();
        foreach (var renderer in frustumVisibleRenderers)
        {
            if (renderer == null || !renderer.enabled) continue;

            AABBOcclusionData data = new AABBOcclusionData
            {
                renderer = renderer,
                bounds = renderer.bounds
            };

            // Calculate distance to camera
            if (_useCenter)
            {
                data.distanceToCamera = Vector3.Distance(cameraPosition, data.bounds.center);
            }
            else
            {
                // Use closest point on bounds for more accurate distance
                data.distanceToCamera = Vector3.Distance(cameraPosition, data.bounds.ClosestPoint(cameraPosition));
            }

            // Check if within force-render distance
            if (_enableDistanceLimiter && data.distanceToCamera <= _forceRenderDistance)
            {
                data.forceRendered = true;
                _occlusionData.Add(data);
                continue; // Skip projection for force-rendered objects
            }

            // Project AABB to screen space (only for objects beyond force-render distance)
            if (ProjectAABBToScreen(data.bounds, out Rect screenRect, out float closestDepth))
            {
                data.screenRect = screenRect;
                data.closestDepthNDC = closestDepth;
                data.forceRendered = false;
                _occlusionData.Add(data);
            }
        }

        // Sort by depth (closest first) - these are potential occluders
        _occlusionData.Sort((a, b) => a.closestDepthNDC.CompareTo(b.closestDepthNDC));

        // Calculate minimum screen area threshold
        float screenArea = Screen.width * Screen.height;
        float minOccluderArea = screenArea * _minOccluderScreenAreaPercent;

        // Mark potential occluders (force-rendered objects can also be occluders)
        foreach (var data in _occlusionData)
        {
            if (data.forceRendered)
            {
                // Force-rendered objects are always potential occluders
                data.isOccluder = true;
            }
            else
            {
                float area = data.screenRect.width * data.screenRect.height;
                data.isOccluder = area >= minOccluderArea;
            }
        }

        // Test occlusion
        _aabbOccludedCount = 0;
        _forceRenderedCount = 0;
        
        for (int i = 0; i < _occlusionData.Count; i++)
        {
            var testObject = _occlusionData[i];
            testObject.isOccluded = false;

            // Force-rendered objects are NEVER occluded
            if (testObject.forceRendered)
            {
                _forceRenderedCount++;
                continue;
            }

            // Check against all objects in front (earlier in sorted list)
            for (int j = 0; j < i; j++)
            {
                var occluder = _occlusionData[j];

                // Only test against marked occluders that are in front
                if (!occluder.isOccluder) continue;
                
                // For force-rendered occluders, skip depth check
                if (!occluder.forceRendered && occluder.closestDepthNDC >= testObject.closestDepthNDC - _depthBias)
                    continue;

                // Check if occluder's screen rect completely contains test object's screen rect
                if (ScreenRectContains(occluder.screenRect, testObject.screenRect))
                {
                    testObject.isOccluded = true;
                    _aabbOccludedCount++;
                    break;
                }
            }

            // Disable occluded renderers
            if (testObject.isOccluded && testObject.renderer != null)
            {
                testObject.renderer.enabled = false;
            }
        }

        if (_debugAABBOcclusion)
        {
            int occluders = _occlusionData.Count(d => d.isOccluder);
            Debug.Log($"[AABBOcclusion] FrustumVisible: {frustumVisibleRenderers.Count}, " +
                     $"Tested: {_occlusionData.Count}, Occluders: {occluders}, " +
                     $"ForceRendered: {_forceRenderedCount}, Occluded: {_aabbOccludedCount}");
        }
    }

    private bool ProjectAABBToScreen(Bounds bounds, out Rect screenRect, out float closestDepthNDC)
    {
        screenRect = new Rect();
        closestDepthNDC = float.MaxValue;

        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(bounds.min.x, bounds.min.y, bounds.min.z);
        corners[1] = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
        corners[2] = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
        corners[3] = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
        corners[4] = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
        corners[5] = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
        corners[6] = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
        corners[7] = new Vector3(bounds.max.x, bounds.max.y, bounds.max.z);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool anyInFront = false;

        for (int i = 0; i < 8; i++)
        {
            Vector3 viewportPoint = _mainCamera.WorldToViewportPoint(corners[i]);

            if (viewportPoint.z > 0)
            {
                anyInFront = true;

                Vector2 screenPoint = new Vector2(
                    viewportPoint.x * Screen.width,
                    viewportPoint.y * Screen.height
                );

                min.x = Mathf.Min(min.x, screenPoint.x);
                min.y = Mathf.Min(min.y, screenPoint.y);
                max.x = Mathf.Max(max.x, screenPoint.x);
                max.y = Mathf.Max(max.y, screenPoint.y);

                closestDepthNDC = Mathf.Min(closestDepthNDC, viewportPoint.z);
            }
        }

        if (!anyInFront)
            return false;

        min.x = Mathf.Clamp(min.x, 0, Screen.width);
        min.y = Mathf.Clamp(min.y, 0, Screen.height);
        max.x = Mathf.Clamp(max.x, 0, Screen.width);
        max.y = Mathf.Clamp(max.y, 0, Screen.height);

        screenRect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        return true;
    }

    private bool ScreenRectContains(Rect occluder, Rect testRect)
    {
        return occluder.xMin <= testRect.xMin &&
               occluder.xMax >= testRect.xMax &&
               occluder.yMin <= testRect.yMin &&
               occluder.yMax >= testRect.yMax;
    }

    [ContextMenu("Reset All Renderers")]
    public void ResetAllRenderers()
    {
        List<MeshRenderer> renderers = GetAllRenderers();
        foreach (var renderer in renderers)
        {
            if (renderer != null)
                renderer.enabled = true;
        }
        Debug.Log($"[OctreeBuilder] Reset {renderers.Count} renderers to enabled");
    }

    public List<MeshRenderer> GetAllRenderers()
    {
        if (_octreeRootGameObject == null)
        {
            Debug.LogWarning("[OctreeBuilder] Octree not built yet. Call BuildOctreeHierarchy() first.");
            return new List<MeshRenderer>();
        }

        List<MeshRenderer> renderers = new List<MeshRenderer>();
        renderers.AddRange(_octreeRootGameObject.GetComponentsInChildren<MeshRenderer>(true));
        return renderers;
    }

    public Dictionary<OctreeNodeInfo, List<MeshRenderer>> GetRenderersByNode()
    {
        if (_octreeRootGameObject == null)
        {
            Debug.LogWarning("[OctreeBuilder] Octree not built yet.");
            return new Dictionary<OctreeNodeInfo, List<MeshRenderer>>();
        }

        Dictionary<OctreeNodeInfo, List<MeshRenderer>> nodeRenderers = new Dictionary<OctreeNodeInfo, List<MeshRenderer>>();
        OctreeNodeInfo[] allNodes = _octreeRootGameObject.GetComponentsInChildren<OctreeNodeInfo>();

        foreach (var node in allNodes)
        {
            if (node.isLeaf)
            {
                List<MeshRenderer> renderers = new List<MeshRenderer>();
                renderers.AddRange(node.GetComponentsInChildren<MeshRenderer>());
                nodeRenderers[node] = renderers;
            }
        }

        Debug.Log($"[OctreeBuilder] Organized renderers into {nodeRenderers.Count} leaf nodes");
        return nodeRenderers;
    }

    public List<OctreeNodeInfo> GetLeafNodes()
    {
        if (_octreeRootGameObject == null)
        {
            Debug.LogWarning("[OctreeBuilder] Octree not built yet.");
            return new List<OctreeNodeInfo>();
        }

        List<OctreeNodeInfo> leafNodes = new List<OctreeNodeInfo>();
        OctreeNodeInfo[] allNodes = _octreeRootGameObject.GetComponentsInChildren<OctreeNodeInfo>();

        foreach (var node in allNodes)
        {
            if (node.isLeaf)
            {
                leafNodes.Add(node);
            }
        }

        Debug.Log($"[OctreeBuilder] Found {leafNodes.Count} leaf nodes");
        return leafNodes;
    }

    public List<OctreeNodeInfo> GetAllNodes()
    {
        if (_octreeRootGameObject == null)
        {
            Debug.LogWarning("[OctreeBuilder] Octree not built yet.");
            return new List<OctreeNodeInfo>();
        }

        List<OctreeNodeInfo> allNodes = new List<OctreeNodeInfo>();
        allNodes.AddRange(_octreeRootGameObject.GetComponentsInChildren<OctreeNodeInfo>());

        Debug.Log($"[OctreeBuilder] Found {allNodes.Count} total octree nodes");
        return allNodes;
    }

    [ContextMenu("Register with Culling Manager")]
    public void RegisterWithCullingManager()
    {
        if (FrustumCullingManager.Instance == null)
        {
            Debug.LogWarning("[OctreeBuilder] FrustumCullingManager not found in scene!");
            return;
        }

        List<MeshRenderer> renderers = GetAllRenderers();
        if (renderers.Count == 0)
        {
            Debug.LogWarning("[OctreeBuilder] No renderers to register!");
            return;
        }

        FrustumCullingManager.Instance.RegisterRenderers(renderers);
        Debug.Log($"[OctreeBuilder] Registered {renderers.Count} renderers with FrustumCullingManager");
    }

    public GameObject GetOctreeRoot()
    {
        return _octreeRootGameObject;
    }

    public int GetAABBOccludedCount()
    {
        return _aabbOccludedCount;
    }

    public int GetForceRenderedCount()
    {
        return _forceRenderedCount;
    }

    private Bounds CalculateTotalBounds()
    {
        Bounds bounds = _allRenderers[0].bounds;
        for (int i = 1; i < _allRenderers.Count; i++)
        {
            bounds.Encapsulate(_allRenderers[i].bounds);
        }

        bounds.Expand(0.1f);
        return bounds;
    }

    private void BuildNode(OctreeNode node, List<MeshRenderer> renderers)
    {
        if (node.depth >= _maxDepth ||
            renderers.Count <= _minObjectsPerNode ||
            node.bounds.size.x <= _minNodeSize)
        {
            node.renderers = renderers;
            return;
        }

        node.children = new OctreeNode[8];
        Vector3 center = node.bounds.center;
        Vector3 quarter = node.bounds.size * 0.25f;

        for (int i = 0; i < 8; i++)
        {
            Vector3 offset = new Vector3(
                (i & 1) == 0 ? -quarter.x : quarter.x,
                (i & 2) == 0 ? -quarter.y : quarter.y,
                (i & 4) == 0 ? -quarter.z : quarter.z
            );

            Bounds childBounds = new Bounds(center + offset, node.bounds.size * 0.5f);
            node.children[i] = new OctreeNode(childBounds, node.depth + 1);
        }

        List<MeshRenderer>[] childRenderers = new List<MeshRenderer>[8];
        for (int i = 0; i < 8; i++)
            childRenderers[i] = new List<MeshRenderer>();

        foreach (var renderer in renderers)
        {
            AssignRendererToChildren(renderer, node.children, childRenderers);
        }

        for (int i = 0; i < 8; i++)
        {
            if (childRenderers[i].Count > 0)
            {
                BuildNode(node.children[i], childRenderers[i]);
            }
            else
            {
                node.children[i] = null;
            }
        }
    }

    private void AssignRendererToChildren(MeshRenderer renderer, OctreeNode[] children, List<MeshRenderer>[] childLists)
    {
        Bounds objBounds = renderer.bounds;

        switch (_assignmentMode)
        {
            case AssignmentMode.AssignToSmallestContaining:
                int bestFit = -1;
                float bestVolume = float.MaxValue;

                for (int i = 0; i < 8; i++)
                {
                    if (children[i] != null && children[i].bounds.Contains(objBounds.min) &&
                        children[i].bounds.Contains(objBounds.max))
                    {
                        float volume = children[i].bounds.size.x * children[i].bounds.size.y * children[i].bounds.size.z;
                        if (volume < bestVolume)
                        {
                            bestVolume = volume;
                            bestFit = i;
                        }
                    }
                }

                if (bestFit >= 0)
                {
                    childLists[bestFit].Add(renderer);
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (children[i] != null && children[i].bounds.Intersects(objBounds))
                        {
                            childLists[i].Add(renderer);
                        }
                    }
                }
                break;

            case AssignmentMode.AssignToCenterOctant:
                for (int i = 0; i < 8; i++)
                {
                    if (children[i] != null && children[i].bounds.Contains(objBounds.center))
                    {
                        childLists[i].Add(renderer);
                        break;
                    }
                }
                break;

            case AssignmentMode.DuplicateInOverlapping:
                for (int i = 0; i < 8; i++)
                {
                    if (children[i] != null && children[i].bounds.Intersects(objBounds))
                    {
                        childLists[i].Add(renderer);
                    }
                }
                break;
        }
    }

    private GameObject CreateHierarchyGameObjects()
    {
        GameObject octreeRoot = new GameObject($"{_nodePrefix}Root");
        octreeRoot.transform.SetParent(_rootObject.transform.parent);
        octreeRoot.transform.position = _root.bounds.center;

        if (_preserveOriginalHierarchy)
        {
            Debug.Log("[OctreeBuilder] Preserving original hierarchy - octree is reference only");
        }
        else
        {
            octreeRoot.transform.SetParent(_rootObject.transform.parent);
            octreeRoot.transform.SetSiblingIndex(_rootObject.transform.GetSiblingIndex());
        }

        CreateNodeGameObject(_root, octreeRoot);

        if (!_preserveOriginalHierarchy)
        {
            _rootObject.SetActive(false);
            Debug.Log("[OctreeBuilder] Original hierarchy disabled. Delete it if octree works correctly.");
        }

        return octreeRoot;
    }

    private void CreateNodeGameObject(OctreeNode node, GameObject parentGO)
    {
        if (node == null) return;

        if (node.IsLeaf())
        {
            GameObject leafGO = new GameObject($"{_nodePrefix}Leaf_D{node.depth}");
            leafGO.transform.SetParent(parentGO.transform);
            leafGO.transform.position = node.bounds.center;

            OctreeNodeInfo info = leafGO.AddComponent<OctreeNodeInfo>();
            info.bounds = node.bounds;
            info.depth = node.depth;
            info.isLeaf = true;
            info.objectCount = node.renderers.Count;

            if (!_preserveOriginalHierarchy)
            {
                foreach (var renderer in node.renderers)
                {
                    if (renderer != null)
                    {
                        renderer.transform.SetParent(leafGO.transform);
                    }
                }
            }
        }
        else
        {
            GameObject internalGO = new GameObject($"{_nodePrefix}Internal_D{node.depth}");
            internalGO.transform.SetParent(parentGO.transform);
            internalGO.transform.position = node.bounds.center;

            OctreeNodeInfo info = internalGO.AddComponent<OctreeNodeInfo>();
            info.bounds = node.bounds;
            info.depth = node.depth;
            info.isLeaf = false;

            for (int i = 0; i < 8; i++)
            {
                if (node.children[i] != null)
                {
                    CreateNodeGameObject(node.children[i], internalGO);
                }
            }
        }
    }

    private int CountNodes(OctreeNode node)
    {
        if (node == null) return 0;
        int count = 1;
        if (node.children != null)
        {
            for (int i = 0; i < 8; i++)
                count += CountNodes(node.children[i]);
        }
        return count;
    }

    private int CountLeafNodes(OctreeNode node)
    {
        if (node == null) return 0;
        if (node.IsLeaf()) return 1;
        int count = 0;
        for (int i = 0; i < 8; i++)
            count += CountLeafNodes(node.children[i]);
        return count;
    }

    private int GetMaxDepth(OctreeNode node)
    {
        if (node == null) return 0;
        if (node.IsLeaf()) return node.depth;
        int maxDepth = node.depth;
        for (int i = 0; i < 8; i++)
            maxDepth = Mathf.Max(maxDepth, GetMaxDepth(node.children[i]));
        return maxDepth;
    }

    void OnDrawGizmos()
    {
        if (!_showGizmos || _root == null) return;
        DrawNodeGizmos(_root);

        if (_visualizeOccluders && _occlusionData.Count > 0)
        {
            foreach (var data in _occlusionData)
            {
                if (data.renderer == null) continue;

                if (data.forceRendered && _visualizeForceRendered)
                {
                    // Force-rendered objects = Cyan
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireCube(data.bounds.center, data.bounds.size * 1.05f);
                }
                else if (data.isOccluder)
                {
                    // Occluders = Blue
                    Gizmos.color = Color.blue;
                    Gizmos.DrawWireCube(data.bounds.center, data.bounds.size * 1.02f);
                }
                else if (data.isOccluded)
                {
                    // Occluded = Red
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireCube(data.bounds.center, data.bounds.size);
                }
            }
        }

        // Visualize force-render distance sphere
        if (_enableDistanceLimiter && _mainCamera != null && _visualizeForceRendered)
        {
            Gizmos.color = new Color(0, 1, 1, 0.1f);
            Gizmos.DrawWireSphere(_mainCamera.transform.position, _forceRenderDistance);
        }
    }

    private void DrawNodeGizmos(OctreeNode node)
    {
        if (node == null) return;

        Gizmos.color = node.IsLeaf() ? _leafNodeColor : _internalNodeColor;
        Gizmos.DrawWireCube(node.bounds.center, node.bounds.size);

        if (node.children != null)
        {
            for (int i = 0; i < 8; i++)
                DrawNodeGizmos(node.children[i]);
        }
    }

    private class OctreeNode
    {
        public Bounds bounds;
        public int depth;
        public OctreeNode[] children;
        public List<MeshRenderer> renderers;

        public OctreeNode(Bounds bounds, int depth)
        {
            this.bounds = bounds;
            this.depth = depth;
        }

        public bool IsLeaf() => children == null;
    }
}

public class OctreeNodeInfo : MonoBehaviour
{
    public Bounds bounds;
    public int depth;
    public bool isLeaf;
    public int objectCount;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = isLeaf ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(bounds.center, bounds.size);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(bounds.center,
            $"Depth: {depth}\n{(isLeaf ? $"Objects: {objectCount}" : "Internal")}");
#endif
    }
}