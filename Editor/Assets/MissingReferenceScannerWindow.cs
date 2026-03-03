using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    public sealed class MissingReferenceScannerWindow : EditorWindow
    {
        [Flags]
        private enum ScanScope
        {
            None = 0,
            ProjectPrefabs = 1 << 0,
            ProjectScriptableObjects = 1 << 1,
            OpenScene = 1 << 2,
            OpenPrefabMode = 1 << 3
        }

        [Flags]
        private enum VisibleColumns
        {
            None = 0,
            Index = 1 << 0,
            Type = 1 << 1,
            Scope = 1 << 2,
            Asset = 1 << 3,
            Object = 1 << 4,
            Component = 1 << 5,
            Property = 1 << 6,
            PropertyType = 1 << 7,
            Action = 1 << 8
        }

        private enum SortColumn
        {
            Index,
            Type,
            Scope,
            Asset,
            Object,
            Component,
            Property,
            PropertyType
        }

        private sealed class ScanResult
        {
            public int OriginalIndex;
            public string Scope;
            public string AssetPath;
            public string ObjectPath;
            public string ComponentName;
            public string PropertyPath;
            public string PropertyValueType;
            public string Message;
            public UnityEngine.Object Context;
        }

        private static readonly List<ScanResult> Results = new();
        private static int s_nextResultIndex;

        private Vector2 _scroll;

        private ScanScope _selectedScopes =
            ScanScope.ProjectPrefabs |
            ScanScope.ProjectScriptableObjects |
            ScanScope.OpenScene |
            ScanScope.OpenPrefabMode;

        private VisibleColumns _visibleColumns =
            VisibleColumns.Index |
            VisibleColumns.Type |
            VisibleColumns.Scope |
            VisibleColumns.Asset |
            VisibleColumns.Object |
            VisibleColumns.Component |
            VisibleColumns.Property |
            VisibleColumns.PropertyType |
            VisibleColumns.Action;

        private SortColumn _sortColumn = SortColumn.Index;
        private bool _sortAscending = true;

        [MenuItem("Tools/LegendaryTools/Assets/Missing Reference Scanner")]
        private static void OpenWindow()
        {
            GetWindow<MissingReferenceScannerWindow>("Missing References");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scan Scopes", EditorStyles.boldLabel);

            DrawScopeToggle(ScanScope.ProjectPrefabs, "Project Wide (Prefabs)");
            DrawScopeToggle(ScanScope.ProjectScriptableObjects, "Project Wide (ScriptableObjects)");
            DrawScopeToggle(ScanScope.OpenScene, "Open Scene");
            DrawScopeToggle(ScanScope.OpenPrefabMode, "Open Prefab (Prefab Mode)");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Visible Columns", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawColumnToggle(VisibleColumns.Index, "#");
                DrawColumnToggle(VisibleColumns.Type, "Type");
                DrawColumnToggle(VisibleColumns.Scope, "Scope");
                DrawColumnToggle(VisibleColumns.Asset, "Asset");
                DrawColumnToggle(VisibleColumns.Object, "Object");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawColumnToggle(VisibleColumns.Component, "Component");
                DrawColumnToggle(VisibleColumns.Property, "Property");
                DrawColumnToggle(VisibleColumns.PropertyType, "Prop Type");
                DrawColumnToggle(VisibleColumns.Action, "Action");
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan", GUILayout.Height(30))) RunSelectedScans();

                if (GUILayout.Button("Clear Results", GUILayout.Height(30)))
                {
                    Results.Clear();
                    s_nextResultIndex = 0;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Results: {Results.Count}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Sorting: {GetSortColumnDisplayName(_sortColumn)} {(_sortAscending ? "↑" : "↓")}");
            EditorGUILayout.Space();

            DrawResultsTable();
        }

        private void DrawScopeToggle(ScanScope scope, string label)
        {
            bool hasFlag = (_selectedScopes & scope) != 0;
            bool newValue = EditorGUILayout.ToggleLeft(label, hasFlag);

            if (newValue)
                _selectedScopes |= scope;
            else
                _selectedScopes &= ~scope;
        }

        private void DrawColumnToggle(VisibleColumns column, string label)
        {
            bool hasFlag = (_visibleColumns & column) != 0;
            bool newValue = EditorGUILayout.ToggleLeft(label, hasFlag, GUILayout.Width(120));

            if (newValue)
                _visibleColumns |= column;
            else
                _visibleColumns &= ~column;
        }

        private void DrawResultsTable()
        {
            DrawTableHeader();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < Results.Count; i++)
            {
                DrawTableRow(Results[i], i);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTableHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (IsColumnVisible(VisibleColumns.Index)) DrawHeaderButton("#", SortColumn.Index, GUILayout.Width(50));

                if (IsColumnVisible(VisibleColumns.Type))
                    DrawHeaderButton("Type", SortColumn.Type, GUILayout.Width(180));

                if (IsColumnVisible(VisibleColumns.Scope))
                    DrawHeaderButton("Scope", SortColumn.Scope, GUILayout.Width(180));

                if (IsColumnVisible(VisibleColumns.Asset))
                    DrawHeaderButton("Asset", SortColumn.Asset, GUILayout.Width(240));

                if (IsColumnVisible(VisibleColumns.Object))
                    DrawHeaderButton("Object", SortColumn.Object, GUILayout.Width(240));

                if (IsColumnVisible(VisibleColumns.Component))
                    DrawHeaderButton("Component", SortColumn.Component, GUILayout.Width(150));

                if (IsColumnVisible(VisibleColumns.Property))
                    DrawHeaderButton("Property", SortColumn.Property, GUILayout.Width(220));

                if (IsColumnVisible(VisibleColumns.PropertyType))
                    DrawHeaderButton("Prop Type", SortColumn.PropertyType, GUILayout.Width(180));

                if (IsColumnVisible(VisibleColumns.Action))
                    GUILayout.Label("Action", EditorStyles.boldLabel, GUILayout.Width(180));
            }
        }

        private void DrawHeaderButton(string label, SortColumn column, params GUILayoutOption[] options)
        {
            string displayLabel = label;
            if (_sortColumn == column) displayLabel += _sortAscending ? " ↑" : " ↓";

            if (GUILayout.Button(displayLabel, EditorStyles.miniButtonMid, options)) ApplySort(column);
        }

        private void DrawTableRow(ScanResult result, int index)
        {
            Rect rowRect = EditorGUILayout.BeginHorizontal();

            GUIStyle rowStyle = GetRowStyle(index);
            GUI.Box(rowRect, GUIContent.none, rowStyle);

            if (IsColumnVisible(VisibleColumns.Index)) GUILayout.Label((index + 1).ToString(), GUILayout.Width(50));

            if (IsColumnVisible(VisibleColumns.Type))
                GUILayout.Label(GetTableValue(result.Message, "<N/A>"), GUILayout.Width(180));

            if (IsColumnVisible(VisibleColumns.Scope))
                GUILayout.Label(GetTableValue(result.Scope, "<N/A>"), GUILayout.Width(180));

            if (IsColumnVisible(VisibleColumns.Asset))
                GUILayout.Label(GetTableValue(result.AssetPath, "<Scene Object>"), GUILayout.Width(240));

            if (IsColumnVisible(VisibleColumns.Object))
                GUILayout.Label(GetTableValue(result.ObjectPath, "<N/A>"), GUILayout.Width(240));

            if (IsColumnVisible(VisibleColumns.Component))
                GUILayout.Label(GetTableValue(result.ComponentName, "<N/A>"), GUILayout.Width(150));

            if (IsColumnVisible(VisibleColumns.Property))
                GUILayout.Label(GetTableValue(result.PropertyPath, "<N/A>"), GUILayout.Width(220));

            if (IsColumnVisible(VisibleColumns.PropertyType))
                GUILayout.Label(GetTableValue(result.PropertyValueType, "<N/A>"), GUILayout.Width(180));

            if (IsColumnVisible(VisibleColumns.Action)) DrawActionButtons(result);

            EditorGUILayout.EndHorizontal();

            HandleRowClick(rowRect, result);
        }

        private void DrawActionButtons(ScanResult result)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(180)))
            {
                if (GUILayout.Button("Ping", GUILayout.Width(80))) FocusResult(result);

                bool canOpenPrefab = IsPrefabAssetPath(result.AssetPath);
                using (new EditorGUI.DisabledScope(!canOpenPrefab))
                {
                    if (GUILayout.Button("Open Prefab", GUILayout.Width(95))) OpenPrefabInPrefabMode(result.AssetPath);
                }
            }
        }

        private static GUIStyle GetRowStyle(int index)
        {
            return new GUIStyle((index & 1) == 0 ? "CN EntryBackEven" : "CN EntryBackOdd");
        }

        private bool IsColumnVisible(VisibleColumns column)
        {
            return (_visibleColumns & column) != 0;
        }

        private static string GetTableValue(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static void HandleRowClick(Rect rowRect, ScanResult result)
        {
            Event currentEvent = Event.current;
            if (currentEvent == null) return;

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;

            if (!rowRect.Contains(currentEvent.mousePosition)) return;

            FocusResult(result);
            currentEvent.Use();
        }

        private static void FocusResult(ScanResult result)
        {
            if (result == null) return;

            if (result.Context != null)
            {
                if (result.Context is Component component)
                {
                    Selection.activeObject = component.gameObject;
                    EditorGUIUtility.PingObject(component.gameObject);
                    SceneView.lastActiveSceneView?.FrameSelected();
                    EditorGUIUtility.ExitGUI();
                    return;
                }

                Selection.activeObject = result.Context;
                EditorGUIUtility.PingObject(result.Context);

                if (result.Context is GameObject) SceneView.lastActiveSceneView?.FrameSelected();

                EditorGUIUtility.ExitGUI();
                return;
            }

            if (!string.IsNullOrEmpty(result.AssetPath))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(result.AssetPath);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    EditorGUIUtility.ExitGUI();
                }
            }
        }

        private void ApplySort(SortColumn column)
        {
            if (_sortColumn == column)
                _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = column;
                _sortAscending = true;
            }

            SortResultsInternal();
            Repaint();
        }

        private void SortResultsInternal()
        {
            Comparison<ScanResult> comparison = CreateComparison(_sortColumn);

            Results.Sort((left, right) =>
            {
                int result = comparison(left, right);
                return _sortAscending ? result : -result;
            });
        }

        private Comparison<ScanResult> CreateComparison(SortColumn column)
        {
            switch (column)
            {
                case SortColumn.Type:
                    return (left, right) => CompareStrings(left.Message, right.Message);

                case SortColumn.Scope:
                    return (left, right) => CompareStrings(left.Scope, right.Scope);

                case SortColumn.Asset:
                    return (left, right) => CompareStrings(left.AssetPath, right.AssetPath);

                case SortColumn.Object:
                    return (left, right) => CompareStrings(left.ObjectPath, right.ObjectPath);

                case SortColumn.Component:
                    return (left, right) => CompareStrings(left.ComponentName, right.ComponentName);

                case SortColumn.Property:
                    return (left, right) => CompareStrings(left.PropertyPath, right.PropertyPath);

                case SortColumn.PropertyType:
                    return (left, right) => CompareStrings(left.PropertyValueType, right.PropertyValueType);

                case SortColumn.Index:
                default:
                    return (left, right) => left.OriginalIndex.CompareTo(right.OriginalIndex);
            }
        }

        private static int CompareStrings(string left, string right)
        {
            return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSortColumnDisplayName(SortColumn column)
        {
            switch (column)
            {
                case SortColumn.Type:
                    return "Type";

                case SortColumn.Scope:
                    return "Scope";

                case SortColumn.Asset:
                    return "Asset";

                case SortColumn.Object:
                    return "Object";

                case SortColumn.Component:
                    return "Component";

                case SortColumn.Property:
                    return "Property";

                case SortColumn.PropertyType:
                    return "Prop Type";

                case SortColumn.Index:
                default:
                    return "#";
            }
        }

        private void RunSelectedScans()
        {
            Results.Clear();
            s_nextResultIndex = 0;

            try
            {
                if ((_selectedScopes & ScanScope.ProjectPrefabs) != 0) ScanAllPrefabsInProject();

                if ((_selectedScopes & ScanScope.ProjectScriptableObjects) != 0) ScanAllScriptableObjectsInProject();

                if ((_selectedScopes & ScanScope.OpenScene) != 0) ScanOpenSceneObjects();

                if ((_selectedScopes & ScanScope.OpenPrefabMode) != 0) ScanOpenPrefabModeObjects();

                SortResultsInternal();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Missing Reference Scanner finished. Found {Results.Count} issue(s).");
            Repaint();
        }

        private void ScanAllPrefabsInProject()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                float progress = prefabGuids.Length == 0 ? 1f : (float)i / prefabGuids.Length;

                EditorUtility.DisplayProgressBar("Scanning Prefabs", path, progress);

                GameObject prefabRoot = null;

                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    if (prefabRoot != null) ScanGameObjectHierarchy(prefabRoot, path, "Project Wide (Prefab)");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to scan prefab: {path}\n{ex}");
                }
                finally
                {
                    if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private void ScanAllScriptableObjectsInProject()
        {
            string[] scriptableObjectGuids = AssetDatabase.FindAssets("t:ScriptableObject");

            for (int i = 0; i < scriptableObjectGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(scriptableObjectGuids[i]);
                float progress = scriptableObjectGuids.Length == 0 ? 1f : (float)i / scriptableObjectGuids.Length;

                EditorUtility.DisplayProgressBar("Scanning ScriptableObjects", path, progress);

                try
                {
                    UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    for (int j = 0; j < assets.Length; j++)
                    {
                        if (assets[j] is ScriptableObject scriptableObject)
                        {
                            ScanSerializedObject(
                                scriptableObject,
                                path,
                                "Project Wide (ScriptableObject)",
                                scriptableObject.name,
                                scriptableObject);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to scan ScriptableObject asset: {path}\n{ex}");
                }
            }
        }

        private void ScanOpenSceneObjects()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("No valid open scene to scan.");
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                float progress = roots.Length == 0 ? 1f : (float)i / roots.Length;

                EditorUtility.DisplayProgressBar("Scanning Open Scene", roots[i].name, progress);
                ScanGameObjectHierarchy(roots[i], scene.path, "Open Scene");
            }
        }

        private void ScanOpenPrefabModeObjects()
        {
            UnityEditor.SceneManagement.PrefabStage prefabStage =
                UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || prefabStage.prefabContentsRoot == null)
            {
                Debug.LogWarning("No prefab is currently open in Prefab Mode.");
                return;
            }

            EditorUtility.DisplayProgressBar("Scanning Open Prefab", prefabStage.assetPath, 1f);
            ScanGameObjectHierarchy(prefabStage.prefabContentsRoot, prefabStage.assetPath, "Open Prefab (Prefab Mode)");
        }

        private static void ScanGameObjectHierarchy(GameObject root, string assetPath, string scope)
        {
            if (root == null) return;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject current = transforms[i].gameObject;
                string objectPath = GetHierarchyPath(current.transform);

                ScanMissingScripts(current, assetPath, scope, objectPath);

                Component[] components = current.GetComponents<Component>();
                for (int j = 0; j < components.Length; j++)
                {
                    Component component = components[j];
                    if (component == null) continue;

                    ScanSerializedObject(
                        component,
                        assetPath,
                        scope,
                        objectPath,
                        current);
                }
            }
        }

        private static void ScanMissingScripts(GameObject gameObject, string assetPath, string scope, string objectPath)
        {
            int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (missingScriptCount <= 0) return;

            AddResult(
                scope,
                assetPath,
                objectPath,
                "GameObject",
                "m_Component",
                "Missing Script",
                "Missing MonoBehaviour",
                gameObject);
        }

        private static void ScanSerializedObject(
            UnityEngine.Object target,
            string assetPath,
            string scope,
            string objectPath,
            UnityEngine.Object context)
        {
            if (target == null) return;

            SerializedObject serializedObject;

            try
            {
                serializedObject = new SerializedObject(target);
                serializedObject.UpdateIfRequiredOrScript();
            }
            catch
            {
                return;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                enterChildren = true;

                if (IsUnityEventTargetProperty(iterator))
                {
                    ScanUnityEventTargetProperty(serializedObject, iterator, assetPath, scope, objectPath, target,
                        context);
                    continue;
                }

                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;

                bool isMissingReference =
                    iterator.objectReferenceValue == null &&
                    iterator.objectReferenceInstanceIDValue != 0;

                if (!isMissingReference) continue;

                AddResult(
                    scope,
                    assetPath,
                    objectPath,
                    target.GetType().Name,
                    iterator.propertyPath,
                    "Missing Reference",
                    GetFriendlySerializedPropertyValueType(iterator),
                    context != null ? context : target);
            }
        }

        private static void ScanUnityEventTargetProperty(
            SerializedObject serializedObject,
            SerializedProperty targetProperty,
            string assetPath,
            string scope,
            string objectPath,
            UnityEngine.Object owner,
            UnityEngine.Object context)
        {
            if (serializedObject == null || targetProperty == null) return;

            const string targetSuffix = ".m_Target";

            if (!targetProperty.propertyPath.EndsWith(targetSuffix, StringComparison.Ordinal)) return;

            string callPrefix = targetProperty.propertyPath.Substring(
                0,
                targetProperty.propertyPath.Length - targetSuffix.Length);

            SerializedProperty methodNameProperty = serializedObject.FindProperty(callPrefix + ".m_MethodName");
            if (methodNameProperty == null || string.IsNullOrEmpty(methodNameProperty.stringValue)) return;

            SerializedProperty callStateProperty = serializedObject.FindProperty(callPrefix + ".m_CallState");
            if (callStateProperty != null && callStateProperty.intValue == 0) return;

            bool isMissingTarget =
                targetProperty.objectReferenceValue == null &&
                targetProperty.objectReferenceInstanceIDValue != 0;

            bool isNullTarget =
                targetProperty.objectReferenceValue == null &&
                targetProperty.objectReferenceInstanceIDValue == 0;

            if (!isMissingTarget && !isNullTarget) return;

            string eventPropertyPath = ExtractUnityEventPropertyPath(targetProperty.propertyPath);
            string issueType = isMissingTarget ? "UnityEvent Missing Target" : "UnityEvent Null Target";

            AddResult(
                scope,
                assetPath,
                objectPath,
                owner.GetType().Name,
                eventPropertyPath,
                issueType,
                "Object",
                context != null ? context : owner);
        }

        private static bool IsUnityEventTargetProperty(SerializedProperty property)
        {
            if (property == null) return false;

            if (property.propertyType != SerializedPropertyType.ObjectReference) return false;

            string path = property.propertyPath;
            if (string.IsNullOrEmpty(path)) return false;

            return path.Contains(".m_PersistentCalls.m_Calls.Array.data[", StringComparison.Ordinal) &&
                   path.EndsWith(".m_Target", StringComparison.Ordinal);
        }

        private static string ExtractUnityEventPropertyPath(string targetPath)
        {
            const string marker = ".m_PersistentCalls.m_Calls.Array.data[";
            int markerIndex = targetPath.IndexOf(marker, StringComparison.Ordinal);

            if (markerIndex <= 0) return targetPath;

            return targetPath.Substring(0, markerIndex);
        }

        private static string GetFriendlySerializedPropertyValueType(SerializedProperty property)
        {
            if (property == null) return string.Empty;

            string rawType = property.type;
            if (string.IsNullOrEmpty(rawType)) rawType = property.propertyType.ToString();

            if (string.IsNullOrEmpty(rawType)) return "<Unknown>";

            string prettyType = rawType;

            if (prettyType.StartsWith("PPtr<$", StringComparison.Ordinal) &&
                prettyType.EndsWith(">", StringComparison.Ordinal))
                prettyType = prettyType.Substring(6, prettyType.Length - 7);
            else if (prettyType.StartsWith("PPtr<", StringComparison.Ordinal) &&
                     prettyType.EndsWith(">", StringComparison.Ordinal))
                prettyType = prettyType.Substring(5, prettyType.Length - 6);

            prettyType = prettyType.Replace("$", string.Empty);
            prettyType = prettyType.Replace("`1", "<T>");
            prettyType = prettyType.Replace("`2", "<T1,T2>");

            switch (prettyType)
            {
                case "int":
                    return "Integer";

                case "long":
                    return "Long";

                case "bool":
                    return "Boolean";

                case "float":
                    return "Float";

                case "double":
                    return "Double";

                case "string":
                    return "String";

                case "Array":
                    return "Array";

                case "Generic":
                    return "Generic";

                case "Quaternionf":
                    return "Quaternion";

                case "Vector2f":
                    return "Vector2";

                case "Vector3f":
                    return "Vector3";

                case "Vector4f":
                    return "Vector4";

                case "Rectf":
                    return "Rect";

                case "ColorRGBA":
                    return "Color";

                case "BoundsInt":
                    return "BoundsInt";

                case "Vector2Int":
                    return "Vector2Int";

                case "Vector3Int":
                    return "Vector3Int";

                case "ExposedReference":
                    return "Exposed Reference";

                case "managedReference":
                    return "Managed Reference";

                default:
                    return prettyType;
            }
        }

        private static bool IsPrefabAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) return false;

            PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(asset);
            return prefabAssetType == PrefabAssetType.Regular || prefabAssetType == PrefabAssetType.Variant;
        }

        private static void OpenPrefabInPrefabMode(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabAsset == null) return;

            AssetDatabase.OpenAsset(prefabAsset);
            Selection.activeObject = prefabAsset;
            EditorGUIUtility.PingObject(prefabAsset);
        }

        private static void AddResult(
            string scope,
            string assetPath,
            string objectPath,
            string componentName,
            string propertyPath,
            string message,
            string propertyValueType,
            UnityEngine.Object context)
        {
            Results.Add(new ScanResult
            {
                OriginalIndex = s_nextResultIndex++,
                Scope = scope,
                AssetPath = assetPath,
                ObjectPath = objectPath,
                ComponentName = componentName,
                PropertyPath = propertyPath,
                PropertyValueType = propertyValueType,
                Message = message,
                Context = context
            });
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;

            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}