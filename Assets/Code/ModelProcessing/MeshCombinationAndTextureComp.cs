using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class MeshCombinationAndTextureComp
{
    public class ModelOptimizer
    {
        public bool combineMeshes = true;
        public int maxVerticesPerMesh = 65000;
        public float spatialClusterRadius = 25f;
        public bool simplifyTexturesBySize = true;
        public float largeMeshThreshold = 0;
        public bool simplifyBeforeCombining = true;

        // ── Shader name constants ───────────────────────────────────────────
        private const string ShaderBase = "Custom/TransparentWithDepthColor";

        private Dictionary<Texture2D, Color> textureAverageCache = new Dictionary<Texture2D, Color>();
        private Dictionary<MaterialKey, Material> simplifiedMaterialCache = new Dictionary<MaterialKey, Material>();

        // Track every renderer we create so we can swap shaders later
        private List<MeshRenderer> combinedRenderers = new List<MeshRenderer>();

        private struct MaterialKey
        {
            public Shader shader;
            public Color color;

            public MaterialKey(Shader shader, Color color) { this.shader = shader; this.color = color; }

            public override bool Equals(object obj)
            {
                if (!(obj is MaterialKey)) return false;
                MaterialKey other = (MaterialKey)obj;
                return shader == other.shader &&
                       Mathf.Approximately(color.r, other.color.r) &&
                       Mathf.Approximately(color.g, other.color.g) &&
                       Mathf.Approximately(color.b, other.color.b) &&
                       Mathf.Approximately(color.a, other.color.a);
            }

            public override int GetHashCode() => shader.GetHashCode() ^ color.GetHashCode();
        }

        // ── Material-mode switch ────────────────────────────────────────────
        public void SetDepthColorMode(bool enabled)
        {
            int switched = 0;
            foreach (MeshRenderer mr in combinedRenderers)
            {
                if (mr == null) continue;

                foreach (Material mat in mr.materials)
                {
                    if (mat == null) continue;
                    mat.SetFloat("_DepthColor", enabled ? 1f : 0f);
                    switched++;
                }
            }

            Debug.Log($"[ModelOptimizer] SetDepthColorMode({enabled}): updated {switched} material(s)");
        }

        // ------------------------------------------------------------------
        //  Public entry points
        // ------------------------------------------------------------------
        public List<MeshRenderer> GetCombinedRenderers() => combinedRenderers;
        
        public void ApplyOptimizations(GameObject model)
        {
            combinedRenderers.Clear();

            if (simplifyTexturesBySize && simplifyBeforeCombining)
            {
                Debug.Log("=== SIMPLIFYING TEXTURES (BEFORE COMBINING) ===");
                SimplifyTexturesByMeshSize(model);
            }

            if (combineMeshes)
            {
                Debug.Log("=== STARTING MESH COMBINING ===");
                CombineMeshesByMaterial(model);
                Debug.Log("=== MESH COMBINING COMPLETE ===");
            }
            else
            {
                // --- NEW: If not combining, populate the list with existing renderers ---
                combinedRenderers.AddRange(model.GetComponentsInChildren<MeshRenderer>(false));
            }

            if (simplifyTexturesBySize && !simplifyBeforeCombining)
            {
                Debug.Log("=== SIMPLIFYING TEXTURES (AFTER COMBINING) ===");
                SimplifyTexturesByMeshSize(model);
            }

            for (int i = 0; i < model.transform.childCount; i++)
            {
                var child = model.transform.GetChild(i);
                Debug.Log($"  Final Child {i}: {child.name}");
                var renderers = child.GetComponentsInChildren<Renderer>();
                Debug.Log($"    Renderers: {renderers.Length}");
            }
        }

        public async Task ApplyOptimizationsAsync(GameObject model, Action<float> onProgress = null)
        {
            combinedRenderers.Clear();

            float currentProgress = 0f;
            int totalSteps = 0;

            bool doSimplifyBefore = simplifyTexturesBySize && simplifyBeforeCombining;
            bool doCombine = combineMeshes;
            bool doSimplifyAfter = simplifyTexturesBySize && !simplifyBeforeCombining;

            if (doSimplifyBefore) totalSteps++;
            if (doCombine) totalSteps += 3;
            else totalSteps++; // Add a step for gathering existing renderers
            
            if (doSimplifyAfter) totalSteps++;

            if (totalSteps == 0) { onProgress?.Invoke(1f); return; }

            float progressPerUnit = 1f / totalSteps;

            if (doSimplifyBefore)
            {
                Debug.Log("=== SIMPLIFYING TEXTURES (BEFORE COMBINING) ===");
                await SimplifyTexturesByMeshSizeAsync(model, p =>
                    onProgress?.Invoke(currentProgress + (p * progressPerUnit)));
                currentProgress += progressPerUnit;
            }

            if (doCombine)
            {
                Debug.Log("=== STARTING MESH COMBINING ===");
                float combineWeight = progressPerUnit * 3;
                await CombineMeshesByMaterialAsync(model, p =>
                    onProgress?.Invoke(currentProgress + (p * combineWeight)));
                currentProgress += combineWeight;
                Debug.Log("=== MESH COMBINING COMPLETE ===");
            }
            else
            {
                // --- NEW: If not combining, populate the list with existing renderers ---
                combinedRenderers.AddRange(model.GetComponentsInChildren<MeshRenderer>(false));
                currentProgress += progressPerUnit;
            }

            if (doSimplifyAfter)
            {
                Debug.Log("=== SIMPLIFYING TEXTURES (AFTER COMBINING) ===");
                await SimplifyTexturesByMeshSizeAsync(model, p =>
                    onProgress?.Invoke(currentProgress + (p * progressPerUnit)));
            }

            onProgress?.Invoke(1f);
        }

        // ------------------------------------------------------------------
        //  Texture simplification
        // ------------------------------------------------------------------

        private void SimplifyTexturesByMeshSize(GameObject targetModel)
        {
            MeshRenderer[] renderers = targetModel.GetComponentsInChildren<MeshRenderer>();
            int simplifiedCount = 0, skippedCount = 0, sharedMaterialCount = 0;

            foreach (MeshRenderer renderer in renderers)
            {
                float meshSize = renderer.bounds.size.magnitude;
                if (meshSize >= largeMeshThreshold)
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool materialsChanged = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material mat = materials[i];
                        if (mat == null) continue;

                        Material simplifiedMat = GetOrCreateSimplifiedMaterial(mat);
                        if (simplifiedMat != null && simplifiedMat != mat)
                        {
                            materials[i] = simplifiedMat;
                            materialsChanged = true;
                            simplifiedCount++;

                            int cacheCount = 0; bool isReused = false;
                            foreach (var cachedMat in simplifiedMaterialCache.Values)
                            {
                                if (cachedMat == simplifiedMat) { cacheCount++; if (cacheCount > 1) { isReused = true; break; } }
                            }
                            if (isReused) sharedMaterialCount++;
                        }
                    }

                    if (materialsChanged) renderer.sharedMaterials = materials;
                }
                else skippedCount++;
            }

            Debug.Log($"Texture simplification: {simplifiedCount} simplified, {skippedCount} kept, {sharedMaterialCount} reused");
        }

        private async Task SimplifyTexturesByMeshSizeAsync(GameObject targetModel, Action<float> onProgress = null)
        {
            MeshRenderer[] renderers = targetModel.GetComponentsInChildren<MeshRenderer>();
            int simplifiedCount = 0, skippedCount = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int k = 0; k < renderers.Length; k++)
            {
                onProgress?.Invoke((float)k / renderers.Length);
                if (sw.ElapsedMilliseconds > 10) { await Task.Yield(); sw.Restart(); }

                MeshRenderer renderer = renderers[k];
                if (renderer == null) continue;

                float meshSize = renderer.bounds.size.magnitude;
                if (meshSize >= largeMeshThreshold)
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool materialsChanged = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material mat = materials[i];
                        if (mat == null) continue;
                        Material simplifiedMat = GetOrCreateSimplifiedMaterial(mat);
                        if (simplifiedMat != null && simplifiedMat != mat)
                        {
                            materials[i] = simplifiedMat;
                            materialsChanged = true;
                            simplifiedCount++;
                        }
                    }

                    if (materialsChanged) renderer.sharedMaterials = materials;
                }
                else skippedCount++;
            }

            onProgress?.Invoke(1f);
            Debug.Log($"Texture simplification (Async): {simplifiedCount} simplified, {skippedCount} skipped.");
        }

        // ------------------------------------------------------------------
        //  Material helpers
        // ------------------------------------------------------------------

        private float ColorDifference(Color a, Color b)
        {
            float dr = (a.r - b.r) * 0.299f;
            float dg = (a.g - b.g) * 0.587f;
            float db = (a.b - b.b) * 0.114f;
            float da = (a.a - b.a) * 0.5f;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db + da * da);
        }

        private Material FindSimilarCachedMaterial(Shader shader, Color targetColor, float threshold = 0.05f)
        {
            foreach (var kvp in simplifiedMaterialCache)
            {
                if (kvp.Key.shader != shader) continue;
                if (ColorDifference(kvp.Key.color, targetColor) <= threshold)
                    return kvp.Value;
            }
            return null;
        }

        private Material GetOrCreateSimplifiedMaterial(Material originalMaterial)
        {
            if (originalMaterial == null) return null;

            Shader customShader = Shader.Find(ShaderBase);
            if (customShader == null)
            {
                Debug.LogError($"Shader not found: {ShaderBase}");
                return originalMaterial;
            }

            Texture mainTex = originalMaterial.mainTexture;
            Color sourceColor = Color.white;

            if (mainTex == null)
            {
                if (originalMaterial.HasProperty("_BaseColor")) sourceColor = originalMaterial.GetColor("_BaseColor");
                else if (originalMaterial.HasProperty("_Color")) sourceColor = originalMaterial.GetColor("_Color");
                else sourceColor = originalMaterial.color;
            }
            else if (mainTex is Texture2D tex2D)
            {
                if (textureAverageCache.ContainsKey(tex2D))
                    sourceColor = textureAverageCache[tex2D];
                else
                {
                    sourceColor = GetAverageColorFromTexture(tex2D);
                    if (sourceColor == Color.clear) sourceColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    textureAverageCache[tex2D] = sourceColor;
                }
            }
            else
            {
                sourceColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            }

            MaterialKey key = new MaterialKey(customShader, sourceColor);
            if (simplifiedMaterialCache.ContainsKey(key))
                return simplifiedMaterialCache[key];

            Material similar = FindSimilarCachedMaterial(customShader, sourceColor);
            if (similar != null) return similar;

            Material newMat = new Material(customShader);
            newMat.name = $"Simplified_{ColorUtility.ToHtmlStringRGB(sourceColor)}";
            newMat.SetColor("_Color", sourceColor);
            newMat.SetFloat("_Alpha", 1.0f);
            simplifiedMaterialCache[key] = newMat;

            Debug.Log($"Created material: {newMat.name} (color: {sourceColor})");
            return newMat;
        }

        private Color GetAverageColorFromTexture(Texture2D texture)
        {
            if (texture == null) return Color.clear;
            try
            {
                Color[] pixels = texture.GetPixels();
                float r = 0, g = 0, b = 0, a = 0;
                foreach (Color p in pixels) { r += p.r; g += p.g; b += p.b; a += p.a; }
                int n = pixels.Length;
                return new Color(r / n, g / n, b / n, a / n);
            }
            catch
            {
                try { return GetAverageColorFromUnreadableTexture(texture); }
                catch { return new Color(0.5f, 0.5f, 0.5f, 1f); }
            }
        }

        private Color GetAverageColorFromUnreadableTexture(Texture2D texture)
        {
            int size = Mathf.Min(256, texture.width, texture.height);
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(size, size, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = readable.GetPixels();
            float r = 0, g = 0, b = 0, a = 0;
            foreach (Color p in pixels) { r += p.r; g += p.g; b += p.b; a += p.a; }
            int n = pixels.Length;
            UnityEngine.Object.DestroyImmediate(readable);
            return new Color(r / n, g / n, b / n, a / n);
        }

        // ------------------------------------------------------------------
        //  Spatial clustering
        // ------------------------------------------------------------------

        private List<List<CombineInstance>> ClusterByProximity(List<CombineInstance> combines, float maxRadius)
        {
            var remaining = new List<CombineInstance>(combines);
            var clusters = new List<List<CombineInstance>>();

            while (remaining.Count > 0)
            {
                var cluster = new List<CombineInstance>();
                CombineInstance seed = remaining[0];
                remaining.RemoveAt(0);
                cluster.Add(seed);

                Vector3 clusterCenter = seed.transform.MultiplyPoint3x4(seed.mesh.bounds.center);

                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    CombineInstance ci = remaining[i];
                    if (ci.mesh == null) { remaining.RemoveAt(i); continue; }

                    Vector3 meshCenter = ci.transform.MultiplyPoint3x4(ci.mesh.bounds.center);
                    if (Vector3.Distance(meshCenter, clusterCenter) <= maxRadius)
                    {
                        cluster.Add(ci);
                        remaining.RemoveAt(i);

                        clusterCenter = Vector3.zero;
                        foreach (var c in cluster)
                            clusterCenter += c.transform.MultiplyPoint3x4(c.mesh.bounds.center);
                        clusterCenter /= cluster.Count;
                    }
                }
                clusters.Add(cluster);
            }

            Debug.Log($"  Proximity clustering: {combines.Count} → {clusters.Count} clusters (r={maxRadius})");
            return clusters;
        }

        // ------------------------------------------------------------------
        //  Mesh combining (sync)
        // ------------------------------------------------------------------

        private void CombineMeshesByMaterial(GameObject targetModel)
        {
            MeshFilter[] meshFilters = targetModel.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0) { Debug.LogWarning("No meshes found!"); return; }

            var materialGroups = new Dictionary<Material, List<CombineInstance>>();

            GameObject combinedParent = new GameObject("CombinedMeshes");
            combinedParent.transform.SetParent(targetModel.transform);
            combinedParent.transform.localPosition = Vector3.zero;
            combinedParent.transform.localRotation = Quaternion.identity;
            combinedParent.transform.localScale = Vector3.one;

            Matrix4x4 toLocal = combinedParent.transform.worldToLocalMatrix;

            foreach (MeshFilter mf in meshFilters)
            {
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;

                Mesh mesh = mf.sharedMesh;
                Material[] mats = mr.sharedMaterials;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = s < mats.Length ? mats[s] : null;
                    if (mat == null) continue;

                    Mesh sub = ExtractSubmesh(mesh, s);
                    if (sub == null || sub.vertexCount == 0) continue;

                    if (!materialGroups.ContainsKey(mat))
                        materialGroups[mat] = new List<CombineInstance>();

                    materialGroups[mat].Add(new CombineInstance
                    {
                        mesh = sub,
                        transform = toLocal * mf.transform.localToWorldMatrix
                    });
                }
            }

            foreach (MeshFilter mf in meshFilters)
                mf.gameObject.SetActive(false);

            int groupIndex = 0;
            foreach (var kvp in materialGroups)
            {
                Material mat = kvp.Key;
                var spatialClusters = ClusterByProximity(kvp.Value, spatialClusterRadius);

                for (int ci = 0; ci < spatialClusters.Count; ci++)
                {
                    int currentVerts = 0, batchIdx = 0;
                    var batch = new List<CombineInstance>();

                    foreach (var instance in spatialClusters[ci])
                    {
                        if (instance.mesh == null) continue;
                        if (currentVerts + instance.mesh.vertexCount > maxVerticesPerMesh && batch.Count > 0)
                        {
                            CreateCombinedMesh(combinedParent, mat, batch, $"{mat.name}_C{ci}_B{batchIdx}");
                            batch.Clear(); currentVerts = 0; batchIdx++;
                        }
                        batch.Add(instance);
                        currentVerts += instance.mesh.vertexCount;
                    }

                    if (batch.Count > 0)
                    {
                        string n = (spatialClusters.Count == 1 && batchIdx == 0)
                            ? mat.name : $"{mat.name}_C{ci}_B{batchIdx}";
                        CreateCombinedMesh(combinedParent, mat, batch, n);
                    }
                }
                groupIndex++;
            }
        }

        // ------------------------------------------------------------------
        //  Mesh combining (async)
        // ------------------------------------------------------------------

        private async Task CombineMeshesByMaterialAsync(GameObject targetModel, Action<float> onProgress = null)
        {
            MeshFilter[] meshFilters = targetModel.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0) { onProgress?.Invoke(1f); return; }

            var materialGroups = new Dictionary<Material, List<CombineInstance>>();

            GameObject combinedParent = new GameObject("CombinedMeshes");
            combinedParent.transform.SetParent(targetModel.transform);
            combinedParent.transform.localPosition = Vector3.zero;
            combinedParent.transform.localRotation = Quaternion.identity;
            combinedParent.transform.localScale = Vector3.one;

            Matrix4x4 toLocal = combinedParent.transform.worldToLocalMatrix;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float phase1 = 0.3f;

            for (int i = 0; i < meshFilters.Length; i++)
            {
                onProgress?.Invoke((float)i / meshFilters.Length * phase1);
                if (sw.ElapsedMilliseconds > 10) { await Task.Yield(); sw.Restart(); }

                MeshFilter mf = meshFilters[i];
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;

                Mesh mesh = mf.sharedMesh;
                Material[] mats = mr.sharedMaterials;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = s < mats.Length ? mats[s] : null;
                    if (mat == null) continue;
                    Mesh sub = ExtractSubmesh(mesh, s);
                    if (sub == null || sub.vertexCount == 0) continue;
                    if (!materialGroups.ContainsKey(mat)) materialGroups[mat] = new List<CombineInstance>();
                    materialGroups[mat].Add(new CombineInstance { mesh = sub, transform = toLocal * mf.transform.localToWorldMatrix });
                }
            }

            foreach (MeshFilter mf in meshFilters)
                mf.gameObject.SetActive(false);

            int groupIndex = 0, totalGroups = materialGroups.Count;

            foreach (var kvp in materialGroups)
            {
                float gStart = phase1 + ((float)groupIndex / totalGroups * (1f - phase1));
                float gEnd = phase1 + ((float)(groupIndex + 1) / totalGroups * (1f - phase1));

                Material mat = kvp.Key;
                var spatialClusters = ClusterByProximity(kvp.Value, spatialClusterRadius);

                for (int ci = 0; ci < spatialClusters.Count; ci++)
                {
                    var clusterMeshes = spatialClusters[ci];
                    int currentVerts = 0, batchIdx = 0;
                    var batch = new List<CombineInstance>();

                    for (int i = 0; i < clusterMeshes.Count; i++)
                    {
                        float inner = ((float)ci / spatialClusters.Count) + ((float)i / clusterMeshes.Count / spatialClusters.Count);
                        onProgress?.Invoke(Mathf.Lerp(gStart, gEnd, inner));
                        if (sw.ElapsedMilliseconds > 10) { await Task.Yield(); sw.Restart(); }

                        CombineInstance instance = clusterMeshes[i];
                        if (instance.mesh == null) continue;

                        if (currentVerts + instance.mesh.vertexCount > maxVerticesPerMesh && batch.Count > 0)
                        {
                            CreateCombinedMesh(combinedParent, mat, batch, $"{mat.name}_C{ci}_B{batchIdx}");
                            batch.Clear(); currentVerts = 0; batchIdx++;
                        }
                        batch.Add(instance);
                        currentVerts += instance.mesh.vertexCount;
                    }

                    if (batch.Count > 0)
                    {
                        string n = (spatialClusters.Count == 1 && batchIdx == 0)
                            ? mat.name : $"{mat.name}_C{ci}_B{batchIdx}";
                        CreateCombinedMesh(combinedParent, mat, batch, n);
                    }
                }
                groupIndex++;
            }

            onProgress?.Invoke(1f);
        }

        // ------------------------------------------------------------------
        //  Submesh extraction  (unchanged)
        // ------------------------------------------------------------------

        private Mesh ExtractSubmesh(Mesh sourceMesh, int submeshIndex)
        {
            if (submeshIndex >= sourceMesh.subMeshCount) return null;
            int[] triangles = sourceMesh.GetTriangles(submeshIndex);
            if (triangles.Length == 0) return null;

            var usedVertices = new HashSet<int>();
            foreach (int idx in triangles) usedVertices.Add(idx);

            int[] oldToNew = new int[sourceMesh.vertexCount];
            var newToOld = new List<int>();

            for (int i = 0; i < sourceMesh.vertexCount; i++)
            {
                if (usedVertices.Contains(i)) { oldToNew[i] = newToOld.Count; newToOld.Add(i); }
            }

            Mesh newMesh = new Mesh { name = $"{sourceMesh.name}_Sub{submeshIndex}" };

            void CopyArray<T>(T[] src, System.Action<T[]> setter)
            {
                if (src == null || src.Length == 0) return;
                T[] dst = new T[newToOld.Count];
                for (int i = 0; i < newToOld.Count; i++) dst[i] = src[newToOld[i]];
                setter(dst);
            }

            CopyArray(sourceMesh.vertices, v => newMesh.vertices = v);
            CopyArray(sourceMesh.normals, v => newMesh.normals = v);
            CopyArray(sourceMesh.uv, v => newMesh.uv = v);
            CopyArray(sourceMesh.uv2, v => newMesh.uv2 = v);
            CopyArray(sourceMesh.tangents, v => newMesh.tangents = v);
            CopyArray(sourceMesh.colors, v => newMesh.colors = v);

            int[] newTris = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++) newTris[i] = oldToNew[triangles[i]];
            newMesh.triangles = newTris;
            newMesh.RecalculateBounds();
            return newMesh;
        }

        // ------------------------------------------------------------------
        //  Create combined mesh  — registers renderer for later shader swap
        // ------------------------------------------------------------------

        private void CreateCombinedMesh(GameObject parent, Material material,
                                         List<CombineInstance> combines, string name)
        {
            GameObject obj = new GameObject($"Combined_{name}");
            obj.transform.SetParent(parent.transform);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;

            MeshFilter mf = obj.AddComponent<MeshFilter>();
            MeshRenderer mr = obj.AddComponent<MeshRenderer>();

            // ── Register so SetDepthColorMode() can find this renderer ──
            combinedRenderers.Add(mr);

            Mesh combinedMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combinedMesh.CombineMeshes(combines.ToArray(), true, true);
            combinedMesh.RecalculateBounds();

            // Weighted pivot
            Vector3 weightedCenter = Vector3.zero;
            float totalWeight = 0f;

            foreach (var ci in combines)
            {
                if (ci.mesh == null) continue;
                float w = ci.mesh.vertexCount;
                Vector3 c = ci.transform.MultiplyPoint3x4(ci.mesh.bounds.center);
                weightedCenter += c * w;
                totalWeight += w;
            }

            Vector3 pivot = totalWeight > 0f ? weightedCenter / totalWeight : combinedMesh.bounds.center;

            Vector3[] verts = combinedMesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= pivot;
            combinedMesh.vertices = verts;
            combinedMesh.RecalculateBounds();
            combinedMesh.Optimize();

            obj.transform.localPosition = pivot;
            mf.mesh = combinedMesh;
            mr.material = material;

            Debug.Log($"Combined: {name} | v:{combinedMesh.vertexCount} t:{combinedMesh.triangles.Length / 3} pivot:{pivot}");
        }
    }
}