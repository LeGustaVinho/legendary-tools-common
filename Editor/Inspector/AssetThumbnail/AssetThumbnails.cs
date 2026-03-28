using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public static class AssetThumbnails
    {
        public static bool Set(Object asset, Object thumbnail)
        {
            if (!TryGetAssetGuid(asset, out string assetGuid)) return false;

            if (!TryValidateThumbnail(thumbnail, out _)) return false;

            return Set(assetGuid, thumbnail);
        }

        public static bool Clear(Object asset)
        {
            return TryGetAssetGuid(asset, out string assetGuid) && Clear(assetGuid);
        }

        public static bool HasThumbnail(Object asset)
        {
            return TryGetAssetGuid(asset, out string assetGuid) && HasThumbnail(assetGuid);
        }

        public static bool TryValidateThumbnail(Object thumbnail, out string validationMessage)
        {
            if (thumbnail == null)
            {
                validationMessage = "Choose a Texture2D or Sprite asset.";
                return false;
            }

            if (!EditorUtility.IsPersistent(thumbnail))
            {
                validationMessage = "The thumbnail must be an asset from the project.";
                return false;
            }

            if (thumbnail is Texture2D || thumbnail is Sprite)
            {
                validationMessage = string.Empty;
                return true;
            }

            validationMessage = "Only Texture2D and Sprite assets are supported.";
            return false;
        }

        internal static bool Set(string assetGuid, Object thumbnail)
        {
            if (string.IsNullOrEmpty(assetGuid) || !TryValidateThumbnail(thumbnail, out _)) return false;

            bool changed = AssetThumbnailStore.instance.SetThumbnail(assetGuid, thumbnail);
            if (changed) EditorApplication.RepaintProjectWindow();

            return changed;
        }

        internal static bool Clear(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid)) return false;

            bool changed = AssetThumbnailStore.instance.RemoveThumbnail(assetGuid);
            if (changed) EditorApplication.RepaintProjectWindow();

            return changed;
        }

        internal static bool HasThumbnail(string assetGuid)
        {
            return !string.IsNullOrEmpty(assetGuid) && AssetThumbnailStore.instance.HasThumbnail(assetGuid);
        }

        internal static bool TryGetAssetGuid(Object asset, out string assetGuid)
        {
            assetGuid = null;
            if (asset == null || !EditorUtility.IsPersistent(asset)) return false;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath)) return false;

            assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            return !string.IsNullOrEmpty(assetGuid);
        }
    }
}