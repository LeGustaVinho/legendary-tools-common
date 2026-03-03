using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public static class AssetUsageFinderPrefabExplicitMappingStore
    {
        [Serializable]
        private sealed class AssetUsageFinderPrefabExplicitMappingStoreData
        {
            public List<AssetUsageFinderPrefabExplicitMappingProfile> Profiles = new();
        }

        private const string StoreFileName = "LegendaryTools.AssetUsageFinderPrefabMappings.json";

        private static readonly string StoreFilePath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", StoreFileName));

        private static AssetUsageFinderPrefabExplicitMappingStoreData _cache;

        public static bool IsValidPrefabAsset(Object asset)
        {
            string path = GetPrefabAssetPath(asset);
            if (string.IsNullOrEmpty(path))
                return false;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return false;

            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(prefab);
            return assetType == PrefabAssetType.Regular || assetType == PrefabAssetType.Variant;
        }

        public static string GetPrefabAssetPath(Object asset)
        {
            if (asset == null)
                return null;

            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
                return null;

            return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        public static int GetMappingCount(Object fromPrefab, Object toPrefab)
        {
            AssetUsageFinderPrefabExplicitMappingProfile profile = GetProfile(fromPrefab, toPrefab);
            return profile?.Entries?.Count ?? 0;
        }

        public static AssetUsageFinderPrefabExplicitMappingProfile GetProfile(Object fromPrefab, Object toPrefab)
        {
            string fromPath = GetPrefabAssetPath(fromPrefab);
            string toPath = GetPrefabAssetPath(toPrefab);
            if (string.IsNullOrEmpty(fromPath) || string.IsNullOrEmpty(toPath))
                return null;

            return GetProfile(fromPath, toPath);
        }

        public static AssetUsageFinderPrefabExplicitMappingProfile GetProfile(string fromPrefabPath,
            string toPrefabPath)
        {
            if (string.IsNullOrEmpty(fromPrefabPath) || string.IsNullOrEmpty(toPrefabPath))
                return null;

            EnsureLoaded();

            AssetUsageFinderPrefabExplicitMappingProfile profile = _cache.Profiles.FirstOrDefault(candidate =>
                IsSameProfile(candidate, fromPrefabPath, toPrefabPath));

            return profile?.Clone();
        }

        public static AssetUsageFinderPrefabExplicitMappingProfile GetOrCreateProfile(Object fromPrefab,
            Object toPrefab)
        {
            string fromPath = GetPrefabAssetPath(fromPrefab);
            string toPath = GetPrefabAssetPath(toPrefab);
            if (string.IsNullOrEmpty(fromPath) || string.IsNullOrEmpty(toPath))
                return null;

            AssetUsageFinderPrefabExplicitMappingProfile existing = GetProfile(fromPath, toPath);
            if (existing != null)
                return existing;

            return new AssetUsageFinderPrefabExplicitMappingProfile
            {
                FromPrefabPath = fromPath,
                ToPrefabPath = toPath
            };
        }

        public static void SaveProfile(AssetUsageFinderPrefabExplicitMappingProfile profile)
        {
            if (profile == null)
                return;

            if (string.IsNullOrEmpty(profile.FromPrefabPath) || string.IsNullOrEmpty(profile.ToPrefabPath))
                return;

            EnsureLoaded();

            AssetUsageFinderPrefabExplicitMappingProfile clone = profile.Clone();
            AssetUsageFinderPrefabExplicitMappingProfile existing = _cache.Profiles.FirstOrDefault(candidate =>
                IsSameProfile(candidate, clone.FromPrefabPath, clone.ToPrefabPath));

            if (existing == null)
                _cache.Profiles.Add(clone);
            else
            {
                existing.FromPrefabPath = clone.FromPrefabPath;
                existing.ToPrefabPath = clone.ToPrefabPath;
                existing.Entries = clone.Entries ?? new List<AssetUsageFinderPrefabExplicitRemapEntry>();
            }

            Persist();
        }

        public static List<AssetUsageFinderPrefabSubobjectDescriptor> GetAvailableDescriptors(Object prefabAsset)
        {
            string prefabPath = GetPrefabAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(prefabPath) || !File.Exists(prefabPath))
                return new List<AssetUsageFinderPrefabSubobjectDescriptor>();

            GameObject root = null;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return new List<AssetUsageFinderPrefabSubobjectDescriptor>();

                return BuildDescriptorList(root);
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static string GetDescriptorDisplayLabel(AssetUsageFinderPrefabSubobjectDescriptor descriptor)
        {
            if (descriptor == null)
                return "<None>";

            string objectPath = string.IsNullOrEmpty(descriptor.RelativePath) ? "<Root>" : descriptor.RelativePath;
            if (descriptor.Kind == AssetUsageFinderPrefabSubobjectKind.GameObject)
                return $"{objectPath} [GameObject]";

            string typeLabel = GetFriendlyTypeName(descriptor.ComponentTypeName);
            return $"{objectPath} [{typeLabel} #{descriptor.ComponentIndex}]";
        }

        public static bool TryResolveObject(
            GameObject root,
            AssetUsageFinderPrefabSubobjectDescriptor descriptor,
            out Object resolvedObject)
        {
            resolvedObject = null;

            if (root == null || descriptor == null)
                return false;

            Transform targetTransform = FindTransform(root.transform, descriptor.RelativePath);
            if (targetTransform == null)
                return false;

            if (descriptor.Kind == AssetUsageFinderPrefabSubobjectKind.GameObject)
            {
                resolvedObject = targetTransform.gameObject;
                return true;
            }

            Type componentType = ResolveComponentType(descriptor.ComponentTypeName);
            if (componentType == null)
                return false;

            Component[] components = targetTransform.GetComponents(componentType);
            if (descriptor.ComponentIndex < 0 || descriptor.ComponentIndex >= components.Length)
                return false;

            resolvedObject = components[descriptor.ComponentIndex];
            return resolvedObject != null;
        }

        private static void EnsureLoaded()
        {
            if (_cache != null)
                return;

            if (!File.Exists(StoreFilePath))
            {
                _cache = new AssetUsageFinderPrefabExplicitMappingStoreData();
                return;
            }

            try
            {
                string json = File.ReadAllText(StoreFilePath);
                _cache = string.IsNullOrWhiteSpace(json)
                    ? new AssetUsageFinderPrefabExplicitMappingStoreData()
                    : JsonUtility.FromJson<AssetUsageFinderPrefabExplicitMappingStoreData>(json);
            }
            catch
            {
                _cache = new AssetUsageFinderPrefabExplicitMappingStoreData();
            }

            _cache ??= new AssetUsageFinderPrefabExplicitMappingStoreData();
            _cache.Profiles ??= new List<AssetUsageFinderPrefabExplicitMappingProfile>();
        }

        private static void Persist()
        {
            EnsureLoaded();

            string directoryPath = Path.GetDirectoryName(StoreFilePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string json = JsonUtility.ToJson(_cache, true);
            File.WriteAllText(StoreFilePath, json);
        }

        private static bool IsSameProfile(
            AssetUsageFinderPrefabExplicitMappingProfile candidate,
            string fromPrefabPath,
            string toPrefabPath)
        {
            if (candidate == null)
                return false;

            return string.Equals(candidate.FromPrefabPath, fromPrefabPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(candidate.ToPrefabPath, toPrefabPath, StringComparison.OrdinalIgnoreCase);
        }

        private static List<AssetUsageFinderPrefabSubobjectDescriptor> BuildDescriptorList(GameObject root)
        {
            List<AssetUsageFinderPrefabSubobjectDescriptor> descriptors = new();
            if (root == null)
                return descriptors;

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                    continue;

                string relativePath = GetRelativeTransformPath(transform, root.transform);
                descriptors.Add(new AssetUsageFinderPrefabSubobjectDescriptor
                {
                    RelativePath = relativePath,
                    Kind = AssetUsageFinderPrefabSubobjectKind.GameObject
                });

                Component[] components = transform.GetComponents<Component>();
                Dictionary<Type, int> indicesByType = new();

                foreach (Component component in components)
                {
                    if (component == null)
                        continue;

                    Type componentType = component.GetType();
                    if (!indicesByType.TryGetValue(componentType, out int componentIndex))
                        componentIndex = 0;

                    indicesByType[componentType] = componentIndex + 1;

                    descriptors.Add(new AssetUsageFinderPrefabSubobjectDescriptor
                    {
                        RelativePath = relativePath,
                        Kind = AssetUsageFinderPrefabSubobjectKind.Component,
                        ComponentTypeName =
                            componentType.AssemblyQualifiedName ?? componentType.FullName ?? string.Empty,
                        ComponentIndex = componentIndex
                    });
                }
            }

            return descriptors;
        }

        private static string GetRelativeTransformPath(Transform candidate, Transform root)
        {
            if (candidate == null || root == null || candidate == root)
                return string.Empty;

            List<string> segments = new();
            Transform current = candidate;

            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != root)
                return string.Empty;

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static Transform FindTransform(Transform root, string relativePath)
        {
            if (root == null)
                return null;

            return string.IsNullOrEmpty(relativePath) ? root : root.Find(relativePath);
        }

        private static Type ResolveComponentType(string componentTypeName)
        {
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return null;

            string normalizedName = componentTypeName.Trim();
            Type resolvedType = Type.GetType(normalizedName, false);
            if (resolvedType != null)
                return resolvedType;

            return TypeCache.GetTypesDerivedFrom<Component>()
                .FirstOrDefault(type =>
                    string.Equals(type.AssemblyQualifiedName, normalizedName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, normalizedName, StringComparison.Ordinal) ||
                    string.Equals(type.Name, normalizedName, StringComparison.Ordinal));
        }

        private static string GetFriendlyTypeName(string componentTypeName)
        {
            Type componentType = ResolveComponentType(componentTypeName);
            if (componentType != null)
                return componentType.Name;

            if (string.IsNullOrWhiteSpace(componentTypeName))
                return "Component";

            int commaIndex = componentTypeName.IndexOf(',');
            string shortName = commaIndex >= 0 ? componentTypeName[..commaIndex] : componentTypeName;
            int lastDotIndex = shortName.LastIndexOf('.');
            return lastDotIndex >= 0 ? shortName[(lastDotIndex + 1)..] : shortName;
        }
    }
}