using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    [Flags]
    public enum AssetUsageFinderSearchScope
    {
        None = 0,
        ProjectScenes = 1 << 0,
        OpenScene = 1 << 1,
        ProjectPrefabs = 1 << 2,
        OpenPrefab = 1 << 3,
        Materials = 1 << 4,
        ScriptableObjects = 1 << 5,
        OtherAssets = 1 << 6
    }

    public enum AssetUsageFinderScopeTargetKind
    {
        AssetPath = 0,
        OpenScene = 1,
        OpenPrefabStage = 2
    }

    public sealed class AssetUsageFinderScopeTarget
    {
        public AssetUsageFinderScopeTargetKind Kind { get; }
        public string AssetPath { get; }

        public AssetUsageFinderScopeTarget(AssetUsageFinderScopeTargetKind kind, string assetPath)
        {
            Kind = kind;
            AssetPath = assetPath ?? string.Empty;
        }

        public string GetProgressLabel()
        {
            return Kind switch
            {
                AssetUsageFinderScopeTargetKind.OpenScene => $"Open Scene: {AssetPath}",
                AssetUsageFinderScopeTargetKind.OpenPrefabStage => $"Open Prefab: {AssetPath}",
                _ => AssetPath
            };
        }
    }

    public static class AssetUsageFinderSearchScopeUtility
    {
        public const string UnsavedOpenSceneKey = "<Unsaved Open Scene>";

        public const AssetUsageFinderSearchScope DefaultProjectScope =
            AssetUsageFinderSearchScope.ProjectScenes |
            AssetUsageFinderSearchScope.ProjectPrefabs |
            AssetUsageFinderSearchScope.Materials |
            AssetUsageFinderSearchScope.ScriptableObjects |
            AssetUsageFinderSearchScope.OtherAssets;

        public const AssetUsageFinderSearchScope HierarchyScopes =
            AssetUsageFinderSearchScope.ProjectScenes |
            AssetUsageFinderSearchScope.OpenScene |
            AssetUsageFinderSearchScope.ProjectPrefabs |
            AssetUsageFinderSearchScope.OpenPrefab;

        public const AssetUsageFinderSearchScope AssetScopes =
            AssetUsageFinderSearchScope.Materials |
            AssetUsageFinderSearchScope.ScriptableObjects |
            AssetUsageFinderSearchScope.OtherAssets;

        public static bool HasAnySelection(AssetUsageFinderSearchScope scope)
        {
            return scope != AssetUsageFinderSearchScope.None;
        }

        public static bool HasAnyHierarchySelection(AssetUsageFinderSearchScope scope)
        {
            return (scope & HierarchyScopes) != AssetUsageFinderSearchScope.None;
        }

        public static bool IsUnsavedOpenSceneKey(string assetPath)
        {
            return string.Equals(assetPath, UnsavedOpenSceneKey, StringComparison.Ordinal);
        }

        public static bool MatchesUsageType(AssetUsageFinderUsageType type, AssetUsageFinderSearchScope scope)
        {
            return type switch
            {
                AssetUsageFinderUsageType.Scene => HasSceneSelection(scope),
                AssetUsageFinderUsageType.SceneWithPrefabInstance => HasSceneSelection(scope),
                AssetUsageFinderUsageType.Prefab => HasPrefabSelection(scope),
                AssetUsageFinderUsageType.Material => scope.HasFlag(AssetUsageFinderSearchScope.Materials),
                AssetUsageFinderUsageType.ScriptableObject =>
                    scope.HasFlag(AssetUsageFinderSearchScope.ScriptableObjects),
                AssetUsageFinderUsageType.Other => scope.HasFlag(AssetUsageFinderSearchScope.OtherAssets),
                _ => false
            };
        }

        public static bool HasSceneSelection(AssetUsageFinderSearchScope scope)
        {
            return scope.HasFlag(AssetUsageFinderSearchScope.ProjectScenes) ||
                   scope.HasFlag(AssetUsageFinderSearchScope.OpenScene);
        }

        public static bool HasPrefabSelection(AssetUsageFinderSearchScope scope)
        {
            return scope.HasFlag(AssetUsageFinderSearchScope.ProjectPrefabs) ||
                   scope.HasFlag(AssetUsageFinderSearchScope.OpenPrefab);
        }

        public static List<AssetUsageFinderScopeTarget> CollectTargets(
            AssetUsageFinderSearchScope scope,
            IReadOnlyCollection<string> supportedExtensions)
        {
            List<AssetUsageFinderScopeTarget> targets = new();
            if (!HasAnySelection(scope) || supportedExtensions == null || supportedExtensions.Count == 0)
                return targets;

            HashSet<string> supportedExt = new(StringComparer.OrdinalIgnoreCase);
            foreach (string ext in supportedExtensions)
            {
                if (string.IsNullOrWhiteSpace(ext))
                    continue;

                string normalized = ext.StartsWith(".", StringComparison.Ordinal) ? ext : $".{ext}";
                supportedExt.Add(normalized);
            }

            if (supportedExt.Count == 0)
                return targets;

            string openScenePath = TryGetOpenScenePath(supportedExt, scope);
            string openPrefabPath = TryGetOpenPrefabPath(supportedExt, scope);

            if (ShouldScanProject(scope))
            {
                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    string ext = Path.GetExtension(path);
                    if (string.IsNullOrEmpty(ext) || !supportedExt.Contains(ext))
                        continue;

                    AssetUsageFinderSearchScope fileScope = GetProjectScopeForPath(path);
                    if (fileScope == AssetUsageFinderSearchScope.None || !scope.HasFlag(fileScope))
                        continue;

                    if (!string.IsNullOrEmpty(openScenePath) &&
                        string.Equals(path, openScenePath, StringComparison.OrdinalIgnoreCase) &&
                        fileScope == AssetUsageFinderSearchScope.ProjectScenes)
                        continue;

                    if (!string.IsNullOrEmpty(openPrefabPath) &&
                        string.Equals(path, openPrefabPath, StringComparison.OrdinalIgnoreCase) &&
                        fileScope == AssetUsageFinderSearchScope.ProjectPrefabs)
                        continue;

                    if (!seen.Add(path))
                        continue;

                    targets.Add(new AssetUsageFinderScopeTarget(AssetUsageFinderScopeTargetKind.AssetPath, path));
                }
            }

            if (!string.IsNullOrEmpty(openScenePath))
            {
                targets.Add(new AssetUsageFinderScopeTarget(AssetUsageFinderScopeTargetKind.OpenScene, openScenePath));
            }

            if (!string.IsNullOrEmpty(openPrefabPath))
            {
                targets.Add(new AssetUsageFinderScopeTarget(
                    AssetUsageFinderScopeTargetKind.OpenPrefabStage,
                    openPrefabPath));
            }

            targets.Sort(CompareTargets);
            return targets;
        }

        private static bool ShouldScanProject(AssetUsageFinderSearchScope scope)
        {
            return scope.HasFlag(AssetUsageFinderSearchScope.ProjectScenes) ||
                   scope.HasFlag(AssetUsageFinderSearchScope.ProjectPrefabs) ||
                   scope.HasFlag(AssetUsageFinderSearchScope.Materials) ||
                   scope.HasFlag(AssetUsageFinderSearchScope.ScriptableObjects) ||
                   scope.HasFlag(AssetUsageFinderSearchScope.OtherAssets);
        }

        private static string TryGetOpenScenePath(
            IReadOnlyCollection<string> supportedExt,
            AssetUsageFinderSearchScope scope)
        {
            if (!scope.HasFlag(AssetUsageFinderSearchScope.OpenScene) || !supportedExt.Contains(".unity"))
                return string.Empty;

            Scene scene = EditorSceneManager.GetActiveScene();
            string path = scene.path;

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;

            return scene.IsValid() && scene.isLoaded ? UnsavedOpenSceneKey : string.Empty;
        }

        private static string TryGetOpenPrefabPath(
            IReadOnlyCollection<string> supportedExt,
            AssetUsageFinderSearchScope scope)
        {
            if (!scope.HasFlag(AssetUsageFinderSearchScope.OpenPrefab) || !supportedExt.Contains(".prefab"))
                return string.Empty;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            string path = stage != null ? stage.assetPath : string.Empty;

            return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : string.Empty;
        }

        private static AssetUsageFinderSearchScope GetProjectScopeForPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return AssetUsageFinderSearchScope.None;

            string ext = Path.GetExtension(path);

            if (string.Equals(ext, ".unity", StringComparison.OrdinalIgnoreCase))
                return AssetUsageFinderSearchScope.ProjectScenes;

            if (string.Equals(ext, ".prefab", StringComparison.OrdinalIgnoreCase))
                return AssetUsageFinderSearchScope.ProjectPrefabs;

            if (string.Equals(ext, ".mat", StringComparison.OrdinalIgnoreCase))
                return AssetUsageFinderSearchScope.Materials;

            if (string.Equals(ext, ".asset", StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                return mainAsset is ScriptableObject
                    ? AssetUsageFinderSearchScope.ScriptableObjects
                    : AssetUsageFinderSearchScope.OtherAssets;
            }

            return AssetUsageFinderSearchScope.OtherAssets;
        }

        private static int CompareTargets(AssetUsageFinderScopeTarget left, AssetUsageFinderScopeTarget right)
        {
            int pathCompare = string.Compare(left?.AssetPath, right?.AssetPath, StringComparison.OrdinalIgnoreCase);
            if (pathCompare != 0)
                return pathCompare;

            return (left?.Kind ?? AssetUsageFinderScopeTargetKind.AssetPath)
                .CompareTo(right?.Kind ?? AssetUsageFinderScopeTargetKind.AssetPath);
        }
    }
}
