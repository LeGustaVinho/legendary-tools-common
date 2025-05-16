using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public class StatePersisterEditor : EditorWindow
    {
        private static string saveFilePath = "Library/StatePersisterData.json";
        private static Dictionary<string, Dictionary<string, object>> savedStates = new();
        private static Dictionary<string, Dictionary<string, Dictionary<string, object>>> groupedStates = new();
        private static Dictionary<string, bool> gameObjectFoldouts = new();
        private static Dictionary<string, Dictionary<string, bool>> componentFoldouts = new();
        private Vector2 scrollPosition;

        /// <summary>
        /// Adds a context menu item to persist the state of a single component.
        /// </summary>
        [MenuItem("CONTEXT/Component/Persist Component State")]
        private static void PersistComponentState(MenuCommand command)
        {
            Component component = command.context as Component;
            if (component != null) SaveComponentState(component);
        }

        /// <summary>
        /// Adds a context menu item to persist the state of all components on a GameObject.
        /// </summary>
        [MenuItem("CONTEXT/Component/Persist GameObject State")]
        private static void PersistGameObjectState(MenuCommand command)
        {
            Component comp = command.context as Component;
            if (comp == null) return;
            GameObject go = comp.gameObject;
            if (go != null)
            {
                Component[] components = go.GetComponents<Component>();
                foreach (Component component in components)
                {
                    SaveComponentState(component);
                }
            }
        }

        /// <summary>
        /// Saves the state of a component to JSON.
        /// </summary>
        private static void SaveComponentState(Component component)
        {
            string componentType = component.GetType().AssemblyQualifiedName;
            string gameObjectPath = GetGameObjectPath(component.gameObject);
            string key = gameObjectPath + "_" + componentType;

            Dictionary<string, object> componentData = new();
            componentData["type"] = componentType;
            componentData["gameObjectPath"] = gameObjectPath;

            // Save public and private fields with SerializeField
            FieldInfo[] fields = component.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                if (field.IsNotSerialized && !field.IsDefined(typeof(SerializeField), true)) continue;

                if (field.FieldType == typeof(GameObject))
                {
                    GameObject go = field.GetValue(component) as GameObject;
                    componentData[field.Name] = go != null ? GetGameObjectPath(go) : null;
                }
                else if (typeof(Object).IsAssignableFrom(field.FieldType))
                {
                    Object obj = field.GetValue(component) as Object;
                    componentData[field.Name] = obj != null ? AssetDatabase.GetAssetPath(obj) : null;
                }
                else
                {
                    componentData[field.Name] = field.GetValue(component);
                }
            }

            // Save public properties visible in the Inspector
            SerializedObject serializedObject = new(component);
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                if (prop.name == "m_Script") continue; // Ignore script reference
                if (fields.Any(f => f.Name == prop.name)) continue; // Avoid duplicating fields

                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        componentData[prop.name] = prop.intValue;
                        break;
                    case SerializedPropertyType.Boolean:
                        componentData[prop.name] = prop.boolValue;
                        break;
                    case SerializedPropertyType.Float:
                        componentData[prop.name] = prop.floatValue;
                        break;
                    case SerializedPropertyType.String:
                        componentData[prop.name] = prop.stringValue;
                        break;
                    case SerializedPropertyType.Vector2:
                        componentData[prop.name] = prop.vector2Value;
                        break;
                    case SerializedPropertyType.Vector3:
                        componentData[prop.name] = prop.vector3Value;
                        break;
                    case SerializedPropertyType.Quaternion:
                        componentData[prop.name] = prop.quaternionValue;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        Object objRef = prop.objectReferenceValue;
                        componentData[prop.name] = objRef != null
                            ? objRef is GameObject go ? GetGameObjectPath(go) : AssetDatabase.GetAssetPath(objRef)
                            : null;
                        break;
                    default:
                        // Ignore unsupported complex types
                        continue;
                }

                enterChildren = false;
            }

            savedStates[key] = componentData;
            SaveStatesToFile();
            GroupStates();
        }

        /// <summary>
        /// Gets the hierarchical path of a GameObject.
        /// </summary>
        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform current = go.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// Saves the states to a JSON file.
        /// </summary>
        private static void SaveStatesToFile()
        {
            string json = JsonConvert.SerializeObject(savedStates, Formatting.Indented, new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.All
            });
            File.WriteAllText(saveFilePath, json);
        }

        /// <summary>
        /// Groups states for display in the editor window.
        /// </summary>
        private static void GroupStates()
        {
            groupedStates.Clear();
            foreach (KeyValuePair<string, Dictionary<string, object>> kvp in savedStates)
            {
                Dictionary<string, object> componentData = kvp.Value;
                string gameObjectPath = componentData["gameObjectPath"] as string;
                string componentType = componentData["type"] as string;
                // Extract simple type name for display
                string componentTypeName = System.Type.GetType(componentType)?.Name ?? componentType;

                if (!groupedStates.ContainsKey(gameObjectPath))
                    groupedStates[gameObjectPath] = new Dictionary<string, Dictionary<string, object>>();
                if (!groupedStates[gameObjectPath].ContainsKey(componentType))
                {
                    groupedStates[gameObjectPath][componentType] = new Dictionary<string, object>();
                    groupedStates[gameObjectPath][componentType]["displayName"] = componentTypeName;
                }

                foreach (KeyValuePair<string, object> field in componentData)
                {
                    if (field.Key != "type" && field.Key != "gameObjectPath")
                        groupedStates[gameObjectPath][componentType][field.Key] = field.Value;
                }
            }
        }

        /// <summary>
        /// Initializes the play mode state change event.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Shows the editor window when exiting play mode.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) ShowStatePersisterWindow();
        }

        /// <summary>
        /// Displays the editor window.
        /// </summary>
        private static void ShowStatePersisterWindow()
        {
            StatePersisterEditor window = GetWindow<StatePersisterEditor>("State Persister");
            window.Show();
        }

        /// <summary>
        /// Renders the GUI for the editor window.
        /// </summary>
        private void OnGUI()
        {
            if (savedStates.Count == 0 && File.Exists(saveFilePath))
            {
                string json = File.ReadAllText(saveFilePath);
                savedStates = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                });
                GroupStates();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Estilos para colorir os campos
            GUIStyle greenStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.green } };
            GUIStyle orangeStyle = new GUIStyle(EditorStyles.label)
                { normal = { textColor = new Color(1f, 0.5f, 0f) } }; // Laranja

            // Collect keys to Click here to avoid modifying dictionary during iteration
            List<string> gameObjectPaths = groupedStates.Keys.ToList();
            foreach (string gameObjectPath in gameObjectPaths)
            {
                if (!gameObjectFoldouts.ContainsKey(gameObjectPath))
                    gameObjectFoldouts[gameObjectPath] = false;

                EditorGUILayout.BeginHorizontal();
                gameObjectFoldouts[gameObjectPath] =
                    EditorGUILayout.Foldout(gameObjectFoldouts[gameObjectPath], gameObjectPath);
                if (GUILayout.Button("Delete", GUILayout.Width(60))) DeleteGameObjectState(gameObjectPath);
                EditorGUILayout.EndHorizontal();

                if (gameObjectFoldouts[gameObjectPath])
                {
                    EditorGUI.indentLevel++;
                    // Collect component keys to avoid modifying dictionary during iteration
                    List<string> componentTypes = groupedStates[gameObjectPath].Keys.ToList();
                    foreach (string componentType in componentTypes)
                    {
                        if (!componentFoldouts.ContainsKey(gameObjectPath))
                            componentFoldouts[gameObjectPath] = new Dictionary<string, bool>();
                        if (!componentFoldouts[gameObjectPath].ContainsKey(componentType))
                            componentFoldouts[gameObjectPath][componentType] = false;

                        // Use simple type name for display
                        string displayName = groupedStates[gameObjectPath][componentType]["displayName"] as string;

                        EditorGUILayout.BeginHorizontal();
                        componentFoldouts[gameObjectPath][componentType] =
                            EditorGUILayout.Foldout(componentFoldouts[gameObjectPath][componentType], displayName);
                        if (GUILayout.Button("Delete", GUILayout.Width(60)))
                            DeleteComponentState(gameObjectPath, componentType);
                        EditorGUILayout.EndHorizontal();

                        if (componentFoldouts[gameObjectPath][componentType])
                        {
                            EditorGUI.indentLevel++;
                            // Collect keys to avoid modifying during iteration
                            List<string> fieldKeys = groupedStates[gameObjectPath][componentType].Keys
                                .Where(k => k != "displayName")
                                .ToList();
                            foreach (string fieldKey in fieldKeys)
                            {
                                // Get the saved value
                                object savedValue = groupedStates[gameObjectPath][componentType][fieldKey];
                                string valueString = savedValue != null ? savedValue.ToString() : "null";

                                // Find the GameObject and Component in the scene
                                GameObject go = FindGameObjectByPath(gameObjectPath);
                                bool isSameValue = false;
                                if (go != null)
                                {
                                    Component component = go.GetComponent(System.Type.GetType(componentType));
                                    if (component != null)
                                    {
                                        // Check field
                                        FieldInfo field = component.GetType()
                                            .GetField(fieldKey,
                                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (field != null && (field.IsDefined(typeof(SerializeField), true) ||
                                                              !field.IsNotSerialized))
                                        {
                                            object currentValue = field.GetValue(component);
                                            isSameValue = AreValuesEqual(savedValue, currentValue, field.FieldType);
                                        }
                                        else
                                        {
                                            // Check property
                                            SerializedObject serializedObject = new SerializedObject(component);
                                            SerializedProperty prop = serializedObject.FindProperty(fieldKey);
                                            if (prop != null)
                                            {
                                                object currentValue = GetPropertyValue(prop);
                                                isSameValue = AreValuesEqual(savedValue, currentValue, prop.propertyType);
                                            }
                                        }
                                    }
                                }

                                // Display the field with appropriate color
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField($"{fieldKey}: {valueString}",
                                    isSameValue ? greenStyle : orangeStyle);
                                if (GUILayout.Button("Delete", GUILayout.Width(60)))
                                    DeleteFieldState(gameObjectPath, componentType, fieldKey);
                                EditorGUILayout.EndHorizontal();
                            }

                            EditorGUI.indentLevel--;
                        }
                    }

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply")) ApplySavedStates();
            if (GUILayout.Button("Clear All")) ClearAllStates();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Compares two values for equality, handling different types.
        /// </summary>
        private bool AreValuesEqual(object savedValue, object currentValue, Type fieldType)
        {
            if (savedValue == null && currentValue == null) return true;
            if (savedValue == null || currentValue == null) return false;

            if (fieldType == typeof(GameObject))
            {
                string savedPath = savedValue as string;
                string currentPath = currentValue is GameObject go ? GetGameObjectPath(go) : null;
                return savedPath == currentPath;
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                string savedPath = savedValue as string;
                string currentPath = currentValue is UnityEngine.Object obj ? AssetDatabase.GetAssetPath(obj) : null;
                return savedPath == currentPath;
            }
            else if (fieldType == typeof(Vector2))
            {
                return savedValue is Vector2 v1 && currentValue is Vector2 v2 && v1 == v2;
            }
            else if (fieldType == typeof(Vector3))
            {
                return savedValue is Vector3 v1 && currentValue is Vector3 v2 && v1 == v2;
            }
            else if (fieldType == typeof(Quaternion))
            {
                return savedValue is Quaternion q1 && currentValue is Quaternion q2 && q1 == q2;
            }
            else if (fieldType == typeof(int) || fieldType == typeof(long))
            {
                return Convert.ToInt64(savedValue) == Convert.ToInt64(currentValue);
            }
            else if (fieldType == typeof(float) || fieldType == typeof(double))
            {
                return Math.Abs(Convert.ToDouble(savedValue) - Convert.ToDouble(currentValue)) < 0.0001;
            }
            else if (fieldType == typeof(bool))
            {
                return savedValue is bool b1 && currentValue is bool b2 && b1 == b2;
            }
            else if (fieldType == typeof(string))
            {
                return savedValue as string == currentValue as string;
            }

            return savedValue.Equals(currentValue);
        }

        /// <summary>
        /// Compares two values for equality, handling different serialized property types.
        /// </summary>
        private bool AreValuesEqual(object savedValue, object currentValue, SerializedPropertyType propertyType)
        {
            if (savedValue == null && currentValue == null) return true;
            if (savedValue == null || currentValue == null) return false;

            switch (propertyType)
            {
                case SerializedPropertyType.Integer:
                    return Convert.ToInt64(savedValue) == Convert.ToInt64(currentValue);
                case SerializedPropertyType.Boolean:
                    return savedValue is bool b1 && currentValue is bool b2 && b1 == b2;
                case SerializedPropertyType.Float:
                    return Math.Abs(Convert.ToDouble(savedValue) - Convert.ToDouble(currentValue)) < 0.0001;
                case SerializedPropertyType.String:
                    return savedValue as string == currentValue as string;
                case SerializedPropertyType.Vector2:
                    return savedValue is Vector2 v1 && currentValue is Vector2 v2 && v1 == v2;
                case SerializedPropertyType.Vector3:
                    return savedValue is Vector3 v11 && currentValue is Vector3 v22 && v11 == v22;
                case SerializedPropertyType.Quaternion:
                    return savedValue is Quaternion q1 && currentValue is Quaternion q2 && q1 == q2;
                case SerializedPropertyType.ObjectReference:
                    string savedPath = savedValue as string;
                    string currentPath = currentValue is GameObject go
                        ? GetGameObjectPath(go)
                        : currentValue is UnityEngine.Object obj
                            ? AssetDatabase.GetAssetPath(obj)
                            : null;
                    return savedPath == currentPath;
                default:
                    return savedValue.Equals(currentValue);
            }
        }

        /// <summary>
        /// Gets the value of a SerializedProperty.
        /// </summary>
        private object GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value;
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value;
                case SerializedPropertyType.Quaternion:
                    return prop.quaternionValue;
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Clears all saved states.
        /// </summary>
        private void ClearAllStates()
        {
            savedStates.Clear();
            groupedStates.Clear();
            gameObjectFoldouts.Clear();
            componentFoldouts.Clear();
            if (File.Exists(saveFilePath)) File.Delete(saveFilePath);
        }

        /// <summary>
        /// Deletes the state of a GameObject.
        /// </summary>
        private void DeleteGameObjectState(string gameObjectPath)
        {
            foreach (string componentType in groupedStates[gameObjectPath].Keys.ToList())
            {
                string key = gameObjectPath + "_" + componentType;
                savedStates.Remove(key);
            }

            groupedStates.Remove(gameObjectPath);
            SaveStatesToFile();
        }

        /// <summary>
        /// Deletes the state of a component.
        /// </summary>
        private void DeleteComponentState(string gameObjectPath, string componentType)
        {
            string key = gameObjectPath + "_" + componentType;
            savedStates.Remove(key);
            groupedStates[gameObjectPath].Remove(componentType);
            if (groupedStates[gameObjectPath].Count == 0) groupedStates.Remove(gameObjectPath);
            SaveStatesToFile();
        }

        /// <summary>
        /// Deletes the state of a field.
        /// </summary>
        private void DeleteFieldState(string gameObjectPath, string componentType, string field)
        {
            string key = gameObjectPath + "_" + componentType;
            if (savedStates.ContainsKey(key))
            {
                savedStates[key].Remove(field);
                if (savedStates[key].Count <= 2) // Only type and gameObjectPath remain
                {
                    savedStates.Remove(key);
                    groupedStates[gameObjectPath].Remove(componentType);
                    if (groupedStates[gameObjectPath].Count == 0) groupedStates.Remove(gameObjectPath);
                }
                else
                {
                    groupedStates[gameObjectPath][componentType].Remove(field);
                }

                SaveStatesToFile();
            }
        }

        /// <summary>
        /// Applies saved states to components.
        /// </summary>
        /// <summary>
        /// Applies saved states to components.
        /// </summary>
        private void ApplySavedStates()
        {
            if (!File.Exists(saveFilePath)) return;

            string json = File.ReadAllText(saveFilePath);
            savedStates = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });

            foreach (KeyValuePair<string, Dictionary<string, object>> kvp in savedStates)
            {
                Dictionary<string, object> componentData = kvp.Value;
                string gameObjectPath = componentData["gameObjectPath"] as string;
                string componentType = componentData["type"] as string;

                GameObject go = FindGameObjectByPath(gameObjectPath);
                if (go != null)
                {
                    // Convert AssemblyQualifiedName to Type
                    Type type = Type.GetType(componentType);
                    if (type != null)
                    {
                        Component component = go.GetComponent(type);
                        if (component != null)
                        {
                            ApplyComponentState(component, componentData);
                        }
                        else
                        {
                            Debug.LogWarning($"Component of type {type.Name} not found on GameObject {gameObjectPath}.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Could not resolve type from AssemblyQualifiedName: {componentType}");
                    }
                }
                else
                {
                    Debug.LogWarning($"GameObject not found at path: {gameObjectPath}");
                }
            }

            // Clear states after applying
            savedStates.Clear();
            groupedStates.Clear();
            if (File.Exists(saveFilePath)) File.Delete(saveFilePath);
        }

        /// <summary>
        /// Finds a GameObject by its path, even if inactive.
        /// </summary>
        private static GameObject FindGameObjectByPath(string path)
        {
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject go in allObjects)
            {
                if (GetGameObjectPath(go) == path) return go;
            }

            return null;
        }

        /// <summary>
        /// Applies saved values to a component.
        /// </summary>
        private static void ApplyComponentState(Component component, Dictionary<string, object> componentData)
        {
            // Apply fields
            FieldInfo[] fields = component.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                if (field.IsNotSerialized && !field.IsDefined(typeof(SerializeField), true)) continue;
                if (!componentData.ContainsKey(field.Name)) continue;

                if (field.FieldType == typeof(GameObject))
                {
                    string path = componentData[field.Name] as string;
                    field.SetValue(component, path != null ? FindGameObjectByPath(path) : null);
                }
                else if (typeof(Object).IsAssignableFrom(field.FieldType))
                {
                    string assetPath = componentData[field.Name] as string;
                    field.SetValue(component,
                        assetPath != null ? AssetDatabase.LoadAssetAtPath(assetPath, field.FieldType) : null);
                }
                else
                {
                    field.SetValue(component, componentData[field.Name]);
                }
            }

            // Apply properties
            SerializedObject serializedObject = new(component);
            foreach (KeyValuePair<string, object> item in componentData)
            {
                if (item.Key == "type" || item.Key == "gameObjectPath" || fields.Any(f => f.Name == item.Key)) continue;

                SerializedProperty prop = serializedObject.FindProperty(item.Key);
                if (prop == null) continue;

                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        if (item.Value is long l) prop.intValue = (int)l;
                        break;
                    case SerializedPropertyType.Boolean:
                        if (item.Value is bool b) prop.boolValue = b;
                        break;
                    case SerializedPropertyType.Float:
                        if (item.Value is double d) prop.floatValue = (float)d;
                        break;
                    case SerializedPropertyType.String:
                        if (item.Value is string s) prop.stringValue = s;
                        break;
                    case SerializedPropertyType.Vector2:
                        if (item.Value is Vector2 v2) prop.vector2Value = v2;
                        break;
                    case SerializedPropertyType.Vector3:
                        if (item.Value is Vector3 v3) prop.vector3Value = v3;
                        break;
                    case SerializedPropertyType.Quaternion:
                        if (item.Value is Quaternion q) prop.quaternionValue = q;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        string path = item.Value as string;
                        if (path != null)
                        {
                            if (path.Contains("/")) // GameObject path
                                prop.objectReferenceValue = FindGameObjectByPath(path);
                            else // Asset path
                                prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath(path, typeof(Object));
                        }
                        else
                        {
                            prop.objectReferenceValue = null;
                        }

                        break;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}