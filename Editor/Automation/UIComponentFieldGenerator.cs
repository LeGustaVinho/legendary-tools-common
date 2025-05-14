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

namespace LegendaryTools.Editor
{
    public class UIComponentFieldGenerator : EditorWindow
    {
        private RectTransform targetRectTransform;
        private string namespaceName = "MyNamespace";
        private string className = "MyUIClass";
        private Vector2 scrollPosition;
        private Dictionary<string, List<ComponentInfo>> componentGroups = new Dictionary<string, List<ComponentInfo>>();
        private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
        private bool expandAllGroups = true;
        private int filterTypeIndex = 0;
        private string gameObjectFilter = "";
        private bool useSerializeField = true;
        private bool useBackingFields = false;

        private static readonly Type[] supportedTypes = new Type[]
        {
            typeof(Text), typeof(TMP_Text), typeof(Image), typeof(Button), typeof(Toggle),
            typeof(Slider), typeof(Scrollbar), typeof(Dropdown), typeof(InputField),
            typeof(RawImage), typeof(Mask), typeof(ScrollRect), typeof(TMP_InputField),
        };

        private static readonly string s_isWaitingRecompile = "UIComponentFieldGenerator_WaitingRecompile";
        private static readonly string s_pendingScriptPath = "UIComponentFieldGenerator_PendingScriptPath";

        private static readonly string s_pendingTargetRectTransformPath =
            "UIComponentFieldGenerator_PendingTargetRectTransformPath";

        private static readonly string s_pendingFieldAssignments = "UIComponentFieldGenerator_PendingFieldAssignments";

        [Serializable]
        private class ComponentInfo
        {
            public Component component;
            public bool isSelected;
            public string gameObjectName;
            public string parentName;
            public string fieldName;
            public string fieldNameInput;
            public List<int> selectedPropertyIndices;
            public List<int> selectedEventIndices;

            public ComponentInfo(Component comp, string goName, string pName, string fName)
            {
                component = comp;
                isSelected = false;
                gameObjectName = goName;
                parentName = pName;
                fieldName = fName;
                fieldNameInput = fName;
                selectedPropertyIndices = ComponentProperties.ContainsKey(comp.GetType()) &&
                                          ComponentProperties[comp.GetType()].Count > 0
                    ? new List<int> { 0 }
                    : new List<int>();
                selectedEventIndices = new List<int>();
            }
        }

        [Serializable]
        private struct FieldAssignment
        {
            public string fieldName;
            public string componentType;
            public string gameObjectName;
        }

        private static readonly Dictionary<Type, List<(string propertyName, string propertyType)>> ComponentProperties =
            new Dictionary<Type, List<(string, string)>>
            {
                { typeof(Text), new List<(string, string)> { ("text", "string"), ("color", "Color") } },
                { typeof(TMP_Text), new List<(string, string)> { ("text", "string"), ("color", "Color") } },
                { typeof(TMP_InputField), new List<(string, string)> { ("text", "string"), ("color", "Color") } },
                { typeof(Image), new List<(string, string)> { ("color", "Color"), ("sprite", "Sprite") } },
                { typeof(RawImage), new List<(string, string)> { ("color", "Color"), ("texture", "Texture") } },
                { typeof(Button), new List<(string, string)> { ("interactable", "bool") } },
                { typeof(Toggle), new List<(string, string)> { ("isOn", "bool") } },
                { typeof(Slider), new List<(string, string)> { ("value", "float") } },
                { typeof(Scrollbar), new List<(string, string)> { ("value", "float") } },
                { typeof(Dropdown), new List<(string, string)> { ("value", "int") } },
                { typeof(InputField), new List<(string, string)> { ("text", "string"), ("color", "Color") } },
                { typeof(ScrollRect), new List<(string, string)> { ("normalizedPosition", "Vector2") } },
                { typeof(Mask), new List<(string, string)> { } }
            };

