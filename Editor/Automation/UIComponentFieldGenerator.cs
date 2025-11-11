using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;
using System;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;

namespace LegendaryTools.Editor
{
    public class UIComponentFieldGenerator : EditorWindow
    {
        private RectTransform targetRectTransform;
        private string namespaceName = "MyNamespace";
        private string className = "MyUIClass";
        private Vector2 scrollPosition;
        private Dictionary<string, List<ComponentInfo>> componentGroups = new(); // key = parent path
        private Dictionary<string, bool> foldoutStates = new(); // key = parent path
        private bool expandAllGroups = true;
        private int filterTypeIndex = 0;
        private string gameObjectFilter = "";
        private bool useSerializeField = true;
        private bool useBackingFields = false;
        private HashSet<string> usedFieldNames = new();

        private static readonly Type[] supportedTypes = new Type[]
        {
            typeof(Text), typeof(TextMeshProUGUI), typeof(TMP_Text), typeof(Image), typeof(Button), typeof(Toggle),
            typeof(Slider), typeof(Scrollbar), typeof(Dropdown), typeof(InputField),
            typeof(RawImage), typeof(Mask), typeof(ScrollRect), typeof(TMP_InputField)
        };

        private static readonly string s_isWaitingRecompile = "UIComponentFieldGenerator_WaitingRecompile";
        private static readonly string s_pendingScriptPath = "UIComponentFieldGenerator_PendingScriptPath";

        private static readonly string s_pendingTargetRectTransformPath =
            "UIComponentFieldGenerator_PendingTargetRectTransformPath";

        private static readonly string s_pendingFieldAssignments = "UIComponentFieldGenerator_PendingFieldAssignments";
        private static readonly string s_windowStatePath = "Library/UIComponentFieldGenerator_State.json";

        [Serializable]
        private class ComponentInfo
        {
            [JsonIgnore] public Component component;
            public string componentType; // FullName
            public bool isSelected;
            public string gameObjectName;
            public string gameObjectPath;
            public string parentName; // display only
            public string fieldName;
            public string fieldNameInput;
            public List<int> selectedPropertyIndices;
            public List<int> selectedEventIndices;

            public ComponentInfo()
            {
            }

            public ComponentInfo(Component comp, string goName, string goPath, string pName, string fName)
            {
                component = comp;
                componentType = comp.GetType().FullName;
                isSelected = false;
                gameObjectName = goName;
                gameObjectPath = goPath;
                parentName = pName;
                fieldName = fName;
                fieldNameInput = fName;
                selectedPropertyIndices =
                    WeaverUtils.TryGetPropertyMap(comp.GetType(),
                        out List<(string propertyName, string propertyType)> list) && list.Count > 0
                        ? new List<int> { 0 }
                        : new List<int>();
                selectedEventIndices = new List<int>();
            }
        }

        [Serializable]
        private struct FieldAssignment
        {
            public string fieldName;
            public string componentType; // FullName
            public string gameObjectName; // path
        }

        [Serializable]
        private class WindowState
        {
            public string targetRectTransformPath;
            public string namespaceName;
            public string className;
            public Dictionary<string, List<ComponentInfo>> componentGroups;
            public Dictionary<string, bool> foldoutStates;
            public bool expandAllGroups;
            public int filterTypeIndex;
            public string gameObjectFilter;
            public bool useSerializeField;
            public bool useBackingFields;
            public HashSet<string> usedFieldNames;
        }

        [MenuItem("Tools/LegendaryTools/Automation/UI Component Field Generator")]
        public static void ShowWindow()
        {
            GetWindow<UIComponentFieldGenerator>("UI Field Generator");
        }

