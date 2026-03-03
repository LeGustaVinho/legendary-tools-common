using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderComponentReplaceBackend
    {
        private static readonly string[] CandidateExtensions = { ".prefab", ".unity" };

        public AssetUsageFinderComponentReplaceResult BuildPreview(
            AssetUsageFinderComponentReplaceRequest request,
            Action<float, string> progressCallback = null)
        {
            return Run(request, false, progressCallback);
        }

        public AssetUsageFinderComponentReplaceResult Apply(
            AssetUsageFinderComponentReplaceRequest request,
            Action<float, string> progressCallback = null)
        {
            return Run(request, true, progressCallback);
        }

        public static bool SupportsDisableInsteadOfRemove(Type componentType)
        {
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                return false;

            PropertyInfo enabledProperty = componentType.GetProperty(
                "enabled",
                BindingFlags.Instance | BindingFlags.Public);

            return enabledProperty != null &&
                   enabledProperty.CanWrite &&
                   enabledProperty.PropertyType == typeof(bool);
        }

        private static AssetUsageFinderComponentReplaceResult Run(
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            Action<float, string> progressCallback)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.FromType == null)
                throw new ArgumentException("From Type is required.", nameof(request));

            if (request.ToType == null)
                throw new ArgumentException("To Type is required.", nameof(request));

            List<AssetUsageFinderScopeTarget> targets =
                AssetUsageFinderSearchScopeUtility.CollectTargets(request.SearchScope, CandidateExtensions);
            List<AssetUsageFinderComponentReplacePreviewItem> items = new();
            int replacedComponentCount = 0;
            int affectedFileCount = 0;
            string activeScenePath = EditorSceneManager.GetActiveScene().path;

            for (int i = 0; i < targets.Count; i++)
            {
                AssetUsageFinderScopeTarget target = targets[i];
                string filePath = target.AssetPath;
                float progress = targets.Count == 0 ? 1f : Mathf.Clamp01((float)(i + 1) / targets.Count);
                progressCallback?.Invoke(progress, $"Processing {target.GetProgressLabel()}");

                bool changed = ProcessTarget(target, request, applyChanges, items, ref replacedComponentCount);

                if (changed || items.Any(item =>
                        string.Equals(item.FileAssetPath, filePath, StringComparison.OrdinalIgnoreCase)))
                    affectedFileCount++;
            }

            if (applyChanges)
                AssetDatabase.SaveAssets();

            RestoreActiveSceneBestEffort(activeScenePath);

            return new AssetUsageFinderComponentReplaceResult(items, replacedComponentCount, affectedFileCount);
        }

        private static bool ProcessTarget(
            AssetUsageFinderScopeTarget target,
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            ref int replacedComponentCount)
        {
            if (target == null || string.IsNullOrEmpty(target.AssetPath))
                return false;

            if (target.Kind == AssetUsageFinderScopeTargetKind.OpenPrefabStage)
            {
                return ProcessOpenPrefabStage(target.AssetPath, request, applyChanges, items,
                    ref replacedComponentCount);
            }

            if (target.Kind == AssetUsageFinderScopeTargetKind.OpenScene)
                return ProcessOpenScene(target.AssetPath, request, applyChanges, items, ref replacedComponentCount);

            return target.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? ProcessPrefabAsset(target.AssetPath, request, applyChanges, items, ref replacedComponentCount)
                : ProcessSceneAsset(target.AssetPath, request, applyChanges, items, ref replacedComponentCount);
        }

        private static bool ProcessPrefabAsset(
            string prefabPath,
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            ref int replacedComponentCount)
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
                    root, default, ref replacedComponentCount);

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
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            ref int replacedComponentCount)
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
                            null, scene, ref replacedComponentCount))
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
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            ref int replacedComponentCount)
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
                stage.prefabContentsRoot,
                default,
                ref replacedComponentCount);

            if (applyChanges && changed)
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, prefabPath);

            return changed;
        }

        private static bool ProcessOpenScene(
            string sceneLabel,
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            ref int replacedComponentCount)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null)
                    continue;

                if (ProcessHierarchy(root, sceneLabel, request, applyChanges, true, items,
                        null, scene, ref replacedComponentCount))
                    changed = true;
            }

            if (applyChanges && changed && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            return changed;
        }

        private static bool ProcessHierarchy(
            GameObject current,
            string fileAssetPath,
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges,
            bool isScene,
            List<AssetUsageFinderComponentReplacePreviewItem> items,
            GameObject prefabRoot,
            Scene scene,
            ref int replacedComponentCount)
        {
            if (current == null)
                return false;

            bool changed = false;
            Component[] allComponents = current.GetComponents<Component>();
            List<Component> matchingComponents = new();

            foreach (Component component in allComponents)
            {
                if (component != null && component.GetType() == request.FromType)
                    matchingComponents.Add(component);
            }

            int previewTargetCount = CountExactComponents(current, request.ToType);
            bool targetDisallowsMultiple = DisallowsMultiple(request.ToType);

            for (int i = 0; i < matchingComponents.Count; i++)
            {
                Component sourceComponent = matchingComponents[i];
                bool canAddTarget = applyChanges
                    ? CanAddTargetComponent(current, request.ToType)
                    : !targetDisallowsMultiple || previewTargetCount == 0;

                if (!canAddTarget)
                    continue;

                items.Add(new AssetUsageFinderComponentReplacePreviewItem(
                    fileAssetPath,
                    GetHierarchyPath(current),
                    matchingComponents.Count > 1 ? $"{request.FromType.Name} #{i + 1}" : request.FromType.Name,
                    request.FromType.FullName ?? request.FromType.Name,
                    request.ToType.FullName ?? request.ToType.Name,
                    isScene,
                    request.DisableOldComponentInsteadOfRemove));

                if (!applyChanges)
                {
                    previewTargetCount++;
                    continue;
                }

                if (ReplaceComponent(sourceComponent, request, prefabRoot, scene))
                {
                    replacedComponentCount++;
                    changed = true;
                }
            }

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
                        prefabRoot, scene, ref replacedComponentCount))
                    changed = true;
            }

            return changed;
        }

        private static bool ReplaceComponent(
            Component sourceComponent,
            AssetUsageFinderComponentReplaceRequest request,
            GameObject prefabRoot,
            Scene scene)
        {
            if (sourceComponent == null || request == null)
                return false;

            if (!CanAddTargetComponent(sourceComponent.gameObject, request.ToType))
                return false;

            Component newComponent = null;

            try
            {
                newComponent = sourceComponent.gameObject.AddComponent(request.ToType);
                if (newComponent == null)
                    return false;

                if (request.CopySerializedValues)
                    TryCopySerializedValues(sourceComponent, newComponent);

                if (request.DisableOldComponentInsteadOfRemove)
                {
                    if (!TryDisableComponent(sourceComponent))
                    {
                        Object.DestroyImmediate(newComponent);
                        return false;
                    }
                }

                TryRemapReferencesAfterComponentReplacement(sourceComponent, newComponent, prefabRoot, scene);

                if (!request.DisableOldComponentInsteadOfRemove)
                    Object.DestroyImmediate(sourceComponent);

                return true;
            }
            catch
            {
                if (newComponent != null)
                    Object.DestroyImmediate(newComponent);

                return false;
            }
        }

        private static bool CanAddTargetComponent(GameObject gameObject, Type targetType)
        {
            if (gameObject == null || targetType == null)
                return false;

            if (!typeof(Component).IsAssignableFrom(targetType))
                return false;

            if (targetType == typeof(Transform))
                return false;

            if (DisallowsMultiple(targetType) && CountExactComponents(gameObject, targetType) > 0)
                return false;

            return true;
        }

        private static void TryRemapReferencesAfterComponentReplacement(
            Component sourceComponent,
            Component targetComponent,
            GameObject prefabRoot,
            Scene scene)
        {
            if (sourceComponent == null || targetComponent == null)
                return;

            try
            {
                Dictionary<Object, Object> referenceMap = new()
                {
                    [sourceComponent] = targetComponent
                };

                if (scene.IsValid() && scene.isLoaded)
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        if (root != null)
                            TryRemapReferencesInHierarchy(root, sourceComponent, referenceMap);
                    }

                    return;
                }

                if (prefabRoot != null)
                    TryRemapReferencesInHierarchy(prefabRoot, sourceComponent, referenceMap);
            }
            catch
            {
                // ignored
            }
        }

        private static void TryRemapReferencesInHierarchy(
            GameObject current,
            Component ignoredSourceComponent,
            Dictionary<Object, Object> referenceMap)
        {
            if (current == null || referenceMap == null || referenceMap.Count == 0)
                return;

            foreach (Component component in current.GetComponents<Component>())
            {
                if (component == null || ReferenceEquals(component, ignoredSourceComponent))
                    continue;

                TryRemapSerializedObjectReferences(component, referenceMap);
            }

            foreach (Transform child in current.transform)
            {
                if (child != null)
                    TryRemapReferencesInHierarchy(child.gameObject, ignoredSourceComponent, referenceMap);
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

                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (iterator.propertyPath == "m_Script" ||
                        iterator.propertyType != SerializedPropertyType.ObjectReference)
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

                if (!changed)
                    return;

                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
            }
            catch
            {
                // ignored
            }
        }

        private static int CountExactComponents(GameObject gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null)
                return 0;

            int count = 0;
            Component[] components = gameObject.GetComponents<Component>();

            foreach (Component component in components)
            {
                if (component != null && component.GetType() == componentType)
                    count++;
            }

            return count;
        }

        private static bool DisallowsMultiple(Type componentType)
        {
            if (componentType == null)
                return false;

            return Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent), true);
        }

        private static void TryCopySerializedValues(Component sourceComponent, Component targetComponent)
        {
            if (sourceComponent == null || targetComponent == null)
                return;

            try
            {
                string json = EditorJsonUtility.ToJson(sourceComponent);
                if (!string.IsNullOrEmpty(json))
                    EditorJsonUtility.FromJsonOverwrite(json, targetComponent);
            }
            catch
            {
                // ignored
            }
        }

        private static bool TryDisableComponent(Component component)
        {
            if (component == null)
                return false;

            PropertyInfo enabledProperty = component.GetType().GetProperty(
                "enabled",
                BindingFlags.Instance | BindingFlags.Public);

            if (enabledProperty == null ||
                !enabledProperty.CanWrite ||
                enabledProperty.PropertyType != typeof(bool))
                return false;

            try
            {
                enabledProperty.SetValue(component, false);
                return true;
            }
            catch
            {
                return false;
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
    }
}