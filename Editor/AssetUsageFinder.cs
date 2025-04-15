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

namespace LegendaryTools.Editor
{
    public class AssetUsageFinder : EditorWindow
    {
        // The asset (or script) to search for.
        private Object assetToFind;

        // Scroll position for the file usage list.
        private Vector2 scrollPos;

        // List of asset usage entries (files that reference the asset).
        private List<AssetUsageEntry> usageEntries = new List<AssetUsageEntry>();

        // Cache for GameObject/Component usage details per file.
        // Key: asset file path; Value: list of cached usage details.
        private static Dictionary<string, List<CachedUsage>> usageCache = new Dictionary<string, List<CachedUsage>>();

        // Cache for file usage results per asset GUID to avoid re-scanning the entire project.
        private static Dictionary<string, List<AssetUsageEntry>> fileUsageCache =
            new Dictionary<string, List<AssetUsageEntry>>();

        // Filter options for the search.
        private bool filterScenes = true;
        private bool filterPrefabs = true;
        private bool filterScriptableObjects = true;
        private bool filterMaterials = true;
        private bool filterOther = true;

        /// <summary>
        /// Enum representing the type of asset file.
        /// </summary>
        enum UsageType
        {
            Scene,
            Prefab,
            ScriptableObject,
            Material,
            Other
        }

        /// <summary>
        /// Represents one asset file that references the searched asset.
        /// </summary>
        class AssetUsageEntry
        {
            public string assetPath;
            public UsageType usageType;
            public bool isExpanded = false; // Controls foldout display for usages.

            public AssetUsageEntry(string path, UsageType type)
            {
                assetPath = path;
                usageType = type;
            }
        }

        /// <summary>
        /// Represents a usage found inside a GameObject or Component.
        /// </summary>
        class CachedUsage
        {
            public bool isComponent;
            public Object reference; // Either a Component or a GameObject.
            public string displayName; // For a component, its type name; for a GameObject, its name.
            public string hierarchyPath; // Full hierarchical path (e.g., "Parent/Child/Grandchild").
            public bool gameObjectActive; // Indicates if the GameObject is active.
            public bool? componentEnabled; // If the usage is a Component of type Behaviour, indicates if it is enabled.

            public CachedUsage(bool isComponent, Object reference, string displayName, string hierarchyPath,
                bool gameObjectActive, bool? componentEnabled)
            {
                this.isComponent = isComponent;
                this.reference = reference;
                this.displayName = displayName;
                this.hierarchyPath = hierarchyPath;
                this.gameObjectActive = gameObjectActive;
                this.componentEnabled = componentEnabled;
            }
        }

        /// <summary>
        /// Displays the Asset Usage Finder window.
        /// </summary>
        [MenuItem("Tools/Asset Usage Finder")]
        public static void ShowWindow()
        {
            AssetUsageFinder window = GetWindow<AssetUsageFinder>("Asset Usage Finder");
            window.Show();
        }