        private static readonly Dictionary<Type, List<(string eventName, string eventType, string handlerSignature)>>
            ComponentEvents = new Dictionary<Type, List<(string, string, string)>>
            {
                { typeof(Button), new List<(string, string, string)> { ("onClick", "UnityEvent", "()") } },
                {
                    typeof(Toggle),
                    new List<(string, string, string)> { ("onValueChanged", "UnityEvent<bool>", "(bool value)") }
                },
                {
                    typeof(Slider),
                    new List<(string, string, string)> { ("onValueChanged", "UnityEvent<float>", "(float value)") }
                },
                {
                    typeof(Scrollbar),
                    new List<(string, string, string)> { ("onValueChanged", "UnityEvent<float>", "(float value)") }
                },
                {
                    typeof(Dropdown),
                    new List<(string, string, string)> { ("onValueChanged", "UnityEvent<int>", "(int value)") }
                },
                {
                    typeof(InputField), new List<(string, string, string)>
                    {
                        ("onValueChanged", "UnityEvent<string>", "(string value)"),
                        ("onEndEdit", "UnityEvent<string>", "(string value)"),
                        ("onSubmit", "UnityEvent<string>", "(string value)"),
                    }
                },
                {
                    typeof(TMP_InputField), new List<(string, string, string)>
                    {
                        ("onValueChanged", "UnityEvent<string>", "(string value)"),
                        ("onEndEdit", "UnityEvent<string>", "(string value)"),
                        ("onSubmit", "UnityEvent<string>", "(string value)"),
                    }
                },
                {
                    typeof(ScrollRect),
                    new List<(string, string, string)> { ("onValueChanged", "UnityEvent<Vector2>", "(Vector2 value)") }
                },
                { typeof(Text), new List<(string, string, string)> { } },
                { typeof(TMP_Text), new List<(string, string, string)> { } },
                { typeof(Image), new List<(string, string, string)> { } },
                { typeof(RawImage), new List<(string, string, string)> { } },
                { typeof(Mask), new List<(string, string, string)> { } }
            };

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
            List<string> pathSegments = new List<string>();
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

