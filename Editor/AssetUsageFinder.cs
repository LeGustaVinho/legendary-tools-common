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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

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
        private AssetGuidMapper _guidMapper = new();
        private bool _isMapping = false;

        private bool _filterScenes = true;
        private bool _filterPrefabs = true;
        private bool _filterScriptableObjects = true;
        private bool _filterMaterials = true;
        private bool _filterOther = true;

        private static readonly string[] SEARCH_EXTENSIONS = { ".unity", ".prefab", ".mat", ".asset", ".anim", ".controller", ".shader" };
        private static readonly string JSON_PATH = Path.Combine("Library", "AssetUsageFinderMapping.json");

        // Shared JSON.NET settings for EditorPrefs cache (avoid loops and type stamps).
        private static readonly JsonSerializerSettings CacheJsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            TypeNameHandling = TypeNameHandling.None,
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            Formatting = Formatting.None
        };

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
        /// Represents cached information about an asset usage, including hierarchy details.
        /// Note: UnityEngine.Object is not serialized to avoid editor crashes.
        /// </summary>
        private class CachedUsage
        {
            public bool IsComponent { get; set; }

            [JsonIgnore] // Prevent deep serialization of native Unity objects.
            public Object Reference { get; set; }

            public string DisplayName { get; set; }
            public string HierarchyPath { get; set; }
            public bool GameObjectActive { get; set; }
            public bool? ComponentEnabled { get; set; }

            // Assembly-qualified name for reliable component resolution at click time.
            public string ComponentTypeAssemblyQualifiedName { get; set; }

            public CachedUsage(bool isComponent, Object reference, string displayName, string hierarchyPath,
                bool gameObjectActive, bool? componentEnabled, string componentTypeAQN = null)
            {
                IsComponent = isComponent;
                Reference = reference;
                DisplayName = displayName;
                HierarchyPath = hierarchyPath;
                GameObjectActive = gameObjectActive;
                ComponentEnabled = componentEnabled;
                ComponentTypeAssemblyQualifiedName = componentTypeAQN;
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

        private void OnEnable()
        {
            // Load existing mapping if available.
            _guidMapper.LoadMappingFromJson(JSON_PATH);
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
            EditorGUI.BeginDisabledGroup(_isMapping);
            if (GUILayout.Button("Find Usages")) FindUsagesAsync();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Clear Cache", GUILayout.Width(100)))
            {
                _guidMapper.ClearMapping();
                File.Delete(JSON_PATH);
                Debug.Log("Cache cleared and JSON file deleted.");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            if (_usageEntries.Count > 0)
            {
                EditorGUILayout.LabelField("Files that reference the asset:", EditorStyles.boldLabel);
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

                // Group SceneWithPrefabInstance entries by AssetPath.
                Dictionary<string, List<string>> scenePrefabGroups = _usageEntries
                    .Where(e => e.UsageType == UsageType.SceneWithPrefabInstance)
                    .GroupBy(e => e.AssetPath)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.SourcePrefabPath).Distinct().ToList());

                // Selected prefab path if target is a prefab.
                string selectedPrefabPath = GetSelectedPrefabAssetPath();

                // Process entries.
                HashSet<string> processedScenes = new();
                foreach (AssetUsageEntry entry in _usageEntries)
                {
                    if (!PassesFilter(entry.UsageType))
                        continue;

                    // Skip SceneWithPrefabInstance duplicates.
                    if (entry.UsageType == UsageType.SceneWithPrefabInstance)
                    {
                        if (processedScenes.Contains(entry.AssetPath))
                            continue;
                        processedScenes.Add(entry.AssetPath);
                    }

                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();

                    string displayLabel = Path.GetFileName(entry.AssetPath) + " (" + entry.UsageType + ")";

                    // Mark Prefab lines that are Variants of the selected prefab.
                    if (entry.UsageType == UsageType.Prefab && !string.IsNullOrEmpty(selectedPrefabPath))
                    {
                        if (IsVariantOfSelectedPrefab(entry.AssetPath, selectedPrefabPath))
                            displayLabel += " — Variant of selected prefab";
                    }

                    EditorGUILayout.LabelField(displayLabel, GUILayout.Width(700));
                    Object fileAsset = AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);

                    if (entry.UsageType == UsageType.Scene || entry.UsageType == UsageType.SceneWithPrefabInstance)
                    {
                        if (GUILayout.Button("Open Scene", GUILayout.Width(100)))
                        {
                            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                                EditorSceneManager.OpenScene(entry.AssetPath);
                        }

                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                            EditorGUIUtility.PingObject(fileAsset);

                        if (GUILayout.Button("Find In Finder", GUILayout.Width(130)))
                            FindUsagesForAsset(fileAsset);
                    }
                    else if (entry.UsageType == UsageType.Prefab)
                    {
                        if (GUILayout.Button("Open Prefab Mode", GUILayout.Width(130)))
                            PrefabStageUtility.OpenPrefab(entry.AssetPath);

                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                            EditorGUIUtility.PingObject(fileAsset);

                        if (GUILayout.Button("Find In Finder", GUILayout.Width(130)))
                            FindUsagesForAsset(fileAsset);
                    }
                    else
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(100)))
                            EditorGUIUtility.PingObject(fileAsset);

                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                            EditorGUIUtility.PingObject(fileAsset);

                        if (GUILayout.Button("Find In Finder", GUILayout.Width(130)))
                            FindUsagesForAsset(fileAsset);
                    }

                    EditorGUILayout.EndHorizontal();

                    // Display list of prefabs for SceneWithPrefabInstance, tagging variants.
                    if (entry.UsageType == UsageType.SceneWithPrefabInstance &&
                        scenePrefabGroups.ContainsKey(entry.AssetPath))
                    {
                        EditorGUI.indentLevel++;
                        List<string> prefabs = scenePrefabGroups[entry.AssetPath];
                        entry.PrefabListExpanded =
                            EditorGUILayout.Foldout(entry.PrefabListExpanded, $"Prefabs ({prefabs.Count})");
                        if (entry.PrefabListExpanded)
                        {
                            foreach (string prefabPath in prefabs)
                            {
                                EditorGUILayout.BeginHorizontal();

                                string prefabName = Path.GetFileName(prefabPath);
                                string variantTag = (!string.IsNullOrEmpty(selectedPrefabPath) &&
                                                     IsVariantOfSelectedPrefab(prefabPath, selectedPrefabPath))
                                    ? " — Variant of selected prefab"
                                    : string.Empty;

                                EditorGUILayout.LabelField($"Prefab: {prefabName}{variantTag}", GUILayout.Width(350));

                                if (GUILayout.Button("Open Prefab Mode", GUILayout.Width(130)))
                                    PrefabStageUtility.OpenPrefab(prefabPath);

                                if (GUILayout.Button("Ping", GUILayout.Width(50)))
                                {
                                    Object prefabAssetObj = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
                                    EditorGUIUtility.PingObject(prefabAssetObj);
                                }

                                if (GUILayout.Button("Find In Finder", GUILayout.Width(130)))
                                {
                                    Object prefabAssetObj = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
                                    FindUsagesForAsset(prefabAssetObj);
                                }

                                EditorGUILayout.EndHorizontal();
                            }
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
                        if (GUILayout.Button("Find Usages in File", GUILayout.Width(250)))
                        {
                            List<CachedUsage> foundUsages = FindAssetUsagesWithReference(_assetToFind);

                            // Serialize with Newtonsoft.Json (UnityEngine.Object fields ignored).
                            string key = $"AssetUsageFinder_{entry.AssetPath}";
                            string json = JsonConvert.SerializeObject(foundUsages, CacheJsonSettings);
                            EditorPrefs.SetString(key, json);
                        }

                        entry.UsageListExpanded = EditorGUILayout.Foldout(entry.UsageListExpanded, "Usages");
                    }

                    if (entry.UsageListExpanded && EditorPrefs.HasKey($"AssetUsageFinder_{entry.AssetPath}"))
                    {
                        string key = $"AssetUsageFinder_{entry.AssetPath}";
                        string json = EditorPrefs.GetString(key);
                        List<CachedUsage> cachedUsages =
                            string.IsNullOrEmpty(json)
                                ? new List<CachedUsage>()
                                : (JsonConvert.DeserializeObject<List<CachedUsage>>(json, CacheJsonSettings) ?? new List<CachedUsage>());

                        EditorGUI.indentLevel++;
                        foreach (CachedUsage usage in cachedUsages)
                        {
                            // Resolve target on-demand (do not read from serialized Reference).
                            Object resolvedRef = ResolveUsageReference(usage);
                            bool valid = resolvedRef != null;

                            EditorGUILayout.BeginHorizontal();
                            EditorGUI.BeginDisabledGroup(!valid);
                            if (GUILayout.Button("Select", GUILayout.Width(100)))
                            {
                                if (valid)
                                {
                                    if (usage.IsComponent)
                                    {
                                        Selection.activeObject = resolvedRef;
                                        EditorGUIUtility.PingObject(resolvedRef);
                                    }
                                    else
                                    {
                                        Selection.activeGameObject = (GameObject)resolvedRef;
                                        EditorGUIUtility.PingObject(resolvedRef);
                                    }
                                }
                            }
                            EditorGUI.EndDisabledGroup();

                            if (GUILayout.Button("Remove", GUILayout.Width(100)))
                            {
                                if (resolvedRef != null)
                                {
                                    if (usage.IsComponent)
                                    {
                                        Component comp = resolvedRef as Component;
                                        if (comp != null)
                                        {
                                            Debug.Log($"Removed component: {comp.GetType().Name} from {usage.HierarchyPath}");
                                            Undo.DestroyObjectImmediate(comp);
                                        }
                                    }
                                    else
                                    {
                                        GameObject go = resolvedRef as GameObject;
                                        if (go != null)
                                        {
                                            RemoveAssetReferenceFromGameObject(go, _assetToFind);
                                            Debug.Log($"Nullified asset reference in GameObject: {go.name} ({usage.HierarchyPath})");
                                        }
                                    }

                                    // Refresh cache (UnityEngine.Object ignored while serializing).
                                    List<CachedUsage> newCache = FindAssetUsagesWithReference(_assetToFind);
                                    string newJson = JsonConvert.SerializeObject(newCache, CacheJsonSettings);
                                    EditorPrefs.SetString(key, newJson);
                                }
                            }

                            string status = $"Active: {(usage.GameObjectActive ? "Yes" : "No")}";
                            if (usage.IsComponent)
                                status += $", Enabled: {(usage.ComponentEnabled.HasValue ? (usage.ComponentEnabled.Value ? "Yes" : "No") : "N/A")}";

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

        /// <summary>
        /// Searches for usages of the selected asset across all relevant assets in the project using AssetGuidMapper.
        /// Populates _usageEntries and supports scenes with prefab instances that reference the target via prefab dependencies.
        /// </summary>
        private async void FindUsagesAsync()
        {
            if (_isMapping)
            {
                Debug.LogWarning("Mapping is already in progress.");
                return;
            }

            _usageEntries.Clear();
            if (_assetToFind == null)
            {
                Debug.LogWarning("Please select an asset or script to find usages.");
                return;
            }

            _isMapping = true;
            string assetPathToFind = AssetDatabase.GetAssetPath(_assetToFind);
            string guidToFind = AssetDatabase.AssetPathToGUID(assetPathToFind);

            // Check if mapping exists, otherwise perform full mapping.
            if (!_guidMapper.LoadMappingFromJson(JSON_PATH))
            {
                await _guidMapper.MapProjectGUIDsAsync(SEARCH_EXTENSIONS);
                _guidMapper.SaveMappingToJson(JSON_PATH);
            }

            // Find files containing the GUID.
            List<string> foundFiles = await _guidMapper.FindFilesContainingGuidAsync(guidToFind);
            List<AssetUsageEntry> foundEntries = new();
            HashSet<string> processedScenes = new();

            foreach (string path in foundFiles)
            {
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

                foundEntries.Add(new AssetUsageEntry(path, type));
            }

            // Handle scenes with prefab instances (scene depends on a prefab that references the target).
            if (_filterScenes)
            {
                foreach (string scenePath in foundFiles.Where(f => f.EndsWith(".unity")))
                {
                    if (processedScenes.Contains(scenePath))
                        continue;

                    string[] prefabDependencies = AssetDatabase.GetDependencies(scenePath, true)
                        .Where(dep => dep.EndsWith(".prefab"))
                        .ToArray();

                    foreach (string prefabPath in prefabDependencies)
                    {
                        bool prefabReferencesAsset = await _guidMapper.FileContainsGuidAsync(prefabPath, guidToFind);
                        if (prefabReferencesAsset)
                        {
                            foundEntries.Add(new AssetUsageEntry(scenePath, UsageType.SceneWithPrefabInstance, prefabPath));
                            processedScenes.Add(scenePath);
                        }
                    }
                }
            }

            _usageEntries = foundEntries;
            EditorUtility.ClearProgressBar();
            _isMapping = false;
            Repaint();
        }

        /// <summary>
        /// Searches for usages of the selected asset across the active scene or the open prefab stage.
        /// If the target asset is a prefab, this also collects nested prefab instances that reference it.
        /// </summary>
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

            // 1) Regular object reference scan on components.
            foreach (GameObject root in roots)
            {
                GetAssetUsagesForGameObject(root, asset, foundUsages);
            }

            // 2) If the target is a prefab asset, also collect nested prefab instances referencing it.
            string targetPrefabPath = GetPrefabAssetPath(asset);
            if (!string.IsNullOrEmpty(targetPrefabPath))
            {
                foreach (GameObject root in roots)
                {
                    GetPrefabInstanceUsages(root, targetPrefabPath, foundUsages);
                }
            }

            return foundUsages;
        }

        /// <summary>
        /// Recursively searches a GameObject and its children for direct references to the specified asset in components.
        /// </summary>
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
                    string aqn = comp.GetType().AssemblyQualifiedName;

                    // Store the reference in memory only; it will not be serialized to JSON.
                    usages.Add(new CachedUsage(true, comp, dispName, hierPath, active, compEnabled, aqn));
                }
            }

            foreach (Transform child in go.transform)
            {
                if (child != null)
                    GetAssetUsagesForGameObject(child.gameObject, asset, usages);
            }
        }

        /// <summary>
        /// Checks if a component references the specified asset.
        /// </summary>
        private bool ComponentReferencesAsset(Component comp, Object asset)
        {
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
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes references to the specified asset from a GameObject's components.
        /// </summary>
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

        // -------- Prefab helpers: variant detection and nested instance search --------

        /// <summary>
        /// Returns the GameObject loaded from a prefab asset path, or null.
        /// </summary>
        private static GameObject LoadPrefabAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Walks up the variant chain returning every ancestor including the start node.
        /// First element is 'start'; last is the ultimate regular base (if found).
        /// </summary>
        private static IEnumerable<GameObject> GetVariantAncestry(GameObject start, int maxHops = 32)
        {
            var list = new List<GameObject>();
            var current = start;
            int guard = 0;

            while (current != null && guard++ < maxHops)
            {
                list.Add(current);

                var type = PrefabUtility.GetPrefabAssetType(current);
                if (type != PrefabAssetType.Variant)
                    break;

                var parent = PrefabUtility.GetCorrespondingObjectFromSource(current) as GameObject;
                if (parent == null || parent == current)
                    break;

                current = parent;
            }

            return list;
        }

        /// <summary>
        /// Returns true if candidatePrefabPath is a variant (direct or transitive) of selectedPrefabPath.
        /// </summary>
        private bool IsVariantOfSelectedPrefab(string candidatePrefabPath, string selectedPrefabPath)
        {
            if (string.IsNullOrEmpty(candidatePrefabPath) || string.IsNullOrEmpty(selectedPrefabPath))
                return false;

            var candidate = LoadPrefabAtPath(candidatePrefabPath);
            var selected = LoadPrefabAtPath(selectedPrefabPath);
            if (candidate == null || selected == null) return false;

            // Candidate must be a variant to be considered.
            if (PrefabUtility.GetPrefabAssetType(candidate) != PrefabAssetType.Variant)
                return false;

            // Build ancestry chain (candidate upwards).
            var candidateChain = GetVariantAncestry(candidate).Select(go => AssetDatabase.GetAssetPath(go)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Specifically check whether candidate is a variant of the selected prefab itself.
            return candidateChain.Contains(selectedPrefabPath);
        }

        /// <summary>
        /// Returns prefab asset path of _assetToFind if it is a prefab asset (regular or variant), else null.
        /// </summary>
        private string GetSelectedPrefabAssetPath()
        {
            return GetPrefabAssetPath(_assetToFind);
        }

        /// <summary>
        /// Returns prefab asset path of an object if it is a prefab asset (regular or variant), else null.
        /// </summary>
        private string GetPrefabAssetPath(Object obj)
        {
            if (obj == null) return null;
            var path = AssetDatabase.GetAssetPath(obj);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return null;

            var go = LoadPrefabAtPath(path);
            if (go == null) return null;

            var type = PrefabUtility.GetPrefabAssetType(go);
            return (type == PrefabAssetType.Regular || type == PrefabAssetType.Variant) ? path : null;
        }

        /// <summary>
        /// Recursively collects nested prefab instances whose source asset path equals targetPrefabPath.
        /// Used to find usages of a prefab asset by nested instances inside the open prefab or active scene.
        /// </summary>
        private void GetPrefabInstanceUsages(GameObject root, string targetPrefabPath, List<CachedUsage> usages)
        {
            if (root == null) return;

            // If this GameObject is an instance root, check its source.
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(root);
                // Only report once at the nearest instance root
                if (instanceRoot == root)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot) as GameObject;
                    if (source != null)
                    {
                        string srcPath = AssetDatabase.GetAssetPath(source);
                        if (!string.IsNullOrEmpty(srcPath) &&
                            string.Equals(srcPath, targetPrefabPath, StringComparison.OrdinalIgnoreCase))
                        {
                            string hierPath = GetGameObjectHierarchyPath(instanceRoot);
                            bool active = instanceRoot.activeInHierarchy;

                            // Store as a GameObject usage (Reference not serialized).
                            usages.Add(new CachedUsage(
                                isComponent: false,
                                reference: instanceRoot,
                                displayName: $"PrefabInstance: {instanceRoot.name}",
                                hierarchyPath: hierPath,
                                gameObjectActive: active,
                                componentEnabled: null
                            ));

                            // Do not recurse into children of this instance root to avoid duplicates.
                            return;
                        }
                    }
                }
            }

            // Recurse into children
            foreach (Transform child in root.transform)
            {
                if (child != null)
                    GetPrefabInstanceUsages(child.gameObject, targetPrefabPath, usages);
            }
        }

        // -------- Selection & resolution helpers --------

        /// <summary>
        /// Resolve a GameObject by its hierarchy path within the active scene or open prefab stage.
        /// </summary>
        private GameObject FindGameObjectByHierarchyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string[] parts = path.Split('/');
            if (parts.Length == 0) return null;

            // Prefer Prefab Stage if open.
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            IEnumerable<GameObject> roots;
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
                roots = new[] { prefabStage.prefabContentsRoot };
            else
                roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();

            GameObject current = roots.FirstOrDefault(r => r.name == parts[0]);
            if (current == null) return null;

            for (int i = 1; i < parts.Length; i++)
            {
                // Search only direct children by name at each level.
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

        /// <summary>
        /// Resolve the live Unity object (GameObject or Component) for a given cached usage.
        /// </summary>
        private Object ResolveUsageReference(CachedUsage usage)
        {
            if (usage == null) return null;

            // GameObject case.
            if (!usage.IsComponent)
                return FindGameObjectByHierarchyPath(usage.HierarchyPath);

            // Component case.
            GameObject go = FindGameObjectByHierarchyPath(usage.HierarchyPath);
            if (go == null) return null;

            Type t = null;

            // Try assembly-qualified name first.
            if (!string.IsNullOrEmpty(usage.ComponentTypeAssemblyQualifiedName))
                t = Type.GetType(usage.ComponentTypeAssemblyQualifiedName);

            if (t != null)
            {
                Component comp = go.GetComponent(t);
                if (comp != null) return comp;
            }

            // Fallback by simple name if AQN missing/unresolved.
            if (!string.IsNullOrEmpty(usage.DisplayName))
            {
                Component match = go.GetComponents<Component>()
                    .FirstOrDefault(c => c != null && c.GetType().Name == usage.DisplayName);
                if (match != null) return match;
            }

            return null;
        }

        /// <summary>
        /// Assign target asset and run the usage search within this window.
        /// </summary>
        private void FindUsagesForAsset(Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("Cannot search: asset is null.");
                return;
            }

            _assetToFind = asset;
            Repaint();
            FindUsagesAsync();
        }
    }
}