        /// <summary>
        /// Draws the GUI for the window.
        /// </summary>
        private void OnGUI()
        {
            // Header area with title, asset selector, and filter options.
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Asset Usage Finder", EditorStyles.boldLabel);
            assetToFind = EditorGUILayout.ObjectField("Asset", assetToFind, typeof(Object), false);

            EditorGUILayout.LabelField("Filter by asset type:");
            filterScenes = EditorGUILayout.Toggle("Scenes", filterScenes);
            filterPrefabs = EditorGUILayout.Toggle("Prefabs", filterPrefabs);
            filterScriptableObjects = EditorGUILayout.Toggle("ScriptableObjects", filterScriptableObjects);
            filterMaterials = EditorGUILayout.Toggle("Materials", filterMaterials);
            filterOther = EditorGUILayout.Toggle("Other", filterOther);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find Usages"))
            {
                FindUsages();
            }

            if (GUILayout.Button("Clear Cache", GUILayout.Width(100)))
            {
                usageCache.Clear();
                fileUsageCache.Clear();
                Debug.Log("All caches cleared.");
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // File usage list: scrollable and expands to fill the available window.
            if (usageEntries.Count > 0)
            {
                EditorGUILayout.LabelField("Files that reference the asset:", EditorStyles.boldLabel);
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
                foreach (var entry in usageEntries)
                {
                    // Skip this entry if its usage type does not pass the current filter.
                    if (!PassesFilter(entry.usageType))
                        continue;

                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();

                    // Display the file name and usage type.
                    EditorGUILayout.LabelField(
                        Path.GetFileName(entry.assetPath) + " (" + entry.usageType.ToString() + ")",
                        GUILayout.Width(250));
                    Object fileAsset = AssetDatabase.LoadAssetAtPath<Object>(entry.assetPath);

                    // Display file-specific buttons.
                    if (entry.usageType == UsageType.Scene)
                    {
                        if (GUILayout.Button("Open Scene", GUILayout.Width(100)))
                        {
                            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                            {
                                EditorSceneManager.OpenScene(entry.assetPath);
                            }
                        }

                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            EditorGUIUtility.PingObject(fileAsset);
                        }
                    }
                    else if (entry.usageType == UsageType.Prefab)
                    {
                        if (GUILayout.Button("Open Prefab Mode", GUILayout.Width(130)))
                        {
                            PrefabStageUtility.OpenPrefab(entry.assetPath);
                        }

                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            EditorGUIUtility.PingObject(fileAsset);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(100)))
                        {
                            EditorGUIUtility.PingObject(fileAsset);
                        }
                    }

                    EditorGUILayout.EndHorizontal();

                    // Determine if this file is active (open as scene or prefab).
                    bool isActive = false;
                    if (entry.usageType == UsageType.Scene)
                    {
                        Scene activeScene = EditorSceneManager.GetActiveScene();
                        if (!string.IsNullOrEmpty(activeScene.path) && activeScene.path == entry.assetPath)
                            isActive = true;
                    }
                    else if (entry.usageType == UsageType.Prefab)
                    {
                        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                        if (prefabStage != null && prefabStage.assetPath == entry.assetPath)
                            isActive = true;
                    }

                    // For active files, update or create the usage cache.
                    if (isActive)
                    {
                        if (usageCache.ContainsKey(entry.assetPath))
                        {
                            List<CachedUsage> cachedList = usageCache[entry.assetPath];
                            bool needUpdate = false;
                            foreach (var item in cachedList)
                            {
                                // If any cached usage reference is null, mark for update.
                                if (item.reference == null)
                                {
                                    needUpdate = true;
                                    break;
                                }
                            }

                            if (needUpdate)
                            {
                                List<CachedUsage> newCache = FindAssetUsagesWithReference(assetToFind);
                                usageCache[entry.assetPath] = newCache;
                            }
                        }
                        else
                        {
                            if (GUILayout.Button("Find Usages in File", GUILayout.Width(250)))
                            {
                                List<CachedUsage> foundUsages = FindAssetUsagesWithReference(assetToFind);
                                usageCache[entry.assetPath] = foundUsages;
                            }
                        }

                        entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded, "Usages");
                    }

                    EditorGUILayout.EndVertical();

