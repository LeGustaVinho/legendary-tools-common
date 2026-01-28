using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class UIPaletteWindowController
    {
        private const string PaletteReorderGuidKey = "UIPaletteReorderGuid";

        private readonly Action _repaint;
        private readonly UIPaletteThumbnailCache _thumbnailCache;

        private List<string> _indexedGuids = new(2048);

        private string _mouseDownGuid;
        private bool _didDrag;

        private Dictionary<string, Rect> _prevCellRects = new(1024);

        public UIPaletteState State => UIPaletteState.instance;

        public bool IsPaletteTab => State.CurrentTab == UIPaletteState.Tab.Palette;

        public UIPaletteWindowController(Action repaint)
        {
            _repaint = repaint;
            _thumbnailCache = new UIPaletteThumbnailCache();
        }

        public void OnEnable()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            RefreshIndex();
            PruneAllLists();
        }

        public void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            _thumbnailCache.Dispose();
        }

        public void BeginFrame(Event e)
        {
            HandlePaletteReorder(e);
        }

        public void EndFrame(Dictionary<string, Rect> cellRects)
        {
            _prevCellRects = cellRects ?? new Dictionary<string, Rect>(0);
            _thumbnailCache.AgeCache(State, _indexedGuids);
        }

        public void ApplyToolbar(UIPaletteToolbarResult toolbar)
        {
            if (toolbar.NewTab.HasValue && toolbar.NewTab.Value != State.CurrentTab)
            {
                State.CurrentTab = toolbar.NewTab.Value;
                State.Save();
                GUI.FocusControl(null);
                _repaint();
            }

            if (toolbar.NewLabelFilter != null && toolbar.NewLabelFilter != State.LabelFilter)
            {
                State.LabelFilter = toolbar.NewLabelFilter.Trim();
                State.Save();
                RefreshIndex();
                _repaint();
            }

            if (toolbar.NewSearch != null && toolbar.NewSearch != State.Search)
            {
                State.Search = toolbar.NewSearch;
                State.Save();
                _repaint();
            }

            if (toolbar.ClearSearchRequested)
                if (!string.IsNullOrEmpty(State.Search))
                {
                    State.Search = string.Empty;
                    State.Save();
                    GUI.FocusControl(null);
                    _repaint();
                }

            if (toolbar.NewThumbnailSize.HasValue &&
                Mathf.Abs(toolbar.NewThumbnailSize.Value - State.ThumbnailSize) > 0.01f)
            {
                State.ThumbnailSize = toolbar.NewThumbnailSize.Value;
                State.Save();
                _repaint();
            }

            if (toolbar.NewThumbnailMode.HasValue && toolbar.NewThumbnailMode.Value != State.ThumbnailDrawMode)
            {
                State.ThumbnailDrawMode = toolbar.NewThumbnailMode.Value;
                State.Save();
                _repaint();
            }

            if (toolbar.RefreshRequested)
            {
                RefreshIndex();
                PruneAllLists();
                _repaint();
            }

            if (toolbar.ClearPaletteRequested && IsPaletteTab)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Clear Palette",
                    "Remove all sprites from the Palette?",
                    "Clear",
                    "Cancel");

                if (ok)
                {
                    UIPaletteUtilities.ClearPalette(State);
                    _thumbnailCache.ClearAll();
                    _repaint();
                }
            }
        }

        public string GetStatsText(int filteredCount)
        {
            return State.CurrentTab switch
            {
                UIPaletteState.Tab.Palette =>
                    $"Palette: {State.PaletteGuids.Count} | Showing: {filteredCount}  (Drag sprites into the drop zone)",
                UIPaletteState.Tab.All => $"Indexed: {_indexedGuids.Count} | Showing: {filteredCount}",
                UIPaletteState.Tab.Favorites => $"Favorites: {State.FavoriteGuids.Count} | Showing: {filteredCount}",
                UIPaletteState.Tab.Recent => $"Recent: {State.RecentGuids.Count} | Showing: {filteredCount}",
                _ => $"Showing: {filteredCount}"
            };
        }

        public List<string> GetFilteredGuids()
        {
            List<string> list = GetActiveGuidList();
            return ApplySearchFilter(list);
        }

        public Sprite GetSprite(string guid)
        {
            return UIPaletteUtilities.LoadSpriteFromGuid(guid);
        }

        public Texture2D GetPreview(string guid, Sprite sprite)
        {
            return _thumbnailCache.GetOrCreate(guid, sprite);
        }

        public bool IsFavorite(string guid)
        {
            return UIPaletteUtilities.IsFavorite(State, guid);
        }

        public bool IsInPalette(string guid)
        {
            return UIPaletteUtilities.IsInPalette(State, guid);
        }

        public void RemoveFromPalette(string guid)
        {
            UIPaletteUtilities.RemoveFromPalette(State, guid);
            _thumbnailCache.Remove(guid);
            _repaint();
        }

        public void ToggleFavorite(string guid)
        {
            UIPaletteUtilities.ToggleFavorite(State, guid);
            _repaint();
        }

        public void AddToPalette(string guid)
        {
            UIPaletteUtilities.AddToPalette(State, guid);
            _repaint();
        }

        public void Ping(Sprite sprite)
        {
            if (sprite == null)
                return;

            EditorGUIUtility.PingObject(sprite);
        }

        public void Select(UnityEngine.Object obj)
        {
            Selection.activeObject = obj;
        }

        public void CreateOrReplaceUIImage(Sprite sprite, bool useNativeSize)
        {
            UIPaletteUtilities.CreateOrReplaceUIImage(sprite, useNativeSize);

            string guid = UIPaletteUtilities.TryGetGuidFromSprite(sprite);
            if (!string.IsNullOrEmpty(guid))
            {
                UIPaletteUtilities.AddToFrontUnique(State.RecentGuids, guid, State.MaxRecent);
                State.Save();
            }
        }

        public void CreateUIButton(Sprite sprite, bool useNativeSize)
        {
            UIPaletteUtilities.CreateUIButton(sprite, useNativeSize);

            string guid = UIPaletteUtilities.TryGetGuidFromSprite(sprite);
            if (!string.IsNullOrEmpty(guid))
            {
                UIPaletteUtilities.AddToFrontUnique(State.RecentGuids, guid, State.MaxRecent);
                State.Save();
            }
        }

        public void CreateUIButtonWithTextTMP(Sprite sprite, bool useNativeSize, string text)
        {
            UIPaletteUtilities.CreateUIButtonWithTextTMP(sprite, useNativeSize, text);

            string guid = UIPaletteUtilities.TryGetGuidFromSprite(sprite);
            if (!string.IsNullOrEmpty(guid))
            {
                UIPaletteUtilities.AddToFrontUnique(State.RecentGuids, guid, State.MaxRecent);
                State.Save();
            }
        }

        public void StartPaletteDrag(Sprite sprite, string guid)
        {
            if (sprite == null || string.IsNullOrEmpty(guid))
                return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new UnityEngine.Object[] { sprite };
            DragAndDrop.SetGenericData(UIPaletteUtilities.DragSpriteGuidKey, guid);
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            DragAndDrop.StartDrag($"UI Palette: {sprite.name}");
        }

        public void BeginMouseDown(string guid)
        {
            _mouseDownGuid = guid;
            _didDrag = false;
        }

        public bool IsMouseDownGuid(string guid)
        {
            return _mouseDownGuid == guid;
        }

        public void MarkDidDrag()
        {
            _didDrag = true;
        }

        public bool DidDrag => _didDrag;

        public void ClearMouseState()
        {
            _mouseDownGuid = null;
            _didDrag = false;
        }

        public bool IsDraggingSpritesFromProject()
        {
            return UIPaletteUtilities.ExtractSpritesFromDrag(DragAndDrop.objectReferences, DragAndDrop.paths).Count > 0;
        }

        public void HandleDropZone(Rect dropRect, Event e)
        {
            if (e == null)
                return;

            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;

            if (!dropRect.Contains(e.mousePosition))
                return;

            List<Sprite> sprites =
                UIPaletteUtilities.ExtractSpritesFromDrag(DragAndDrop.objectReferences, DragAndDrop.paths);
            if (sprites.Count == 0)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                int added = 0;

                for (int i = 0; i < sprites.Count; i++)
                {
                    string guid = UIPaletteUtilities.TryGetGuidFromSprite(sprites[i]);
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    if (!State.PaletteGuids.Contains(guid))
                    {
                        State.PaletteGuids.Insert(0, guid);
                        added++;
                    }
                }

                if (added > 0)
                {
                    State.Save();
                    _repaint();
                }
            }

            e.Use();
        }

        public void HandleWindowDropFallback(Rect windowRect, Event e)
        {
            if (e == null)
                return;

            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;

            if (!windowRect.Contains(e.mousePosition))
                return;

            List<Sprite> sprites =
                UIPaletteUtilities.ExtractSpritesFromDrag(DragAndDrop.objectReferences, DragAndDrop.paths);
            if (sprites.Count == 0)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                int added = 0;

                for (int i = 0; i < sprites.Count; i++)
                {
                    string guid = UIPaletteUtilities.TryGetGuidFromSprite(sprites[i]);
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    if (!State.PaletteGuids.Contains(guid))
                    {
                        State.PaletteGuids.Insert(0, guid);
                        added++;
                    }
                }

                if (added > 0)
                {
                    State.CurrentTab = UIPaletteState.Tab.Palette;
                    State.Save();
                    _repaint();
                }
            }

            e.Use();
        }

        public void ShowContextMenu(string guid, Sprite sprite)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            bool isFav = IsFavorite(guid);
            bool inPalette = IsInPalette(guid);

            GenericMenu menu = new();

            menu.AddItem(new GUIContent("Create as Button With Text (TMP)"), false,
                () => CreateUIButtonWithTextTMP(sprite, true, "Button"));
            menu.AddItem(new GUIContent("Create as Button"), false, () => CreateUIButton(sprite, true));
            menu.AddItem(new GUIContent("Create as Image"), false, () => CreateOrReplaceUIImage(sprite, true));

            menu.AddSeparator("");

            if (inPalette)
                menu.AddItem(new GUIContent("Remove from Palette"), false, () => RemoveFromPalette(guid));
            else
                menu.AddItem(new GUIContent("Add to Palette"), false, () => AddToPalette(guid));

            menu.AddSeparator("");

            if (isFav)
                menu.AddItem(new GUIContent("Remove from Favorites"), false, () => ToggleFavorite(guid));
            else
                menu.AddItem(new GUIContent("Add to Favorites"), false, () => ToggleFavorite(guid));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Ping in Project"), false, () => Ping(sprite));
            menu.AddItem(new GUIContent("Select Asset"), false, () => Select(sprite));

            if (IsPaletteTab)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Clear Palette"), false, () =>
                {
                    bool ok = EditorUtility.DisplayDialog(
                        "Clear Palette",
                        "Remove all sprites from the Palette?",
                        "Clear",
                        "Cancel");

                    if (ok)
                    {
                        UIPaletteUtilities.ClearPalette(State);
                        _thumbnailCache.ClearAll();
                        _repaint();
                    }
                });
            }

            menu.ShowAsContext();
        }

        private void HandlePaletteReorder(Event e)
        {
            if (!IsPaletteTab || e == null)
                return;

            object data = DragAndDrop.GetGenericData(PaletteReorderGuidKey);
            string draggedGuid = data as string;

            if (string.IsNullOrEmpty(draggedGuid))
                return;

            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;

            string targetGuid = FindGuidUnderMouse(e.mousePosition);

            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                ReorderPalette(draggedGuid, targetGuid);
                DragAndDrop.SetGenericData(PaletteReorderGuidKey, null);
                e.Use();
            }
        }

        private string FindGuidUnderMouse(Vector2 mousePos)
        {
            foreach (KeyValuePair<string, Rect> kvp in _prevCellRects)
            {
                if (kvp.Value.Contains(mousePos))
                    return kvp.Key;
            }

            return null;
        }

        public void StartReorderDrag(string guid, string displayName)
        {
            if (!IsPaletteTab || string.IsNullOrEmpty(guid))
                return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.SetGenericData(PaletteReorderGuidKey, guid);
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            DragAndDrop.StartDrag($"Reorder: {displayName}");
        }

        private void ReorderPalette(string draggedGuid, string targetGuidOrNull)
        {
            if (string.IsNullOrEmpty(draggedGuid))
                return;

            int fromIndex = State.PaletteGuids.IndexOf(draggedGuid);
            if (fromIndex < 0)
                return;

            State.PaletteGuids.RemoveAt(fromIndex);

            if (string.IsNullOrEmpty(targetGuidOrNull))
            {
                State.PaletteGuids.Add(draggedGuid);
            }
            else
            {
                int toIndex = State.PaletteGuids.IndexOf(targetGuidOrNull);
                if (toIndex < 0)
                    State.PaletteGuids.Add(draggedGuid);
                else
                    State.PaletteGuids.Insert(toIndex, draggedGuid);
            }

            State.Save();
            _repaint();
        }

        private void OnProjectChanged()
        {
            RefreshIndex();
            PruneAllLists();
            _repaint();
        }

        private void RefreshIndex()
        {
            _indexedGuids = UIPaletteUtilities.FindSpriteGuids(State.LabelFilter);
            State.LastRefreshTime = (float)EditorApplication.timeSinceStartup;
            State.Save();
        }

        private void PruneAllLists()
        {
            UIPaletteUtilities.PruneMissingGuids(State.PaletteGuids);
            UIPaletteUtilities.PruneMissingGuids(State.FavoriteGuids);
            UIPaletteUtilities.PruneMissingGuids(State.RecentGuids);
            State.Save();
        }

        private List<string> GetActiveGuidList()
        {
            switch (State.CurrentTab)
            {
                case UIPaletteState.Tab.Palette:
                    return State.PaletteGuids
                        .Where(g => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(g)))
                        .ToList();

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
    }
}