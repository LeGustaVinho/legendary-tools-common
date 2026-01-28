using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal readonly struct UIPaletteToolbarResult
    {
        public readonly UIPaletteState.Tab? NewTab;
        public readonly string NewLabelFilter;
        public readonly string NewSearch;
        public readonly bool ClearSearchRequested;
        public readonly float? NewThumbnailSize;
        public readonly UIPaletteState.ThumbnailMode? NewThumbnailMode;
        public readonly bool RefreshRequested;
        public readonly bool ClearPaletteRequested;

        public UIPaletteToolbarResult(
            UIPaletteState.Tab? newTab,
            string newLabelFilter,
            string newSearch,
            bool clearSearchRequested,
            float? newThumbnailSize,
            UIPaletteState.ThumbnailMode? newThumbnailMode,
            bool refreshRequested,
            bool clearPaletteRequested)
        {
            NewTab = newTab;
            NewLabelFilter = newLabelFilter;
            NewSearch = newSearch;
            ClearSearchRequested = clearSearchRequested;
            NewThumbnailSize = newThumbnailSize;
            NewThumbnailMode = newThumbnailMode;
            RefreshRequested = refreshRequested;
            ClearPaletteRequested = clearPaletteRequested;
        }
    }

    internal static class UIPaletteWindowView
    {
        private static class Styles
        {
            private static GUIStyle _searchField;
            private static GUIStyle _searchCancelButton;
            private static GUIStyle _searchCancelButtonEmpty;

            public static GUIStyle ToolbarSearchField
            {
                get
                {
                    if (_searchField != null) return _searchField;

                    _searchField =
                        GUI.skin.FindStyle("ToolbarSearchTextField") ??
                        GUI.skin.FindStyle("ToolbarSearchField") ??
                        EditorStyles.toolbarTextField;

                    return _searchField;
                }
            }

            public static GUIStyle ToolbarSearchCancelButton
            {
                get
                {
                    if (_searchCancelButton != null) return _searchCancelButton;

                    _searchCancelButton =
                        GUI.skin.FindStyle("ToolbarSearchCancelButton") ??
                        GUI.skin.FindStyle("ToolbarSearchFieldCancelButton") ??
                        EditorStyles.toolbarButton;

                    return _searchCancelButton;
                }
            }

            public static GUIStyle ToolbarSearchCancelButtonEmpty
            {
                get
                {
                    if (_searchCancelButtonEmpty != null) return _searchCancelButtonEmpty;

                    _searchCancelButtonEmpty =
                        GUI.skin.FindStyle("ToolbarSearchCancelButtonEmpty") ??
                        GUI.skin.FindStyle("ToolbarSearchFieldCancelButtonEmpty") ??
                        EditorStyles.toolbarButton;

                    return _searchCancelButtonEmpty;
                }
            }
        }

        public static UIPaletteToolbarResult DrawToolbar(UIPaletteState state)
        {
            UIPaletteState.Tab? newTab = null;
            string newLabel = null;
            string newSearch = null;
            bool clearSearch = false;
            float? newSize = null;
            UIPaletteState.ThumbnailMode? newMode = null;
            bool refresh = false;
            bool clearPalette = false;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int currentIndex = TabToToolbarIndex(state.CurrentTab);

                int selectedIndex = GUILayout.Toolbar(
                    currentIndex,
                    new[] { "Palette", "All", "Favorites", "Recent" },
                    EditorStyles.toolbarButton,
                    GUILayout.Width(320f));

                UIPaletteState.Tab tab = ToolbarIndexToTab(selectedIndex);
                if (tab != state.CurrentTab)
                    newTab = tab;

                GUILayout.FlexibleSpace();

                if (state.CurrentTab == UIPaletteState.Tab.Palette)
                {
                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                        clearPalette = true;

                    GUILayout.Space(6f);
                }

                GUILayout.Label("Label", GUILayout.Width(36f));
                string labelField = GUILayout.TextField(state.LabelFilter ?? string.Empty,
                    EditorStyles.toolbarTextField, GUILayout.Width(120f));
                if (labelField != state.LabelFilter)
                    newLabel = labelField.Trim();

                GUILayout.Space(6f);

                string searchField = GUILayout.TextField(state.Search ?? string.Empty, Styles.ToolbarSearchField,
                    GUILayout.MinWidth(160f));
                if (searchField != state.Search)
                    newSearch = searchField;

                GUIStyle cancelStyle = string.IsNullOrEmpty(state.Search)
                    ? Styles.ToolbarSearchCancelButtonEmpty
                    : Styles.ToolbarSearchCancelButton;

                if (GUILayout.Button(GUIContent.none, cancelStyle))
                    clearSearch = true;

                GUILayout.Space(6f);
                GUILayout.Label("Size", GUILayout.Width(30f));

                float sizeField = GUILayout.HorizontalSlider(state.ThumbnailSize, 48f, 128f, GUILayout.Width(90f));
                if (Mathf.Abs(sizeField - state.ThumbnailSize) > 0.01f)
                    newSize = sizeField;

                GUILayout.Space(6f);

                UIPaletteState.ThumbnailMode modeField =
                    (UIPaletteState.ThumbnailMode)EditorGUILayout.EnumPopup(state.ThumbnailDrawMode,
                        EditorStyles.toolbarPopup, GUILayout.Width(120f));

                if (modeField != state.ThumbnailDrawMode)
                    newMode = modeField;

                GUILayout.Space(6f);

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    refresh = true;
            }

            return new UIPaletteToolbarResult(
                newTab,
                newLabel,
                newSearch,
                clearSearch,
                newSize,
                newMode,
                refresh,
                clearPalette);
        }

        public static Rect DrawDropZone(bool isDraggingSprites)
        {
            float height = 68f;
            Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));

            Event e = Event.current;
            bool isHover = isDraggingSprites && rect.Contains(e.mousePosition);

            Color old = GUI.color;

            if (isHover)
                GUI.color = EditorGUIUtility.isProSkin
                    ? new Color(0.35f, 0.65f, 1f, 1f)
                    : new Color(0.15f, 0.45f, 0.95f, 1f);

            GUI.Box(rect, GUIContent.none);
            GUI.color = old;

            Rect labelRect = new(rect.x + 10f, rect.y + 10f, rect.width - 20f, rect.height - 20f);

            GUIStyle style = new(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            GUI.Label(labelRect, isHover ? "Release to add sprites to Palette" : "Drop sprites here", style);

            return rect;
        }

        public static void DrawStats(string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(text, MessageType.None);
            }
        }

        public static Dictionary<string, Rect> DrawGrid(
            List<string> guids,
            UIPaletteWindowController controller,
            float windowWidth)
        {
            Dictionary<string, Rect> rects = new(guids.Count);

            float thumb = Mathf.Clamp(controller.State.ThumbnailSize, 48f, 128f);
            float cell = thumb + 22f;

            int columns = Mathf.Max(1, Mathf.FloorToInt((windowWidth - 16f) / cell));

            int index = 0;
            while (index < guids.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && index < guids.Count; c++, index++)
                    {
                        string guid = guids[index];
                        Rect cellRect = DrawCell(guid, thumb, cell, controller);
                        rects[guid] = cellRect;
                    }
                }
            }

            return rects;
        }

        private static Rect DrawCell(string guid, float thumb, float cellWidth, UIPaletteWindowController controller)
        {
            Sprite sprite = controller.GetSprite(guid);
            if (sprite == null)
                return Rect.zero;

            bool isFav = controller.IsFavorite(guid);
            bool inPalette = controller.IsInPalette(guid);
            bool isPaletteTab = controller.IsPaletteTab;

            string assetName = sprite.name;

            Rect cellRect = GUILayoutUtility.GetRect(cellWidth, thumb + 18f, GUILayout.Width(cellWidth));
            Rect thumbRect = new(cellRect.x + 4f, cellRect.y + 2f, thumb, thumb);
            Rect nameRect = new(cellRect.x + 4f, cellRect.y + thumb + 4f, cellRect.width - 8f, 16f);

            GUI.Box(cellRect, GUIContent.none);

            // Checker background + thumbnail draw mode.
            UIPaletteUtilities.DrawChecker(thumbRect);

            Texture2D preview = controller.GetPreview(guid, sprite);
            if (preview != null)
                UIPaletteUtilities.DrawThumbnail(thumbRect, preview, controller.State.ThumbnailDrawMode);

            GUI.Label(nameRect, assetName, EditorStyles.miniLabel);

            if (isFav)
            {
                Rect starRect = new(thumbRect.xMax - 14f, thumbRect.y + 2f, 12f, 12f);
                GUI.Label(starRect, "★", EditorStyles.boldLabel);
            }

            if (isPaletteTab)
            {
                Rect xRect = new(thumbRect.x + 2f, thumbRect.y + 2f, 14f, 14f);
                if (GUI.Button(xRect, "×", EditorStyles.miniButton))
                {
                    controller.RemoveFromPalette(guid);
                    return cellRect;
                }
            }

            Event e = Event.current;

            if (!cellRect.Contains(e.mousePosition))
                return cellRect;

            // Palette reorder drag (Palette tab only).
            if (isPaletteTab && e.type == EventType.MouseDown && e.button == 0)
            {
                controller.BeginMouseDown(guid);
                e.Use();
                return cellRect;
            }

            if (isPaletteTab && e.type == EventType.MouseDrag && e.button == 0 && controller.IsMouseDownGuid(guid))
            {
                controller.MarkDidDrag();
                controller.StartReorderDrag(guid, assetName);
                e.Use();
                return cellRect;
            }

            // Drag out to Scene/Hierarchy (non-palette tabs).
            if (!isPaletteTab && e.type == EventType.MouseDown && e.button == 0)
            {
                controller.BeginMouseDown(guid);
                e.Use();
                return cellRect;
            }

            if (!isPaletteTab && e.type == EventType.MouseDrag && e.button == 0 && controller.IsMouseDownGuid(guid))
            {
                controller.MarkDidDrag();
                controller.StartPaletteDrag(sprite, guid);
                e.Use();
                return cellRect;
            }

            if (e.type == EventType.MouseUp && e.button == 0 && controller.IsMouseDownGuid(guid))
            {
                bool wasDrag = controller.DidDrag;
                controller.ClearMouseState();

                if (!wasDrag)
                {
                    bool useNativeSize = e.shift;
                    controller.CreateOrReplaceUIImage(sprite, useNativeSize);
                }

                e.Use();
                return cellRect;
            }

            if (e.type == EventType.ContextClick)
            {
                controller.ShowContextMenu(guid, sprite);
                e.Use();
                return cellRect;
            }

            return cellRect;
        }

        private static int TabToToolbarIndex(UIPaletteState.Tab tab)
        {
            return tab switch
            {
                UIPaletteState.Tab.Palette => 0,
                UIPaletteState.Tab.All => 1,
                UIPaletteState.Tab.Favorites => 2,
                UIPaletteState.Tab.Recent => 3,
                _ => 0
            };
        }

        private static UIPaletteState.Tab ToolbarIndexToTab(int index)
        {
            return index switch
            {
                0 => UIPaletteState.Tab.Palette,
                1 => UIPaletteState.Tab.All,
                2 => UIPaletteState.Tab.Favorites,
                3 => UIPaletteState.Tab.Recent,
                _ => UIPaletteState.Tab.Palette
            };
        }
    }
}