using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderSerializedFieldValueReplaceBackend
    {
        private sealed class PropertyMatch
        {
            public string PropertyPath { get; }
            public string CurrentValue { get; }

            public PropertyMatch(string propertyPath, string currentValue)
            {
                PropertyPath = propertyPath;
                CurrentValue = currentValue;
            }
        }

        private static readonly string[] CandidateExtensions =
        {
            ".asset", ".mat", ".controller", ".anim", ".overrideController", ".shader", ".compute", ".playable",
            ".prefab", ".unity"
        };

        public AssetUsageFinderSerializedFieldValueReplaceResult BuildPreview(
            AssetUsageFinderSerializedFieldValueReplaceRequest request,
            Action<float, string> progressCallback = null)
        {
            return Run(request, false, progressCallback);
        }

        public AssetUsageFinderSerializedFieldValueReplaceResult Apply(
            AssetUsageFinderSerializedFieldValueReplaceRequest request,
            Action<float, string> progressCallback = null)
        {
            return Run(request, true, progressCallback);
        }

        public static bool SupportsReplacementType(Type valueType)
        {
            if (valueType == null)
                return false;

            if (typeof(Object).IsAssignableFrom(valueType))
                return true;

            if (valueType.IsEnum)
                return true;

            if (IsIntegerType(valueType) || IsFloatingPointType(valueType))
                return true;

            return valueType == typeof(bool) ||
                   valueType == typeof(string) ||
                   valueType == typeof(Vector2) ||
                   valueType == typeof(Vector3) ||
                   valueType == typeof(Vector4) ||
                   valueType == typeof(Color) ||
                   valueType == typeof(Rect) ||
                   valueType == typeof(Bounds) ||
                   valueType == typeof(AnimationCurve) ||
                   valueType == typeof(Quaternion);
        }

        private static AssetUsageFinderSerializedFieldValueReplaceResult Run(
            AssetUsageFinderSerializedFieldValueReplaceRequest request,
            bool applyChanges,
            Action<float, string> progressCallback)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.Filters == null || request.Filters.Count == 0)
                throw new ArgumentException("At least one filter is required.", nameof(request));

            if (!SupportsReplacementType(request.ReplaceValueType))
                throw new ArgumentException("Replace value type is not supported.", nameof(request));

            List<SerializedFieldFilterRow> filters = request.Filters
                .Where(row => row != null)
                .Select(CloneAndNormalize)
                .ToList();

            if (filters.Count == 0)
            {
                return new AssetUsageFinderSerializedFieldValueReplaceResult(
                    new List<AssetUsageFinderSerializedFieldValueReplacePreviewItem>(),
                    0,
                    0);
            }

            SerializedFieldValueBox replaceWithValue = CloneValueBox(request.ReplaceWithValue);
            Type replaceValueType = request.ReplaceValueType;

            List<AssetUsageFinderScopeTarget> targets =
                AssetUsageFinderSearchScopeUtility.CollectTargets(request.SearchScope, CandidateExtensions);
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items = new();
            int replacedValueCount = 0;
            int affectedFileCount = 0;
            string activeScenePath = EditorSceneManager.GetActiveScene().path;

            for (int i = 0; i < targets.Count; i++)
            {
                AssetUsageFinderScopeTarget target = targets[i];
                float progress = targets.Count == 0 ? 1f : Mathf.Clamp01((float)(i + 1) / targets.Count);
                progressCallback?.Invoke(progress, $"Processing {target.GetProgressLabel()}");

                int beforeItemCount = items.Count;
                bool changed = ProcessTarget(
                    target,
                    filters,
                    replaceValueType,
                    replaceWithValue,
                    applyChanges,
                    items,
                    ref replacedValueCount);

                if (changed || items.Count > beforeItemCount)
                    affectedFileCount++;
            }

            if (applyChanges)
                AssetDatabase.SaveAssets();

            RestoreActiveSceneBestEffort(activeScenePath);

            return new AssetUsageFinderSerializedFieldValueReplaceResult(items, replacedValueCount, affectedFileCount);
        }

        private static bool ProcessTarget(
            AssetUsageFinderScopeTarget target,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (target == null || string.IsNullOrEmpty(target.AssetPath))
                return false;

            if (target.Kind == AssetUsageFinderScopeTargetKind.OpenScene)
            {
                return ProcessOpenScene(
                    target.AssetPath,
                    filters,
                    replaceValueType,
                    replaceWithValue,
                    applyChanges,
                    items,
                    ref replacedValueCount);
            }

            if (target.Kind == AssetUsageFinderScopeTargetKind.OpenPrefabStage)
            {
                return ProcessOpenPrefabStage(
                    target.AssetPath,
                    filters,
                    replaceValueType,
                    replaceWithValue,
                    applyChanges,
                    items,
                    ref replacedValueCount);
            }

            if (target.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessPrefabAsset(
                    target.AssetPath,
                    filters,
                    replaceValueType,
                    replaceWithValue,
                    applyChanges,
                    items,
                    ref replacedValueCount);
            }

            if (target.AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return ProcessSceneAsset(
                    target.AssetPath,
                    filters,
                    replaceValueType,
                    replaceWithValue,
                    applyChanges,
                    items,
                    ref replacedValueCount,
                    false);
            }

            return ProcessAssetFile(
                target.AssetPath,
                filters,
                replaceValueType,
                replaceWithValue,
                applyChanges,
                items,
                ref replacedValueCount);
        }

        private static bool ProcessAssetFile(
            string assetPath,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (objects == null || objects.Length == 0)
                return false;

            bool changed = false;
            Dictionary<Object, Object> referenceMap = CreateReferenceMapIfNeeded(applyChanges, replaceValueType);

            foreach (Object obj in objects)
            {
                if (obj == null)
                    continue;

                if (ProcessSerializedObject(
                        assetPath,
                        obj.name,
                        obj,
                        filters,
                        replaceValueType,
                        replaceWithValue,
                        applyChanges,
                        false,
                        referenceMap,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            if (applyChanges && TryRemapObjectReferencesInAssetObjects(
                    assetPath,
                    objects,
                    referenceMap,
                    false,
                    items,
                    ref replacedValueCount))
                changed = true;

            return changed;
        }

        private static bool ProcessPrefabAsset(
            string prefabPath,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (!File.Exists(prefabPath))
                return false;

            GameObject root = null;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return false;

                Dictionary<Object, Object> referenceMap = CreateReferenceMapIfNeeded(applyChanges, replaceValueType);
                bool changed = ProcessGameObjectHierarchy(
                    prefabPath,
                    root,
                    filters,
                    replaceValueType,
                    replaceWithValue,
                    applyChanges,
                    false,
                    referenceMap,
                    items,
                    ref replacedValueCount);

                if (applyChanges && TryRemapObjectReferencesInGameObjectHierarchy(
                        prefabPath,
                        root,
                        referenceMap,
                        false,
                        items,
                        ref replacedValueCount))
                    changed = true;

                if (applyChanges && changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                return changed;
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool ProcessSceneAsset(
            string scenePath,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount,
            bool useLoadedSceneOnly)
        {
            if (!File.Exists(scenePath))
                return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;

            try
            {
                if (useLoadedSceneOnly && openedHere)
                    return false;

                if (openedHere)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid() || !scene.isLoaded)
                    return false;

                bool changed = false;
                Dictionary<Object, Object> referenceMap = CreateReferenceMapIfNeeded(applyChanges, replaceValueType);

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null)
                        continue;

                    if (ProcessGameObjectHierarchy(
                            scenePath,
                            root,
                            filters,
                            replaceValueType,
                            replaceWithValue,
                            applyChanges,
                            true,
                            referenceMap,
                            items,
                            ref replacedValueCount))
                        changed = true;
                }

                if (applyChanges && TryRemapObjectReferencesInScene(
                        scenePath,
                        scene,
                        referenceMap,
                        items,
                        ref replacedValueCount))
                    changed = true;

                if (applyChanges && changed)
                    EditorSceneManager.SaveScene(scene);

                return changed;
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
                        // ignored
                    }
                }
            }
        }

        private static bool ProcessOpenScene(
            string sceneLabel,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            bool changed = false;
            Dictionary<Object, Object> referenceMap = CreateReferenceMapIfNeeded(applyChanges, replaceValueType);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null)
                    continue;

                if (ProcessGameObjectHierarchy(
                        sceneLabel,
                        root,
                        filters,
                        replaceValueType,
                        replaceWithValue,
                        applyChanges,
                        true,
                        referenceMap,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            if (applyChanges && TryRemapObjectReferencesInScene(
                    sceneLabel,
                    scene,
                    referenceMap,
                    items,
                    ref replacedValueCount))
                changed = true;

            if (applyChanges && changed && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            return changed;
        }

        private static bool ProcessOpenPrefabStage(
            string prefabPath,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
                return false;

            if (!string.Equals(stage.assetPath, prefabPath, StringComparison.OrdinalIgnoreCase))
                return false;

            Dictionary<Object, Object> referenceMap = CreateReferenceMapIfNeeded(applyChanges, replaceValueType);
            bool changed = ProcessGameObjectHierarchy(
                prefabPath,
                stage.prefabContentsRoot,
                filters,
                replaceValueType,
                replaceWithValue,
                applyChanges,
                false,
                referenceMap,
                items,
                ref replacedValueCount);

            if (applyChanges && TryRemapObjectReferencesInGameObjectHierarchy(
                    prefabPath,
                    stage.prefabContentsRoot,
                    referenceMap,
                    false,
                    items,
                    ref replacedValueCount))
                changed = true;

            if (applyChanges && changed)
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, prefabPath);

            return changed;
        }

        private static bool ProcessGameObjectHierarchy(
            string fileAssetPath,
            GameObject root,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            bool isScene,
            Dictionary<Object, Object> referenceMap,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (root == null)
                return false;

            bool changed = false;
            string goPath = GetHierarchyPath(root);

            foreach (Component component in root.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                string objectPath = $"{goPath} ({component.GetType().Name})";
                if (ProcessSerializedObject(
                        fileAssetPath,
                        objectPath,
                        component,
                        filters,
                        replaceValueType,
                        replaceWithValue,
                        applyChanges,
                        isScene,
                        referenceMap,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            foreach (Transform child in root.transform)
            {
                if (child == null)
                    continue;

                if (ProcessGameObjectHierarchy(
                        fileAssetPath,
                        child.gameObject,
                        filters,
                        replaceValueType,
                        replaceWithValue,
                        applyChanges,
                        isScene,
                        referenceMap,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            return changed;
        }

        private static bool ProcessSerializedObject(
            string fileAssetPath,
            string objectPath,
            Object obj,
            IReadOnlyList<SerializedFieldFilterRow> filters,
            Type replaceValueType,
            SerializedFieldValueBox replaceWithValue,
            bool applyChanges,
            bool isScene,
            Dictionary<Object, Object> referenceMap,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (obj == null)
                return false;

            SerializedObject serializedObject;

            try
            {
                serializedObject = new SerializedObject(obj);
            }
            catch
            {
                return false;
            }

            List<List<PropertyMatch>> rowMatches = new();
            List<bool> rowBools = new();

            for (int i = 0; i < filters.Count; i++)
            {
                List<PropertyMatch> matches = FindMatchingProperties(serializedObject, filters[i]);
                rowMatches.Add(matches);
                rowBools.Add(matches.Count > 0);
            }

            if (!CombineRowBools(filters, rowBools))
                return false;

            bool changed = false;
            int initialItemCount = items.Count;
            HashSet<string> processedPaths = new(StringComparer.Ordinal);

            for (int i = 0; i < filters.Count; i++)
            {
                SerializedFieldFilterRow row = filters[i];
                if (!AreTypesCompatibleForReplace(row.EffectiveValueType, replaceValueType))
                    continue;

                foreach (PropertyMatch match in rowMatches[i])
                {
                    if (match == null || string.IsNullOrEmpty(match.PropertyPath))
                        continue;

                    if (!processedPaths.Add(match.PropertyPath))
                        continue;

                    SerializedProperty property = serializedObject.FindProperty(match.PropertyPath);
                    if (property == null)
                        continue;

                    if (!DoesPropertyTypeMatchRow(property, replaceValueType))
                        continue;

                    if (!TryGetReplacementDisplayValue(property, replaceValueType, replaceWithValue,
                            out string newValue))
                        continue;

                    if (HasSameValue(property, replaceValueType, replaceWithValue))
                        continue;

                    items.Add(new AssetUsageFinderSerializedFieldValueReplacePreviewItem(
                        fileAssetPath,
                        objectPath,
                        obj.GetType().FullName,
                        match.PropertyPath,
                        match.CurrentValue,
                        newValue,
                        isScene));

                    if (!applyChanges)
                        continue;

                    Object previousReference = referenceMap != null ? property.objectReferenceValue : null;
                    if (TryAssignPropertyValue(property, replaceValueType, replaceWithValue))
                    {
                        changed = true;
                        replacedValueCount++;
                        TrackObjectReferenceRemap(previousReference, replaceWithValue, referenceMap);
                    }
                }
            }

            if (applyChanges && changed)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(obj);
            }

            return changed || items.Count > initialItemCount;
        }

        private static Dictionary<Object, Object> CreateReferenceMapIfNeeded(bool applyChanges, Type replaceValueType)
        {
            if (!applyChanges || replaceValueType == null || !typeof(Object).IsAssignableFrom(replaceValueType))
                return null;

            return new Dictionary<Object, Object>();
        }

        private static void TrackObjectReferenceRemap(
            Object previousReference,
            SerializedFieldValueBox replaceWithValue,
            Dictionary<Object, Object> referenceMap)
        {
            if (previousReference == null || referenceMap == null)
                return;

            Object newReference = replaceWithValue?.ObjectValue;
            if (newReference == null || previousReference == newReference)
                return;

            referenceMap[previousReference] = newReference;
        }

        private static bool TryRemapObjectReferencesInAssetObjects(
            string fileAssetPath,
            IEnumerable<Object> objects,
            Dictionary<Object, Object> referenceMap,
            bool isScene,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (objects == null || referenceMap == null || referenceMap.Count == 0)
                return false;

            bool changed = false;

            foreach (Object obj in objects)
            {
                if (obj == null)
                    continue;

                if (TryRemapObjectReferencesInSerializedObject(
                        fileAssetPath,
                        obj.name,
                        obj,
                        referenceMap,
                        isScene,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            return changed;
        }

        private static bool TryRemapObjectReferencesInScene(
            string fileAssetPath,
            Scene scene,
            Dictionary<Object, Object> referenceMap,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (!scene.IsValid() || !scene.isLoaded || referenceMap == null || referenceMap.Count == 0)
                return false;

            bool changed = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null)
                    continue;

                if (TryRemapObjectReferencesInGameObjectHierarchy(
                        fileAssetPath,
                        root,
                        referenceMap,
                        true,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            return changed;
        }

        private static bool TryRemapObjectReferencesInGameObjectHierarchy(
            string fileAssetPath,
            GameObject root,
            Dictionary<Object, Object> referenceMap,
            bool isScene,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (root == null || referenceMap == null || referenceMap.Count == 0)
                return false;

            bool changed = false;
            string goPath = GetHierarchyPath(root);

            foreach (Component component in root.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                string objectPath = $"{goPath} ({component.GetType().Name})";
                if (TryRemapObjectReferencesInSerializedObject(
                        fileAssetPath,
                        objectPath,
                        component,
                        referenceMap,
                        isScene,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            foreach (Transform child in root.transform)
            {
                if (child == null)
                    continue;

                if (TryRemapObjectReferencesInGameObjectHierarchy(
                        fileAssetPath,
                        child.gameObject,
                        referenceMap,
                        isScene,
                        items,
                        ref replacedValueCount))
                    changed = true;
            }

            return changed;
        }

        private static bool TryRemapObjectReferencesInSerializedObject(
            string fileAssetPath,
            string objectPath,
            Object obj,
            Dictionary<Object, Object> referenceMap,
            bool isScene,
            List<AssetUsageFinderSerializedFieldValueReplacePreviewItem> items,
            ref int replacedValueCount)
        {
            if (obj == null || referenceMap == null || referenceMap.Count == 0)
                return false;

            SerializedObject serializedObject;

            try
            {
                serializedObject = new SerializedObject(obj);
            }
            catch
            {
                return false;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            bool changed = false;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script" ||
                    iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                Object currentReference = iterator.objectReferenceValue;
                if (currentReference == null)
                    continue;

                if (!referenceMap.TryGetValue(currentReference, out Object newReference) ||
                    newReference == null ||
                    currentReference == newReference)
                    continue;

                items.Add(new AssetUsageFinderSerializedFieldValueReplacePreviewItem(
                    fileAssetPath,
                    objectPath,
                    obj.GetType().FullName,
                    iterator.propertyPath,
                    GetObjectDisplayString(currentReference),
                    GetObjectDisplayString(newReference),
                    isScene));

                iterator.objectReferenceValue = newReference;
                changed = true;
                replacedValueCount++;
            }

            if (!changed)
                return false;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
            return true;
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
                Value = CloneValueBox(row.Value)
            };

            if (clone.IsCollection)
                clone.Comparison = FieldComparison.Contains;

            return clone;
        }

        private static SerializedFieldValueBox CloneValueBox(SerializedFieldValueBox source)
        {
            source ??= new SerializedFieldValueBox();

            AnimationCurve sourceCurve = source.CurveValue;
            AnimationCurve clonedCurve = sourceCurve == null
                ? null
                : new AnimationCurve(sourceCurve.keys)
                {
                    preWrapMode = sourceCurve.preWrapMode,
                    postWrapMode = sourceCurve.postWrapMode
                };

            return new SerializedFieldValueBox
            {
                ObjectValue = source.ObjectValue,
                BoolValue = source.BoolValue,
                IntValue = source.IntValue,
                LongValue = source.LongValue,
                FloatValue = source.FloatValue,
                DoubleValue = source.DoubleValue,
                StringValue = source.StringValue ?? string.Empty,
                EnumIndex = source.EnumIndex,
                EnumName = source.EnumName ?? string.Empty,
                Vector2Value = source.Vector2Value,
                Vector3Value = source.Vector3Value,
                Vector4Value = source.Vector4Value,
                ColorValue = source.ColorValue,
                RectValue = source.RectValue,
                BoundsValue = source.BoundsValue,
                CurveValue = clonedCurve,
                QuaternionValue = source.QuaternionValue
            };
        }

        private static List<PropertyMatch> FindMatchingProperties(
            SerializedObject serializedObject,
            SerializedFieldFilterRow row)
        {
            List<PropertyMatch> matches = new();

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script")
                    continue;

                if (row.IsCollection)
                {
                    if (!iterator.isArray || iterator.propertyType == SerializedPropertyType.String)
                        continue;

                    AddMatchingArrayElements(iterator, row, matches);
                    continue;
                }

                if (!DoesPropertyTypeMatchRow(iterator, row.EffectiveValueType))
                    continue;

                if (EvaluateScalarProperty(iterator, row, out string currentValue))
                    matches.Add(new PropertyMatch(iterator.propertyPath, currentValue));
            }

            return matches;
        }

        private static void AddMatchingArrayElements(
            SerializedProperty arrayProperty,
            SerializedFieldFilterRow row,
            List<PropertyMatch> matches)
        {
            int size = arrayProperty.arraySize;

            for (int i = 0; i < size; i++)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(i);
                if (element == null)
                    continue;

                if (!DoesPropertyTypeMatchRow(element, row.EffectiveValueType))
                    continue;

                SerializedFieldFilterRow scalarRow = new()
                {
                    ValueType = row.EffectiveValueType,
                    Collection = CollectionKind.None,
                    Comparison = FieldComparison.Equals,
                    Value = row.Value
                };
                scalarRow.EnsureDefaults();

                if (EvaluateScalarProperty(element, scalarRow, out string elementValue))
                    matches.Add(new PropertyMatch(element.propertyPath, elementValue));
            }
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
                return string.Empty;

            string path = gameObject.name;
            Transform current = gameObject.transform;

            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }

        private static void RestoreActiveSceneBestEffort(string activeScenePath)
        {
            if (string.IsNullOrEmpty(activeScenePath))
                return;

            Scene scene = SceneManager.GetSceneByPath(activeScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            try
            {
                SceneManager.SetActiveScene(scene);
            }
            catch
            {
                // ignored
            }
        }

        private static bool CombineRowBools(
            IReadOnlyList<SerializedFieldFilterRow> filters,
            IReadOnlyList<bool> rowBools)
        {
            if (filters == null || filters.Count == 0)
                return false;

            if (rowBools == null || rowBools.Count != filters.Count)
                return false;

            bool accumulator = rowBools[0];

            for (int i = 1; i < filters.Count; i++)
            {
                accumulator = filters[i].JoinWithPrevious == LogicalOperator.And
                    ? accumulator && rowBools[i]
                    : accumulator || rowBools[i];
            }

            return accumulator;
        }

        private static bool AreTypesCompatibleForReplace(Type filterType, Type replaceType)
        {
            if (filterType == null || replaceType == null)
                return false;

            if (typeof(Object).IsAssignableFrom(filterType) && typeof(Object).IsAssignableFrom(replaceType))
                return true;

            if (filterType.IsEnum && replaceType.IsEnum)
                return true;

            if (IsIntegerType(filterType) && IsIntegerType(replaceType))
                return true;

            if (IsFloatingPointType(filterType) && IsFloatingPointType(replaceType))
                return true;

            return filterType == replaceType;
        }

        private static bool DoesPropertyTypeMatchRow(SerializedProperty property, Type desiredType)
        {
            if (property == null || desiredType == null)
                return false;

            if (typeof(Object).IsAssignableFrom(desiredType))
                return property.propertyType == SerializedPropertyType.ObjectReference;

            if (desiredType.IsEnum)
                return property.propertyType == SerializedPropertyType.Enum;

            if (desiredType == typeof(bool))
                return property.propertyType == SerializedPropertyType.Boolean;

            if (desiredType == typeof(string))
                return property.propertyType == SerializedPropertyType.String;

            if (IsIntegerType(desiredType))
                return property.propertyType == SerializedPropertyType.Integer;

            if (IsFloatingPointType(desiredType))
                return property.propertyType == SerializedPropertyType.Float;

            if (desiredType == typeof(Vector2))
                return property.propertyType == SerializedPropertyType.Vector2;

            if (desiredType == typeof(Vector3))
                return property.propertyType == SerializedPropertyType.Vector3;

            if (desiredType == typeof(Vector4))
                return property.propertyType == SerializedPropertyType.Vector4;

            if (desiredType == typeof(Color))
                return property.propertyType == SerializedPropertyType.Color;

            if (desiredType == typeof(Rect))
                return property.propertyType == SerializedPropertyType.Rect;

            if (desiredType == typeof(Bounds))
                return property.propertyType == SerializedPropertyType.Bounds;

            if (desiredType == typeof(AnimationCurve))
                return property.propertyType == SerializedPropertyType.AnimationCurve;

            if (desiredType == typeof(Quaternion))
                return property.propertyType == SerializedPropertyType.Vector4;

            return false;
        }

        private static bool EvaluateScalarProperty(
            SerializedProperty property,
            SerializedFieldFilterRow row,
            out string currentValue)
        {
            currentValue = string.Empty;
            Type valueType = row.EffectiveValueType ?? typeof(string);
            FieldComparison comparison = row.EffectiveComparison;
            SerializedFieldValueBox box = row.Value ?? new SerializedFieldValueBox();

            try
            {
                if (typeof(Object).IsAssignableFrom(valueType))
                {
                    Object current = property.objectReferenceValue;
                    Object target = box.ObjectValue;
                    currentValue = GetObjectDisplayString(current);

                    bool equals = current == target;
                    return comparison switch
                    {
                        FieldComparison.Equals => equals,
                        FieldComparison.NotEquals => !equals,
                        _ => equals
                    };
                }

                if (valueType.IsEnum)
                {
                    string currentName = GetEnumDisplayName(property);
                    currentValue = currentName;
                    string targetName = box.EnumName ?? string.Empty;

                    return comparison switch
                    {
                        FieldComparison.Equals => string.Equals(currentName, targetName, StringComparison.Ordinal),
                        FieldComparison.NotEquals => !string.Equals(currentName, targetName, StringComparison.Ordinal),
                        FieldComparison.Contains =>
                            currentName.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0,
                        FieldComparison.StartsWith => currentName.StartsWith(targetName,
                            StringComparison.OrdinalIgnoreCase),
                        FieldComparison.EndsWith =>
                            currentName.EndsWith(targetName, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.Regex => SafeRegexIsMatch(currentName, targetName),
                        _ => false
                    };
                }

                if (valueType == typeof(bool))
                {
                    bool current = property.boolValue;
                    bool target = box.BoolValue;
                    currentValue = current ? "true" : "false";

                    return comparison switch
                    {
                        FieldComparison.Equals => current == target,
                        FieldComparison.NotEquals => current != target,
                        _ => false
                    };
                }

                if (valueType == typeof(string))
                {
                    string current = property.stringValue ?? string.Empty;
                    string target = box.StringValue ?? string.Empty;
                    currentValue = current;

                    return comparison switch
                    {
                        FieldComparison.Equals => string.Equals(current, target, StringComparison.Ordinal),
                        FieldComparison.NotEquals => !string.Equals(current, target, StringComparison.Ordinal),
                        FieldComparison.Contains => current.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0,
                        FieldComparison.StartsWith => current.StartsWith(target, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.EndsWith => current.EndsWith(target, StringComparison.OrdinalIgnoreCase),
                        FieldComparison.Regex => SafeRegexIsMatch(current, target),
                        _ => false
                    };
                }

                if (IsIntegerType(valueType))
                {
                    long current = property.longValue;
                    long target = GetIntegerBoxValue(valueType, box);
                    currentValue = current.ToString();
                    return CompareNumbers(current, target, comparison);
                }

                if (valueType == typeof(float))
                {
                    double current = property.floatValue;
                    double target = box.FloatValue;
                    currentValue = current.ToString("R");
                    return CompareNumbers(current, target, comparison);
                }

                if (valueType == typeof(double))
                {
                    double current = property.doubleValue;
                    double target = box.DoubleValue;
                    currentValue = current.ToString("R");
                    return CompareNumbers(current, target, comparison);
                }

                if (valueType == typeof(Vector2))
                {
                    Vector2 current = property.vector2Value;
                    Vector2 target = box.Vector2Value;
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current == target,
                        FieldComparison.NotEquals => current != target,
                        _ => false
                    };
                }

                if (valueType == typeof(Vector3))
                {
                    Vector3 current = property.vector3Value;
                    Vector3 target = box.Vector3Value;
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current == target,
                        FieldComparison.NotEquals => current != target,
                        _ => false
                    };
                }

                if (valueType == typeof(Vector4))
                {
                    Vector4 current = property.vector4Value;
                    Vector4 target = box.Vector4Value;
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current == target,
                        FieldComparison.NotEquals => current != target,
                        _ => false
                    };
                }

                if (valueType == typeof(Color))
                {
                    Color current = property.colorValue;
                    Color target = box.ColorValue;
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current.Equals(target),
                        FieldComparison.NotEquals => !current.Equals(target),
                        _ => false
                    };
                }

                if (valueType == typeof(Rect))
                {
                    Rect current = property.rectValue;
                    Rect target = box.RectValue;
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current.Equals(target),
                        FieldComparison.NotEquals => !current.Equals(target),
                        _ => false
                    };
                }

                if (valueType == typeof(Bounds))
                {
                    Bounds current = property.boundsValue;
                    Bounds target = box.BoundsValue;
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current.Equals(target),
                        FieldComparison.NotEquals => !current.Equals(target),
                        _ => false
                    };
                }

                if (valueType == typeof(AnimationCurve))
                {
                    AnimationCurve current = property.animationCurveValue;
                    AnimationCurve target = box.CurveValue;
                    currentValue = current != null ? $"Keys={current.length}" : "null";

                    bool equals = CurvesRoughlyEqual(current, target);
                    return comparison switch
                    {
                        FieldComparison.Equals => equals,
                        FieldComparison.NotEquals => !equals,
                        _ => false
                    };
                }

                if (valueType == typeof(Quaternion))
                {
                    Vector4 current = property.vector4Value;
                    Vector4 target = new(box.QuaternionValue.x, box.QuaternionValue.y, box.QuaternionValue.z,
                        box.QuaternionValue.w);
                    currentValue = current.ToString();
                    return comparison switch
                    {
                        FieldComparison.Equals => current == target,
                        FieldComparison.NotEquals => current != target,
                        _ => false
                    };
                }

                string fallback = PropertyToString(property);
                string targetValue = box.StringValue ?? string.Empty;
                currentValue = fallback;

                return comparison switch
                {
                    FieldComparison.Equals => string.Equals(fallback, targetValue, StringComparison.Ordinal),
                    FieldComparison.NotEquals => !string.Equals(fallback, targetValue, StringComparison.Ordinal),
                    FieldComparison.Contains => fallback.IndexOf(targetValue, StringComparison.OrdinalIgnoreCase) >= 0,
                    FieldComparison.StartsWith => fallback.StartsWith(targetValue, StringComparison.OrdinalIgnoreCase),
                    FieldComparison.EndsWith => fallback.EndsWith(targetValue, StringComparison.OrdinalIgnoreCase),
                    FieldComparison.Regex => SafeRegexIsMatch(fallback, targetValue),
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }

        private static bool HasSameValue(SerializedProperty property, Type valueType, SerializedFieldValueBox box)
        {
            box ??= new SerializedFieldValueBox();

            try
            {
                if (typeof(Object).IsAssignableFrom(valueType))
                    return property.objectReferenceValue == box.ObjectValue;

                if (valueType.IsEnum)
                {
                    return TryResolveEnumIndex(property, box, out int enumIndex) &&
                           property.enumValueIndex == enumIndex;
                }

                if (valueType == typeof(bool))
                    return property.boolValue == box.BoolValue;

                if (valueType == typeof(string))
                {
                    return string.Equals(property.stringValue ?? string.Empty, box.StringValue ?? string.Empty,
                        StringComparison.Ordinal);
                }

                if (valueType == typeof(int))
                    return property.intValue == box.IntValue;

                if (valueType == typeof(short))
                    return property.intValue == Mathf.Clamp(box.IntValue, short.MinValue, short.MaxValue);

                if (valueType == typeof(byte))
                    return property.intValue == Mathf.Clamp(box.IntValue, byte.MinValue, byte.MaxValue);

                if (valueType == typeof(long))
                    return property.longValue == box.LongValue;

                if (valueType == typeof(uint) || valueType == typeof(ulong))
                    return property.longValue == Math.Max(0L, box.LongValue);

                if (valueType == typeof(float))
                    return Math.Abs(property.floatValue - box.FloatValue) <= 0f;

                if (valueType == typeof(double))
                    return Math.Abs(property.doubleValue - box.DoubleValue) <= 0d;

                if (valueType == typeof(Vector2))
                    return property.vector2Value == box.Vector2Value;

                if (valueType == typeof(Vector3))
                    return property.vector3Value == box.Vector3Value;

                if (valueType == typeof(Vector4))
                    return property.vector4Value == box.Vector4Value;

                if (valueType == typeof(Color))
                    return property.colorValue.Equals(box.ColorValue);

                if (valueType == typeof(Rect))
                    return property.rectValue.Equals(box.RectValue);

                if (valueType == typeof(Bounds))
                    return property.boundsValue.Equals(box.BoundsValue);

                if (valueType == typeof(AnimationCurve))
                    return CurvesRoughlyEqual(property.animationCurveValue, box.CurveValue);

                if (valueType == typeof(Quaternion))
                {
                    Vector4 target = new(box.QuaternionValue.x, box.QuaternionValue.y, box.QuaternionValue.z,
                        box.QuaternionValue.w);
                    return property.vector4Value == target;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryGetReplacementDisplayValue(
            SerializedProperty property,
            Type valueType,
            SerializedFieldValueBox box,
            out string value)
        {
            value = string.Empty;
            box ??= new SerializedFieldValueBox();

            if (!DoesPropertyTypeMatchRow(property, valueType))
                return false;

            if (typeof(Object).IsAssignableFrom(valueType))
            {
                value = GetObjectDisplayString(box.ObjectValue);
                return true;
            }

            if (valueType.IsEnum)
            {
                if (!TryResolveEnumIndex(property, box, out int enumIndex))
                    return false;

                string[] names = property.enumDisplayNames;
                value = names != null && enumIndex >= 0 && enumIndex < names.Length
                    ? names[enumIndex]
                    : enumIndex.ToString();
                return true;
            }

            if (valueType == typeof(bool))
            {
                value = box.BoolValue ? "true" : "false";
                return true;
            }

            if (valueType == typeof(string))
            {
                value = box.StringValue ?? string.Empty;
                return true;
            }

            if (valueType == typeof(int))
            {
                value = box.IntValue.ToString();
                return true;
            }

            if (valueType == typeof(short))
            {
                value = Mathf.Clamp(box.IntValue, short.MinValue, short.MaxValue).ToString();
                return true;
            }

            if (valueType == typeof(byte))
            {
                value = Mathf.Clamp(box.IntValue, byte.MinValue, byte.MaxValue).ToString();
                return true;
            }

            if (valueType == typeof(long))
            {
                value = box.LongValue.ToString();
                return true;
            }

            if (valueType == typeof(uint) || valueType == typeof(ulong))
            {
                value = Math.Max(0L, box.LongValue).ToString();
                return true;
            }

            if (valueType == typeof(float))
            {
                value = box.FloatValue.ToString("R");
                return true;
            }

            if (valueType == typeof(double))
            {
                value = box.DoubleValue.ToString("R");
                return true;
            }

            if (valueType == typeof(Vector2))
            {
                value = box.Vector2Value.ToString();
                return true;
            }

            if (valueType == typeof(Vector3))
            {
                value = box.Vector3Value.ToString();
                return true;
            }

            if (valueType == typeof(Vector4))
            {
                value = box.Vector4Value.ToString();
                return true;
            }

            if (valueType == typeof(Color))
            {
                value = box.ColorValue.ToString();
                return true;
            }

            if (valueType == typeof(Rect))
            {
                value = box.RectValue.ToString();
                return true;
            }

            if (valueType == typeof(Bounds))
            {
                value = box.BoundsValue.ToString();
                return true;
            }

            if (valueType == typeof(AnimationCurve))
            {
                value = box.CurveValue != null ? $"Keys={box.CurveValue.length}" : "null";
                return true;
            }

            if (valueType == typeof(Quaternion))
            {
                value = new Vector4(box.QuaternionValue.x, box.QuaternionValue.y, box.QuaternionValue.z,
                    box.QuaternionValue.w).ToString();
                return true;
            }

            return false;
        }

        private static bool TryAssignPropertyValue(
            SerializedProperty property,
            Type valueType,
            SerializedFieldValueBox box)
        {
            box ??= new SerializedFieldValueBox();

            try
            {
                if (typeof(Object).IsAssignableFrom(valueType))
                {
                    property.objectReferenceValue = box.ObjectValue;
                    return true;
                }

                if (valueType.IsEnum)
                {
                    if (!TryResolveEnumIndex(property, box, out int enumIndex))
                        return false;

                    property.enumValueIndex = enumIndex;
                    return true;
                }

                if (valueType == typeof(bool))
                {
                    property.boolValue = box.BoolValue;
                    return true;
                }

                if (valueType == typeof(string))
                {
                    property.stringValue = box.StringValue ?? string.Empty;
                    return true;
                }

                if (valueType == typeof(int))
                {
                    property.intValue = box.IntValue;
                    return true;
                }

                if (valueType == typeof(short))
                {
                    property.intValue = Mathf.Clamp(box.IntValue, short.MinValue, short.MaxValue);
                    return true;
                }

                if (valueType == typeof(byte))
                {
                    property.intValue = Mathf.Clamp(box.IntValue, byte.MinValue, byte.MaxValue);
                    return true;
                }

                if (valueType == typeof(long))
                {
                    property.longValue = box.LongValue;
                    return true;
                }

                if (valueType == typeof(uint) || valueType == typeof(ulong))
                {
                    property.longValue = Math.Max(0L, box.LongValue);
                    return true;
                }

                if (valueType == typeof(float))
                {
                    property.floatValue = box.FloatValue;
                    return true;
                }

                if (valueType == typeof(double))
                {
                    property.doubleValue = box.DoubleValue;
                    return true;
                }

                if (valueType == typeof(Vector2))
                {
                    property.vector2Value = box.Vector2Value;
                    return true;
                }

                if (valueType == typeof(Vector3))
                {
                    property.vector3Value = box.Vector3Value;
                    return true;
                }

                if (valueType == typeof(Vector4))
                {
                    property.vector4Value = box.Vector4Value;
                    return true;
                }

                if (valueType == typeof(Color))
                {
                    property.colorValue = box.ColorValue;
                    return true;
                }

                if (valueType == typeof(Rect))
                {
                    property.rectValue = box.RectValue;
                    return true;
                }

                if (valueType == typeof(Bounds))
                {
                    property.boundsValue = box.BoundsValue;
                    return true;
                }

                if (valueType == typeof(AnimationCurve))
                {
                    property.animationCurveValue = box.CurveValue == null
                        ? null
                        : new AnimationCurve(box.CurveValue.keys)
                        {
                            preWrapMode = box.CurveValue.preWrapMode,
                            postWrapMode = box.CurveValue.postWrapMode
                        };
                    return true;
                }

                if (valueType == typeof(Quaternion))
                {
                    property.vector4Value = new Vector4(box.QuaternionValue.x, box.QuaternionValue.y,
                        box.QuaternionValue.z, box.QuaternionValue.w);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryResolveEnumIndex(
            SerializedProperty property,
            SerializedFieldValueBox box,
            out int enumIndex)
        {
            enumIndex = -1;
            string[] names = property.enumDisplayNames;
            if (names == null || names.Length == 0)
                return false;

            string enumName = box?.EnumName;
            if (!string.IsNullOrEmpty(enumName))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], enumName, StringComparison.Ordinal))
                    {
                        enumIndex = i;
                        return true;
                    }
                }
            }

            int requestedIndex = box?.EnumIndex ?? -1;
            if (requestedIndex >= 0 && requestedIndex < names.Length)
            {
                enumIndex = requestedIndex;
                return true;
            }

            return false;
        }

        private static string GetEnumDisplayName(SerializedProperty property)
        {
            string[] names = property.enumDisplayNames;
            return names != null && property.enumValueIndex >= 0 && property.enumValueIndex < names.Length
                ? names[property.enumValueIndex]
                : property.enumValueIndex.ToString();
        }

        private static string PropertyToString(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Integer => property.longValue.ToString(),
                SerializedPropertyType.Boolean => property.boolValue ? "true" : "false",
                SerializedPropertyType.Float => property.doubleValue.ToString("R"),
                SerializedPropertyType.String => property.stringValue ?? string.Empty,
                SerializedPropertyType.ObjectReference => GetObjectDisplayString(property.objectReferenceValue),
                SerializedPropertyType.Enum => GetEnumDisplayName(property),
                SerializedPropertyType.Vector2 => property.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => property.vector3Value.ToString(),
                SerializedPropertyType.Vector4 => property.vector4Value.ToString(),
                SerializedPropertyType.Color => property.colorValue.ToString(),
                SerializedPropertyType.Rect => property.rectValue.ToString(),
                SerializedPropertyType.Bounds => property.boundsValue.ToString(),
                SerializedPropertyType.AnimationCurve => property.animationCurveValue != null
                    ? $"Keys={property.animationCurveValue.length}"
                    : "null",
                _ => property.propertyPath
            };
        }

        private static string GetObjectDisplayString(Object obj)
        {
            if (obj == null)
                return "null";

            string assetPath = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(assetPath) ? assetPath : obj.name;
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
            if (a == null && b == null)
                return true;

            if (a == null || b == null)
                return false;

            if (a.length != b.length)
                return false;

            for (int i = 0; i < a.length; i++)
            {
                Keyframe left = a.keys[i];
                Keyframe right = b.keys[i];

                if (Math.Abs(left.time - right.time) > 0.0001f)
                    return false;

                if (Math.Abs(left.value - right.value) > 0.0001f)
                    return false;

                if (Math.Abs(left.inTangent - right.inTangent) > 0.0001f)
                    return false;

                if (Math.Abs(left.outTangent - right.outTangent) > 0.0001f)
                    return false;
            }

            return true;
        }

        private static bool CompareNumbers(double current, double target, FieldComparison comparison)
        {
            return comparison switch
            {
                FieldComparison.Equals => Math.Abs(current - target) <= 0.0,
                FieldComparison.NotEquals => Math.Abs(current - target) > 0.0,
                FieldComparison.GreaterThan => current > target,
                FieldComparison.LessThan => current < target,
                _ => false
            };
        }

        private static bool IsIntegerType(Type valueType)
        {
            return valueType == typeof(int) ||
                   valueType == typeof(short) ||
                   valueType == typeof(byte) ||
                   valueType == typeof(long) ||
                   valueType == typeof(uint) ||
                   valueType == typeof(ulong);
        }

        private static bool IsFloatingPointType(Type valueType)
        {
            return valueType == typeof(float) || valueType == typeof(double);
        }

        private static long GetIntegerBoxValue(Type valueType, SerializedFieldValueBox box)
        {
            if (valueType == typeof(int) || valueType == typeof(short) || valueType == typeof(byte))
                return box.IntValue;

            return box.LongValue;
        }
    }
}