                    // Display cached usage details if the foldout is expanded.
                    if (entry.isExpanded && usageCache.ContainsKey(entry.assetPath))
                    {
                        List<CachedUsage> cachedUsages = usageCache[entry.assetPath];
                        EditorGUI.indentLevel++;
                        foreach (CachedUsage usage in cachedUsages)
                        {
                            EditorGUILayout.BeginHorizontal();

                            // "Select" button.
                            bool valid = usage.reference != null;
                            EditorGUI.BeginDisabledGroup(!valid);
                            if (GUILayout.Button("Select", GUILayout.Width(100)))
                            {
                                if (valid)
                                {
                                    if (usage.isComponent)
                                    {
                                        Selection.activeObject = usage.reference;
                                        EditorGUIUtility.PingObject(usage.reference);
                                    }
                                    else
                                    {
                                        Selection.activeGameObject = (GameObject)usage.reference;
                                        EditorGUIUtility.PingObject(usage.reference);
                                    }
                                }
                            }

                            EditorGUI.EndDisabledGroup();

                            // "Remove" button: if usage is from a Component, remove it;
                            // otherwise, attempt to null out the reference in the GameObject's serialized properties.
                            if (GUILayout.Button("Remove", GUILayout.Width(100)))
                            {
                                if (usage.reference != null)
                                {
                                    if (usage.isComponent)
                                    {
                                        Component comp = usage.reference as Component;
                                        if (comp != null)
                                        {
                                            Debug.Log(
                                                $"Removed component: {comp.GetType().Name} from {usage.hierarchyPath}");
                                            Undo.DestroyObjectImmediate(comp);
                                        }
                                    }
                                    else
                                    {
                                        // Remove (null out) the asset reference from the GameObject's serialized properties.
                                        GameObject go = usage.reference as GameObject;
                                        if (go != null)
                                        {
                                            RemoveAssetReferenceFromGameObject(go, assetToFind);
                                            Debug.Log(
                                                $"Nullified asset reference in GameObject: {go.name} ({usage.hierarchyPath})");
                                        }
                                    }

                                    // Refresh the usage cache after removal.
                                    List<CachedUsage> newCache = FindAssetUsagesWithReference(assetToFind);
                                    usageCache[entry.assetPath] = newCache;
                                }
                            }

                            // Build and display usage status information.
                            string status = $"Active: {(usage.gameObjectActive ? "Yes" : "No")}";
                            if (usage.isComponent)
                            {
                                status +=
                                    $", Enabled: {(usage.componentEnabled.HasValue ? (usage.componentEnabled.Value ? "Yes" : "No") : "N/A")}";
                            }

                            EditorGUILayout.LabelField(usage.displayName, GUILayout.Width(200));
                            EditorGUILayout.LabelField(usage.hierarchyPath, GUILayout.Width(600));
                            EditorGUILayout.LabelField(status, GUILayout.Width(200));
                            EditorGUILayout.EndHorizontal();
                        }

                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        /// Checks whether the specified usage type passes the current filter settings.
        /// </summary>
        /// <param name="type">The usage type.</param>
        /// <returns>True if the type is allowed; otherwise, false.</returns>
        private bool PassesFilter(UsageType type)
        {
            switch (type)
            {
                case UsageType.Scene: return filterScenes;
                case UsageType.Prefab: return filterPrefabs;
                case UsageType.ScriptableObject: return filterScriptableObjects;
                case UsageType.Material: return filterMaterials;
                case UsageType.Other: return filterOther;
                default: return true;
            }
        }

        /// <summary>
        /// Returns the full hierarchical path of the specified GameObject.
        /// </summary>
        /// <param name="go">The GameObject.</param>
        /// <returns>The hierarchical path (e.g., "Parent/Child/Grandchild").</returns>
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
        /// Scans the project for files that truly reference the specified asset.
        /// Only files matching the current filter options are processed.
        /// </summary>
        private void FindUsages()
        {
            usageEntries.Clear();
            if (assetToFind == null)
            {
                Debug.LogWarning("Please select an asset or script to find usages.");
                return;
            }

            string assetPathToFind = AssetDatabase.GetAssetPath(assetToFind);
            string guidToFind = AssetDatabase.AssetPathToGUID(assetPathToFind);

            // Use cached file usage if available.
            if (fileUsageCache.ContainsKey(guidToFind))
            {
                usageEntries = new List<AssetUsageEntry>(fileUsageCache[guidToFind]);
                EditorUtility.ClearProgressBar();
                return;
            }

            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            int totalPaths = allPaths.Length;
            List<AssetUsageEntry> foundEntries = new List<AssetUsageEntry>();

            for (int i = 0; i < totalPaths; i++)
            {
                string path = allPaths[i];

                if (path == assetPathToFind)
                    continue;
                if (!path.StartsWith("Assets"))
                    continue;

                // Filter file by extension according to current filter settings.
                string ext = Path.GetExtension(path).ToLower();
                bool allowed = false;
                switch (ext)
                {
                    case ".unity":
                        allowed = filterScenes;
                        break;
                    case ".prefab":
                        allowed = filterPrefabs;
                        break;
                    case ".mat":
                        allowed = filterMaterials;
                        break;
                    case ".asset":
                        Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                        if (obj is ScriptableObject)
                            allowed = filterScriptableObjects;
                        else
                            allowed = filterOther;
                        break;
                    default:
                        allowed = filterOther;
                        break;
                }

                if (!allowed)
                    continue;

                EditorUtility.DisplayProgressBar("Finding Usages", "Analyzing " + path, (float)i / totalPaths);

                // Check if the file's dependencies include the asset.
                string[] dependencies = AssetDatabase.GetDependencies(path, true);
                bool dependencyFound = false;
                foreach (string dependency in dependencies)
                {
                    if (dependency == assetPathToFind)
                    {
                        dependencyFound = true;
                        break;
                    }
                }

                if (!dependencyFound)
                    continue;

                // For text files, verify that the asset's GUID is present.
                bool guidFound = false;
                if (ext == ".unity" || ext == ".prefab" || ext == ".asset" || ext == ".mat")
                {
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
                }
                else
                {
                    guidFound = dependencyFound;
                }

                if (!guidFound)
                    continue;

                // Determine the usage type.
                UsageType type;
                if (ext == ".unity")
                    type = UsageType.Scene;
                else if (ext == ".prefab")
                    type = UsageType.Prefab;
                else if (ext == ".mat")
                    type = UsageType.Material;
                else if (ext == ".asset")
                {
                    Object o = AssetDatabase.LoadAssetAtPath<Object>(path);
                    type = (o is ScriptableObject) ? UsageType.ScriptableObject : UsageType.Other;
                }
                else
                    type = UsageType.Other;

                foundEntries.Add(new AssetUsageEntry(path, type));
            }

            EditorUtility.ClearProgressBar();
            usageEntries = foundEntries;
            fileUsageCache[guidToFind] = new List<AssetUsageEntry>(usageEntries);
        }

        /// <summary>
        /// Finds all asset usages (returns a list of CachedUsage objects) in the currently active scene or prefab.
        /// </summary>
        /// <param name="asset">The asset to search for.</param>
        /// <returns>List of usage details.</returns>
        List<CachedUsage> FindAssetUsagesWithReference(Object asset)
        {
            List<CachedUsage> foundUsages = new List<CachedUsage>();
            GameObject[] roots;
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
                roots = new GameObject[] { prefabStage.prefabContentsRoot };
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
        /// Recursively traverses the GameObject hierarchy and collects usage details.
        /// </summary>
        /// <param name="go">The current GameObject.</param>
        /// <param name="asset">The asset to check for.</param>
        /// <param name="usages">The list to accumulate usage details.</param>
        void GetAssetUsagesForGameObject(GameObject go, Object asset, List<CachedUsage> usages)
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null)
                    continue;
                if (ComponentReferencesAsset(comp, asset))
                {
                    bool active = go.activeInHierarchy;
                    bool? compEnabled = null;
                    if (comp is Behaviour)
                        compEnabled = ((Behaviour)comp).enabled;
                    string dispName = comp.GetType().Name;
                    string hierPath = GetGameObjectHierarchyPath(go);
                    usages.Add(new CachedUsage(true, comp, dispName, hierPath, active, compEnabled));
                }
            }