            // Find root objects
            GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject root in rootObjects)
            {
                if (root.name == pathSegments[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null) return null;

            // Traverse hierarchy
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

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            if (EditorPrefs.GetBool(s_isWaitingRecompile)) EditorApplication.delayCall += OnInspectorReloadComplete;
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
                Component targetComponent = null;
                foreach (RectTransform rt in targetRectTransform.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rt.gameObject.name == assignment.gameObjectName)
                    {
                        targetComponent = rt.GetComponent(assignment.componentType);
                        if (targetComponent != null)
                            break;
                    }
                }

                if (targetComponent == null)
                {
                    Debug.LogWarning(
                        $"UIComponentFieldGenerator: Could not find component '{assignment.componentType}' on GameObject '{assignment.gameObjectName}' for field '{assignment.fieldName}'.");
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
                targetRectTransform,
                typeof(RectTransform), true);
            namespaceName = EditorGUILayout.TextField("Namespace", namespaceName);
            className = EditorGUILayout.TextField("Class Name", className);

            EditorGUILayout.BeginHorizontal();
            List<string> typeOptions = new List<string> { "All" };
            typeOptions.AddRange(supportedTypes.Select(t => t.Name));
            filterTypeIndex =
                EditorGUILayout.Popup("Filter Type", filterTypeIndex, typeOptions.ToArray(), GUILayout.Width(200));
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
                    if (ComponentProperties.ContainsKey(comp.component.GetType()) &&
                        ComponentProperties[comp.component.GetType()].Count > 0)
                        comp.selectedPropertyIndices =
                            Enumerable.Range(0, ComponentProperties[comp.component.GetType()].Count).ToList();
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

            foreach (KeyValuePair<string, List<ComponentInfo>> group in componentGroups)
            {
                string parentName = group.Key;
                List<ComponentInfo> filteredComponents = group.Value.Where(c =>
                    (filterTypeIndex == 0 || c.component.GetType() == supportedTypes[filterTypeIndex - 1]) &&
                    (string.IsNullOrEmpty(gameObjectFilter) ||
                     c.gameObjectName.ToLower().Contains(gameObjectFilter.ToLower()))
                ).ToList();

                if (filteredComponents.Count == 0)
                    continue;

                if (!foldoutStates.ContainsKey(parentName))
                    foldoutStates[parentName] = expandAllGroups;

                foldoutStates[parentName] = EditorGUILayout.Foldout(foldoutStates[parentName], $"Parent: {parentName}");

                if (foldoutStates[parentName])
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Selected", EditorStyles.boldLabel, GUILayout.Width(60));
                    EditorGUILayout.LabelField("GameObject", EditorStyles.boldLabel, GUILayout.Width(150));
                    EditorGUILayout.LabelField("Field", EditorStyles.boldLabel, GUILayout.Width(120));
                    EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.Width(80));
                    EditorGUILayout.LabelField("Properties", EditorStyles.boldLabel, GUILayout.Width(120));
                    EditorGUILayout.LabelField("Events", EditorStyles.boldLabel, GUILayout.Width(120));
                    EditorGUILayout.EndHorizontal();

                    foreach (ComponentInfo comp in filteredComponents)
                    {
                        EditorGUILayout.BeginHorizontal();
                        comp.isSelected = EditorGUILayout.Toggle(comp.isSelected, GUILayout.Width(60));

                        GUIStyle buttonStyle = new GUIStyle(GUI.skin.label)
                            { normal = { textColor = GUI.skin.label.normal.textColor } };
                        if (GUI.Button(EditorGUILayout.GetControlRect(GUILayout.Width(150)), comp.gameObjectName,
                                buttonStyle)) Selection.activeObject = comp.component.gameObject;

                        comp.fieldNameInput = EditorGUILayout.TextField(comp.fieldNameInput, GUILayout.Width(120));
                        EditorGUILayout.LabelField(comp.component.GetType().Name, GUILayout.Width(80));

                        Type compType = comp.component.GetType();
                        if (ComponentProperties.ContainsKey(compType) && ComponentProperties[compType].Count > 0)
                        {
                            string[] propertyOptions =
                                ComponentProperties[compType].Select(p => p.propertyName).ToArray();
                            int currentMask = 0;
                            for (int i = 0; i < comp.selectedPropertyIndices.Count; i++)
                            {
                                currentMask |= 1 << comp.selectedPropertyIndices[i];
                            }

                            int newMask = EditorGUILayout.MaskField(currentMask, propertyOptions, GUILayout.Width(120));
                            comp.selectedPropertyIndices = new List<int>();
                            for (int i = 0; i < propertyOptions.Length; i++)
                            {
                                if ((newMask & (1 << i)) != 0)
                                    comp.selectedPropertyIndices.Add(i);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("None", GUILayout.Width(120));
                        }

                        if (ComponentEvents.ContainsKey(compType) && ComponentEvents[compType].Count > 0)
                        {
                            string[] eventOptions = ComponentEvents[compType].Select(e => e.eventName).ToArray();
                            int currentMask = 0;
                            for (int i = 0; i < comp.selectedEventIndices.Count; i++)
                            {
                                currentMask |= 1 << comp.selectedEventIndices[i];
                            }

                            int newMask = EditorGUILayout.MaskField(currentMask, eventOptions, GUILayout.Width(120));
                            comp.selectedEventIndices = new List<int>();
                            for (int i = 0; i < eventOptions.Length; i++)
                            {
                                if ((newMask & (1 << i)) != 0)
                                    comp.selectedEventIndices.Add(i);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("None", GUILayout.Width(120));
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

                GenerateClassFile();
            }
        }

        private void FindUIComponents()
        {
            componentGroups.Clear();
            foldoutStates.Clear();
            if (targetRectTransform == null) return;

            foreach (RectTransform rt in targetRectTransform.GetComponentsInChildren<RectTransform>(true))
            {
                foreach (Component comp in rt.GetComponents<Component>())
                {
                    if (supportedTypes.Contains(comp.GetType()))
                    {
                        string goName = comp.gameObject.name;
                        string parentName = rt.parent != null ? rt.parent.name : "Root";
                        string fieldName = ConvertToValidFieldName(goName);

                        if (!componentGroups.ContainsKey(parentName))
                            componentGroups[parentName] = new List<ComponentInfo>();

                        componentGroups[parentName].Add(new ComponentInfo(comp, goName, parentName, fieldName));
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

        private void GenerateClassFile()
        {
            List<ComponentInfo> selectedComponents =
                componentGroups.Values.SelectMany(g => g).Where(c => c.isSelected).ToList();
            if (selectedComponents.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No components selected.", "OK");
                return;
            }

            Dictionary<string, int> fieldNameCounts = new Dictionary<string, int>();
            Dictionary<ComponentInfo, string> uniqueFieldNames = new Dictionary<ComponentInfo, string>();
            List<FieldAssignment> fieldAssignments = new List<FieldAssignment>();
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
                    componentType = comp.component.GetType().Name,
                    gameObjectName = comp.gameObjectName
                });
            }

            string classContent = "";
            List<string> usingStatements = new List<string> { "UnityEngine", "UnityEngine.UI" };
            if (selectedComponents.Any(c => c.component.GetType() == typeof(TMP_Text)))
                usingStatements.Add("TMPro");
            if (selectedComponents.Any(c => c.selectedEventIndices.Any()))
                usingStatements.Add("UnityEngine.Events");

            foreach (string us in usingStatements)
            {
                classContent += $"using {us};\n";
            }

            classContent += "\n";

            if (!string.IsNullOrEmpty(namespaceName))
                classContent += $"namespace {namespaceName}\n{{\n";

            classContent += $"    public class {className} : MonoBehaviour\n    {{\n";

            foreach (ComponentInfo comp in selectedComponents)
            {
                string typeName = comp.component.GetType().Name;
                string fieldName = uniqueFieldNames[comp];
                string accessModifier = useSerializeField ? "[SerializeField] private" : "public";
                classContent += $"        {accessModifier} {typeName} {fieldName};\n";
            }

            classContent += "\n";

            foreach (ComponentInfo comp in selectedComponents)
            {
                Type compType = comp.component.GetType();
                if (!ComponentProperties.ContainsKey(compType) || ComponentProperties[compType].Count == 0)
                    continue;

                string fieldName = uniqueFieldNames[comp];
                foreach (int propIndex in comp.selectedPropertyIndices)
                {
                    (string propertyName, string propertyType) prop = ComponentProperties[compType][propIndex];
                    string propName = prop.propertyName;
                    string propType = prop.propertyType;
                    string capitalizedProp = char.ToUpper(propName[0]) + propName.Substring(1);
                    string backingFieldName = $"_{fieldName}{capitalizedProp}";

                    if (useBackingFields)
                    {
                        classContent += $"        [SerializeField] private {propType} {backingFieldName};\n";
                        classContent += $"        public {propType} {fieldName}{capitalizedProp}\n";
                        classContent += $"        {{\n";
                        classContent += $"            get => {backingFieldName};\n";
                        classContent +=
                            $"            set {{ {backingFieldName} = value; {fieldName}.{propName} = value; }}\n";
                        classContent += $"        }}\n";
                    }
                    else
                    {
                        classContent += $"        public {propType} {fieldName}{capitalizedProp}\n";
                        classContent += $"        {{\n";
                        classContent += $"            get => {fieldName}.{propName};\n";
                        classContent += $"            set => {fieldName}.{propName} = value;\n";
                        classContent += $"        }}\n";
                    }
                }
            }

            classContent += "\n";

            bool hasEvents = selectedComponents.Any(c => c.selectedEventIndices.Any());
            if (hasEvents)
            {
                classContent += $"        private void Awake()\n        {{\n";
                foreach (ComponentInfo comp in selectedComponents.Where(c => c.selectedEventIndices.Any()))
                {
                    string fieldName = uniqueFieldNames[comp];
                    Type compType = comp.component.GetType();
                    foreach (int eventIndex in comp.selectedEventIndices)
                    {
                        (string eventName, string eventType, string handlerSignature) eventInfo =
                            ComponentEvents[compType][eventIndex];
                        string handlerName = $"On{fieldName}{eventInfo.eventName}";
                        classContent += $"            {fieldName}.{eventInfo.eventName}.AddListener({handlerName});\n";
                    }
                }

                classContent += $"        }}\n\n";

                classContent += $"        private void OnDestroy()\n        {{\n";
                foreach (ComponentInfo comp in selectedComponents.Where(c => c.selectedEventIndices.Any()))
                {
                    string fieldName = uniqueFieldNames[comp];
                    Type compType = comp.component.GetType();
                    foreach (int eventIndex in comp.selectedEventIndices)
                    {
                        (string eventName, string eventType, string handlerSignature) eventInfo =
                            ComponentEvents[compType][eventIndex];
                        string handlerName = $"On{fieldName}{eventInfo.eventName}";
                        classContent +=
                            $"            {fieldName}.{eventInfo.eventName}.RemoveListener({handlerName});\n";
                    }
                }

                classContent += $"        }}\n\n";
            }

            foreach (ComponentInfo comp in selectedComponents.Where(c => c.selectedEventIndices.Any()))
            {
                Type compType = comp.component.GetType();
                string fieldName = uniqueFieldNames[comp];
                foreach (int eventIndex in comp.selectedEventIndices)
                {
                    (string eventName, string eventType, string handlerSignature) eventInfo =
                        ComponentEvents[compType][eventIndex];
                    string handlerName = $"On{fieldName}{eventInfo.eventName}";
                    classContent += $"        private void {handlerName}{eventInfo.handlerSignature}\n";
                    classContent += $"        {{\n";
                    classContent += $"            // TODO: Implement {eventInfo.eventName} handler for {fieldName}\n";
                    classContent += $"        }}\n\n";
                }
            }

            classContent += "    }\n";
            if (!string.IsNullOrEmpty(namespaceName))
                classContent += "}";

            string path = EditorUtility.SaveFilePanel("Save Script", "Assets", className + ".cs", "cs");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, classContent);
                string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
                EditorPrefs.SetBool(s_isWaitingRecompile, true);
                EditorPrefs.SetString(s_pendingScriptPath, relativePath);
                EditorPrefs.SetString(s_pendingTargetRectTransformPath,
                    GetGameObjectPath(targetRectTransform.gameObject));
                EditorPrefs.SetString(s_pendingFieldAssignments, JsonConvert.SerializeObject(fieldAssignments));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}