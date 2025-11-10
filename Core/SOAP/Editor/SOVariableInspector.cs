#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.SOAP.Editor
{
    /// <summary>
    /// Custom inspector for SOVariable<T> (via SOVariableBase).
    /// Shows Global (runtime/initial) and lists ALL usages (direct or via SOReference<T>)
    /// across open scenes and project assets/prefabs. Traverses referenced ScriptableObjects recursively.
    /// </summary>
    [CustomEditor(typeof(SOVariable<>), true)]
    public class SOVariableInspector : UnityEditor.Editor
    {
        // Scan configuration
        private bool _scanOpenScenes = true;
        private bool _scanAssetsAndPrefabs = true;
        private int _maxDepth = 3; // Graph traversal depth limit

        private Vector2 _scroll;
        private readonly List<Usage> _usages = new();

        // Cached reflection for runtime Value property
        private PropertyInfo _valuePi;
        private Type _cachedType;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Global (Runtime) Value", EditorStyles.boldLabel);
                DrawGlobalRuntimeAndInitial();
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Usages (Direct & via SOReference<T>)", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _scanOpenScenes = EditorGUILayout.ToggleLeft(new GUIContent("Open Scenes"), _scanOpenScenes,
                        GUILayout.Width(120));
                    _scanAssetsAndPrefabs = EditorGUILayout.ToggleLeft(new GUIContent("Assets/Prefabs"),
                        _scanAssetsAndPrefabs, GUILayout.Width(140));
                    EditorGUILayout.LabelField("Depth", GUILayout.MaxWidth(40));
                    _maxDepth = Mathf.Clamp(EditorGUILayout.IntField(_maxDepth, GUILayout.MaxWidth(40)), 1, 8);

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Scan", GUILayout.Width(90)))
                        ScanUsages(_scanOpenScenes, _scanAssetsAndPrefabs, _maxDepth);
                }

                EditorGUILayout.Space(4);

                if (_usages.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No usages found. Click Scan to search in open scenes and/or assets.\nThis includes indirect references (e.g., MonoBehaviour -> GameConfig -> SOVariable).",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.LabelField($"{_usages.Count} usage(s) found.");
                    EditorGUILayout.Space(2);

                    _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));
                    foreach (Usage u in _usages)
                    {
                        DrawUsageCard(u);
                        EditorGUILayout.Space(6);
                    }

                    EditorGUILayout.EndScrollView();
                }
            }
        }

        // ----------------- Global (runtime + initial) -----------------

        private void DrawGlobalRuntimeAndInitial()
        {
            EnsureValuePropertyInfo();

            // InitialValue is serialized; Value is runtime (non-serialized).
            SerializedProperty initialProp = serializedObject.FindProperty("_initialValue");
            string runtimeStr = ReadRuntimeValueString();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(new GUIContent("Runtime Value"), runtimeStr);
                if (initialProp != null)
                    EditorGUILayout.PropertyField(initialProp, new GUIContent("Initial Value"), true);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Runtime to Initial"))
                {
                    Undo.RecordObject(target, "Reset Runtime");
                    MethodInfo mi = target.GetType()
                        .GetMethod("ResetToInitial", BindingFlags.Instance | BindingFlags.Public);
                    mi?.Invoke(target, null);
                    EditorUtility.SetDirty(target);
                    Repaint();
                }

                if (GUILayout.Button("Refresh")) Repaint();
            }
        }

        private void EnsureValuePropertyInfo()
        {
            Type t = target.GetType();
            if (_cachedType == t && _valuePi != null) return;

            _cachedType = t;
            _valuePi = t.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        }

        private string ReadRuntimeValueString()
        {
            try
            {
                if (_valuePi == null) return "—";
                object rv = _valuePi.GetValue(target, null);
                return ToReadable(rv);
            }
            catch
            {
                return "—";
            }
        }

        // ----------------- Scanning entrypoints -----------------

        private void ScanUsages(bool scanScenes, bool scanAssets, int maxDepth)
        {
            _usages.Clear();

            if (scanScenes)
                ScanOpenScenes(maxDepth);

            if (scanAssets)
                ScanProjectAssetsAndPrefabs(maxDepth);

            _usages.Sort((a, b) => string.CompareOrdinal(a.OwnerName, b.OwnerName));
        }

        private void ScanOpenScenes(int maxDepth)
        {
            IEnumerable<MonoBehaviour> monos = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(m => m != null &&
                            !EditorUtility.IsPersistent(m) &&
                            m.gameObject.scene.IsValid());

            foreach (MonoBehaviour m in monos)
            {
                try
                {
                    TraverseObjectGraph(
                        m,
                        m.name,
                        m.gameObject,
                        m,
                        maxDepth
                    );
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        private void ScanProjectAssetsAndPrefabs(int maxDepth)
        {
            string varPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(varPath)) return;

            // Filter to ScriptableObjects and Prefabs only (faster than scanning the entire project).
            string[] allGuids = AssetDatabase.FindAssets("t:ScriptableObject t:Prefab");
            foreach (string g in allGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path)) continue;

                // Skip self asset path to reduce noise
                if (path == varPath) continue;

                Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main == null) continue;

                // Prefab asset → traverse its components (read-only)
                if (PrefabUtility.IsPartOfPrefabAsset(main))
                {
                    ScanPrefabFile(path, maxDepth);
                    continue;
                }

                // ScriptableObjects at this path (main + subassets)
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (Object obj in assets)
                {
                    if (obj is ScriptableObject so)
                        TraverseObjectGraph(
                            so,
                            so.name,
                            so,
                            null,
                            maxDepth
                        );
                }
            }
        }

        // Prefab scanning via LoadPrefabContents (read-only)
        private void ScanPrefabFile(string path, int maxDepth)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                MonoBehaviour[] monos = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour m in monos)
                {
                    if (m == null) continue;
                    TraverseObjectGraph(
                        m,
                        $"{root.name} (Prefab)",
                        root,
                        m,
                        maxDepth
                    );
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ----------------- Graph traversal -----------------

        /// <summary>
        /// Traverses object graph starting at 'root', following object-reference fields (SerializedProperty),
        /// up to 'maxDepth'. Records:
        /// - Direct references to the target SOVariable (field points to it);
        /// - SOReference<T> blocks where ".Variable" points to it (with scope breakdown).
        /// </summary>
        private void TraverseObjectGraph(Object root, string ownerName, Object pingTarget, Component componentContext,
            int maxDepth)
        {
            if (root == null) return;

            HashSet<int> visited = new();
            Queue<Node> queue = new();
            queue.Enqueue(new Node(root, 0, string.Empty, ownerName, pingTarget, componentContext));

            while (queue.Count > 0)
            {
                Node node = queue.Dequeue();
                if (node.Obj == null) continue;

                int id = node.Obj.GetInstanceID();
                if (visited.Contains(id)) continue;
                visited.Add(id);

                SerializedObject sObj = new(node.Obj);
                SerializedProperty it = sObj.GetIterator();
                bool enterChildren = true;

                while (it.Next(enterChildren))
                {
                    enterChildren = true;

                    if (it.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    Object refObj = it.objectReferenceValue;
                    if (refObj == null) continue;

                    // Case A: property points directly to our SOVariable asset
                    if (ReferenceEquals(refObj, target))
                    {
                        RegisterUsageForProperty(sObj, it, node);
                        continue;
                    }

                    // Case B: follow object reference if depth allows
                    if (node.Depth + 1 <= maxDepth)
                    {
                        if (refObj is ScriptableObject so)
                        {
                            queue.Enqueue(new Node(so, node.Depth + 1, it.propertyPath, node.OwnerName, node.PingTarget,
                                node.ComponentCtx));
                            continue;
                        }

                        if (refObj is Component comp)
                        {
                            queue.Enqueue(new Node(comp, node.Depth + 1, it.propertyPath, node.OwnerName,
                                comp.gameObject, comp));
                            continue;
                        }

                        if (refObj is GameObject go)
                            foreach (Component comp2 in go.GetComponents<Component>())
                            {
                                if (comp2 == null) continue;
                                queue.Enqueue(new Node(comp2, node.Depth + 1, it.propertyPath, node.OwnerName, go,
                                    comp2));
                            }
                    }
                }
            }
        }

        /// <summary>
        /// Registers a usage entry for a property that references the target variable.
        /// If it's part of an SOReference (ends with ".Variable"), extracts scope info; otherwise, marks as Direct.
        /// </summary>
        private void RegisterUsageForProperty(SerializedObject sObj, SerializedProperty prop, Node node)
        {
            string basePath = prop.propertyPath;
            int idx = basePath.LastIndexOf(".Variable", StringComparison.Ordinal);
            bool isSOReference = idx > 0;
            if (isSOReference)
                basePath = basePath.Substring(0, idx);

            Usage usage = new()
            {
                OwnerName = node.OwnerName,
                OwnerPingTarget = node.PingTarget,
                Component = node.ComponentCtx,
                FieldLabel = NicifyFieldLabel(basePath,
                    node.ComponentCtx != null ? node.ComponentCtx.GetType().Name : node.OwnerName),
                Kind = isSOReference ? UsageKind.SOReference : UsageKind.Direct
            };

            if (isSOReference)
            {
                SerializedProperty useConstant = sObj.FindProperty(basePath + ".UseConstant");
                SerializedProperty constantVal = sObj.FindProperty(basePath + ".ConstantValue");

                SerializedProperty useScoped = sObj.FindProperty(basePath + ".UseScoped");
                SerializedProperty useSession = sObj.FindProperty(basePath + ".UseSessionOverride");
                SerializedProperty sessionValue = sObj.FindProperty(basePath + ".SessionValue");
                SerializedProperty useScene = sObj.FindProperty(basePath + ".UseSceneOverride");
                SerializedProperty sceneValue = sObj.FindProperty(basePath + ".SceneValue");
                SerializedProperty usePrefab = sObj.FindProperty(basePath + ".UsePrefabOverride");
                SerializedProperty prefabValue = sObj.FindProperty(basePath + ".PrefabValue");

                usage.HasGlobal = useConstant != null;
                usage.GlobalConstantValueString = constantVal != null ? PropertyToReadable(constantVal) : null;
                usage.GlobalVariableValueString = ReadRuntimeValueString();

                usage.HasSession = useSession != null;
                usage.SessionValueString = sessionValue != null ? PropertyToReadable(sessionValue) : null;
                usage.HasScene = useScene != null;
                usage.SceneValueString = sceneValue != null ? PropertyToReadable(sceneValue) : null;
                usage.HasPrefab = usePrefab != null;
                usage.PrefabValueString = prefabValue != null ? PropertyToReadable(prefabValue) : null;

                usage.ActiveScope = ResolveActiveScope(useScoped, usePrefab, useScene, useSession);
                usage.EffectiveValueString = ComputeEffectiveValueString(usage, useConstant);
            }
            else
            {
                usage.HasGlobal = true;
                usage.GlobalConstantValueString = null; // N/A
                usage.GlobalVariableValueString = ReadRuntimeValueString();
                usage.ActiveScope = VariableScope.Global;
                usage.EffectiveValueString = usage.GlobalVariableValueString;
            }

            _usages.Add(usage);
        }

        // ----------------- UI helpers -----------------

        private void DrawUsageCard(Usage u)
        {
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(u.Kind == UsageKind.SOReference
                            ? EditorGUIUtility.IconContent("d_ScriptableObject Icon").image
                            : EditorGUIUtility.IconContent("ScriptableObject Icon").image, GUILayout.Width(16),
                        GUILayout.Height(16));

                    EditorGUILayout.LabelField($"{u.OwnerName}  •  {u.ComponentName}  •  {u.FieldLabel}",
                        GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                        if (u.OwnerPingTarget != null)
                        {
                            EditorGUIUtility.PingObject(u.OwnerPingTarget);
                            Selection.activeObject = u.OwnerPingTarget;
                        }
                }

                EditorGUILayout.LabelField("Reference Kind", u.Kind.ToString());
                EditorGUILayout.LabelField("Active Scope", u.ActiveScope.ToString());

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Effective Value", u.EffectiveValueString ?? "—");
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Values", EditorStyles.miniBoldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    if (u.HasGlobal)
                    {
                        if (u.Kind == UsageKind.SOReference)
                            EditorGUILayout.TextField("Global (Constant)", u.GlobalConstantValueString ?? "—");
                        EditorGUILayout.TextField("Global (Variable)", u.GlobalVariableValueString ?? "—");
                    }

                    if (u.HasSession)
                        EditorGUILayout.TextField("Session", u.SessionValueString ?? "—");
                    if (u.HasScene)
                        EditorGUILayout.TextField("Scene", u.SceneValueString ?? "—");
                    if (u.HasPrefab)
                        EditorGUILayout.TextField("Prefab", u.PrefabValueString ?? "—");
                }
            }
        }

        // ----------------- Small helpers -----------------

        private static VariableScope ResolveActiveScope(SerializedProperty useScoped, SerializedProperty usePrefab,
            SerializedProperty useScene, SerializedProperty useSession)
        {
            bool scoped = useScoped != null && useScoped.boolValue;
            if (scoped)
            {
                if (usePrefab != null && usePrefab.boolValue) return VariableScope.Prefab;
                if (useScene != null && useScene.boolValue) return VariableScope.Scene;
                if (useSession != null && useSession.boolValue) return VariableScope.Session;
            }

            return VariableScope.Global;
        }

        private string ComputeEffectiveValueString(Usage u, SerializedProperty useConstant)
        {
            switch (u.ActiveScope)
            {
                case VariableScope.Prefab: return u.PrefabValueString ?? "—";
                case VariableScope.Scene: return u.SceneValueString ?? "—";
                case VariableScope.Session: return u.SessionValueString ?? "—";
                case VariableScope.Global:
                default:
                    if (u.Kind == UsageKind.SOReference && useConstant != null && useConstant.boolValue)
                        return u.GlobalConstantValueString ?? "—";
                    return u.GlobalVariableValueString ?? "—";
            }
        }

        private static string NicifyFieldLabel(string basePath, string ownerLabel)
        {
            int lastDot = basePath.LastIndexOf('.');
            string tail = lastDot >= 0 ? basePath.Substring(lastDot + 1) : basePath;
            return ObjectNames.NicifyVariableName(tail);
        }

        private static string PropertyToReadable(SerializedProperty p)
        {
            if (p == null) return "—";

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Float: return p.floatValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.String: return p.stringValue ?? string.Empty;
                case SerializedPropertyType.Color: return p.colorValue.ToString();
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString("F3");
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString("F3");
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString("F3");
#if UNITY_2020_1_OR_NEWER
                case SerializedPropertyType.Quaternion:
                    return p.quaternionValue.eulerAngles.ToString("F1") + " (euler)";
#endif
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue ? p.objectReferenceValue.name : "null";
                case SerializedPropertyType.Enum:
                    return p.enumDisplayNames != null && p.enumValueIndex >= 0 &&
                           p.enumValueIndex < p.enumDisplayNames.Length
                        ? p.enumDisplayNames[p.enumValueIndex]
                        : p.enumValueIndex.ToString();
                case SerializedPropertyType.Rect: return p.rectValue.ToString();
                case SerializedPropertyType.Bounds: return p.boundsValue.ToString();
                case SerializedPropertyType.RectInt: return p.rectIntValue.ToString();
                case SerializedPropertyType.BoundsInt: return p.boundsIntValue.ToString();
                case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
                default: return "(unsupported)";
            }
        }

        private static string ToReadable(object v)
        {
            if (v == null) return "null";
            if (v is string s) return s;
            if (v is bool b) return b ? "true" : "false";
            if (v is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (v is Color col) return col.ToString();
            if (v is Vector2 v2) return v2.ToString("F3");
            if (v is Vector3 v3) return v3.ToString("F3");
            if (v is Quaternion q) return q.eulerAngles.ToString("F1") + " (euler)";
            if (v is Object o) return o ? o.name : "null";
            return v.ToString();
        }

        // ----------------- Models -----------------

        private enum UsageKind
        {
            Direct,
            SOReference
        }

        private sealed class Usage
        {
            public string OwnerName;
            public Object OwnerPingTarget;
            public Component Component;
            public string ComponentName => Component != null ? Component.GetType().Name : "(Asset)";
            public string FieldLabel;
            public UsageKind Kind;

            public bool HasGlobal;
            public string GlobalConstantValueString;
            public string GlobalVariableValueString;

            public bool HasSession;
            public string SessionValueString;

            public bool HasScene;
            public string SceneValueString;

            public bool HasPrefab;
            public string PrefabValueString;

            public VariableScope ActiveScope;
            public string EffectiveValueString;
        }

        private sealed class Node
        {
            public Object Obj;
            public int Depth;
            public string ParentPath;
            public string OwnerName;
            public Object PingTarget;
            public Component ComponentCtx;

            public Node(Object obj, int depth, string parentPath, string ownerName, Object pingTarget,
                Component componentCtx)
            {
                Obj = obj;
                Depth = depth;
                ParentPath = parentPath;
                OwnerName = ownerName;
                PingTarget = pingTarget;
                ComponentCtx = componentCtx;
            }
        }
    }
}
#endif