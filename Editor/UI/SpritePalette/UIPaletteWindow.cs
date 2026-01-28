using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class UIPaletteWindow : EditorWindow
    {
        private readonly Dictionary<string, Texture2D> _previewCache = new(2048);
        private List<string> _indexedGuids = new(2048);
        private Vector2 _scroll;

        private string _mouseDownGuid;
        private bool _didDrag;

        private UIPaletteState State => UIPaletteState.instance;

        [MenuItem("Tools/LegendaryTools/UI/UI Palette")]
        public static void Open()
        {
            UIPaletteWindow window = GetWindow<UIPaletteWindow>();
            window.titleContent = new GUIContent("UI Palette");
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            RefreshIndex();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnProjectChanged()
        {
            RefreshIndex();
            Repaint();
        }

        private void RefreshIndex()
        {
            _indexedGuids = UIPaletteUtilities.FindSpriteGuids(State.LabelFilter);
            State.LastRefreshTime = (float)EditorApplication.timeSinceStartup;
            State.Save();
        }

        private void OnGUI()
        {
            DrawToolbar();

            GUILayout.Space(4);

            List<string> list = GetActiveGuidList();
            list = ApplySearchFilter(list);

            DrawStats(list.Count);

            GUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGrid(list);
            EditorGUILayout.EndScrollView();

            HandlePreviewCacheAging();
        }

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

                    // Common names across Unity versions/skins
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

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                UIPaletteState.Tab newTab = (UIPaletteState.Tab)GUILayout.Toolbar(
                    (int)State.CurrentTab,
                    new[] { "All", "Favorites", "Recent" },
                    EditorStyles.toolbarButton,
                    GUILayout.Width(240f));

                if (newTab != State.CurrentTab)
                {
                    State.CurrentTab = newTab;
                    State.Save();
                    GUI.FocusControl(null);
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label("Label", GUILayout.Width(36f));
                string newLabel = GUILayout.TextField(State.LabelFilter ?? string.Empty, EditorStyles.toolbarTextField,
                    GUILayout.Width(120f));
                if (newLabel != State.LabelFilter)
                {
                    State.LabelFilter = newLabel.Trim();
                    State.Save();
                    RefreshIndex();
                }

                GUILayout.Space(6);

                // Search box (robust styles)
                string newSearch = GUILayout.TextField(State.Search ?? string.Empty, Styles.ToolbarSearchField,
                    GUILayout.MinWidth(160f));
                if (newSearch != State.Search)
                {
                    State.Search = newSearch;
                    State.Save();
                }

                // Cancel button: show "empty" style when there's no text (keeps layout consistent)
                GUIStyle cancelStyle = string.IsNullOrEmpty(State.Search)
                    ? Styles.ToolbarSearchCancelButtonEmpty
                    : Styles.ToolbarSearchCancelButton;

                if (GUILayout.Button(GUIContent.none, cancelStyle))
                    if (!string.IsNullOrEmpty(State.Search))
                    {
                        State.Search = string.Empty;
                        State.Save();
                        GUI.FocusControl(null);
                        Repaint();
                    }

                GUILayout.Space(6);
                GUILayout.Label("Size", GUILayout.Width(30f));
                float newSize = GUILayout.HorizontalSlider(State.ThumbnailSize, 48f, 128f, GUILayout.Width(110f));
                if (Mathf.Abs(newSize - State.ThumbnailSize) > 0.01f)
                {
                    State.ThumbnailSize = newSize;
                    State.Save();
                    Repaint();
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    RefreshIndex();
                    Repaint();
                }
            }
        }

        private void DrawStats(int filteredCount)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string label = State.CurrentTab switch
                {
                    UIPaletteState.Tab.All => $"Indexed: {_indexedGuids.Count} | Showing: {filteredCount}",
                    UIPaletteState.Tab.Favorites =>
                        $"Favorites: {State.FavoriteGuids.Count} | Showing: {filteredCount}",
                    UIPaletteState.Tab.Recent => $"Recent: {State.RecentGuids.Count} | Showing: {filteredCount}",
                    _ => $"Showing: {filteredCount}"
                };

                EditorGUILayout.HelpBox(label, MessageType.None);
            }
        }

        private List<string> GetActiveGuidList()
        {
            switch (State.CurrentTab)
            {
                case UIPaletteState.Tab.Favorites:
                    return State.FavoriteGuids.Where(g => _indexedGuids.Contains(g)).ToList();

                case UIPaletteState.Tab.Recent:
                    return State.RecentGuids.Where(g => _indexedGuids.Contains(g)).ToList();

                default:
                    return _indexedGuids;
            }
        }

        private List<string> ApplySearchFilter(List<string> guids)
        {
            string search = (State.Search ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(search))
                return guids;

            search = search.ToLowerInvariant();
            return guids
                .Where(g => UIPaletteUtilities.GetAssetNameFromGuid(g).ToLowerInvariant().Contains(search))
                .ToList();
        }

        private void DrawGrid(List<string> guids)
        {
            float thumb = Mathf.Clamp(State.ThumbnailSize, 48f, 128f);
            float cell = thumb + 22f;

            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 16f) / cell));

            int index = 0;
            while (index < guids.Count)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && index < guids.Count; c++, index++)
                    {
                        DrawCell(guids[index], thumb, cell);
                    }
                }
            }
        }

        private void DrawCell(string guid, float thumb, float cellWidth)
        {
            Sprite sprite = UIPaletteUtilities.LoadSpriteFromGuid(guid);
            if (sprite == null)
                return;

            string assetName = sprite.name;
            bool isFav = UIPaletteUtilities.IsFavorite(State, guid);

            Rect cellRect = GUILayoutUtility.GetRect(cellWidth, thumb + 18f, GUILayout.Width(cellWidth));
            Rect thumbRect = new(cellRect.x + 4f, cellRect.y + 2f, thumb, thumb);
            Rect nameRect = new(cellRect.x + 4f, cellRect.y + thumb + 4f, cellRect.width - 8f, 16f);

            GUI.Box(cellRect, GUIContent.none);

            Texture2D preview = GetPreviewTexture(guid, sprite);
            if (preview != null)
                GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit, true);

            GUI.Label(nameRect, assetName, EditorStyles.miniLabel);

            if (isFav)
            {
                Rect starRect = new(thumbRect.xMax - 14f, thumbRect.y + 2f, 12f, 12f);
                GUI.Label(starRect, "★", EditorStyles.boldLabel);
            }

            Event e = Event.current;
            if (!cellRect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _mouseDownGuid = guid;
                _didDrag = false;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && _mouseDownGuid == guid)
            {
                _didDrag = true;

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new Object[] { sprite };
                DragAndDrop.SetGenericData(UIPaletteUtilities.DragSpriteGuidKey, guid);
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                DragAndDrop.StartDrag($"UI Palette: {sprite.name}");
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 0 && _mouseDownGuid == guid)
            {
                if (!_didDrag)
                {
                    bool useNative = e.shift;
                    UIPaletteUtilities.CreateOrReplaceUIImage(sprite, useNative);

                    UIPaletteUtilities.AddToFrontUnique(State.RecentGuids, guid, State.MaxRecent);
                    State.Save();
                }

                _mouseDownGuid = null;
                _didDrag = false;

                e.Use();
                return;
            }

            if (e.type == EventType.ContextClick)
            {
                GenericMenu menu = new();

                if (isFav)
                    menu.AddItem(new GUIContent("Remove from Favorites"), false, () =>
                    {
                        UIPaletteUtilities.ToggleFavorite(State, guid);
                        Repaint();
                    });
                else
                    menu.AddItem(new GUIContent("Add to Favorites"), false, () =>
                    {
                        UIPaletteUtilities.ToggleFavorite(State, guid);
                        Repaint();
                    });

                menu.AddSeparator("");

                menu.AddItem(new GUIContent("Ping in Project"), false, () => EditorGUIUtility.PingObject(sprite));
                menu.AddItem(new GUIContent("Select Asset"), false, () => Selection.activeObject = sprite);

                menu.ShowAsContext();
                e.Use();
            }
        }

        private Texture2D GetPreviewTexture(string guid, Sprite sprite)
        {
            if (_previewCache.TryGetValue(guid, out Texture2D cached) && cached != null)
                return cached;

            Texture2D tex = AssetPreview.GetAssetPreview(sprite);
            if (tex == null)
                tex = AssetPreview.GetMiniThumbnail(sprite) as Texture2D;

            if (tex != null)
                _previewCache[guid] = tex;

            return tex;
        }

        private void HandlePreviewCacheAging()
        {
            if (_previewCache.Count <= 4000)
                return;

            HashSet<string> keep = new(_indexedGuids);
            foreach (string g in State.FavoriteGuids)
            {
                keep.Add(g);
            }

            foreach (string g in State.RecentGuids)
            {
                keep.Add(g);
            }

            List<string> keys = _previewCache.Keys.ToList();
            foreach (string k in keys)
            {
                if (!keep.Contains(k))
                    _previewCache.Remove(k);
            }
        }
    }
}