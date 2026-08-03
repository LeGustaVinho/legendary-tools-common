using System;
using UnityEditor;

namespace LegendaryTools.Editor
{
    internal static class UITextureImporterApplier
    {
        public static bool Apply(UITextureOptimizationResult result, out string error)
        {
            error = string.Empty;
            if (result == null || !result.CanApply)
            {
                error = result == null
                    ? "The optimization result is missing."
                    : $"{result.AssetPath} no longer has an applicable Max Size reduction.";
                return false;
            }

            if (result.AssetPath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase))
            {
                error = "SpriteAtlas importers are never modified by this tool.";
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(result.AssetPath) as TextureImporter;
            if (importer == null)
            {
                error = $"TextureImporter not found for {result.AssetPath}.";
                return false;
            }

            try
            {
                Undo.RecordObject(importer, "Optimize UI Texture Max Size");
                importer.maxTextureSize = result.RecommendedMaxSize;
                importer.SaveAndReimport();
                result.RefreshImporterState();
                return true;
            }
            catch (Exception exception)
            {
                error = $"Failed to update {result.AssetPath}: {exception.Message}";
                return false;
            }
        }
    }
}
