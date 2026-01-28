using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    internal static class UIPaletteUtilities
    {
        public const string DragSpriteGuidKey = "UIPaletteSpriteGuid";

        public static List<string> FindSpriteGuids(string labelFilter)
        {
            string query = string.IsNullOrWhiteSpace(labelFilter)
                ? "t:Sprite"
                : $"t:Sprite l:{labelFilter}";

            return AssetDatabase.FindAssets(query).ToList();
        }

        public static Sprite LoadSpriteFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        public static string GetAssetNameFromGuid(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public static void AddToFrontUnique(List<string> list, string guid, int maxCount)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            list.Remove(guid);
            list.Insert(0, guid);

            if (maxCount > 0 && list.Count > maxCount)
                list.RemoveRange(maxCount, list.Count - maxCount);
        }

        public static bool IsFavorite(UIPaletteState state, string guid)
        {
            return state.FavoriteGuids.Contains(guid);
        }

        public static void ToggleFavorite(UIPaletteState state, string guid)
        {
            if (state.FavoriteGuids.Contains(guid))
                state.FavoriteGuids.Remove(guid);
            else
                state.FavoriteGuids.Add(guid);

            state.Save();
        }

        /// <summary>
        /// Only returns true for drags started by UI Palette (requires GenericData key).
        /// This avoids hijacking Unity's default Sprite drag from Project window.
        /// </summary>
        public static bool TryGetDraggedSprite(out Sprite sprite, out string guid)
        {
            sprite = null;
            guid = DragAndDrop.GetGenericData(DragSpriteGuidKey) as string;

            // Strict: only accept drags created by our palette.
            if (string.IsNullOrEmpty(guid))
                return false;

            // Prefer loading by GUID (stable).
            sprite = LoadSpriteFromGuid(guid);
            if (sprite != null)
                return true;

            // Fallback: if something went weird, try objectReferences.
            if (DragAndDrop.objectReferences != null)
                for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
                {
                    if (DragAndDrop.objectReferences[i] is Sprite s)
                    {
                        sprite = s;
                        break;
                    }
                }

            if (sprite != null)
            {
                string path = AssetDatabase.GetAssetPath(sprite);
                if (!string.IsNullOrEmpty(path))
                    guid = AssetDatabase.AssetPathToGUID(path);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates or replaces a UI Image on the current selection. If a Button is selected, replaces its targetGraphic when possible.
        /// </summary>
        public static void CreateOrReplaceUIImage(Sprite sprite, bool useNativeSize)
        {
            if (sprite == null)
                return;

            GameObject selected = Selection.activeGameObject;

            if (TryReplaceOnSelection(selected, sprite, useNativeSize))
                return;

            Canvas canvas = FindBestCanvasForSelection(selected);
            if (canvas == null)
                canvas = CreateCanvasAndEventSystem();

            Transform parent = FindBestParentUnderCanvas(canvas, selected);
            CreateUIImageUnderParent(parent, sprite, useNativeSize, Vector2.zero);
        }

        /// <summary>
        /// Drop handler used by SceneView/Hierarchy drag and drop.
        /// </summary>
        public static void CreateUIImageForDrop(Sprite sprite, GameObject dropTarget, Vector2? screenPosition,
            Camera eventCamera, bool useNativeSize)
        {
            if (sprite == null)
                return;

            // If drop target is a selectable (especially Button), prefer replace behavior.
            if (TryReplaceOnSelection(dropTarget, sprite, useNativeSize))
                return;

            Canvas canvas = FindBestCanvasForSelection(dropTarget);
            if (canvas == null)
                canvas = CreateCanvasAndEventSystem();

            Transform parent = FindBestParentUnderCanvas(canvas, dropTarget);

            Vector2 anchored = Vector2.zero;
            if (screenPosition.HasValue)
                anchored = ScreenToAnchoredPosition(canvas, parent as RectTransform, screenPosition.Value, eventCamera);

            CreateUIImageUnderParent(parent, sprite, useNativeSize, anchored);
        }

        private static bool TryReplaceOnSelection(GameObject selected, Sprite sprite, bool useNativeSize)
        {
            if (selected == null)
                return false;

            // Button smart replace: update targetGraphic (Image) when possible.
            if (selected.TryGetComponent<Button>(out Button button))
            {
                Graphic target = button.targetGraphic;

                // If targetGraphic is null, try to find an Image in children first (common setup),
                // then fall back to Image on self, then add.
                if (target == null)
                {
                    Image img = selected.GetComponentInChildren<Image>(true);

                    if (img == null)
                        img = selected.GetComponent<Image>();

                    if (img == null)
                        img = selected.AddComponent<Image>();

                    Undo.RecordObject(button, "Assign Button Target Graphic");
                    button.targetGraphic = img;
                    EditorUtility.SetDirty(button);
                    target = img;
                }

                if (target is Image targetImage)
                {
                    Undo.RecordObject(targetImage, "Replace Button Target Graphic Sprite");
                    targetImage.sprite = sprite;
                    targetImage.preserveAspect = true;

                    if (useNativeSize)
                        targetImage.SetNativeSize();

                    EditorUtility.SetDirty(targetImage);
                    return true;
                }
            }

            // Replace Image directly.
            if (selected.TryGetComponent<Image>(out Image existingImage))
            {
                Undo.RecordObject(existingImage, "Replace UI Image Sprite");
                existingImage.sprite = sprite;
                existingImage.preserveAspect = true;

                if (useNativeSize)
                    existingImage.SetNativeSize();

                EditorUtility.SetDirty(existingImage);
                return true;
            }

            // Replace if selection is a Graphic that is an Image.
            if (selected.TryGetComponent<Graphic>(out Graphic graphic) && graphic is Image graphicImage)
            {
                Undo.RecordObject(graphicImage, "Replace UI Graphic Sprite");
                graphicImage.sprite = sprite;
                graphicImage.preserveAspect = true;

                if (useNativeSize)
                    graphicImage.SetNativeSize();

                EditorUtility.SetDirty(graphicImage);
                return true;
            }

            return false;
        }

        private static Vector2 ScreenToAnchoredPosition(Canvas canvas, RectTransform parentRect, Vector2 screenPosition,
            Camera eventCamera)
        {
            if (canvas == null)
                return Vector2.zero;

            if (parentRect == null)
                parentRect = canvas.transform as RectTransform;

            Camera camToUse;

            // Screen Space Overlay: camera should be null.
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                camToUse = null;
            else
                camToUse = canvas.worldCamera != null ? canvas.worldCamera : eventCamera;

            if (parentRect != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPosition, camToUse,
                    out Vector2 localPoint))
                return localPoint;

            return Vector2.zero;
        }

        private static Transform FindBestParentUnderCanvas(Canvas canvas, GameObject selected)
        {
            if (selected == null)
                return canvas.transform;

            RectTransform selectedRect = selected.GetComponent<RectTransform>();
            if (selectedRect == null)
                return canvas.transform;

            Canvas selectedCanvas = selected.GetComponentInParent<Canvas>();
            if (selectedCanvas == canvas)
                return selectedRect;

            return canvas.transform;
        }

        private static Canvas FindBestCanvasForSelection(GameObject selected)
        {
            if (selected != null)
            {
                Canvas canvasInParents = selected.GetComponentInParent<Canvas>();
                if (canvasInParents != null)
                    return canvasInParents;
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas c in canvases)
            {
                if (c != null && c.gameObject.activeInHierarchy)
                    return c;
            }

            return null;
        }

        private static Canvas CreateCanvasAndEventSystem()
        {
            GameObject canvasGO = new("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");

            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            if (Object.FindFirstObjectByType<EventSystem>() == null) CreateEventSystem();

            Selection.activeGameObject = canvasGO;
            return canvas;
        }

        private static void CreateEventSystem()
        {
            GameObject esGO = new("EventSystem", typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");

            Type inputSystemModuleType =
                Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModuleType != null && esGO.GetComponent(inputSystemModuleType) == null)
                esGO.AddComponent(inputSystemModuleType);
            else
                esGO.AddComponent<StandaloneInputModule>();
        }

        private static void CreateUIImageUnderParent(Transform parent, Sprite sprite, bool useNativeSize,
            Vector2 anchoredPosition)
        {
            GameObject go = new($"Image_{sprite.name}", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Create UI Image");

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            // Default to native size for palette workflows.
            image.SetNativeSize();

            if (!useNativeSize)
                if (rect.sizeDelta.sqrMagnitude < 1f)
                    rect.sizeDelta = new Vector2(100f, 100f);

            rect.anchoredPosition = anchoredPosition;

            Selection.activeGameObject = go;
        }
    }
}