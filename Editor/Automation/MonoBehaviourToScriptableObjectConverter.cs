using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// A custom Unity Editor window that converts MonoBehaviour components into ScriptableObject assets.
    /// This tool allows users to select MonoBehaviours, generate corresponding ScriptableObject classes,
    /// and create ScriptableObject assets with serialized fields from the MonoBehaviours.
    /// </summary>
    public class MonoBehaviourToScriptableObjectConverter : EditorWindow
    {
        [SerializeField] private List<MonoBehaviour> monoBehaviours = new List<MonoBehaviour>();
        private Type _scriptableObjectType;
        private string _namespaceName = "Generated";
        private static readonly string s_isWaitingRecompile = "isWaitingRecompile";
        private bool _needToRecompile = false;
        private string _scriptableObjectOutputFolderPath = "Assets/";
        private string _scriptableObjectClassOutputFolderPath = "Assets/";
        private string _componentTypeName = "";
        private bool _ensureDistinctSoInstances;
        private int _ensureDistinctSoInstancesCount;

        /// <summary>
        /// Displays the MonoBehaviour to ScriptableObject Converter window in the Unity Editor.
        /// </summary>
        [MenuItem("Tools/LegendaryTools/Automation/MonoBehaviour to ScriptableObject Converter")]
        public static void ShowWindow()
        {
            GetWindow<MonoBehaviourToScriptableObjectConverter>("MB to SO Converter");
        }

        /// <summary>
        /// Renders the GUI for the Editor window, allowing users to configure and initiate the conversion process.
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Select MonoBehaviours", EditorStyles.boldLabel);
            SerializedObject so = new SerializedObject(this);

            // Display the array of MonoBehaviours in the Inspector
            SerializedProperty monoBehavioursProp = so.FindProperty("monoBehaviours");
            EditorGUILayout.PropertyField(monoBehavioursProp, true);

            // Input field for specifying the component type to collect
            _componentTypeName = EditorGUILayout.TextField("Component Type:", _componentTypeName);

            // Button to collect components of the specified type from the scene
            if (GUILayout.Button("Collect Components by Type"))
            {
                CollectComponentsByType();
            }

            // Allow users to specify the namespace for generated ScriptableObject classes
            _namespaceName = EditorGUILayout.TextField("Namespace:", _namespaceName);

            // Input field and button for selecting the output folder for ScriptableObject assets
            EditorGUILayout.BeginHorizontal();
            _scriptableObjectOutputFolderPath = EditorGUILayout.TextField("SO Output Folder:", _scriptableObjectOutputFolderPath);
            if (GUILayout.Button("Select Folder", GUILayout.Width(120)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets/", "");
                if (!string.IsNullOrEmpty(selectedPath))
                    _scriptableObjectOutputFolderPath = selectedPath.Replace(Application.dataPath, "Assets");
            }
            EditorGUILayout.EndHorizontal();

            // Input field and button for selecting the output folder for ScriptableObject class files
            EditorGUILayout.BeginHorizontal();
            _scriptableObjectClassOutputFolderPath = EditorGUILayout.TextField("SO Class Output Folder:", _scriptableObjectClassOutputFolderPath);
            if (GUILayout.Button("Select Folder", GUILayout.Width(120)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets/", "");
                if (!string.IsNullOrEmpty(selectedPath))
                    _scriptableObjectClassOutputFolderPath = selectedPath.Replace(Application.dataPath, "Assets");
            }
            EditorGUILayout.EndHorizontal();

            _ensureDistinctSoInstances = EditorGUILayout.Toggle("Ensure Distinct SO Instances:", _ensureDistinctSoInstances);

            // Button to start the conversion process
            if (GUILayout.Button("Generate ScriptableObject"))
            {
                ProcessMonoBehaviours();
            }

            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// Collects all components of the specified type from the active scene and populates the monoBehaviours array.
        /// </summary>
        private void CollectComponentsByType()
        {
            if (string.IsNullOrEmpty(_componentTypeName))
            {
                EditorUtility.DisplayDialog("Error", "Please specify a component type.", "OK");
                return;
            }

            // Find the type by name
            Type componentType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == _componentTypeName && typeof(MonoBehaviour).IsAssignableFrom(t));

            if (componentType == null)
            {
                EditorUtility.DisplayDialog("Error", $"Type '{_componentTypeName}' not found or is not a MonoBehaviour.", "OK");
                return;
            }

            // Collect all components of the specified type from the scene
            Object[] found = FindObjectsOfType(componentType, true);
            monoBehaviours.AddRange(found.Cast<MonoBehaviour>().ToArray());

            if (found.Length == 0)
            {
                EditorUtility.DisplayDialog("Warning", $"No components of type '{_componentTypeName}' found in the scene.", "OK");
            }
            else
            {
                Debug.Log($"Collected {monoBehaviours.Count} components of type '{_componentTypeName}'.");
            }

            // Force GUI repaint to update the monoBehaviours array display
            Repaint();
        }

        /// <summary>
        /// Processes the selected MonoBehaviours, generating ScriptableObject classes and assets.
        /// </summary>
        private void ProcessMonoBehaviours()
        {
            // Validate that MonoBehaviours are selected
            if (monoBehaviours == null || monoBehaviours.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No MonoBehaviours selected.", "OK");
                return;
            }

            // Validate the output folder
            if (!Directory.Exists(_scriptableObjectOutputFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "Output folder does not exist.", "OK");
                return;
            }
            
            try
            {
                for (int i = 0; i < monoBehaviours.Count; i++)
                {
                    MonoBehaviour mb = monoBehaviours[i];
                    float progress = (float)(i + 1) / monoBehaviours.Count;

                    // Generate the ScriptableObject class
                    string classPath = Path.Combine(_scriptableObjectClassOutputFolderPath, mb.GetType().Name + "Config.cs");
                    EditorUtility.DisplayProgressBar("Processing Files", $"Generating ScriptableObject class: {Path.GetFileName(classPath)}", progress);
                    CreateScriptableObjectClass(mb);

                    // Ensure the MonoBehaviour has a config field
                    string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(mb));
                    EditorUtility.DisplayProgressBar("Processing Files", $"Modifying MonoBehaviour script: {Path.GetFileName(scriptPath)}", progress);
                    EnsureConfigFieldOnMonoBehaviour(mb);
                }

                // Trigger script compilation if new classes or fields were added
                if (_needToRecompile)
                {
                    EditorPrefs.SetBool(s_isWaitingRecompile, true);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                }
                else
                {
                    ContinueGenerateScriptableObjectInstance(this);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Generates a ScriptableObject asset for the given MonoBehaviour and copies its serialized fields.
        /// </summary>
        /// <param name="mb">The MonoBehaviour to convert.</param>
        private void GenerateScriptableObjectInstance(MonoBehaviour mb)
        {
            string scriptableObjectName = mb.gameObject.name + "Config";

            if (_ensureDistinctSoInstances)
            {
                scriptableObjectName += _ensureDistinctSoInstancesCount.ToString();
                _ensureDistinctSoInstancesCount++;
            }

            string baseName = mb.GetType().Name + "Config";
            string assetPath = Path.Combine(_scriptableObjectOutputFolderPath, scriptableObjectName + ".asset").Replace("\\", "/");

            // Show progress for creating the ScriptableObject asset
            EditorUtility.DisplayProgressBar("Processing Files", $"Creating ScriptableObject asset: {Path.GetFileName(assetPath)}", 1f);

            // Load or create the ScriptableObject asset
            ScriptableObject soInstance = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (soInstance == null)
            {
                soInstance = CreateInstance(_namespaceName + "." + baseName);
                AssetDatabase.CreateAsset(soInstance, assetPath);
                EditorUtility.SetDirty(soInstance);
                Debug.Log($"ScriptableObject asset created: {assetPath}");
            }

            // Copy serialized fields from the MonoBehaviour to the ScriptableObject
            foreach (FieldInfo field in mb.GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.IsPublic || field.IsDefined(typeof(SerializeField), false))
                {
                    object value = field.GetValue(mb);

                    // Handle null or missing field values
                    if (value == null)
                    {
                        Debug.LogWarning($"[Warning] Field '{field.Name}' in {mb.name} is null or missing.");
                        continue;
                    }

                    // Copy Unity Object references (e.g., GameObject, Transform)
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                    {
                        FieldInfo soField = soInstance.GetType().GetField(field.Name);
                        if (soField != null) soField.SetValue(soInstance, value);
                    }
                    // Copy arrays and lists
                    else if (field.FieldType.IsArray || (field.FieldType.IsGenericType &&
                                                        field.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
                    {
                        FieldInfo soField = soInstance.GetType().GetField(field.Name);
                        if (soField != null && value != null) soField.SetValue(soInstance, value);
                    }
                    // Copy other serializable types
                    else
                    {
                        FieldInfo soField = soInstance.GetType().GetField(field.Name);
                        if (soField != null)
                            soField.SetValue(soInstance, value);
                        else
                            Debug.LogError(
                                $"[Error] Field '{field.Name}' in {mb.name} is of an unsupported type: {field.FieldType}");
                    }
                }
            }

            // Link the ScriptableObject to the MonoBehaviour's config field
            FieldInfo configField = mb.GetType().GetField(baseName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            if (configField != null)
            {
                configField.SetValue(mb, soInstance);
                Debug.Log($"ScriptableObject {scriptableObjectName} linked to field {configField.Name} in {mb.name}");
                EditorUtility.SetDirty(mb);
            }
            else
            {
                Debug.LogWarning($"[Warning] Field '{baseName}' not found in {mb.name}. " +
                                "EnsureConfigFieldOnMonoBehaviour should have created it.");
            }
        }

        /// <summary>
        /// Creates a ScriptableObject class based on the MonoBehaviour's serialized fields.
        /// </summary>
        /// <param name="mb">The MonoBehaviour to base the ScriptableObject on.</param>
        private void CreateScriptableObjectClass(MonoBehaviour mb)
        {
            string baseName = mb.GetType().Name + "Config";
            string classPath = Path.Combine(_scriptableObjectClassOutputFolderPath, baseName + ".cs");

            // Skip if the class already exists
            if (File.Exists(classPath)) return;

            // Generate the ScriptableObject class file
            using (StreamWriter writer = new StreamWriter(classPath))
            {
                writer.WriteLine("using UnityEngine;");
                writer.WriteLine("using System.Collections.Generic;");
                writer.WriteLine($"namespace {_namespaceName}");
                writer.WriteLine("{");
                writer.WriteLine($"    [CreateAssetMenu(fileName = \"{baseName}\", menuName = \"{_namespaceName}/{baseName}\")]");
                writer.WriteLine($"    public class {baseName} : ScriptableObject");
                writer.WriteLine("    {");

                // Add fields to the ScriptableObject class
                foreach (FieldInfo field in mb.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), false))
                    {
                        string fieldType = GetSimplifiedTypeName(field.FieldType);
                        writer.WriteLine($"        public {fieldType} {field.Name};");
                    }
                }

                writer.WriteLine("    }");
                writer.WriteLine("}");
            }

            _needToRecompile = true;
            Debug.Log($"ScriptableObject class created: {classPath}");
        }

        /// <summary>
        /// Ensures the MonoBehaviour has a field to reference the generated ScriptableObject.
        /// </summary>
        /// <param name="mb">The MonoBehaviour to modify.</param>
        private void EnsureConfigFieldOnMonoBehaviour(MonoBehaviour mb)
        {
            string baseName = mb.GetType().Name + "Config";
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(mb));
            if (string.IsNullOrEmpty(scriptPath)) return;

            string[] lines = File.ReadAllLines(scriptPath);

            // Check if the namespace and field already exist
            bool namespaceExists = lines.Any(line => line.Contains($"using {_namespaceName};"));
            bool fieldExists = lines.Any(line => line.Contains($"private {baseName} {baseName};"));

            if (namespaceExists && fieldExists)
            {
                Debug.Log($"Config field already exists in MonoBehaviour: {mb.GetType().Name}");
                return;
            }

            bool classFound = false;
            bool needsModification = !namespaceExists || !fieldExists;

            if (needsModification)
            {
                bool namespaceAdded = false;
                bool fieldAdded = false;
                // Modify the MonoBehaviour script to add the config field
                using (StreamWriter writer = new StreamWriter(scriptPath))
                {
                    foreach (string line in lines)
                    {
                        // Add namespace after the last 'using' statement if it doesn't exist
                        if (!namespaceExists && !namespaceAdded && line.StartsWith("using") &&
                            !line.Contains(_namespaceName))
                        {
                            if (!lines.Any(l => l.Trim() == $"using {_namespaceName};"))
                            {
                                writer.WriteLine($"using {_namespaceName};");
                                namespaceAdded = true;
                            }
                        }

                        writer.WriteLine(line);

                        if (line.Contains($"class {mb.GetType().Name}")) classFound = true;

                        if (!fieldExists && classFound && line.Contains("{") && !fieldAdded)
                        {
                            writer.WriteLine($"    [SerializeField] private {baseName} {baseName};");
                            fieldAdded = true;
                        }
                    }
                }
                
                _needToRecompile = true;
                Debug.Log($"Config field added to MonoBehaviour: {mb.GetType().Name}");
            }
        }

        /// <summary>
        /// Callback invoked after Unity recompiles scripts, used to finalize the conversion process.
        /// </summary>
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnAfterAssemblyReload()
        {
            if (EditorPrefs.GetBool(s_isWaitingRecompile))
                EditorApplication.delayCall += OnInspectorReloadComplete;
        }

        /// <summary>
        /// Finalizes the conversion process after script recompilation.
        /// </summary>
        private static void OnInspectorReloadComplete()
        {
            EditorApplication.delayCall -= OnInspectorReloadComplete;
            MonoBehaviourToScriptableObjectConverter activeConverter = GetWindow<MonoBehaviourToScriptableObjectConverter>("MB to SO Converter");
            if (activeConverter != null)
            {
                try
                {
                    // Process ScriptableObject instances with progress feedback
                    for (int i = 0; i < activeConverter.monoBehaviours.Count; i++)
                    {
                        MonoBehaviour mb = activeConverter.monoBehaviours[i];
                        float progress = (float)(i + 1) / activeConverter.monoBehaviours.Count;
                        string assetName = (mb.gameObject.name + "Config" + (activeConverter._ensureDistinctSoInstances ? activeConverter._ensureDistinctSoInstancesCount.ToString() : "") + ".asset");
                        EditorUtility.DisplayProgressBar("Processing Files", $"Generating ScriptableObject instance: {assetName}", progress);
                        activeConverter.GenerateScriptableObjectInstance(mb);
                    }

                    // Save and refresh assets to ensure changes are applied
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    Debug.Log("Conversion complete.");
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    activeConverter._needToRecompile = false;
                    EditorPrefs.SetBool(s_isWaitingRecompile, false);
                }
            }
        }

        /// <summary>
        /// Continues generating ScriptableObject instances for all selected MonoBehaviours.
        /// </summary>
        /// <param name="activeConverter">The active converter instance.</param>
        private static void ContinueGenerateScriptableObjectInstance(MonoBehaviourToScriptableObjectConverter activeConverter)
        {
            try
            {
                for (int i = 0; i < activeConverter.monoBehaviours.Count; i++)
                {
                    MonoBehaviour mb = activeConverter.monoBehaviours[i];
                    float progress = (float)(i + 1) / activeConverter.monoBehaviours.Count;
                    string assetName = (mb.gameObject.name + "Config" + (activeConverter._ensureDistinctSoInstances ? activeConverter._ensureDistinctSoInstancesCount.ToString() : "") + ".asset");
                    EditorUtility.DisplayProgressBar("Processing Files", $"Generating ScriptableObject instance: {assetName}", progress);
                    activeConverter.GenerateScriptableObjectInstance(mb);
                }

                // Save and refresh assets to ensure changes are applied
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("Conversion complete.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Returns a simplified string representation of a type for use in generated code.
        /// </summary>
        /// <param name="type">The type to simplify.</param>
        /// <returns>A string representing the simplified type name.</returns>
        private string GetSimplifiedTypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return $"List<{GetSimplifiedTypeName(type.GetGenericArguments()[0])}>";
            if (type.IsArray) return $"{GetSimplifiedTypeName(type.GetElementType())}[]";

            return type.Name;
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
    }
}
