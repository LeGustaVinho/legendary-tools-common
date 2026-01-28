using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
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

        private static Texture2D _checkerTexture;

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

        public static string TryGetGuidFromSprite(Sprite sprite)
        {
            if (sprite == null)
                return null;

            string path = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(path))
                return null;

            return AssetDatabase.AssetPathToGUID(path);
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

        public static bool IsInPalette(UIPaletteState state, string guid)
        {
            return state.PaletteGuids.Contains(guid);
        }

        public static void ToggleFavorite(UIPaletteState state, string guid)
        {
            if (state.FavoriteGuids.Contains(guid))
                state.FavoriteGuids.Remove(guid);
            else
                state.FavoriteGuids.Add(guid);

            state.Save();
        }

        public static void AddToPalette(UIPaletteState state, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            if (!state.PaletteGuids.Contains(guid))
                state.PaletteGuids.Insert(0, guid);

            state.Save();
        }

        public static void RemoveFromPalette(UIPaletteState state, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            state.PaletteGuids.Remove(guid);
            state.Save();
        }

        public static void ClearPalette(UIPaletteState state)
        {
            state.PaletteGuids.Clear();
            state.Save();
        }

        public static void PruneMissingGuids(List<string> guids)
        {
            if (guids == null || guids.Count == 0)
                return;

            for (int i = guids.Count - 1; i >= 0; i--)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    guids.RemoveAt(i);
            }
        }

        /// <summary>
        /// Extracts sprites from DragAndDrop data. Supports Sprite references and container assets (Texture2D, paths).
        /// </summary>
        public static List<Sprite> ExtractSpritesFromDrag(Object[] objectReferences, string[] paths)
        {
            List<Sprite> sprites = new(32);

            if (objectReferences != null)
                for (int i = 0; i < objectReferences.Length; i++)
                {
                    Object o = objectReferences[i];
                    if (o == null)
                        continue;

                    if (o is Sprite s)
                    {
                        sprites.Add(s);
                        continue;
                    }

                    string p = AssetDatabase.GetAssetPath(o);
                    AddSpritesFromPath(p, sprites);
                }

            if (paths != null)
                for (int i = 0; i < paths.Length; i++)
                {
                    AddSpritesFromPath(paths[i], sprites);
                }

            sprites = sprites
                .Where(x => x != null)
                .GroupBy(x => x.GetInstanceID())
                .Select(g => g.First())
                .ToList();

            return sprites;
        }

        private static void AddSpritesFromPath(string path, List<Sprite> sprites)
        {
            if (string.IsNullOrEmpty(path))
                return;

            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null)
            {
                sprites.Add(direct);
                return;
            }

            Object[] reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            if (reps != null && reps.Length > 0)
            {
                for (int i = 0; i < reps.Length; i++)
                {
                    if (reps[i] is Sprite s)
                        sprites.Add(s);
                }

                if (sprites.Count > 0)
                    return;
            }

            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all == null || all.Length == 0)
                return;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is Sprite s)
                    sprites.Add(s);
            }
        }

        /// <summary>
        /// Only returns true for drags started by UI Palette (requires GenericData key).
        /// This avoids hijacking Unity's default Sprite drag from Project window.
        /// </summary>
        public static bool TryGetDraggedSprite(out Sprite sprite, out string guid)
        {
            sprite = null;
            guid = DragAndDrop.GetGenericData(DragSpriteGuidKey) as string;

            if (string.IsNullOrEmpty(guid))
                return false;

            sprite = LoadSpriteFromGuid(guid);
            if (sprite != null)
                return true;

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
        /// Renders a sprite into a standalone Texture2D using textureRect (works well for packed sprites).
        /// Uses Graphics.CopyTexture to avoid requiring Read/Write on the source texture.
        /// Returns null if it cannot be created.
        /// </summary>
        public static Texture2D RenderSpriteToTexture(Sprite sprite)
        {
            if (sprite == null)
                return null;

            Texture2D src = sprite.texture;
            if (src == null)
                return null;

            Rect r = sprite.textureRect;

            int w = Mathf.Max(1, Mathf.RoundToInt(r.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(r.height));

            Texture2D dst = new(w, h, TextureFormat.RGBA32, false);
            dst.name = $"UIPalettePreview_{sprite.name}";
            dst.hideFlags = HideFlags.HideAndDontSave;
            dst.filterMode = FilterMode.Bilinear;
            dst.wrapMode = TextureWrapMode.Clamp;

            try
            {
                Graphics.CopyTexture(src, 0, 0, (int)r.x, (int)r.y, w, h, dst, 0, 0, 0, 0);
                dst.Apply(false, true);
                return dst;
            }
            catch
            {
                try
                {
                    if (src.isReadable)
                    {
                        Color[] pixels = src.GetPixels((int)r.x, (int)r.y, w, h);
                        dst.SetPixels(pixels);
                        dst.Apply(false, true);
                        return dst;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            Object.DestroyImmediate(dst);
            return null;
        }

        public static Texture2D GetCheckerTexture()
        {
            if (_checkerTexture != null)
                return _checkerTexture;

            const int size = 8;
            Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
            tex.name = "UIPalette_Checker";
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            bool pro = EditorGUIUtility.isProSkin;
            Color a = pro ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
            Color b = pro ? new Color(0.28f, 0.28f, 0.28f, 1f) : new Color(0.90f, 0.90f, 0.90f, 1f);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool odd = (x / 2 + y / 2) % 2 == 0;
                    pixels[y * size + x] = odd ? a : b;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);

            _checkerTexture = tex;
            return _checkerTexture;
        }

        /// <summary>
        /// Draws a checker background tiled across the given rect.
        /// </summary>
        public static void DrawChecker(Rect rect)
        {
            Texture2D checker = GetCheckerTexture();
            if (checker == null)
                return;

            float u = rect.width / checker.width;
            float v = rect.height / checker.height;

            GUI.DrawTextureWithTexCoords(rect, checker, new Rect(0f, 0f, u, v), false);
        }

        /// <summary>
        /// Draws a thumbnail texture using the configured mode:
        /// PreserveAspect (no scale-up), Fit (ScaleToFit), Fill (ScaleAndCrop).
        /// </summary>
        public static void DrawThumbnail(Rect rect, Texture2D tex, UIPaletteState.ThumbnailMode mode)
        {
            if (tex == null)
                return;

            switch (mode)
            {
                case UIPaletteState.ThumbnailMode.PreserveAspect:
                {
                    float scale = Mathf.Min(rect.width / tex.width, rect.height / tex.height);
                    scale = Mathf.Min(1f, scale);
                    float w = tex.width * scale;
                    float h = tex.height * scale;

                    Rect r = new(
                        rect.x + (rect.width - w) * 0.5f,
                        rect.y + (rect.height - h) * 0.5f,
                        w,
                        h);

                    GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
                    break;
                }

                case UIPaletteState.ThumbnailMode.Fit:
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
                    break;

                case UIPaletteState.ThumbnailMode.Fill:
                    GUI.DrawTexture(rect, tex, ScaleMode.ScaleAndCrop, true);
                    break;
            }
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
        /// Creates a new UI Button with an Image using the given sprite.
        /// </summary>
        public static void CreateUIButton(Sprite sprite, bool useNativeSize)
        {
            if (sprite == null)
                return;

            GameObject selected = Selection.activeGameObject;

            Canvas canvas = FindBestCanvasForSelection(selected);
            if (canvas == null)
                canvas = CreateCanvasAndEventSystem();

            Transform parent = FindBestParentUnderCanvas(canvas, selected);
            CreateUIButtonUnderParent(parent, sprite, useNativeSize, Vector2.zero);
        }

        /// <summary>
        /// Creates a new UI Button with an Image and a child TextMeshProUGUI label.
        /// </summary>
        public static void CreateUIButtonWithTextTMP(Sprite sprite, bool useNativeSize, string text)
        {
            if (sprite == null)
                return;

            if (string.IsNullOrWhiteSpace(text))
                text = "Button";

            GameObject selected = Selection.activeGameObject;

            Canvas canvas = FindBestCanvasForSelection(selected);
            if (canvas == null)
                canvas = CreateCanvasAndEventSystem();

            Transform parent = FindBestParentUnderCanvas(canvas, selected);
            CreateUIButtonWithTextTMPUnderParent(parent, sprite, useNativeSize, Vector2.zero, text);
        }

        /// <summary>
        /// Drop handler used by SceneView/Hierarchy drag and drop.
        /// </summary>
        public static void CreateUIImageForDrop(Sprite sprite, GameObject dropTarget, Vector2? screenPosition,
            Camera eventCamera, bool useNativeSize)
        {
            if (sprite == null)
                return;

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

            if (selected.TryGetComponent<Button>(out Button button))
            {
                Graphic target = button.targetGraphic;

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

            if (Object.FindFirstObjectByType<EventSystem>() == null)
                CreateEventSystem();

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

            image.SetNativeSize();

            if (!useNativeSize)
                if (rect.sizeDelta.sqrMagnitude < 1f)
                    rect.sizeDelta = new Vector2(100f, 100f);

            rect.anchoredPosition = anchoredPosition;

            Selection.activeGameObject = go;
        }

        private static void CreateUIButtonUnderParent(Transform parent, Sprite sprite, bool useNativeSize,
            Vector2 anchoredPosition)
        {
            GameObject go = new($"Button_{sprite.name}", typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(go, "Create UI Button");

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            Undo.RecordObject(button, "Assign Button Target Graphic");
            button.targetGraphic = image;
            EditorUtility.SetDirty(button);

            image.SetNativeSize();

            if (!useNativeSize)
                if (rect.sizeDelta.sqrMagnitude < 1f)
                    rect.sizeDelta = new Vector2(160f, 40f);

            rect.anchoredPosition = anchoredPosition;

            Selection.activeGameObject = go;
        }

        private static void CreateUIButtonWithTextTMPUnderParent(Transform parent, Sprite sprite, bool useNativeSize,
            Vector2 anchoredPosition, string text)
        {
            GameObject go = new($"Button_{sprite.name}", typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(go, "Create UI Button With Text (TMP)");

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            Undo.RecordObject(button, "Assign Button Target Graphic");
            button.targetGraphic = image;
            EditorUtility.SetDirty(button);

            image.SetNativeSize();

            if (!useNativeSize)
                if (rect.sizeDelta.sqrMagnitude < 1f)
                    rect.sizeDelta = new Vector2(200f, 60f);

            rect.anchoredPosition = anchoredPosition;

            GameObject textGO = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(textGO, "Create TMP Text");

            RectTransform textRect = textGO.GetComponent<RectTransform>();
            textRect.SetParent(go.transform, false);
            textRect.localScale = Vector3.one;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 6f);
            textRect.offsetMax = new Vector2(-10f, -6f);

            TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.raycastTarget = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 12f;
            tmp.fontSizeMax = 40f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.richText = true;

            Selection.activeGameObject = go;
        }
    }
}