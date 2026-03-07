using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Backend for "Finder • By Serialized Field".
    /// Runs entirely on the Editor main thread via EditorApplication.update stepping.
    /// </summary>
    public sealed class AssetUsageFinderSerializedFieldFinderBackend
    {
        public sealed class Match
        {
            public string FileAssetPath { get; }
            public string ObjectPath { get; }
            public string ObjectTypeName { get; }
            public string PropertyPath { get; }
            public string CurrentValue { get; }

            public Match(string fileAssetPath, string objectPath, string objectTypeName, string propertyPath,
                string currentValue)
            {
                FileAssetPath = fileAssetPath;
                ObjectPath = objectPath;
                ObjectTypeName = objectTypeName;
                PropertyPath = propertyPath;
                CurrentValue = currentValue;
            }
        }

        private static readonly string[] SupportedExtensions =
        {
            ".asset", ".mat", ".controller", ".anim", ".overrideController", ".shader", ".compute", ".playable",
            ".prefab", ".unity"
        };

        private ScanSession _session;

        /// <summary>
        /// Starts a scan. If a scan is already running, it will be canceled and replaced.
        /// This method must be called from the main thread (Editor).
        /// </summary>
        public void StartScan(
            IReadOnlyList<SerializedFieldFilterRow> filters,
            AssetUsageFinderSearchScope searchScope,
            Action<float, string> progressCallback,
            Action<List<Match>> completedCallback,
            Action canceledCallback,
            Action<Exception> errorCallback,
            CancellationToken cancellationToken)
        {
            CancelScan();

            if (filters == null || filters.Count == 0)
            {
                completedCallback?.Invoke(new List<Match>());
                return;
            }

            List<SerializedFieldFilterRow> normalized = filters
                .Where(f => f != null)
                .Select(CloneAndNormalize)
                .ToList();

            if (normalized.Count == 0)
            {
                completedCallback?.Invoke(new List<Match>());
                return;
            }

            List<AssetUsageFinderScopeTarget> targets =
                AssetUsageFinderSearchScopeUtility.CollectTargets(searchScope, SupportedExtensions);

            _session = new ScanSession(
                normalized,
                targets,
                progressCallback,
                completedCallback,
                canceledCallback,
                errorCallback,
                cancellationToken);

            EditorApplication.update += _session.Tick;
        }

        public void CancelScan()
        {
            if (_session == null)
                return;

            _session.RequestCancel();
            // Session will detach itself on next Tick.
        }

        private sealed class ScanSession
        {
            private readonly List<SerializedFieldFilterRow> _filters;
            private readonly List<AssetUsageFinderScopeTarget> _targets;
            private readonly Action<float, string> _progress;
            private readonly Action<List<Match>> _completed;
            private readonly Action _canceled;
            private readonly Action<Exception> _error;
            private readonly CancellationToken _token;

            private readonly List<Match> _results = new();

            private int _index;
            private bool _cancelRequested;

            // Remember active scene to restore best-effort.
            private readonly Scene _activeScene;

            public ScanSession(
                List<SerializedFieldFilterRow> filters,
                List<AssetUsageFinderScopeTarget> targets,
                Action<float, string> progress,
                Action<List<Match>> completed,
                Action canceled,
                Action<Exception> error,
                CancellationToken token)
            {
                _filters = filters;
                _targets = targets ?? new List<AssetUsageFinderScopeTarget>();
                _progress = progress;
                _completed = completed;
                _canceled = canceled;
                _error = error;
                _token = token;

                _activeScene = EditorSceneManager.GetActiveScene();
            }

            public void RequestCancel()
            {
                _cancelRequested = true;
            }

            public void Tick()
            {
                try
                {
                    if (_cancelRequested || _token.IsCancellationRequested)
                    {
                        CleanupAndDetach();
                        RestoreActiveSceneBestEffort();
                        _canceled?.Invoke();
                        return;
                    }

                    if (_targets.Count == 0)
                    {
                        CleanupAndDetach();
                        RestoreActiveSceneBestEffort();
                        _completed?.Invoke(_results);
                        return;
                    }

                    // Process a small budget per frame to keep Editor responsive.
                    // Tune this if needed.
                    const int targetsPerTick = 2;

                    int processedThisTick = 0;
                    while (processedThisTick < targetsPerTick && _index < _targets.Count)
                    {
                        if (_cancelRequested || _token.IsCancellationRequested)
                            break;

                        AssetUsageFinderScopeTarget target = _targets[_index++];
                        processedThisTick++;

                        float p = Mathf.Clamp01(_targets.Count == 0 ? 1f : (float)_index / _targets.Count);
                        _progress?.Invoke(p, $"Scanning {target.GetProgressLabel()}");

                        ScanSingleTarget(target);
                    }

                    if (_cancelRequested || _token.IsCancellationRequested)
                    {
                        CleanupAndDetach();
                        RestoreActiveSceneBestEffort();
                        _canceled?.Invoke();
                        return;
                    }

                    if (_index >= _targets.Count)
                    {
                        CleanupAndDetach();
                        RestoreActiveSceneBestEffort();
                        _completed?.Invoke(_results);
                    }
                }
                catch (Exception ex)
                {
                    CleanupAndDetach();
                    RestoreActiveSceneBestEffort();
                    _error?.Invoke(ex);
                }
            }

            private void CleanupAndDetach()
            {
                EditorApplication.update -= Tick;
            }

            private void RestoreActiveSceneBestEffort()
            {
                if (!_activeScene.IsValid() || !_activeScene.isLoaded)
                    return;

                Scene current = EditorSceneManager.GetActiveScene();
                if (current == _activeScene)
                    return;

                try
                {
                    EditorSceneManager.SetActiveScene(_activeScene);
                }
                catch
                {
                    // ignore
                }
            }

            private void ScanSingleTarget(AssetUsageFinderScopeTarget target)
            {
                if (target == null)
                    return;

                if (target.Kind == AssetUsageFinderScopeTargetKind.OpenScene)
                {
                    ScanOpenScene(target.AssetPath);
                    return;
                }

                if (target.Kind == AssetUsageFinderScopeTargetKind.OpenPrefabStage)
                {
                    ScanOpenPrefabStage(target.AssetPath);
                    return;
                }

                string assetPath = target.AssetPath;
                string ext = Path.GetExtension(assetPath);

                if (ext.Equals(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    ScanScene(assetPath, false);
                    return;
                }

                if (ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    ScanPrefab(assetPath);
                    return;
                }

                ScanAssetFile(assetPath);
            }

            private void ScanAssetFile(string assetPath)
            {
                Object[] objs = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (objs == null || objs.Length == 0)
                    return;

                foreach (Object obj in objs)
                {
                    if (obj == null) continue;
                    ScanSerializedObject(assetPath, obj, obj.name);
                }
            }

            private void ScanPrefab(string prefabPath)
            {
                GameObject root = null;

                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    if (root == null) return;

                    ScanGameObjectHierarchy(prefabPath, root);
                }
                finally
                {
                    if (root != null)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }

            private void ScanScene(string scenePath, bool useLoadedSceneOnly)
            {
                if (!File.Exists(scenePath))
                    return;

                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool openedHere = !scene.IsValid() || !scene.isLoaded;

                try
                {
                    if (useLoadedSceneOnly && openedHere)
                        return;

                    if (openedHere)
                        scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                    if (!scene.IsValid() || !scene.isLoaded)
                        return;

                    foreach (GameObject go in scene.GetRootGameObjects())
                    {
                        if (go == null) continue;
                        ScanGameObjectHierarchy(scenePath, go);
                    }
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
                            // ignore
                        }
                    }
                }
            }

            private void ScanOpenScene(string sceneLabel)
            {
                Scene scene = EditorSceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                    return;

                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    if (go == null)
                        continue;

                    ScanGameObjectHierarchy(sceneLabel, go);
                }
            }

            private void ScanOpenPrefabStage(string prefabPath)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null || stage.prefabContentsRoot == null)
                    return;

                if (!string.Equals(stage.assetPath, prefabPath, StringComparison.OrdinalIgnoreCase))
                    return;

                ScanGameObjectHierarchy(prefabPath, stage.prefabContentsRoot);
            }

        private void ScanGameObjectHierarchy(string fileAssetPath, GameObject root)
        {
            if (root == null) return;

            string goPath = GetHierarchyPath(root);
            ScanSerializedObject(fileAssetPath, root, goPath);

            Component[] comps = root.GetComponents<Component>();
            foreach (Component comp in comps)
            {
                if (comp == null) continue;

                string objPath = $"{goPath} ({comp.GetType().Name})";
                ScanSerializedObject(fileAssetPath, comp, objPath);
            }

                foreach (Transform child in root.transform)
                {
                    if (child == null) continue;
                    ScanGameObjectHierarchy(fileAssetPath, child.gameObject);
                }
            }

            private void ScanSerializedObject(string fileAssetPath, Object obj, string objectPath)
            {
                SerializedObject so;

                try
                {
                    so = new SerializedObject(obj);
                }
                catch
                {
                    return;
                }

                // For each filter row, gather matches.
                List<List<(SerializedProperty prop, string valueStr)>> rowMatches = new();
                List<bool> rowBools = new();

                for (int i = 0; i < _filters.Count; i++)
                {
                    SerializedFieldFilterRow row = _filters[i];
                    List<(SerializedProperty prop, string valueStr)> matches = FindMatchingProperties(so, row);
                    rowMatches.Add(matches);
                    rowBools.Add(matches.Count > 0);
                }

                bool include = CombineRowBools(_filters, rowBools);
                if (!include)
                    return;

                // If included, emit all matched properties (across all rows).
                for (int i = 0; i < _filters.Count; i++)
                {
                    foreach ((SerializedProperty prop, string valueStr) in rowMatches[i])
                    {
                        _results.Add(new Match(
                            fileAssetPath,
                            objectPath,
                            obj.GetType().FullName,
                            prop.propertyPath,
                            valueStr));
                    }
                }
            }
        }

        private static SerializedFieldFilterRow CloneAndNormalize(SerializedFieldFilterRow row)
        {
            row.EnsureDefaults();

            SerializedFieldFilterRow clone = new()
            {
                Expanded = row.Expanded,
                JoinWithPrevious = row.JoinWithPrevious,
                TypeQuery = row.TypeQuery ?? string.Empty,
                ValueType = row.ValueType ?? typeof(string),
                Collection = row.Collection,
                Comparison = row.Comparison,
                Value = row.Value ?? new SerializedFieldValueBox()
            };

            if (clone.Value.StringValue == null)
                clone.Value.StringValue = string.Empty;

            if (clone.IsCollection)
                clone.Comparison = FieldComparison.Contains;

            return clone;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;

            string path = go.name;
            Transform t = go.transform;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
        }

        private static bool CombineRowBools(IReadOnlyList<SerializedFieldFilterRow> filters,
            IReadOnlyList<bool> rowBools)
        {
            if (filters.Count == 0) return false;
            if (rowBools.Count != filters.Count) return false;

            bool acc = rowBools[0];

            for (int i = 1; i < filters.Count; i++)
            {
                LogicalOperator op = filters[i].JoinWithPrevious;
                bool val = rowBools[i];

                acc = op == LogicalOperator.And ? acc && val : acc || val;
            }

            return acc;
        }

        private static List<(SerializedProperty prop, string valueStr)> FindMatchingProperties(
            SerializedObject so,
            SerializedFieldFilterRow row)
        {
            List<(SerializedProperty prop, string valueStr)> matches = new();

            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (it.propertyPath == "m_Script")
                    continue;

                if (row.IsCollection)
                {
                    if (!it.isArray || it.propertyType == SerializedPropertyType.String)
                        continue;

                    if (PropertyArrayContains(it, row, out string currentValue))
                        matches.Add((it.Copy(), currentValue));

                    continue;
                }

                if (!DoesPropertyTypeMatchRow(it, row.EffectiveValueType))
                    continue;

                if (EvaluateScalarProperty(it, row, out string scalarValue))
                    matches.Add((it.Copy(), scalarValue));
            }

            return matches;
        }

        private static bool PropertyArrayContains(SerializedProperty arrayProp, SerializedFieldFilterRow row,
            out string currentValue)
        {
            currentValue = $"Size={arrayProp.arraySize}";
            int size = arrayProp.arraySize;

            for (int i = 0; i < size; i++)
            {
                SerializedProperty el = arrayProp.GetArrayElementAtIndex(i);
                if (el == null) continue;

                if (!DoesPropertyTypeMatchRow(el, row.EffectiveValueType))
                    continue;

                SerializedFieldFilterRow scalarRow = new()
                {
                    ValueType = row.EffectiveValueType,
                    Collection = CollectionKind.None,
                    Comparison = FieldComparison.Equals,
                    Value = row.Value
                };
                scalarRow.EnsureDefaults();

                if (EvaluateScalarProperty(el, scalarRow, out string elValue))
                {
                    currentValue = $"[{i}]={elValue}";
                    return true;
                }
            }

            return false;
        }

        private static bool DoesPropertyTypeMatchRow(SerializedProperty prop, Type desiredType)
        {
            if (desiredType == null) return true;

            if (typeof(Object).IsAssignableFrom(desiredType))
                return prop.propertyType == SerializedPropertyType.ObjectReference;

            if (desiredType.IsEnum)
                return prop.propertyType == SerializedPropertyType.Enum;

            if (desiredType == typeof(bool))
                return prop.propertyType == SerializedPropertyType.Boolean;

            if (desiredType == typeof(string))
                return prop.propertyType == SerializedPropertyType.String;

            if (desiredType == typeof(int) || desiredType == typeof(short) || desiredType == typeof(byte))
                return prop.propertyType == SerializedPropertyType.Integer;

            if (desiredType == typeof(long) || desiredType == typeof(uint) || desiredType == typeof(ulong))
                return prop.propertyType == SerializedPropertyType.Integer;

            if (desiredType == typeof(float) || desiredType == typeof(double))
                return prop.propertyType == SerializedPropertyType.Float;

            if (desiredType == typeof(Vector2))
                return prop.propertyType == SerializedPropertyType.Vector2;

            if (desiredType == typeof(Vector3))
                return prop.propertyType == SerializedPropertyType.Vector3;

            if (desiredType == typeof(Vector4))
                return prop.propertyType == SerializedPropertyType.Vector4;

            if (desiredType == typeof(Color))
                return prop.propertyType == SerializedPropertyType.Color;

            if (desiredType == typeof(Rect))
                return prop.propertyType == SerializedPropertyType.Rect;

            if (desiredType == typeof(Bounds))
                return prop.propertyType == SerializedPropertyType.Bounds;

            if (desiredType == typeof(AnimationCurve))
                return prop.propertyType == SerializedPropertyType.AnimationCurve;

            if (desiredType == typeof(Quaternion))
                return prop.propertyType == SerializedPropertyType.Vector4;

            return false;
        }

        private static bool EvaluateScalarProperty(SerializedProperty prop, SerializedFieldFilterRow row,
            out string currentValue)
        {
            currentValue = string.Empty;

            Type t = row.EffectiveValueType ?? typeof(string);
            FieldComparison cmp = row.EffectiveComparison;
            SerializedFieldValueBox box = row.Value ?? new SerializedFieldValueBox();

            try
            {
                if (typeof(Object).IsAssignableFrom(t))
                {
                    Object cur = prop.objectReferenceValue;
                    Object target = box.ObjectValue;

                    currentValue = cur != null ? AssetDatabase.GetAssetPath(cur) : "null";

                    bool eq = cur == target;
                    return cmp switch
                    {
                        FieldComparison.Equals => eq,
                        FieldComparison.NotEquals => !eq,
                        _ => eq
                    };
                }

                if (t.IsEnum)
                {
                    string curName = prop.enumDisplayNames != null && prop.enumValueIndex >= 0 &&
                                     prop.enumValueIndex < prop.enumDisplayNames.Length
                        ? prop.enumDisplayNames[prop.enumValueIndex]
                        : prop.enumValueIndex.ToString();

                    currentValue = curName;

                    string targetName = !string.IsNullOrEmpty(box.EnumName) ? box.EnumName : string.Empty;

                    return cmp switch
                    {
                        FieldComparison.Equals => string.Equals(curName, targetName, StringComparison.Ordinal),
                        FieldComparison.NotEquals => !string.Equals(curName, targetName, StringComparison.Ordinal),
                        FieldComparison.Contains => curName.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >=
                                                    0,
                        FieldComparison.StartsWith =>
                            curName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.EndsWith => curName.EndsWith(targetName, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.Regex => SafeRegexIsMatch(curName, targetName),
                        _ => false
                    };
                }

                if (t == typeof(bool))
                {
                    bool cur = prop.boolValue;
                    bool target = box.BoolValue;
                    currentValue = cur ? "true" : "false";

                    return cmp switch
                    {
                        FieldComparison.Equals => cur == target,
                        FieldComparison.NotEquals => cur != target,
                        _ => false
                    };
                }

                if (t == typeof(string))
                {
                    string cur = prop.stringValue ?? string.Empty;
                    string target = box.StringValue ?? string.Empty;
                    currentValue = cur;

                    return cmp switch
                    {
                        FieldComparison.Equals => string.Equals(cur, target, StringComparison.Ordinal),
                        FieldComparison.NotEquals => !string.Equals(cur, target, StringComparison.Ordinal),
                        FieldComparison.Contains => cur.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0,
                        FieldComparison.StartsWith => cur.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.EndsWith => cur.EndsWith(target, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.Regex => SafeRegexIsMatch(cur, target),
                        _ => false
                    };
                }

                if (t == typeof(int) || t == typeof(short) || t == typeof(byte))
                {
                    long cur = prop.longValue;
                    long target = box.IntValue;
                    currentValue = cur.ToString();
                    return CompareNumbers(cur, target, cmp);
                }

                if (t == typeof(long) || t == typeof(uint) || t == typeof(ulong))
                {
                    long cur = prop.longValue;
                    long target = box.LongValue;
                    currentValue = cur.ToString();
                    return CompareNumbers(cur, target, cmp);
                }

                if (t == typeof(float))
                {
                    double cur = prop.floatValue;
                    double target = box.FloatValue;
                    currentValue = cur.ToString("R");
                    return CompareNumbers(cur, target, cmp);
                }

                if (t == typeof(double))
                {
                    double cur = prop.doubleValue;
                    double target = box.DoubleValue;
                    currentValue = cur.ToString("R");
                    return CompareNumbers(cur, target, cmp);
                }

                if (t == typeof(Vector2))
                {
                    Vector2 cur = prop.vector2Value;
                    Vector2 target = box.Vector2Value;
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur == target,
                        FieldComparison.NotEquals => cur != target,
                        _ => false
                    };
                }

                if (t == typeof(Vector3))
                {
                    Vector3 cur = prop.vector3Value;
                    Vector3 target = box.Vector3Value;
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur == target,
                        FieldComparison.NotEquals => cur != target,
                        _ => false
                    };
                }

                if (t == typeof(Vector4))
                {
                    Vector4 cur = prop.vector4Value;
                    Vector4 target = box.Vector4Value;
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur == target,
                        FieldComparison.NotEquals => cur != target,
                        _ => false
                    };
                }

                if (t == typeof(Color))
                {
                    Color cur = prop.colorValue;
                    Color target = box.ColorValue;
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur.Equals(target),
                        FieldComparison.NotEquals => !cur.Equals(target),
                        _ => false
                    };
                }

                if (t == typeof(Rect))
                {
                    Rect cur = prop.rectValue;
                    Rect target = box.RectValue;
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur.Equals(target),
                        FieldComparison.NotEquals => !cur.Equals(target),
                        _ => false
                    };
                }

                if (t == typeof(Bounds))
                {
                    Bounds cur = prop.boundsValue;
                    Bounds target = box.BoundsValue;
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur.Equals(target),
                        FieldComparison.NotEquals => !cur.Equals(target),
                        _ => false
                    };
                }

                if (t == typeof(AnimationCurve))
                {
                    AnimationCurve cur = prop.animationCurveValue;
                    AnimationCurve target = box.CurveValue;
                    currentValue = cur != null ? $"Keys={cur.length}" : "null";

                    bool eq = CurvesRoughlyEqual(cur, target);
                    return cmp switch
                    {
                        FieldComparison.Equals => eq,
                        FieldComparison.NotEquals => !eq,
                        _ => false
                    };
                }

                if (t == typeof(Quaternion))
                {
                    Vector4 cur = prop.vector4Value;
                    Vector4 target = new(box.QuaternionValue.x, box.QuaternionValue.y, box.QuaternionValue.z,
                        box.QuaternionValue.w);
                    currentValue = cur.ToString();
                    return cmp switch
                    {
                        FieldComparison.Equals => cur == target,
                        FieldComparison.NotEquals => cur != target,
                        _ => false
                    };
                }

                string fallback = PropertyToString(prop);
                string targetStr = box.StringValue ?? string.Empty;
                currentValue = fallback;

                return cmp switch
                {
                    FieldComparison.Equals => string.Equals(fallback, targetStr, StringComparison.Ordinal),
                    FieldComparison.NotEquals => !string.Equals(fallback, targetStr, StringComparison.Ordinal),
                    FieldComparison.Contains => fallback.IndexOf(targetStr, StringComparison.OrdinalIgnoreCase) >= 0,
                    FieldComparison.StartsWith => fallback.StartsWith(targetStr, StringComparison.OrdinalIgnoreCase),
                    FieldComparison.EndsWith => fallback.EndsWith(targetStr, StringComparison.OrdinalIgnoreCase),
                    FieldComparison.Regex => SafeRegexIsMatch(fallback, targetStr),
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        private static bool CompareNumbers(double cur, double target, FieldComparison cmp)
        {
            return cmp switch
            {
                FieldComparison.Equals => Math.Abs(cur - target) <= 0.0,
                FieldComparison.NotEquals => Math.Abs(cur - target) > 0.0,
                FieldComparison.GreaterThan => cur > target,
                FieldComparison.LessThan => cur < target,
                _ => false
            };
        }

        private static string PropertyToString(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.longValue.ToString(),
                SerializedPropertyType.Boolean => prop.boolValue ? "true" : "false",
                SerializedPropertyType.Float => prop.doubleValue.ToString("R"),
                SerializedPropertyType.String => prop.stringValue ?? string.Empty,
                SerializedPropertyType.ObjectReference => prop.objectReferenceValue != null
                    ? AssetDatabase.GetAssetPath(prop.objectReferenceValue)
                    : "null",
                SerializedPropertyType.Enum => prop.enumDisplayNames != null && prop.enumValueIndex >= 0 &&
                                               prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? prop.enumDisplayNames[prop.enumValueIndex]
                    : prop.enumValueIndex.ToString(),
                SerializedPropertyType.Vector2 => prop.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => prop.vector3Value.ToString(),
                SerializedPropertyType.Vector4 => prop.vector4Value.ToString(),
                SerializedPropertyType.Color => prop.colorValue.ToString(),
                SerializedPropertyType.Rect => prop.rectValue.ToString(),
                SerializedPropertyType.Bounds => prop.boundsValue.ToString(),
                SerializedPropertyType.AnimationCurve => prop.animationCurveValue != null
                    ? $"Keys={prop.animationCurveValue.length}"
                    : "null",
                _ => prop.propertyPath
            };
        }

        private static bool SafeRegexIsMatch(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            try
            {
                return Regex.IsMatch(input ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool CurvesRoughlyEqual(AnimationCurve a, AnimationCurve b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.length != b.length) return false;

            for (int i = 0; i < a.length; i++)
            {
                Keyframe ka = a.keys[i];
                Keyframe kb = b.keys[i];
                if (Math.Abs(ka.time - kb.time) > 0.0001f) return false;
                if (Math.Abs(ka.value - kb.value) > 0.0001f) return false;
                if (Math.Abs(ka.inTangent - kb.inTangent) > 0.0001f) return false;
                if (Math.Abs(ka.outTangent - kb.outTangent) > 0.0001f) return false;
            }

            return true;
        }
    }
}
