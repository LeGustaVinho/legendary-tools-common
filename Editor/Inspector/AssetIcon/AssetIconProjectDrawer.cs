using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    internal static class AssetThumbnailProjectDrawer
    {
        private static readonly Color32 FreeBackground = new(190, 190, 190, 255);
        private static readonly Color32 ProBackground = new(51, 51, 51, 255);
        private static readonly Color32 SelectedTint = new(200, 200, 255, 255);

        static AssetThumbnailProjectDrawer()
        {
            EditorApplication.projectWindowItemOnGUI -= DrawProjectItem;
            EditorApplication.projectWindowItemOnGUI += DrawProjectItem;
        }

        private static void DrawProjectItem(string guid, Rect selectionRect)
        {
            if (string.IsNullOrEmpty(guid)) return;

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath)) return;

            Object thumbnail;
            if (!AssetThumbnailStore.instance.TryGetThumbnail(guid, out thumbnail))
            {
                Texture2D generatedThumbnail;
                if (!PrefabThumbnailCache.TryGetThumbnail(guid, out generatedThumbnail)) return;

                thumbnail = generatedThumbnail;
            }

            Rect iconRect = GetIconRect(selectionRect);
            EditorGUI.DrawRect(iconRect, EditorGUIUtility.isProSkin ? ProBackground : FreeBackground);

            Color previousColor = GUI.color;
            if (Array.IndexOf(Selection.assetGUIDs, guid) >= 0) GUI.color *= SelectedTint;

            try
            {
                DrawThumbnail(iconRect, thumbnail);
            }
            finally
            {
                GUI.color = previousColor;
            }
        }

        private static void DrawThumbnail(Rect iconRect, Object thumbnail)
        {
            switch (thumbnail)
            {
                case Sprite sprite:
                    DrawSprite(iconRect, sprite);
                    break;
                case Texture2D texture:
                    GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, true);
                    break;
            }
        }

        private static void DrawSprite(Rect iconRect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;

            Rect textureRect = sprite.textureRect;
            Rect uvRect = new(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);

            Rect drawRect = GetAspectFitRect(iconRect, textureRect.size);
            GUI.DrawTextureWithTexCoords(drawRect, sprite.texture, uvRect, true);
        }

        private static Rect GetIconRect(Rect itemRect)
        {
            bool treeView = IsTreeView(itemRect);
            Rect iconRect = itemRect;

            if (itemRect.width > itemRect.height)
            {
                iconRect.width = itemRect.height;
                if (!treeView) iconRect.x += itemRect.height * 0.25f;
            }
            else
                iconRect.height = itemRect.width;

            return iconRect;
        }

        private static bool IsTreeView(Rect rect)
        {
            return (rect.x - 16f) % 14f == 0f;
        }

        private static Rect GetAspectFitRect(Rect bounds, Vector2 sourceSize)
        {
            if (sourceSize.x <= 0f || sourceSize.y <= 0f) return bounds;

            float width = bounds.width;
            float height = bounds.height;
            float sourceAspect = sourceSize.x / sourceSize.y;
            float boundsAspect = bounds.width / bounds.height;

            if (boundsAspect > sourceAspect)
                width = bounds.height * sourceAspect;
            else
                height = bounds.width / sourceAspect;

            return new Rect(
                bounds.x + (bounds.width - width) * 0.5f,
                bounds.y + (bounds.height - height) * 0.5f,
                width,
                height);
        }
    }
}