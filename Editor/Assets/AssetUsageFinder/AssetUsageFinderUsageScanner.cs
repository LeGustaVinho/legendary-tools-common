using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderUsageScanner
    {
        private readonly AssetUsageFinderCache _cache;

        public AssetUsageFinderUsageScanner(AssetUsageFinderCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public List<AssetUsageFinderCachedUsage> FindUsagesInActiveContext(Object targetAsset)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null && stage.prefabContentsRoot != null
                ? FindUsagesInOpenPrefabStage(targetAsset)
                : FindUsagesInOpenScene(targetAsset);
        }

        public bool TryFindUsagesInOpenContext(
            string fileAssetPath,
            Object targetAsset,
            out List<AssetUsageFinderCachedUsage> usages)
        {
            usages = new List<AssetUsageFinderCachedUsage>();
            if (targetAsset == null || string.IsNullOrEmpty(fileAssetPath))
                return false;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null &&
                stage.prefabContentsRoot != null &&
                string.Equals(stage.assetPath, fileAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                usages = FindUsagesInOpenPrefabStage(targetAsset);
                return true;
            }

            if (!AssetUsageFinderSearchScopeUtility.TryGetCurrentOpenScene(out _))
                return false;

            if (!AssetUsageFinderSearchScopeUtility.IsCurrentOpenScene(fileAssetPath))
                return false;

            usages = FindUsagesInOpenScene(targetAsset);
            return true;
        }

        public List<AssetUsageFinderCachedUsage> FindUsagesInOpenScene(Object targetAsset)
        {
            return !AssetUsageFinderSearchScopeUtility.TryGetCurrentOpenScene(out Scene activeScene)
                ? new List<AssetUsageFinderCachedUsage>()
                : FindUsagesInRoots(activeScene.GetRootGameObjects(), targetAsset);
        }

        public List<AssetUsageFinderCachedUsage> FindUsagesInOpenPrefabStage(Object targetAsset)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage == null || stage.prefabContentsRoot == null
                ? new List<AssetUsageFinderCachedUsage>()
                : FindUsagesInRoots(new[] { stage.prefabContentsRoot }, targetAsset);
        }

        public bool HasUsagesInSceneAsset(string scenePath, Object targetAsset)
        {
            if (AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(scenePath))
                return FindUsagesInOpenScene(targetAsset).Count > 0;

            if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
                return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;

            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid() || !scene.isLoaded)
                    return false;

                return FindUsagesInRoots(scene.GetRootGameObjects(), targetAsset).Count > 0;
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                {
                    try
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }

        public bool HasUsagesInPrefabAsset(string prefabPath, Object targetAsset)
        {
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath))
                return false;

            GameObject root = null;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                return root != null && FindUsagesInRoots(new[] { root }, targetAsset).Count > 0;
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public void SaveCache(string fileAssetPath, List<AssetUsageFinderCachedUsage> usages)
        {
            _cache.Save(fileAssetPath, usages);
        }

        public List<AssetUsageFinderCachedUsage> LoadCache(string fileAssetPath)
        {
            return _cache.Load(fileAssetPath);
        }

        public Object ResolveUsageReference(AssetUsageFinderCachedUsage usage)
        {
            if (usage == null) return null;

            if (!usage.IsComponent)
                return FindGameObjectByHierarchyPath(usage.HierarchyPath);

            GameObject go = FindGameObjectByHierarchyPath(usage.HierarchyPath);
            if (go == null) return null;

            Type t = null;
            if (!string.IsNullOrEmpty(usage.ComponentTypeAssemblyQualifiedName))
                t = Type.GetType(usage.ComponentTypeAssemblyQualifiedName);

            if (t != null)
            {
                Component comp = go.GetComponent(t);
                if (comp != null) return comp;
            }

            if (!string.IsNullOrEmpty(usage.DisplayName))
            {
                Component match = go.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetType().Name == usage.DisplayName);
                if (match != null) return match;
            }

            return null;
        }

        public Object ResolveContextualResultReference(
            string fileAssetPath,
            AssetUsageFinderUsageType usageType,
            string objectPath,
            string objectTypeName)
        {
            if (string.IsNullOrEmpty(objectPath))
                return null;

            string hierarchyPath = ExtractHierarchyPath(objectPath);
            IEnumerable<GameObject> roots = GetOpenContextRoots(fileAssetPath, usageType);
            if (roots == null)
                return null;

            GameObject go = FindGameObjectByHierarchyPath(hierarchyPath, roots);
            if (go == null)
                return null;

            if (string.Equals(objectTypeName, typeof(GameObject).FullName, StringComparison.Ordinal) ||
                string.Equals(objectTypeName, typeof(GameObject).Name, StringComparison.Ordinal))
                return go;

            Type componentType = ResolveComponentType(objectTypeName);
            if (componentType != null)
            {
                Component component = go.GetComponents<Component>()
                    .FirstOrDefault(candidate => candidate != null && componentType.IsAssignableFrom(candidate.GetType()));
                if (component != null)
                    return component;
            }

            string componentTypeName = ExtractComponentTypeName(objectPath);
            if (!string.IsNullOrEmpty(componentTypeName))
            {
                Component component = go.GetComponents<Component>()
                    .FirstOrDefault(candidate => candidate != null &&
                                                 string.Equals(candidate.GetType().Name, componentTypeName,
                                                     StringComparison.Ordinal));
                if (component != null)
                    return component;
            }

            return go;
        }

        public void RemoveUsage(Object targetAsset, AssetUsageFinderCachedUsage usage)
        {
            if (targetAsset == null || usage == null)
                return;

            Object resolved = ResolveUsageReference(usage);
            if (resolved == null)
                return;

            if (usage.IsComponent)
            {
                if (resolved is Component comp)
                    Undo.DestroyObjectImmediate(comp);

                return;
            }

            if (resolved is GameObject go)
                RemoveAssetReferenceFromGameObject(go, targetAsset);
        }

        // ----------------- Variant helpers -----------------

        public bool IsVariantOfSelectedPrefab(string candidatePrefabPath, string selectedPrefabPath)
        {
            if (string.IsNullOrEmpty(candidatePrefabPath) || string.IsNullOrEmpty(selectedPrefabPath))
                return false;

            GameObject candidate = LoadPrefabAtPath(candidatePrefabPath);
            GameObject selected = LoadPrefabAtPath(selectedPrefabPath);
            if (candidate == null || selected == null) return false;

            if (PrefabUtility.GetPrefabAssetType(candidate) != PrefabAssetType.Variant)
                return false;

            HashSet<string> chain = GetVariantAncestry(candidate)
                .Select(go => AssetDatabase.GetAssetPath(go))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return chain.Contains(selectedPrefabPath);
        }

        private static GameObject LoadPrefabAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static IEnumerable<GameObject> GetVariantAncestry(GameObject start, int maxHops = 32)
        {
            List<GameObject> list = new();
            GameObject current = start;
            int guard = 0;

            while (current != null && guard++ < maxHops)
            {
                list.Add(current);

                PrefabAssetType type = PrefabUtility.GetPrefabAssetType(current);
                if (type != PrefabAssetType.Variant)
                    break;

                GameObject parent = PrefabUtility.GetCorrespondingObjectFromSource(current) as GameObject;
                if (parent == null || parent == current)
                    break;

                current = parent;
            }

            return list;
        }

        // ----------------- Core scanning -----------------

        private static List<AssetUsageFinderCachedUsage> FindUsagesInRoots(
            IEnumerable<GameObject> roots,
            Object targetAsset)
        {
            List<AssetUsageFinderCachedUsage> foundUsages = new();
            if (roots == null)
                return foundUsages;

            foreach (GameObject root in roots)
            {
                CollectDirectReferences(root, targetAsset, foundUsages);
            }

            string targetPrefabPath = GetPrefabAssetPath(targetAsset);
            if (string.IsNullOrEmpty(targetPrefabPath))
                return foundUsages;

            foreach (GameObject root in roots)
            {
                CollectPrefabInstanceUsages(root, targetPrefabPath, foundUsages);
            }

            return foundUsages;
        }

        private static void CollectDirectReferences(GameObject root, Object asset,
            List<AssetUsageFinderCachedUsage> usages)
        {
            if (root == null) return;

            Component[] comps = root.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null)
                    continue;

                if (ComponentReferencesAsset(comp, asset))
                {
                    bool active = root.activeInHierarchy;
                    bool? compEnabled = comp is Behaviour b ? b.enabled : (bool?)null;
                    string displayName = comp.GetType().Name;
                    string hierPath = GetGameObjectHierarchyPath(root);
                    string aqn = comp.GetType().AssemblyQualifiedName;

                    usages.Add(new AssetUsageFinderCachedUsage(
                        true,
                        comp,
                        displayName,
                        hierPath,
                        active,
                        compEnabled,
                        aqn));
                }
            }

            foreach (Transform child in root.transform)
            {
                if (child != null)
                    CollectDirectReferences(child.gameObject, asset, usages);
            }
        }

        private static bool ComponentReferencesAsset(Component comp, Object asset)
        {
            // MonoScript special-case.
            if (asset is MonoScript monoScript)
            {
                if (comp is MonoBehaviour mb)
                {
                    MonoScript script = MonoScript.FromMonoBehaviour(mb);
                    if (script == monoScript)
                        return true;
                }
            }

            SerializedObject so = new(comp);
            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                    prop.objectReferenceValue == asset)
                    return true;
            }

            return false;
        }

        private static void RemoveAssetReferenceFromGameObject(GameObject go, Object asset)
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null)
                    continue;

                bool modified = false;
                SerializedObject so = new(comp);
                SerializedProperty prop = so.GetIterator();
                bool enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                        prop.objectReferenceValue == asset)
                    {
                        prop.objectReferenceValue = null;
                        modified = true;
                    }
                }

                if (modified)
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(comp);
                }
            }
        }

        private static void CollectPrefabInstanceUsages(GameObject root, string targetPrefabPath,
            List<AssetUsageFinderCachedUsage> usages)
        {
            if (root == null) return;

            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(root);
                if (instanceRoot == root)
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot) as GameObject;
                    if (source != null)
                    {
                        string srcPath = AssetDatabase.GetAssetPath(source);
                        if (!string.IsNullOrEmpty(srcPath) &&
                            string.Equals(srcPath, targetPrefabPath, StringComparison.OrdinalIgnoreCase))
                        {
                            string hierPath = GetGameObjectHierarchyPath(instanceRoot);
                            bool active = instanceRoot.activeInHierarchy;

                            usages.Add(new AssetUsageFinderCachedUsage(
                                false,
                                instanceRoot,
                                $"PrefabInstance: {instanceRoot.name}",
                                hierPath,
                                active,
                                null));

                            // Avoid duplicates under the same instance root.
                            return;
                        }
                    }
                }
            }

            foreach (Transform child in root.transform)
            {
                if (child != null)
                    CollectPrefabInstanceUsages(child.gameObject, targetPrefabPath, usages);
            }
        }

        // ----------------- Hierarchy resolution -----------------

        private static string GetGameObjectHierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;

            string path = go.name;
            Transform current = go.transform;

            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        private static GameObject FindGameObjectByHierarchyPath(string path)
        {
            IEnumerable<GameObject> roots;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                roots = new[] { stage.prefabContentsRoot };
            else
                roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();

            return FindGameObjectByHierarchyPath(path, roots);
        }

        private static GameObject FindGameObjectByHierarchyPath(string path, IEnumerable<GameObject> roots)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string[] parts = path.Split('/');
            if (parts.Length == 0) return null;

            GameObject current = roots.FirstOrDefault(r => r.name == parts[0]);
            if (current == null) return null;

            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = null;
                foreach (Transform t in current.transform)
                {
                    if (t != null && t.name == parts[i])
                    {
                        child = t;
                        break;
                    }
                }

                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        private static IEnumerable<GameObject> GetOpenContextRoots(string fileAssetPath, AssetUsageFinderUsageType usageType)
        {
            if (usageType == AssetUsageFinderUsageType.Prefab)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null ||
                    stage.prefabContentsRoot == null ||
                    !string.Equals(stage.assetPath, fileAssetPath, StringComparison.OrdinalIgnoreCase))
                    return null;

                return new[] { stage.prefabContentsRoot };
            }

            if (usageType != AssetUsageFinderUsageType.Scene &&
                usageType != AssetUsageFinderUsageType.SceneWithPrefabInstance)
                return null;

            if (AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(fileAssetPath))
            {
                if (!AssetUsageFinderSearchScopeUtility.TryGetCurrentOpenScene(out Scene activeScene) ||
                    !string.IsNullOrEmpty(activeScene.path))
                    return null;

                return activeScene.GetRootGameObjects();
            }

            Scene scene = SceneManager.GetSceneByPath(fileAssetPath);
            return scene.IsValid() && scene.isLoaded
                ? scene.GetRootGameObjects()
                : null;
        }

        private static string ExtractHierarchyPath(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath))
                return string.Empty;

            int suffixIndex = objectPath.LastIndexOf(" (", StringComparison.Ordinal);
            return suffixIndex > 0 && objectPath.EndsWith(")", StringComparison.Ordinal)
                ? objectPath.Substring(0, suffixIndex)
                : objectPath;
        }

        private static string ExtractComponentTypeName(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath) || !objectPath.EndsWith(")", StringComparison.Ordinal))
                return string.Empty;

            int suffixIndex = objectPath.LastIndexOf(" (", StringComparison.Ordinal);
            if (suffixIndex < 0 || suffixIndex + 3 >= objectPath.Length)
                return string.Empty;

            return objectPath.Substring(suffixIndex + 2, objectPath.Length - suffixIndex - 3);
        }

        private static Type ResolveComponentType(string objectTypeName)
        {
            if (string.IsNullOrWhiteSpace(objectTypeName))
                return null;

            Type type = Type.GetType(objectTypeName, false);
            if (type != null && typeof(Component).IsAssignableFrom(type))
                return type;

            string shortName = objectTypeName.Trim();
            return TypeCache.GetTypesDerivedFrom<Component>()
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    (string.Equals(candidate.FullName, shortName, StringComparison.Ordinal) ||
                     string.Equals(candidate.Name, shortName, StringComparison.Ordinal)));
        }

        private static string GetPrefabAssetPath(Object obj)
        {
            if (obj == null) return null;

            string path = AssetDatabase.GetAssetPath(obj);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return null;

            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return null;

            PrefabAssetType type = PrefabUtility.GetPrefabAssetType(go);
            return type == PrefabAssetType.Regular || type == PrefabAssetType.Variant ? path : null;
        }
    }
}
