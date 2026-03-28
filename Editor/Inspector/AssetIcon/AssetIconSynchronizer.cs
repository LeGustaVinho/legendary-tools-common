using System;
using System.Collections.Generic;
using System.Reflection;
using LegendaryTools.Inspector;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    internal static class ThumbnailIconAssetSynchronizer
    {
        private sealed class MemberAccessor
        {
            public string Description;
            public Func<ScriptableObject, Object> Getter;
        }

        private static readonly Dictionary<Type, MemberAccessor> AccessorByType = new();
        private static readonly HashSet<Type> InvalidTypeWarnings = new();
        private static readonly HashSet<Type> MultipleMemberWarnings = new();
        private static readonly HashSet<string> PendingAssetPaths = new(StringComparer.OrdinalIgnoreCase);

        private static bool _fullSyncScheduled;
        private static bool _pathSyncScheduled;

        static ThumbnailIconAssetSynchronizer()
        {
            ScheduleFullSync();
        }

        private static void ScheduleFullSync()
        {
            if (_fullSyncScheduled) return;

            _fullSyncScheduled = true;
            EditorApplication.delayCall += FlushFullSync;
        }

        private static void SchedulePathSync(IEnumerable<string> assetPaths)
        {
            if (assetPaths == null) return;

            bool hasPendingPath = false;

            foreach (string assetPath in assetPaths)
            {
                if (string.IsNullOrEmpty(assetPath) ||
                    !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;

                if (PendingAssetPaths.Add(assetPath)) hasPendingPath = true;
            }

            if (!hasPendingPath || _pathSyncScheduled) return;

            _pathSyncScheduled = true;
            EditorApplication.delayCall += FlushPathSync;
        }

        private static void FlushFullSync()
        {
            EditorApplication.delayCall -= FlushFullSync;
            _fullSyncScheduled = false;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleFullSync();
                return;
            }

            string[] scriptableObjectGuids = AssetDatabase.FindAssets("t:ScriptableObject");
            for (int i = 0; i < scriptableObjectGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(scriptableObjectGuids[i]);
                SyncAssetsAtPath(assetPath);
            }
        }

        private static void FlushPathSync()
        {
            EditorApplication.delayCall -= FlushPathSync;
            _pathSyncScheduled = false;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _pathSyncScheduled = true;
                EditorApplication.delayCall += FlushPathSync;
                return;
            }

            string[] assetPaths = new string[PendingAssetPaths.Count];
            PendingAssetPaths.CopyTo(assetPaths);
            PendingAssetPaths.Clear();

            for (int i = 0; i < assetPaths.Length; i++)
            {
                SyncAssetsAtPath(assetPaths[i]);
            }
        }

        private static void SyncAssetsAtPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is ScriptableObject scriptableObject) SyncAssetThumbnail(scriptableObject);
            }
        }

        private static void SyncAssetThumbnail(ScriptableObject asset)
        {
            if (asset == null || !AssetThumbnails.TryGetAssetGuid(asset, out string assetGuid)) return;

            if (!TryGetAccessor(asset.GetType(), out MemberAccessor accessor)) return;

            Object thumbnail = null;
            try
            {
                thumbnail = accessor.Getter(asset);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, asset);
            }

            if (thumbnail != null && AssetThumbnails.TryValidateThumbnail(thumbnail, out _))
            {
                AssetThumbnails.Set(assetGuid, thumbnail);
                return;
            }

            AssetThumbnails.Clear(assetGuid);
        }

        private static bool TryGetAccessor(Type type, out MemberAccessor accessor)
        {
            if (type == null)
            {
                accessor = null;
                return false;
            }

            if (AccessorByType.TryGetValue(type, out accessor)) return accessor != null;

            accessor = BuildAccessor(type);
            AccessorByType[type] = accessor;
            return accessor != null;
        }

        private static MemberAccessor BuildAccessor(Type type)
        {
            List<MemberAccessor> candidates = new();

            for (Type current = type;
                 current != null && typeof(ScriptableObject).IsAssignableFrom(current);
                 current = current.BaseType)
            {
                CollectFieldCandidates(current, candidates);
                CollectPropertyCandidates(current, candidates);
            }

            if (candidates.Count == 0) return null;

            if (candidates.Count > 1 && MultipleMemberWarnings.Add(type))
            {
                Debug.LogWarning(
                    $"Multiple [ThumbnailIcon] members were found on '{type.FullName}'. Using '{candidates[0].Description}'.");
            }

            return candidates[0];
        }

        private static void CollectFieldCandidates(Type type, List<MemberAccessor> candidates)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.DeclaredOnly);

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!Attribute.IsDefined(field, typeof(AssetIconAttribute), true)) continue;

                if (!IsSupportedThumbnailType(field.FieldType))
                {
                    WarnInvalidMemberType(type, $"{field.DeclaringType?.FullName}.{field.Name}", field.FieldType);
                    continue;
                }

                candidates.Add(new MemberAccessor
                {
                    Description = $"{field.DeclaringType?.Name}.{field.Name}",
                    Getter = asset => field.GetValue(asset) as Object
                });
            }
        }

        private static void CollectPropertyCandidates(Type type, List<MemberAccessor> candidates)
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                           BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (!Attribute.IsDefined(property, typeof(AssetIconAttribute), true)) continue;

                if (property.GetIndexParameters().Length > 0 || property.GetGetMethod(true) == null) continue;

                if (!IsSupportedThumbnailType(property.PropertyType))
                {
                    WarnInvalidMemberType(type, $"{property.DeclaringType?.FullName}.{property.Name}",
                        property.PropertyType);
                    continue;
                }

                candidates.Add(new MemberAccessor
                {
                    Description = $"{property.DeclaringType?.Name}.{property.Name}",
                    Getter = asset => property.GetValue(asset) as Object
                });
            }
        }

        private static bool IsSupportedThumbnailType(Type type)
        {
            return type == typeof(Sprite) || type == typeof(Texture2D);
        }

        private static void WarnInvalidMemberType(Type ownerType, string memberName, Type memberType)
        {
            if (!InvalidTypeWarnings.Add(ownerType)) return;

            Debug.LogWarning(
                $"[ThumbnailIcon] on '{memberName}' was ignored because the member type is '{memberType.Name}'. Only Sprite and Texture2D are supported.");
        }

        private sealed class ThumbnailIconAssetPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                bool requiresFullSync = ContainsScriptChange(importedAssets) ||
                                        ContainsScriptChange(deletedAssets) ||
                                        ContainsScriptChange(movedAssets) ||
                                        ContainsScriptChange(movedFromAssetPaths) ||
                                        HasAnyEntries(deletedAssets) ||
                                        HasAnyEntries(movedFromAssetPaths);

                if (requiresFullSync) ScheduleFullSync();

                SchedulePathSync(importedAssets);
                SchedulePathSync(movedAssets);
            }

            private static bool ContainsScriptChange(string[] assetPaths)
            {
                if (assetPaths == null) return false;

                for (int i = 0; i < assetPaths.Length; i++)
                {
                    string assetPath = assetPaths[i];
                    if (!string.IsNullOrEmpty(assetPath) &&
                        assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            private static bool HasAnyEntries(string[] assetPaths)
            {
                return assetPaths != null && assetPaths.Length > 0;
            }
        }
    }
}