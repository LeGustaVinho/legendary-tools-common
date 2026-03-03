using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public static class AssetUsageFinderPrefabReferenceMapBuilder
    {
        public static Dictionary<Object, Object> Build(
            GameObject sourceRoot,
            GameObject targetRoot,
            GameObject fromPrefabAsset,
            GameObject toPrefabAsset)
        {
            Dictionary<Object, Object> referenceMap = new();
            if (sourceRoot == null || targetRoot == null)
                return referenceMap;

            if (fromPrefabAsset != null && toPrefabAsset != null && fromPrefabAsset != toPrefabAsset)
                referenceMap[fromPrefabAsset] = toPrefabAsset;

            AddExplicitMappings(sourceRoot, targetRoot, fromPrefabAsset, toPrefabAsset, referenceMap);
            AddFallbackMappings(sourceRoot, targetRoot, referenceMap);
            return referenceMap;
        }

        private static void AddExplicitMappings(
            GameObject sourceRoot,
            GameObject targetRoot,
            GameObject fromPrefabAsset,
            GameObject toPrefabAsset,
            Dictionary<Object, Object> referenceMap)
        {
            if (sourceRoot == null || targetRoot == null || referenceMap == null)
                return;

            string fromPrefabPath = AssetUsageFinderPrefabExplicitMappingStore.GetPrefabAssetPath(fromPrefabAsset);
            string toPrefabPath = AssetUsageFinderPrefabExplicitMappingStore.GetPrefabAssetPath(toPrefabAsset);
            if (string.IsNullOrEmpty(fromPrefabPath) || string.IsNullOrEmpty(toPrefabPath))
                return;

            AssetUsageFinderPrefabExplicitMappingProfile profile =
                AssetUsageFinderPrefabExplicitMappingStore.GetProfile(fromPrefabPath, toPrefabPath);

            if (profile?.Entries == null || profile.Entries.Count == 0)
                return;

            for (int i = 0; i < profile.Entries.Count; i++)
            {
                AssetUsageFinderPrefabExplicitRemapEntry entry = profile.Entries[i];
                if (entry?.From == null || entry.To == null)
                    continue;

                if (!AssetUsageFinderPrefabExplicitMappingStore.TryResolveObject(sourceRoot, entry.From,
                        out Object sourceObject))
                    continue;

                if (!AssetUsageFinderPrefabExplicitMappingStore.TryResolveObject(targetRoot, entry.To,
                        out Object targetObject))
                    continue;

                if (sourceObject == null || targetObject == null || sourceObject == targetObject)
                    continue;

                bool sourceIsNode = IsNodeReference(sourceObject);
                bool targetIsNode = IsNodeReference(targetObject);
                if (sourceIsNode != targetIsNode)
                    continue;

                if (TryAddNodeMappings(sourceObject, targetObject, referenceMap))
                    continue;

                referenceMap[sourceObject] = targetObject;
            }
        }

        private static bool TryAddNodeMappings(
            Object sourceObject,
            Object targetObject,
            Dictionary<Object, Object> referenceMap)
        {
            if (!TryGetNodeTransform(sourceObject, out Transform sourceTransform) ||
                !TryGetNodeTransform(targetObject, out Transform targetTransform))
                return false;

            if (sourceTransform == null || targetTransform == null)
                return false;

            referenceMap[sourceTransform] = targetTransform;
            referenceMap[sourceTransform.gameObject] = targetTransform.gameObject;
            return true;
        }

        private static void AddFallbackMappings(
            GameObject sourceRoot,
            GameObject targetRoot,
            Dictionary<Object, Object> referenceMap)
        {
            foreach (Transform sourceTransform in sourceRoot.GetComponentsInChildren<Transform>(true))
            {
                if (sourceTransform == null)
                    continue;

                if (!TryGetRelativeTransformPath(sourceTransform, sourceRoot.transform, out string path))
                    continue;

                Transform targetTransform = FindTargetTransform(targetRoot.transform, path);
                if (targetTransform == null)
                    continue;

                if (!referenceMap.ContainsKey(sourceTransform))
                    referenceMap[sourceTransform] = targetTransform;

                if (!referenceMap.ContainsKey(sourceTransform.gameObject))
                    referenceMap[sourceTransform.gameObject] = targetTransform.gameObject;

                AddComponentMappings(sourceTransform.gameObject, targetTransform.gameObject, referenceMap);
            }
        }

        private static void AddComponentMappings(
            GameObject sourceGameObject,
            GameObject targetGameObject,
            Dictionary<Object, Object> referenceMap)
        {
            if (sourceGameObject == null || targetGameObject == null || referenceMap == null)
                return;

            Component[] sourceComponents = sourceGameObject.GetComponents<Component>();
            Dictionary<Type, int> componentTypeIndices = new();

            foreach (Component sourceComponent in sourceComponents)
            {
                if (sourceComponent == null)
                    continue;

                Type componentType = sourceComponent.GetType();
                if (!componentTypeIndices.TryGetValue(componentType, out int componentIndex))
                    componentIndex = 0;

                componentTypeIndices[componentType] = componentIndex + 1;

                Component[] targetComponents = targetGameObject.GetComponents(componentType);
                if (componentIndex >= targetComponents.Length)
                    continue;

                if (referenceMap.ContainsKey(sourceComponent))
                    continue;

                Component targetComponent = targetComponents[componentIndex];
                if (targetComponent != null)
                    referenceMap[sourceComponent] = targetComponent;
            }
        }

        private static bool TryGetRelativeTransformPath(
            Transform candidate,
            Transform root,
            out string path)
        {
            path = string.Empty;

            if (candidate == null || root == null)
                return false;

            if (candidate == root)
                return true;

            List<string> segments = new();
            Transform current = candidate;

            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != root)
                return false;

            segments.Reverse();
            path = string.Join("/", segments);
            return true;
        }

        private static Transform FindTargetTransform(Transform root, string relativePath)
        {
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(relativePath))
                return root;

            return root.Find(relativePath);
        }

        private static bool TryGetNodeTransform(Object obj, out Transform transform)
        {
            transform = null;

            switch (obj)
            {
                case GameObject gameObject:
                    transform = gameObject.transform;
                    return transform != null;

                case Transform transformComponent:
                    transform = transformComponent;
                    return true;

                default:
                    return false;
            }
        }

        private static bool IsNodeReference(Object obj)
        {
            return obj is GameObject || obj is Transform;
        }
    }
}