using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderPrefabOrVariantReplaceBackend
    {
        private static readonly string[] CandidateExtensions = { ".prefab", ".unity" };

        public AssetUsageFinderPrefabOrVariantReplaceResult BuildPreview(
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            Action<float, string> progressCallback = null)
        {
            return Run(request, false, progressCallback);
        }

        public AssetUsageFinderPrefabOrVariantReplaceResult Apply(
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            Action<float, string> progressCallback = null)
        {
            return Run(request, true, progressCallback);
        }

        private static AssetUsageFinderPrefabOrVariantReplaceResult Run(
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            Action<float, string> progressCallback)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.FromPrefab == null)
                throw new ArgumentException("From Prefab is required.", nameof(request));

            if (request.ToPrefab == null)
                throw new ArgumentException("To Prefab is required.", nameof(request));

            List<AssetUsageFinderScopeTarget> targets =
                AssetUsageFinderSearchScopeUtility.CollectTargets(request.SearchScope, CandidateExtensions);
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items = new();
            int replacedInstanceCount = 0;
            int affectedFileCount = 0;
            string activeScenePath = EditorSceneManager.GetActiveScene().path;

            for (int i = 0; i < targets.Count; i++)
            {
                AssetUsageFinderScopeTarget target = targets[i];
                string filePath = target.AssetPath;
                float progress = targets.Count == 0 ? 1f : Mathf.Clamp01((float)(i + 1) / targets.Count);
                progressCallback?.Invoke(progress, $"Processing {target.GetProgressLabel()}");

                bool changed = ProcessTarget(target, request, applyChanges, items, ref replacedInstanceCount);

                if (changed || items.Any(item =>
                        string.Equals(item.FileAssetPath, filePath, StringComparison.OrdinalIgnoreCase)))
                    affectedFileCount++;
            }

            if (applyChanges)
                AssetDatabase.SaveAssets();

            RestoreActiveSceneBestEffort(activeScenePath);

            return new AssetUsageFinderPrefabOrVariantReplaceResult(items, replacedInstanceCount, affectedFileCount);
        }

        private static bool ProcessTarget(
            AssetUsageFinderScopeTarget target,
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            ref int replacedInstanceCount)
        {
            if (target == null || string.IsNullOrEmpty(target.AssetPath))
                return false;

            if (target.Kind == AssetUsageFinderScopeTargetKind.OpenPrefabStage)
            {
                return ProcessOpenPrefabStage(target.AssetPath, request, applyChanges, items,
                    ref replacedInstanceCount);
            }

            if (target.Kind == AssetUsageFinderScopeTargetKind.OpenScene)
                return ProcessOpenScene(target.AssetPath, request, applyChanges, items, ref replacedInstanceCount);

            return target.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? ProcessPrefabAsset(target.AssetPath, request, applyChanges, items, ref replacedInstanceCount)
                : ProcessSceneAsset(target.AssetPath, request, applyChanges, items, ref replacedInstanceCount);
        }

        private static bool ProcessPrefabAsset(
            string prefabPath,
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            ref int replacedInstanceCount)
        {
            if (!File.Exists(prefabPath))
                return false;

            GameObject root = null;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return false;

                bool changed = ProcessHierarchy(root, prefabPath, request, applyChanges, false, items,
                    ref replacedInstanceCount);

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
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            ref int replacedInstanceCount)
        {
            if (!File.Exists(scenePath))
                return false;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;

            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid() || !scene.isLoaded)
                    return false;

                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null)
                        continue;

                    if (ProcessHierarchy(root, scenePath, request, applyChanges, true, items,
                            ref replacedInstanceCount))
                        changed = true;
                }

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

        private static bool ProcessOpenPrefabStage(
            string prefabPath,
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            ref int replacedInstanceCount)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null)
                return false;

            if (!string.Equals(stage.assetPath, prefabPath, StringComparison.OrdinalIgnoreCase))
                return false;

            bool changed = ProcessHierarchy(
                stage.prefabContentsRoot,
                prefabPath,
                request,
                applyChanges,
                false,
                items,
                ref replacedInstanceCount);

            if (applyChanges && changed)
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, prefabPath);

            return changed;
        }

        private static bool ProcessOpenScene(
            string sceneLabel,
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            ref int replacedInstanceCount)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null)
                    continue;

                if (ProcessHierarchy(root, sceneLabel, request, applyChanges, true, items, ref replacedInstanceCount))
                    changed = true;
            }

            if (applyChanges && changed && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            return changed;
        }

        private static bool ProcessHierarchy(
            GameObject current,
            string fileAssetPath,
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges,
            bool isScene,
            List<AssetUsageFinderPrefabOrVariantReplacePreviewItem> items,
            ref int replacedInstanceCount)
        {
            if (current == null)
                return false;

            if (PrefabUtility.IsAnyPrefabInstanceRoot(current) &&
                TryBuildPreviewItem(current, fileAssetPath, request, isScene,
                    out AssetUsageFinderPrefabOrVariantReplacePreviewItem item))
            {
                items.Add(item);

                if (applyChanges && ReplacePrefabInstanceRoot(current, request))
                {
                    replacedInstanceCount++;
                    return true;
                }

                return false;
            }

            bool changed = false;
            List<Transform> children = new();

            foreach (Transform child in current.transform)
            {
                if (child != null)
                    children.Add(child);
            }

            foreach (Transform child in children)
            {
                if (child == null)
                    continue;

                if (ProcessHierarchy(child.gameObject, fileAssetPath, request, applyChanges, isScene, items,
                        ref replacedInstanceCount))
                    changed = true;
            }

            return changed;
        }

        private static bool TryBuildPreviewItem(
            GameObject instanceRoot,
            string fileAssetPath,
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool isScene,
            out AssetUsageFinderPrefabOrVariantReplacePreviewItem item)
        {
            item = null;

            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (string.IsNullOrEmpty(sourcePrefabPath))
                return false;

            string fromPrefabPath = AssetDatabase.GetAssetPath(request.FromPrefab);
            if (string.IsNullOrEmpty(fromPrefabPath))
                return false;

            bool isDirectMatch = string.Equals(sourcePrefabPath, fromPrefabPath, StringComparison.OrdinalIgnoreCase);
            bool isVariantMatch = !isDirectMatch &&
                                  request.IncludeVariants &&
                                  IsVariantOfSelectedPrefab(sourcePrefabPath, fromPrefabPath);

            if (!isDirectMatch && !isVariantMatch)
                return false;

            item = new AssetUsageFinderPrefabOrVariantReplacePreviewItem(
                fileAssetPath,
                GetHierarchyPath(instanceRoot),
                sourcePrefabPath,
                isScene,
                isVariantMatch);

            return true;
        }

        private static bool ReplacePrefabInstanceRoot(
            GameObject instanceRoot,
            AssetUsageFinderPrefabOrVariantReplaceRequest request)
        {
            if (instanceRoot == null || request?.ToPrefab == null)
                return false;

            Transform oldTransform = instanceRoot.transform;
            Transform parent = oldTransform.parent;
            int siblingIndex = oldTransform.GetSiblingIndex();
            Scene destinationScene = instanceRoot.scene;

            Vector3 localPosition = oldTransform.localPosition;
            Quaternion localRotation = oldTransform.localRotation;
            Vector3 localScale = oldTransform.localScale;

            string oldName = instanceRoot.name;
            string oldTag = instanceRoot.tag;
            int oldLayer = instanceRoot.layer;
            bool oldIsStatic = instanceRoot.isStatic;
            bool oldActiveSelf = instanceRoot.activeSelf;

            PropertyModification[] propertyModifications = request.KeepOverrides
                ? SafeGetPropertyModifications(instanceRoot)
                : null;

            GameObject newInstance = null;

            try
            {
                newInstance = PrefabUtility.InstantiatePrefab(request.ToPrefab, destinationScene) as GameObject;
                if (newInstance == null)
                    return false;

                if (parent != null)
                    newInstance.transform.SetParent(parent, false);

                newInstance.transform.SetSiblingIndex(siblingIndex);
                newInstance.transform.localPosition = localPosition;
                newInstance.transform.localRotation = localRotation;
                newInstance.transform.localScale = localScale;
                newInstance.SetActive(oldActiveSelf);

                if (request.CopyCommonRootComponentValues)
                    TryCopyCommonRootComponentValues(instanceRoot, newInstance);

                if (request.KeepOverrides)
                {
                    newInstance.name = oldName;
                    newInstance.tag = oldTag;
                    newInstance.layer = oldLayer;
                    newInstance.isStatic = oldIsStatic;
                    TryApplyPropertyModifications(newInstance, propertyModifications);
                }

                TryRemapReferencesToReplacedInstance(
                    destinationScene,
                    instanceRoot,
                    newInstance,
                    request.FromPrefab,
                    request.ToPrefab);

                Object.DestroyImmediate(instanceRoot);
                return true;
            }
            catch
            {
                if (newInstance != null)
                    Object.DestroyImmediate(newInstance);

                return false;
            }
        }

        private static void TryCopyCommonRootComponentValues(GameObject sourceRoot, GameObject targetRoot)
        {
            if (sourceRoot == null || targetRoot == null)
                return;

            try
            {
                Dictionary<Type, Queue<Component>> targetComponentsByType = BuildRootComponentLookup(targetRoot);
                Component[] sourceComponents = sourceRoot.GetComponents<Component>();

                foreach (Component sourceComponent in sourceComponents)
                {
                    if (sourceComponent == null)
                        continue;

                    Type componentType = sourceComponent.GetType();
                    if (componentType == typeof(Transform))
                        continue;

                    if (!targetComponentsByType.TryGetValue(componentType, out Queue<Component> targetComponents) ||
                        targetComponents.Count == 0)
                        continue;

                    Component targetComponent = targetComponents.Dequeue();
                    TryCopySerializedValues(sourceComponent, targetComponent);
                }
            }
            catch
            {
                // ignored
            }
        }

        private static Dictionary<Type, Queue<Component>> BuildRootComponentLookup(GameObject root)
        {
            Dictionary<Type, Queue<Component>> componentsByType = new();
            if (root == null)
                return componentsByType;

            Component[] components = root.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                Type componentType = component.GetType();
                if (componentType == typeof(Transform))
                    continue;

                if (!componentsByType.TryGetValue(componentType, out Queue<Component> bucket))
                {
                    bucket = new Queue<Component>();
                    componentsByType.Add(componentType, bucket);
                }

                bucket.Enqueue(component);
            }

            return componentsByType;
        }

        private static void TryCopySerializedValues(
            Component sourceComponent,
            Component targetComponent)
        {
            if (sourceComponent == null || targetComponent == null)
                return;

            if (sourceComponent.GetType() != targetComponent.GetType())
                return;

            try
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(sourceComponent);
                UnityEditorInternal.ComponentUtility.PasteComponentValues(targetComponent);
            }
            catch
            {
                // ignored
            }
        }

        private static void TryRemapReferencesToReplacedInstance(
            Scene scene,
            GameObject sourceRoot,
            GameObject targetRoot,
            GameObject fromPrefabAsset,
            GameObject toPrefabAsset)
        {
            if (!scene.IsValid() || !scene.isLoaded || sourceRoot == null || targetRoot == null)
                return;

            try
            {
                Dictionary<Object, Object> referenceMap = AssetUsageFinderPrefabReferenceMapBuilder.Build(
                    sourceRoot,
                    targetRoot,
                    fromPrefabAsset,
                    toPrefabAsset);

                if (referenceMap.Count == 0)
                    return;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null)
                        continue;

                    TryRemapReferencesInHierarchy(root, sourceRoot.transform, referenceMap);
                }
            }
            catch
            {
                // ignored
            }
        }

        private static void TryRemapReferencesInHierarchy(
            GameObject current,
            Transform sourceRootTransform,
            Dictionary<Object, Object> referenceMap)
        {
            if (current == null || referenceMap == null || referenceMap.Count == 0)
                return;

            if (sourceRootTransform != null &&
                (current.transform == sourceRootTransform || current.transform.IsChildOf(sourceRootTransform)))
                return;

            Component[] components = current.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null || component is Transform)
                    continue;

                TryRemapSerializedObjectReferences(component, referenceMap);
            }

            foreach (Transform child in current.transform)
            {
                if (child != null)
                    TryRemapReferencesInHierarchy(child.gameObject, sourceRootTransform, referenceMap);
            }
        }

        private static void TryRemapSerializedObjectReferences(
            Component component,
            Dictionary<Object, Object> referenceMap)
        {
            if (component == null || referenceMap == null || referenceMap.Count == 0)
                return;

            try
            {
                SerializedObject serializedObject = new(component);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                bool changed = false;

                while (iterator.Next(enterChildren))
                {
                    enterChildren = true;

                    if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    Object currentReference = iterator.objectReferenceValue;
                    if (currentReference == null)
                        continue;

                    if (!referenceMap.TryGetValue(currentReference, out Object newReference))
                        continue;

                    if (currentReference == newReference)
                        continue;

                    iterator.objectReferenceValue = newReference;
                    changed = true;
                }

                if (changed)
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
            catch
            {
                // ignored
            }
        }

        private static PropertyModification[] SafeGetPropertyModifications(GameObject instanceRoot)
        {
            try
            {
                return PrefabUtility.GetPropertyModifications(instanceRoot);
            }
            catch
            {
                return null;
            }
        }

        private static void TryApplyPropertyModifications(GameObject newInstance, PropertyModification[] modifications)
        {
            if (newInstance == null || modifications == null || modifications.Length == 0)
                return;

            try
            {
                PrefabUtility.SetPropertyModifications(newInstance, modifications);
            }
            catch
            {
                // ignored
            }
        }

        private static bool IsVariantOfSelectedPrefab(string candidatePrefabPath, string selectedPrefabPath)
        {
            if (string.IsNullOrEmpty(candidatePrefabPath) || string.IsNullOrEmpty(selectedPrefabPath))
                return false;

            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePrefabPath);
            if (candidate == null)
                return false;

            if (PrefabUtility.GetPrefabAssetType(candidate) != PrefabAssetType.Variant)
                return false;

            HashSet<string> chain = GetVariantAncestry(candidate)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return chain.Contains(selectedPrefabPath);
        }

        private static IEnumerable<GameObject> GetVariantAncestry(GameObject start, int maxHops = 32)
        {
            List<GameObject> ancestry = new();
            GameObject current = start;
            int guard = 0;

            while (current != null && guard++ < maxHops)
            {
                ancestry.Add(current);

                if (PrefabUtility.GetPrefabAssetType(current) != PrefabAssetType.Variant)
                    break;

                GameObject parent = PrefabUtility.GetCorrespondingObjectFromSource(current) as GameObject;
                if (parent == null || parent == current)
                    break;

                current = parent;
            }

            return ancestry;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null)
                return string.Empty;

            string path = go.name;
            Transform current = go.transform;

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
    }
}