        [MenuItem("Component/UI Component Field Generator", false, 1000)]
        private static void OpenFromContextMenu()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null && selected.GetComponent<RectTransform>() != null)
            {
                UIComponentFieldGenerator window = GetWindow<UIComponentFieldGenerator>("UI Field Generator");
                window.targetRectTransform = selected.GetComponent<RectTransform>();
                window.FindUIComponents();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a GameObject with a RectTransform component.",
                    "OK");
            }
        }

        [MenuItem("Component/UI Component Field Generator", true)]
        private static bool ValidateOpenFromContextMenu()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponent<RectTransform>() != null;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "";
            List<string> pathSegments = new();
            Transform current = go.transform;
            while (current != null)
            {
                pathSegments.Add(current.name);
                current = current.parent;
            }

            pathSegments.Reverse();
            return string.Join("/", pathSegments);
        }

        private static GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string[] pathSegments = path.Split('/');
            GameObject current = null;

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                GameObject prefabRoot = prefabStage.prefabContentsRoot;
                if (prefabRoot.name == pathSegments[0]) current = prefabRoot;
            }
            else
            {
                GameObject[] rootObjects =
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                foreach (GameObject root in rootObjects)
                {
                    if (root.name == pathSegments[0])
                    {
                        current = root;
                        break;
                    }
                }
            }

            if (current == null) return null;

            for (int i = 1; i < pathSegments.Length; i++)
            {
                Transform child = null;
                foreach (Transform t in current.transform)
                {
                    if (t.name == pathSegments[i])
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

        // Finds a Type by simple name or full name scanning common namespaces and loaded assemblies.
        private static Type ResolveType(string typeName)
        {
            Type type = Type.GetType(typeName);
            if (type != null) return type;

            string[] candidates =
            {
                $"UnityEngine.UI.{typeName}",
                $"TMPro.{typeName}",
                $"UnityEngine.{typeName}"
            };

            foreach (string c in candidates)
            {
                type = Type.GetType(c);
                if (type != null) return type;
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetTypes().FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
                    if (type != null) return type;
                }
                catch
                {
                    /* ignore */
                }
            }

            return null;
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            if (EditorPrefs.GetBool(s_isWaitingRecompile))
                EditorApplication.delayCall += OnInspectorReloadComplete;

            if (File.Exists(s_windowStatePath))
                EditorApplication.delayCall += () =>
                {
                    UIComponentFieldGenerator window = GetWindow<UIComponentFieldGenerator>("UI Field Generator");
                    window.RestoreWindowState();
                };
        }

        private void OnDestroy()
        {
            if (File.Exists(s_windowStatePath)) File.Delete(s_windowStatePath);
        }

        private static void OnInspectorReloadComplete()
        {
            EditorApplication.delayCall -= OnInspectorReloadComplete;
            EditorPrefs.SetBool(s_isWaitingRecompile, false);

            string scriptPath = EditorPrefs.GetString(s_pendingScriptPath, "");
            string targetRectTransformPath = EditorPrefs.GetString(s_pendingTargetRectTransformPath, "");
            string fieldAssignmentsJson = EditorPrefs.GetString(s_pendingFieldAssignments, "");

            if (string.IsNullOrEmpty(scriptPath) || string.IsNullOrEmpty(targetRectTransformPath) ||
                string.IsNullOrEmpty(fieldAssignmentsJson))
            {
                Debug.LogWarning(
                    "UIComponentFieldGenerator: Failed to retrieve pending data for component assignment.");
                return;
            }

            GameObject targetGO = FindGameObjectByPath(targetRectTransformPath);
            if (targetGO == null)
            {
                Debug.LogWarning(
                    $"UIComponentFieldGenerator: Target RectTransform at path '{targetRectTransformPath}' not found.");
                return;
            }

            RectTransform targetRectTransform = targetGO.GetComponent<RectTransform>();
            if (targetRectTransform == null)
            {
                Debug.LogWarning(
                    $"UIComponentFieldGenerator: Target RectTransform component not found at '{targetRectTransformPath}'.");
                return;
            }

            MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (monoScript == null)
            {
                Debug.LogWarning($"UIComponentFieldGenerator: Generated script at '{scriptPath}' not found.");
                return;
            }

            Type scriptType = monoScript.GetClass();
            if (scriptType == null)
            {
                Debug.LogWarning(
                    $"UIComponentFieldGenerator: Could not retrieve class type from script at '{scriptPath}'.");
                return;
            }

            Component newComponent = targetRectTransform.gameObject.AddComponent(scriptType);
            if (newComponent == null)
            {
                Debug.LogWarning(
                    $"UIComponentFieldGenerator: Failed to add component '{scriptType.Name}' to '{targetRectTransformPath}'.");
                return;
            }

            FieldAssignment[] fieldAssignments = JsonConvert.DeserializeObject<FieldAssignment[]>(fieldAssignmentsJson);
            if (fieldAssignments == null || fieldAssignments.Length == 0)
            {
                Debug.LogWarning("UIComponentFieldGenerator: No field assignments found for auto-assignment.");
                return;
            }

            foreach (FieldAssignment assignment in fieldAssignments)
            {
                GameObject targetGameObject = FindGameObjectByPath(assignment.gameObjectName);
                Component targetComponent = null;

                if (targetGameObject != null)
                {
                    Type targetType = ResolveType(assignment.componentType);
                    if (targetType != null)
                        targetComponent = targetGameObject.GetComponent(targetType);
                }

                if (targetComponent == null)
                {
                    Debug.LogWarning(
                        $"UIComponentFieldGenerator: Could not find component '{assignment.componentType}' on GameObject at path '{assignment.gameObjectName}' for field '{assignment.fieldName}'.");
                    continue;
                }

                FieldInfo field = scriptType.GetField(assignment.fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType.IsAssignableFrom(targetComponent.GetType()))
                    field.SetValue(newComponent, targetComponent);
                else
                    Debug.LogWarning(
                        $"UIComponentFieldGenerator: Field '{assignment.fieldName}' not found or type mismatch for component '{assignment.componentType}' in '{scriptType.Name}'.");
            }

            EditorUtility.SetDirty(targetRectTransform.gameObject);
            Debug.Log(
                $"UIComponentFieldGenerator: Successfully added '{scriptType.Name}' to '{targetRectTransformPath}' and assigned field references.");
        }

        private void OnGUI()
        {
            GUILayout.Label("UI Component Field Generator", EditorStyles.boldLabel);

            targetRectTransform = (RectTransform)EditorGUILayout.ObjectField("Target RectTransform",
                targetRectTransform, typeof(RectTransform), true);
            namespaceName = EditorGUILayout.TextField("Namespace", namespaceName);
            className = EditorGUILayout.TextField("Class Name", className);

            EditorGUILayout.BeginHorizontal();
            List<string> typeOptions = new() { "All" };
            typeOptions.AddRange(supportedTypes.Select(t => t.Name));
            filterTypeIndex = EditorGUILayout.Popup("Filter Type", filterTypeIndex, typeOptions.ToArray(),
                GUILayout.Width(200));
            gameObjectFilter = EditorGUILayout.TextField("Filter GameObject", gameObjectFilter, GUILayout.Width(500));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            useSerializeField = EditorGUILayout.Toggle("Use [SerializeField]", useSerializeField, GUILayout.Width(200));
            useBackingFields = EditorGUILayout.Toggle("Use Backing Fields", useBackingFields, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Preview Fields"))
            {
                if (targetRectTransform == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please assign a RectTransform.", "OK");
                    return;
                }

                FindUIComponents();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
                foreach (List<ComponentInfo> group in componentGroups.Values)
                foreach (ComponentInfo comp in group)
                {
                    comp.isSelected = true;
                }

            if (GUILayout.Button("Deselect All"))
                foreach (List<ComponentInfo> group in componentGroups.Values)
                foreach (ComponentInfo comp in group)
                {
                    comp.isSelected = false;
                }

            if (GUILayout.Button("All Getters/Setters"))
                foreach (List<ComponentInfo> group in componentGroups.Values)
                foreach (ComponentInfo comp in group)
                {
                    if (comp.component != null &&
                        WeaverUtils.TryGetPropertyMap(comp.component.GetType(),
                            out List<(string propertyName, string propertyType)> map) &&
                        map.Count > 0)
                        comp.selectedPropertyIndices = Enumerable.Range(0, map.Count).ToList();
                }

            if (GUILayout.Button("No Getters/Setters"))
                foreach (List<ComponentInfo> group in componentGroups.Values)
                foreach (ComponentInfo comp in group)
                {
                    comp.selectedPropertyIndices = new List<int>();
                }

            if (GUILayout.Button(expandAllGroups ? "Collapse All" : "Expand All"))
            {
                expandAllGroups = !expandAllGroups;
                foreach (string key in foldoutStates.Keys.ToList())
                {
                    foldoutStates[key] = expandAllGroups;
                }
            }

            EditorGUILayout.EndHorizontal();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            usedFieldNames.Clear();
            foreach (KeyValuePair<string, List<ComponentInfo>> group in componentGroups)
            {
                string parentPath = group.Key;
                string parentNameDisplay = parentPath.Contains("/") ? parentPath.Split('/').Last() : parentPath;
                List<ComponentInfo> filteredComponents = group.Value.Where(c =>
                    c.component != null &&
                    (filterTypeIndex == 0 || c.component.GetType() == supportedTypes[filterTypeIndex - 1]) &&
                    (string.IsNullOrEmpty(gameObjectFilter) ||
                     c.gameObjectName.ToLower().Contains(gameObjectFilter.ToLower()))
                ).ToList();

                if (filteredComponents.Count == 0)
                    continue;

                if (!foldoutStates.ContainsKey(parentPath))
                    foldoutStates[parentPath] = expandAllGroups;

                foldoutStates[parentPath] = EditorGUILayout.Foldout(foldoutStates[parentPath],
                    $"Parent: {parentNameDisplay} ({parentPath})");

                if (foldoutStates[parentPath])
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Selected", EditorStyles.boldLabel, GUILayout.Width(60));
                    EditorGUILayout.LabelField("GameObject", EditorStyles.boldLabel, GUILayout.Width(150));
                    EditorGUILayout.LabelField("Field", EditorStyles.boldLabel, GUILayout.Width(120));
                    EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.Width(160));
                    EditorGUILayout.LabelField("Properties", EditorStyles.boldLabel, GUILayout.Width(140));
                    EditorGUILayout.LabelField("Events", EditorStyles.boldLabel, GUILayout.Width(140));
                    EditorGUILayout.EndHorizontal();

                    foreach (ComponentInfo comp in filteredComponents)
                    {
                        EditorGUILayout.BeginHorizontal();
                        comp.isSelected = EditorGUILayout.Toggle(comp.isSelected, GUILayout.Width(60));

                        GUIStyle buttonStyle = new(GUI.skin.label)
                            { normal = { textColor = GUI.skin.label.normal.textColor } };
                        if (GUI.Button(EditorGUILayout.GetControlRect(GUILayout.Width(150)), comp.gameObjectName,
                                buttonStyle))
                            Selection.activeObject = comp.component.gameObject;

                        // Editable field name with uniqueness and identifier validation after edit
                        string beforeEdit = comp.fieldNameInput;
                        string edited = EditorGUILayout.TextField(beforeEdit, GUILayout.Width(120));
                        string candidate = string.IsNullOrEmpty(edited) ? comp.fieldName : edited;
                        if (!IsValidCSharpIdentifier(candidate)) candidate = comp.fieldName;

                        string unique = candidate;
                        int suffix = 0;
                        while (usedFieldNames.Contains(unique))
                        {
                            suffix++;
                            unique = $"{candidate}{suffix}";
                        }

                        comp.fieldNameInput = unique;
                        usedFieldNames.Add(unique);

                        // Type label
                        EditorGUILayout.LabelField(comp.component.GetType().FullName, GUILayout.Width(160));

                        // Properties mask
                        Type compType = comp.component.GetType();
                        if (WeaverUtils.TryGetPropertyMap(compType,
                                out List<(string propertyName, string propertyType)> propMap) && propMap.Count > 0)
                        {
                            string[] propertyOptions = propMap.Select(p => p.propertyName).ToArray();
                            int currentMask = 0;
                            for (int i = 0; i < comp.selectedPropertyIndices.Count; i++)
                            {
                                currentMask |= 1 << comp.selectedPropertyIndices[i];
                            }

                            int newMask = EditorGUILayout.MaskField(currentMask, propertyOptions, GUILayout.Width(140));
                            comp.selectedPropertyIndices = new List<int>();
                            for (int i = 0; i < propertyOptions.Length; i++)
                            {
                                if ((newMask & (1 << i)) != 0)
                                    comp.selectedPropertyIndices.Add(i);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("None", GUILayout.Width(140));
                        }

                        // Events mask
                        if (WeaverUtils.TryGetEventMap(compType,
                                out List<(string eventName, string eventType, string handlerSignature)> evtMap) &&
                            evtMap.Count > 0)
                        {
                            string[] eventOptions = evtMap.Select(e => e.eventName).ToArray();
                            int currentMask = 0;
                            for (int i = 0; i < comp.selectedEventIndices.Count; i++)
                            {
                                currentMask |= 1 << comp.selectedEventIndices[i];
                            }

                            int newMask = EditorGUILayout.MaskField(currentMask, eventOptions, GUILayout.Width(140));
                            comp.selectedEventIndices = new List<int>();
                            for (int i = 0; i < eventOptions.Length; i++)
                            {
                                if ((newMask & (1 << i)) != 0)
                                    comp.selectedEventIndices.Add(i);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("None", GUILayout.Width(140));
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Generate"))
            {
                if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(namespaceName))
                {
                    EditorUtility.DisplayDialog("Error", "Please provide a valid namespace and class name.", "OK");
                    return;
                }

                if (!componentGroups.Any(g => g.Value.Any(c => c.isSelected)))
                {
                    EditorUtility.DisplayDialog("Error", "No components selected.", "OK");
                    return;
                }

                SaveWindowState();
                GenerateClassFile(); // now delegates generation to WeaverUtils
            }
        }

        private void FindUIComponents()
        {
            componentGroups.Clear();
            foldoutStates.Clear();
            usedFieldNames.Clear();
            if (targetRectTransform == null) return;

            Dictionary<string, int> fieldNameCounts = new();

            foreach (RectTransform rt in targetRectTransform.GetComponentsInChildren<RectTransform>(true))
            {
                foreach (Component comp in rt.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    if (supportedTypes.Contains(comp.GetType()))
                    {
                        string goName = comp.gameObject.name;
                        string parentKey = rt.parent != null ? GetGameObjectPath(rt.parent.gameObject) : "Root";
                        string parentName = rt.parent != null ? rt.parent.name : "Root";
                        string baseFieldName = ConvertToValidFieldName(goName);
                        string fieldName = baseFieldName;

                        if (fieldNameCounts.ContainsKey(baseFieldName))
                        {
                            fieldNameCounts[baseFieldName]++;
                            fieldName = $"{baseFieldName}{fieldNameCounts[baseFieldName]}";
                        }
                        else
                        {
                            fieldNameCounts[baseFieldName] = 0;
                        }

                        if (!componentGroups.ContainsKey(parentKey))
                            componentGroups[parentKey] = new List<ComponentInfo>();

                        componentGroups[parentKey].Add(new ComponentInfo(
                            comp,
                            comp.gameObject.name,
                            GetGameObjectPath(comp.gameObject),
                            parentName,
                            fieldName));
                    }
                }
            }
        }

        private string ConvertToValidFieldName(string name)
        {
            string validName = "";
            bool nextCharUpper = false;

            if (name.Length > 0 && char.IsDigit(name[0]))
                validName = "_";

            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    validName += nextCharUpper ? char.ToUpper(c) : c;
                    nextCharUpper = false;
                }
                else
                {
                    nextCharUpper = true;
                }
            }

            if (string.IsNullOrEmpty(validName))
                validName = "field";

            string[] keywords = { "class", "int", "float", "string", "public", "private" };
            if (keywords.Contains(validName))
                validName = "_" + validName;

            return validName;
        }

        private bool IsValidCSharpIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                return false;

            string[] keywords = { "class", "int", "float", "string", "public", "private", "void", "bool", "namespace" };
            return !keywords.Contains(name);
        }

        /// <summary>
        /// Generates the class file via WeaverUtils and preserves the post-recompile auto-attachment/assignment flow.
        /// </summary>
        private void GenerateClassFile()
        {
            List<ComponentInfo> selectedComponents =
                componentGroups.Values.SelectMany(g => g).Where(c => c.isSelected && c.component != null).ToList();
            if (selectedComponents.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No components selected.", "OK");
                return;
            }

            // Unique name resolution per selection round
            Dictionary<string, int> fieldNameCounts = new();
            Dictionary<ComponentInfo, string> uniqueFieldNames = new();
            List<FieldAssignment> fieldAssignments = new();

            foreach (ComponentInfo comp in selectedComponents)
            {
                string baseName = comp.fieldNameInput;
                if (!IsValidCSharpIdentifier(baseName))
                {
                    Debug.LogWarning(
                        $"Invalid C# field name '{baseName}' for {comp.gameObjectName}. Using fallback name: {comp.fieldName}");
                    baseName = comp.fieldName;
                }

                if (fieldNameCounts.ContainsKey(baseName))
                {
                    fieldNameCounts[baseName]++;
                    string newName = $"{baseName}{fieldNameCounts[baseName]}";
                    Debug.LogWarning(
                        $"Duplicate field name '{baseName}' for {comp.gameObjectName}. Renamed to '{newName}'");
                    uniqueFieldNames[comp] = newName;
                }
                else
                {
                    fieldNameCounts[baseName] = 0;
                    uniqueFieldNames[comp] = baseName;
                }

                fieldAssignments.Add(new FieldAssignment
                {
                    fieldName = uniqueFieldNames[comp],
                    componentType = comp.component.GetType().FullName,
                    gameObjectName = GetGameObjectPath(comp.component.gameObject)
                });
            }

            // Prepare binding items for WeaverUtils
            List<WeaverUtils.UIBindingItem> items = selectedComponents.Select(ci => new WeaverUtils.UIBindingItem
            {
                FieldName = uniqueFieldNames[ci],
                Component = ci.component,
                SelectedPropertyIndices = new List<int>(ci.selectedPropertyIndices),
                SelectedEventIndices = new List<int>(ci.selectedEventIndices)
            }).ToList();

            // Delegate generation + file saving to WeaverUtils
            string relativePath = WeaverUtils.GenerateUIBindingClassFile(
                namespaceName,
                className,
                items,
                useSerializeField,
                useBackingFields);

            if (string.IsNullOrEmpty(relativePath))
                return; // user cancelled or invalid path

            // Prepare data for post-compile auto-binding
            EditorPrefs.SetBool(s_isWaitingRecompile, true);
            EditorPrefs.SetString(s_pendingScriptPath, relativePath);
            EditorPrefs.SetString(s_pendingTargetRectTransformPath, GetGameObjectPath(targetRectTransform.gameObject));
            EditorPrefs.SetString(s_pendingFieldAssignments, JsonConvert.SerializeObject(fieldAssignments));
        }

        private void SaveWindowState()
        {
            WindowState state = new()
            {
                targetRectTransformPath =
                    targetRectTransform != null ? GetGameObjectPath(targetRectTransform.gameObject) : "",
                namespaceName = namespaceName,
                className = className,
                componentGroups = componentGroups,
                foldoutStates = foldoutStates,
                expandAllGroups = expandAllGroups,
                filterTypeIndex = filterTypeIndex,
                gameObjectFilter = gameObjectFilter,
                useSerializeField = useSerializeField,
                useBackingFields = useBackingFields,
                usedFieldNames = usedFieldNames
            };

            JsonSerializerSettings settings = new()
            {
                Formatting = Formatting.Indented
            };
            string json = JsonConvert.SerializeObject(state, Formatting.Indented, settings);
            File.WriteAllText(s_windowStatePath, json);
        }

        private void RestoreWindowState()
        {
            if (!File.Exists(s_windowStatePath))
                return;

            try
            {
                string json = File.ReadAllText(s_windowStatePath);
                WindowState state = JsonConvert.DeserializeObject<WindowState>(json);

                GameObject targetGO = FindGameObjectByPath(state.targetRectTransformPath);
                targetRectTransform = targetGO != null ? targetGO.GetComponent<RectTransform>() : null;

                namespaceName = state.namespaceName;
                className = state.className;
                componentGroups = state.componentGroups ?? new Dictionary<string, List<ComponentInfo>>();
                foldoutStates = state.foldoutStates ?? new Dictionary<string, bool>();
                expandAllGroups = state.expandAllGroups;
                filterTypeIndex = state.filterTypeIndex;
                gameObjectFilter = state.gameObjectFilter;
                useSerializeField = state.useSerializeField;
                useBackingFields = state.useBackingFields;
                usedFieldNames = state.usedFieldNames ?? new HashSet<string>();

                // Rebind components (resolve by FullName)
                foreach (KeyValuePair<string, List<ComponentInfo>> group in componentGroups.ToList())
                {
                    foreach (ComponentInfo compInfo in group.Value)
                    {
                        GameObject go = FindGameObjectByPath(compInfo.gameObjectPath);
                        if (go != null)
                        {
                            Type t = ResolveType(compInfo.componentType);
                            compInfo.component = t != null ? go.GetComponent(t) : null;
                        }
                        else
                        {
                            compInfo.component = null;
                        }
                    }
                }

                // Remove empty groups or entries with null components
                foreach (string groupKey in componentGroups.Keys.ToList())
                {
                    componentGroups[groupKey].RemoveAll(c => c.component == null);
                    if (componentGroups[groupKey].Count == 0)
                        componentGroups.Remove(groupKey);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UIComponentFieldGenerator: Failed to restore window state: {ex.Message}");
                File.Delete(s_windowStatePath);
            }
        }
    }
}