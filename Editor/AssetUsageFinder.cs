/// <summary>
/// AssetUsageFinder is an Editor window tool that searches for files referencing a specified asset.
/// It includes filtering by asset type (Scenes, Prefabs, ScriptableObjects, Materials, Others) 
/// and displays usage details for GameObjects and Components. It also allows you to remove a usage 
/// by either deleting a component or nulling out a reference.
/// </summary>

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// A custom Unity Editor window for finding and displaying asset usage across scenes, prefabs, and other asset types.
    /// </summary>
    public class AssetUsageFinder : EditorWindow
    {
        private Object _assetToFind;
        private Vector2 _scrollPos;
        private List<AssetUsageEntry> _usageEntries = new();

        private static readonly Dictionary<string, List<CachedUsage>> s_usageCache = new();

        private static readonly Dictionary<string, List<AssetUsageEntry>> s_fileUsageCache = new();

        private bool _filterScenes = true;
        private bool _filterPrefabs = true;
        private bool _filterScriptableObjects = true;
        private bool _filterMaterials = true;
        private bool _filterOther = true;

        /// <summary>
        /// Defines the types of asset usage that can be searched for.
        /// </summary>
        private enum UsageType
        {
            Scene,
            Prefab,
            ScriptableObject,
            Material,
            Other,
            SceneWithPrefabInstance
        }

        /// <summary>
        /// Represents an entry for an asset usage, including its path and type.
        /// </summary>
        private class AssetUsageEntry
        {
            public string AssetPath { get; set; }
            public UsageType UsageType { get; set; }
            public bool UsageListExpanded { get; set; }
            public bool PrefabListExpanded { get; set; }
            public string SourcePrefabPath { get; set; }

            public AssetUsageEntry(string path, UsageType type, string sourcePrefabPath = null)
            {
                AssetPath = path;
                UsageType = type;
                SourcePrefabPath = sourcePrefabPath;
            }
        }

        /// <summary>
        /// Represents cached information about an asset usage, including its reference and hierarchy details.
        /// </summary>
        private class CachedUsage
        {
            public bool IsComponent { get; set; }
            public Object Reference { get; set; }
            public string DisplayName { get; set; }
            public string HierarchyPath { get; set; }
            public bool GameObjectActive { get; set; }
            public bool? ComponentEnabled { get; set; }

            public CachedUsage(bool isComponent, Object reference, string displayName, string hierarchyPath,
                bool gameObjectActive, bool? componentEnabled)
            {
                IsComponent = isComponent;
                Reference = reference;
                DisplayName = displayName;
                HierarchyPath = hierarchyPath;
                GameObjectActive = gameObjectActive;
                ComponentEnabled = componentEnabled;
            }
        }

        /// <summary>
        /// Opens the Asset Usage Finder window from the Unity Editor menu.
        /// </summary>
        [MenuItem("Tools/LegendaryTools/Asset Usage Finder")]
        public static void ShowWindow()
        {
            AssetUsageFinder window = GetWindow<AssetUsageFinder>("Asset Usage Finder");
            window.Show();
        }

        /// <summary>
        /// Renders the GUI for the Asset Usage Finder window.
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Asset Usage Finder", EditorStyles.boldLabel);
            _assetToFind = EditorGUILayout.ObjectField("Asset", _assetToFind, typeof(Object), false);
            EditorGUILayout.LabelField("Filter by asset type:");
            _filterScenes = EditorGUILayout.Toggle("Scenes", _filterScenes);
            _filterPrefabs = EditorGUILayout.Toggle("Prefabs", _filterPrefabs);
            _filterScriptableObjects = EditorGUILayout.Toggle("ScriptableObjects", _filterScriptableObjects);
            _filterMaterials = EditorGUILayout.Toggle("Materials", _filterMaterials);
            _filterOther = EditorGUILayout.Toggle("Other", _filterOther);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find Usages")) FindUsages();
            if (GUILayout.Button("Clear Cache", GUILayout.Width(100)))
            {
                s_usageCache.Clear();
                s_fileUsageCache.Clear();
                Debug.Log("All caches cleared.");
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            if (_usageEntries.Count > 0)
            {
                EditorGUILayout.LabelField("Files that reference the asset:", EditorStyles.boldLabel);
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

                // Group SceneWithPrefabInstance entries by AssetPath
                Dictionary<string, List<string>> scenePrefabGroups = _usageEntries
                    .Where(e => e.UsageType == UsageType.SceneWithPrefabInstance)
                    .GroupBy(e => e.AssetPath)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.SourcePrefabPath).Distinct().ToList());

                // Process entries, handling scenes with prefabs in a grouped manner
                HashSet<string> processedScenes = new();
                foreach (AssetUsageEntry entry in _usageEntries)
                {
                    if (!PassesFilter(entry.UsageType))
                        continue;

                    // Skip SceneWithPrefabInstance if already processed
                    if (entry.UsageType == UsageType.SceneWithPrefabInstance)
                    {
                        if (processedScenes.Contains(entry.AssetPath))
                            continue;
                        processedScenes.Add(entry.AssetPath);
                    }

                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();

                    string displayLabel = Path.GetFileName(entry.AssetPath) + " (" + entry.UsageType.ToString();
                    if (entry.UsageType == UsageType.SceneWithPrefabInstance)
                    {
                        List<string> prefabs = scenePrefabGroups[entry.AssetPath];
                        displayLabel += $", via {prefabs.Count} Prefab{(prefabs.Count > 1 ? "s" : "")}";
                    }

                    displayLabel += ")";
                    EditorGUILayout.LabelField(displayLabel, GUILayout.Width(700));
                    Object fileAsset = AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);

                    if (entry.UsageType == UsageType.Scene || entry.UsageType == UsageType.SceneWithPrefabInstance)
                    {
                        if (GUILayout.Button("Open Scene", GUILayout.Width(100)))
                            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                                EditorSceneManager.OpenScene(entry.AssetPath);

                        if (GUILayout.Button("Ping", GUILayout.Width(50))) EditorGUIUtility.PingObject(fileAsset);
                    }
                    else if (entry.UsageType == UsageType.Prefab)
                    {
                        if (GUILayout.Button("Open Prefab Mode", GUILayout.Width(130)))
                            PrefabStageUtility.OpenPrefab(entry.AssetPath);
                        if (GUILayout.Button("Ping", GUILayout.Width(50))) EditorGUIUtility.PingObject(fileAsset);
                    }
                    else
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(100))) EditorGUIUtility.PingObject(fileAsset);
                    }

                    EditorGUILayout.EndHorizontal();

                    // Display list of prefabs for SceneWithPrefabInstance
                    if (entry.UsageType == UsageType.SceneWithPrefabInstance &&
                        scenePrefabGroups.ContainsKey(entry.AssetPath))
                    {
                        EditorGUI.indentLevel++;
                        List<string> prefabs = scenePrefabGroups[entry.AssetPath];
                        entry.PrefabListExpanded =
                            EditorGUILayout.Foldout(entry.PrefabListExpanded, $"Prefabs ({prefabs.Count})");
                        if (entry.PrefabListExpanded)
                            foreach (string prefabPath in prefabs)
                            {
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField($"Prefab: {Path.GetFileName(prefabPath)}",
                                    GUILayout.Width(300));
                                if (GUILayout.Button("Open Prefab Mode", GUILayout.Width(130)))
                                    PrefabStageUtility.OpenPrefab(prefabPath);
                                if (GUILayout.Button("Ping", GUILayout.Width(50)))
                                {
                                    Object prefabAsset = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
                                    EditorGUIUtility.PingObject(prefabAsset);
                                }

                                EditorGUILayout.EndHorizontal();
                            }

                        EditorGUI.indentLevel--;
                    }

                    bool isActive = false;
                    if (entry.UsageType == UsageType.Scene || entry.UsageType == UsageType.SceneWithPrefabInstance)
                    {
                        Scene activeScene = EditorSceneManager.GetActiveScene();
                        if (!string.IsNullOrEmpty(activeScene.path) && activeScene.path == entry.AssetPath)
                            isActive = true;
                    }
                    else if (entry.UsageType == UsageType.Prefab)
                    {
                        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                        if (prefabStage != null && prefabStage.assetPath == entry.AssetPath)
                            isActive = true;
                    }

                    if (isActive)
                    {
                        if (s_usageCache.ContainsKey(entry.AssetPath))
                        {
                            List<CachedUsage> cachedList = s_usageCache[entry.AssetPath];
                            bool needUpdate = cachedList.Any(item => item.Reference == null);
                            if (needUpdate)
                            {
                                List<CachedUsage> newCache = FindAssetUsagesWithReference(_assetToFind);
                                s_usageCache[entry.AssetPath] = newCache;
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("Find Usages in File", GUILayout.Width(250)))
                            {
                                List<CachedUsage> foundUsages = FindAssetUsagesWithReference(_assetToFind);
                                s_usageCache[entry.AssetPath] = foundUsages;
                            }
                        }

                        entry.UsageListExpanded = EditorGUILayout.Foldout(entry.UsageListExpanded, "Usages");
                    }

                    if (entry.UsageListExpanded && s_usageCache.ContainsKey(entry.AssetPath))
                    {
                        List<CachedUsage> cachedUsages = s_usageCache[entry.AssetPath];
                        EditorGUI.indentLevel++;
                        foreach (CachedUsage usage in cachedUsages)
                        {
                            EditorGUILayout.BeginHorizontal();
                            bool valid = usage.Reference != null;
                            EditorGUI.BeginDisabledGroup(!valid);
                            if (GUILayout.Button("Select", GUILayout.Width(100)))
                                if (valid)
                                {
                                    if (usage.IsComponent)
                                    {
                                        Selection.activeObject = usage.Reference;
                                        EditorGUIUtility.PingObject(usage.Reference);
                                    }
                                    else
                                    {
                                        Selection.activeGameObject = (GameObject)usage.Reference;
                                        EditorGUIUtility.PingObject(usage.Reference);
                                    }
                                }

                            EditorGUI.EndDisabledGroup();

                            if (GUILayout.Button("Remove", GUILayout.Width(100)))
                                if (usage.Reference != null)
                                {
                                    if (usage.IsComponent)
                                    {
                                        Component comp = usage.Reference as Component;
                                        if (comp != null)
                                        {
                                            Debug.Log(
                                                $"Removed component: {comp.GetType().Name} from {usage.HierarchyPath}");
                                            Undo.DestroyObjectImmediate(comp);
                                        }
                                    }
                                    else
                                    {
                                        GameObject go = usage.Reference as GameObject;
                                        if (go != null)
                                        {
                                            RemoveAssetReferenceFromGameObject(go, _assetToFind);
                                            Debug.Log(
                                                $"Nullified asset reference in GameObject: {go.name} ({usage.HierarchyPath})");
                                        }
                                    }

                                    List<CachedUsage> newCache = FindAssetUsagesWithReference(_assetToFind);
                                    s_usageCache[entry.AssetPath] = newCache;
                                }

                            string status = $"Active: {(usage.GameObjectActive ? "Yes" : "No")}";
                            if (usage.IsComponent)
                                status +=
                                    $", Enabled: {(usage.ComponentEnabled.HasValue ? usage.ComponentEnabled.Value ? "Yes" : "No" : "N/A")}";
                            EditorGUILayout.LabelField(usage.DisplayName, GUILayout.Width(200));
                            EditorGUILayout.LabelField(usage.HierarchyPath, GUILayout.Width(600));
                            EditorGUILayout.LabelField(status, GUILayout.Width(200));
                            EditorGUILayout.EndHorizontal();
                        }

                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        /// Determines if the specified usage type passes the current filter settings.
        /// </summary>
        /// <param name="type">The type of asset usage to check.</param>
        /// <returns>True if the usage type is allowed by the filter; otherwise, false.</returns>
        private bool PassesFilter(UsageType type)
        {
            switch (type)
            {
                case UsageType.Scene:
                case UsageType.SceneWithPrefabInstance:
                    return _filterScenes;
                case UsageType.Prefab:
                    return _filterPrefabs;
                case UsageType.ScriptableObject:
                    return _filterScriptableObjects;
                case UsageType.Material:
                    return _filterMaterials;
                case UsageType.Other:
                    return _filterOther;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Gets the hierarchy path of a GameObject in the scene or prefab.
        /// </summary>
        /// <param name="go">The GameObject to get the hierarchy path for.</param>
        /// <returns>The hierarchy path as a string, or an empty string if the GameObject is null.</returns>
        private static string GetGameObjectHierarchyPath(GameObject go)
        {
            if (go == null) return "";
            string path = go.name;
            Transform current = go.transform;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        /// <summary>
        /// Searches for usages of the selected asset across all relevant assets in the project.
        /// </summary>
        private void FindUsages()
        {
            _usageEntries.Clear();
            if (_assetToFind == null)
            {
                Debug.LogWarning("Please select an asset or script to find usages.");
                return;
            }

            string assetPathToFind = AssetDatabase.GetAssetPath(_assetToFind);
            string guidToFind = AssetDatabase.AssetPathToGUID(assetPathToFind);

            if (s_fileUsageCache.ContainsKey(guidToFind))
            {
                _usageEntries = new List<AssetUsageEntry>(s_fileUsageCache[guidToFind]);
                EditorUtility.ClearProgressBar();
                return;
            }

            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            int totalPaths = allPaths.Length;
            List<AssetUsageEntry> foundEntries = new();
            HashSet<string> processedScenes = new();

            for (int i = 0; i < totalPaths; i++)
            {
                string path = allPaths[i];
                if (path == assetPathToFind || !path.StartsWith("Assets"))
                    continue;

                string ext = Path.GetExtension(path).ToLower();
                bool allowed = false;
                UsageType type;
                switch (ext)
                {
                    case ".unity":
                        allowed = _filterScenes;
                        type = UsageType.Scene;
                        break;
                    case ".prefab":
                        allowed = _filterPrefabs;
                        type = UsageType.Prefab;
                        break;
                    case ".mat":
                        allowed = _filterMaterials;
                        type = UsageType.Material;
                        break;
                    case ".asset":
                        Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                        type = obj is ScriptableObject ? UsageType.ScriptableObject : UsageType.Other;
                        allowed = obj is ScriptableObject ? _filterScriptableObjects : _filterOther;
                        break;
                    default:
                        allowed = _filterOther;
                        type = UsageType.Other;
                        break;
                }

                if (!allowed)
                    continue;

                EditorUtility.DisplayProgressBar("Finding Usages", "Analyzing " + path, (float)i / totalPaths);

                bool guidFound = false;
                string[] dependencies = AssetDatabase.GetDependencies(path, true);
                bool dependencyFound = dependencies.Contains(assetPathToFind);

                if (ext == ".unity" || ext == ".prefab" || ext == ".asset" || ext == ".mat")
                    try
                    {
                        string fileContent = File.ReadAllText(path);
                        if (fileContent.Contains(guidToFind))
                            guidFound = true;
                    }
                    catch
                    {
                        guidFound = dependencyFound;
                    }
                else
                    guidFound = dependencyFound;

                if (guidFound) foundEntries.Add(new AssetUsageEntry(path, type));

                if (ext == ".unity" && _filterScenes && !processedScenes.Contains(path))
                {
                    string[] prefabDependencies = AssetDatabase.GetDependencies(path, true)
                        .Where(dep => dep.EndsWith(".prefab"))
                        .ToArray();

                    foreach (string prefabPath in prefabDependencies)
                    {
                        bool prefabReferencesAsset = false;
                        if (_assetToFind is MonoScript)
                        {
                            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                            if (prefab != null)
                            {
                                Component[] components = prefab.GetComponentsInChildren<Component>(true);
                                foreach (Component comp in components)
                                {
                                    if (comp is MonoBehaviour mb)
                                    {
                                        MonoScript script = MonoScript.FromMonoBehaviour(mb);
                                        if (script == _assetToFind)
                                        {
                                            prefabReferencesAsset = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                string prefabContent = File.ReadAllText(prefabPath);
                                if (prefabContent.Contains(guidToFind))
                                    prefabReferencesAsset = true;
                            }
                            catch
                            {
                                string[] prefabDeps = AssetDatabase.GetDependencies(prefabPath, true);
                                prefabReferencesAsset = prefabDeps.Contains(assetPathToFind);
                            }
                        }

                        if (prefabReferencesAsset)
                        {
                            foundEntries.Add(new AssetUsageEntry(path, UsageType.SceneWithPrefabInstance, prefabPath));
                            processedScenes.Add(path);
                        }
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            _usageEntries = foundEntries;
            s_fileUsageCache[guidToFind] = new List<AssetUsageEntry>(_usageEntries);
        }

        /// <summary>
        /// Finds all usages of the specified asset within the active scene or prefab.
        /// </summary>
        /// <param name="asset">The asset to find usages for.</param>
        /// <returns>A list of cached usage entries for the asset.</returns>
        private List<CachedUsage> FindAssetUsagesWithReference(Object asset)
        {
            List<CachedUsage> foundUsages = new();
            GameObject[] roots;
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                roots = new GameObject[] { prefabStage.prefabContentsRoot };
            }
            else
            {
                Scene activeScene = EditorSceneManager.GetActiveScene();
                roots = activeScene.GetRootGameObjects();
            }

            foreach (GameObject root in roots)
            {
                GetAssetUsagesForGameObject(root, asset, foundUsages);
            }

            return foundUsages;
        }

        /// <summary>
        /// Recursively searches a GameObject and its children for references to the specified asset.
        /// </summary>
        /// <param name="go">The GameObject to search.</param>
        /// <param name="asset">The asset to find references to.</param>
        /// <param name="usages">The list to store found usages.</param>
        private void GetAssetUsagesForGameObject(GameObject go, Object asset, List<CachedUsage> usages)
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null)
                    continue;
                if (ComponentReferencesAsset(comp, asset))
                {
                    bool active = go.activeInHierarchy;
                    bool? compEnabled = comp is Behaviour behaviour ? behaviour.enabled : null;
                    string dispName = comp.GetType().Name;
                    string hierPath = GetGameObjectHierarchyPath(go);
                    usages.Add(new CachedUsage(true, comp, dispName, hierPath, active, compEnabled));
                }
            }

            foreach (Transform child in go.transform)
            {
                GetAssetUsagesForGameObject(child.gameObject, asset, usages);
            }
        }

        /// <summary>
        /// Checks if a component references the specified asset.
        /// </summary>
        /// <param name="comp">The component to check.</param>
        /// <param name="asset">The asset to look for.</param>
        /// <returns>True if the component references the asset; otherwise, false.</returns>
        private bool ComponentReferencesAsset(Component comp, Object asset)
        {
            if (asset is MonoScript monoScript)
                if (comp is MonoBehaviour mb)
                {
                    MonoScript script = MonoScript.FromMonoBehaviour(mb);
                    if (script == monoScript)
                        return true;
                }

            SerializedObject so = new(comp);
            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == asset)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Removes references to the specified asset from a GameObject's components.
        /// </summary>
        /// <param name="go">The GameObject to remove references from.</param>
        /// <param name="asset">The asset to remove references to.</param>
        private void RemoveAssetReferenceFromGameObject(GameObject go, Object asset)
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
    }
}