            // (Optional: If you want to capture usages that aren’t tied to a specific component, you could check here.)
            // Recurse in children.
            foreach (Transform child in go.transform)
            {
                GetAssetUsagesForGameObject(child.gameObject, asset, usages);
            }
        }

        /// <summary>
        /// Checks whether a component references the specified asset.
        /// </summary>
        /// <param name="comp">The component to check.</param>
        /// <param name="asset">The asset to look for.</param>
        /// <returns>True if the asset is referenced; otherwise, false.</returns>
        bool ComponentReferencesAsset(Component comp, Object asset)
        {
            // Special case: if the asset is a MonoScript, check the m_Script reference.
            if (asset is MonoScript)
            {
                MonoBehaviour mb = comp as MonoBehaviour;
                if (mb != null)
                {
                    MonoScript script = MonoScript.FromMonoBehaviour(mb);
                    if (script == asset)
                        return true;
                }
            }

            SerializedObject so = new SerializedObject(comp);
            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    if (prop.objectReferenceValue == asset)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// For non-component usages: searches through each component on the GameObject and nulls out any serialized field that references the asset.
        /// </summary>
        /// <param name="go">The GameObject.</param>
        /// <param name="asset">The asset to remove references for.</param>
        void RemoveAssetReferenceFromGameObject(GameObject go, Object asset)
        {
            Component[] comps = go.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null)
                    continue;
                bool modified = false;
                SerializedObject so = new SerializedObject(comp);
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