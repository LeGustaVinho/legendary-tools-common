using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class GUIStyleBrowser : EditorWindow
    {
        private sealed class Entry
        {
            public string Name;
            public Func<GUIStyle> GetStyle; // Deferred getter resolved during OnGUI.
            public string Source; // e.g., "GUI.skin", "Builtin:Inspector", "EditorStyles".
        }

        private Vector2 _scroll;
        private string _search = string.Empty;
        private bool _showEmptyNames = false;
        private bool _groupBySource = false; // Grid works better ungrouped by default.
        private bool _showBoxes = true;
        private bool _showButtons = true;
        private bool _showLabels = true;
        private bool _showFields = true;
        private bool _showOthers = true;

        // --- GRID options ---
        private float _cellSize = 96f; // Square size in pixels.
        private float _cellGap = 8f; // Gap between cells.
        private bool _drawCard = true; // Draw a subtle background card.
        private bool _nameOverlay = true; // Show name over the cell; if false, show below.
        private float _nameHeight = 18f; // Height used when showing name below.

        private string _sampleText = "{0}";
        private readonly List<Entry> _entries = new();
        private bool _initialized;

        [MenuItem("Tools/LegendaryTools/Editor/Unity GUIStyle Browser")]
        private static void Open()
        {
            GUIStyleBrowser wnd = GetWindow<GUIStyleBrowser>("GUIStyle Browser");
            wnd.minSize = new Vector2(520, 340);
            wnd.ScheduleRefresh();
            wnd.Show();
        }

        private void OnEnable()
        {
            ScheduleRefresh();
        }

        private void OnFocus()
        {
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            EditorApplication.delayCall -= CollectEntries;
            EditorApplication.delayCall += CollectEntries;
        }

        private void OnGUI()
        {
            DrawToolbar();

            // Space to prevent toolbar overlap.
            GUILayout.Space(2);

            if (!_initialized)
            {
                EditorGUILayout.HelpBox("Collecting GUIStyles...", MessageType.Info);
                if (GUILayout.Button("Refresh Now"))
                    ScheduleRefresh();
                return;
            }

            List<Entry> filtered = ApplyFilters(_entries).ToList();

            // Stats bar (separate block in layout).
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"Total styles: {filtered.Count}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(EditorGUIUtility.isProSkin ? "Theme: Pro" : "Theme: Personal", EditorStyles.miniLabel);
            }

            // Space between stats and grid.
            GUILayout.Space(2);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("No GUIStyles found with current filters.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawGrid(filtered);

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            // Single toolbar container keeps correct layout height.
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Search", GUILayout.Width(45));
                GUI.SetNextControlName("SearchField");
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));

                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24)))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                }

                GUILayout.Space(8);

                _showEmptyNames = GUILayout.Toggle(_showEmptyNames, "Show <no-name>", EditorStyles.toolbarButton);
                _drawCard = GUILayout.Toggle(_drawCard, "Card Background", EditorStyles.toolbarButton);
                _nameOverlay = GUILayout.Toggle(_nameOverlay, "Name Overlay", EditorStyles.toolbarButton);

                GUILayout.Space(8);

                // Filters submenu-style row
                if (EditorGUILayout.DropdownButton(new GUIContent("Filters"), FocusType.Passive,
                        EditorStyles.toolbarDropDown))
                {
                    GenericMenu menu = new();
                    menu.AddItem(new GUIContent("Boxes"), _showBoxes, () => _showBoxes = !_showBoxes);
                    menu.AddItem(new GUIContent("Buttons"), _showButtons, () => _showButtons = !_showButtons);
                    menu.AddItem(new GUIContent("Labels"), _showLabels, () => _showLabels = !_showLabels);
                    menu.AddItem(new GUIContent("Fields"), _showFields, () => _showFields = !_showFields);
                    menu.AddItem(new GUIContent("Others"), _showOthers, () => _showOthers = !_showOthers);
                    menu.DropDown(GUILayoutUtility.GetLastRect());
                }

                GUILayout.Space(8);

                GUILayout.Label("Cell", EditorStyles.miniLabel, GUILayout.Width(28));
                _cellSize = Mathf.Round(GUILayout.HorizontalSlider(_cellSize, 56f, 192f, GUILayout.Width(160)));
                GUILayout.Label($"{_cellSize:0}", EditorStyles.miniLabel, GUILayout.Width(36));

                GUILayout.Space(8);
                GUILayout.Label("Gap", EditorStyles.miniLabel, GUILayout.Width(24));
                _cellGap = Mathf.Round(GUILayout.HorizontalSlider(_cellGap, 2f, 24f, GUILayout.Width(120)));
                GUILayout.Label($"{_cellGap:0}", EditorStyles.miniLabel, GUILayout.Width(28));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    ScheduleRefresh();
            }

            // Second line for sample text (kept outside the toolbar so it gets normal layout height).
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Sample Text", GUILayout.Width(80));
                _sampleText = EditorGUILayout.TextField(_sampleText);
            }
        }

        private IEnumerable<Entry> ApplyFilters(IEnumerable<Entry> source)
        {
            string s = _search?.Trim() ?? string.Empty;
            bool hasSearch = !string.IsNullOrEmpty(s);
            s = s.ToLowerInvariant();

            foreach (Entry e in source)
            {
                string name = e.Name ?? string.Empty;
                string nameLower = name.ToLowerInvariant();

                if (!_showEmptyNames && string.IsNullOrEmpty(name))
                    continue;

                bool isBox = nameLower == "box" || nameLower.Contains("box");
                bool isButton = nameLower == "button" || nameLower.Contains("button");
                bool isLabel = nameLower == "label" || nameLower.Contains("label");
                bool isField = nameLower.Contains("textfield") || nameLower.Contains("password") ||
                               nameLower.Contains("textarea") || nameLower.Contains("field");

                bool passCategory =
                    (isBox && _showBoxes) ||
                    (isButton && _showButtons) ||
                    (isLabel && _showLabels) ||
                    (isField && _showFields) ||
                    (!isBox && !isButton && !isLabel && !isField && _showOthers);

                if (!passCategory)
                    continue;

                if (hasSearch)
                    if (!nameLower.Contains(s) && !(e.Source?.ToLowerInvariant().Contains(s) ?? false))
                        continue;

                yield return e;
            }
        }

        // ===================== GRID RENDERING =====================

        private void DrawGrid(List<Entry> items)
        {
            // Cell metrics.
            float cellW = _cellSize;
            float cellH = _cellSize + (_nameOverlay ? 0f : _nameHeight);
            float stepX = cellW + _cellGap;
            float stepY = cellH + _cellGap;

            // First, compute rows/height using a provisional width guess (will be recomputed after we get the actual rect).
            // We will reserve space using GUILayout so nothing overlaps.
            // Reserve a big rect for manual drawing, adopting final content height.
            // We need the actual width to compute columns; we get it from the rect returned by GUILayoutUtility.GetRect.
            // To get a correct height, we must know rows; thus we compute columns after getting width, then recompute height.
            // Strategy: get a temporary rect, compute columns, rows, height, then draw using that rect.

            // Get a rect with minimal fixed height; we'll replace by the exact content rect via GUIHelper with layout trick:
            // Approach: Ask for a very tall rect based on a first pass using window width.
            float firstGuessWidth = Mathf.Max(100f, position.width - 20f);
            int firstCols = Mathf.Max(1, Mathf.FloorToInt((firstGuessWidth + _cellGap) / stepX));
            int firstRows = Mathf.CeilToInt(items.Count / (float)firstCols);
            float firstHeight = Mathf.Max(stepY, firstRows * stepY - _cellGap);

            // Reserve rect (layout).
            Rect gridRect = GUILayoutUtility.GetRect(10, firstHeight, GUILayout.ExpandWidth(true));

            // Recompute using the actual allotted width.
            float viewWidth = Mathf.Max(1f, gridRect.width);
            int columns = Mathf.Max(1, Mathf.FloorToInt((viewWidth + _cellGap) / stepX));
            int rows = Mathf.CeilToInt(items.Count / (float)columns);
            float contentHeight = Mathf.Max(stepY, rows * stepY - _cellGap);

            // If contentHeight changed a lot, add a corrective spacer so layout can grow; this avoids overlap with next controls.
            float delta = contentHeight - gridRect.height;
            if (delta > 1f)
                GUILayout.Space(delta);

            // Draw per-cell using the final metrics.
            for (int i = 0; i < items.Count; i++)
            {
                int cx = i % columns;
                int cy = i / columns;

                float x = gridRect.x + cx * stepX;
                float y = gridRect.y + cy * stepY;

                Rect cellRect = new(x, y, cellW, cellH);
                Rect previewRect = new(x, y, cellW, _cellSize);

                // Background card.
                if (_drawCard && Event.current.type == EventType.Repaint)
                {
                    Rect cardRect = new(cellRect.x - 1, cellRect.y - 1, cellRect.width + 2, cellRect.height + 2);
                    EditorGUI.DrawRect(cardRect, EditorGUIUtility.isProSkin
                        ? new Color(1, 1, 1, 0.06f)
                        : new Color(0, 0, 0, 0.06f));
                }

                // Border (draw only on Repaint).
                if (Event.current.type == EventType.Repaint)
                    Handles.DrawAAPolyLine(1.5f,
                        new Vector3(cellRect.x, cellRect.y),
                        new Vector3(cellRect.xMax, cellRect.y),
                        new Vector3(cellRect.xMax, cellRect.yMax),
                        new Vector3(cellRect.x, cellRect.yMax),
                        new Vector3(cellRect.x, cellRect.y));

                // Style preview.
                GUIStyle style = SafeGetStyle(items[i].GetStyle);
                string name = string.IsNullOrEmpty(items[i].Name) ? "<no-name>" : items[i].Name;
                string text = string.Format(_sampleText, name);

                if (style != null)
                {
                    if (Event.current.type == EventType.Repaint)
                        style.Draw(previewRect, new GUIContent(text), false, false, false, false);
                    else
                        GUI.Label(previewRect, text, style);
                }
                else
                {
                    EditorGUI.LabelField(previewRect, "∅", EditorStyles.centeredGreyMiniLabel);
                }

                // Name overlay or below.
                if (_nameOverlay)
                {
                    Rect nameBg = new(previewRect.x, previewRect.yMax - 18f, previewRect.width, 18f);
                    if (Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(nameBg, new Color(0, 0, 0, 0.25f));
                    Rect nameRect = new(nameBg.x + 4, nameBg.y + 1, nameBg.width - 8, nameBg.height - 2);
                    GUI.Label(nameRect, name, EditorStyles.whiteMiniLabel);
                }
                else
                {
                    Rect nameRect = new(previewRect.x, previewRect.yMax + 2f, previewRect.width, _nameHeight);
                    GUI.Label(nameRect, name, EditorStyles.miniLabel);
                }
            }
        }

        private static GUIStyle SafeGetStyle(Func<GUIStyle> getter)
        {
            try
            {
                return getter?.Invoke();
            }
            catch
            {
                return null;
            }
        }

        // ===================== COLLECTION (no GUI calls) =====================

        private void CollectEntries()
        {
            _entries.Clear();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            TryCollectSkinNames(() => GUI.skin, "GUI.skin", seen);
            TryCollectSkinNames(() => EditorGUIUtility.GetBuiltinSkin(EditorSkin.Game), "Builtin:Game", seen);
            TryCollectSkinNames(() => EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector), "Builtin:Inspector", seen);
            TryCollectSkinNames(() => EditorGUIUtility.GetBuiltinSkin(EditorSkin.Scene), "Builtin:Editor", seen);

            TryCollectEditorStyles("EditorStyles", seen);
            SeedCanonicalNames(seen);

            // Order for stable browsing.
            _entries.Sort((a, b) =>
            {
                int cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return cmp != 0 ? cmp : string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);
            });

            _initialized = true;
            Repaint();
        }

        private void TryCollectSkinNames(Func<GUISkin> skinGetter, string source, HashSet<string> seen)
        {
            PropertyInfo[] props = typeof(GUISkin)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(GUIStyle) && p.CanRead)
                .ToArray();

            foreach (PropertyInfo p in props)
            {
                string propName = p.Name;
                string key = $"{source}:{propName}";
                if (seen.Add(key))
                    _entries.Add(new Entry
                    {
                        Name = propName,
                        Source = source,
                        GetStyle = () =>
                        {
                            GUISkin skin = skinGetter();
                            if (skin == null) return null;
                            try
                            {
                                return p.GetValue(skin, null) as GUIStyle;
                            }
                            catch
                            {
                                return null;
                            }
                        }
                    });
            }

            // Custom names (best-effort).
            try
            {
                GUISkin s = skinGetter();
                foreach (GUIStyle cs in s?.customStyles ?? Array.Empty<GUIStyle>())
                {
                    string name = cs?.name ?? string.Empty;
                    string key = $"{source}:{name}";
                    if (string.IsNullOrEmpty(name) || !seen.Add(key))
                        continue;

                    _entries.Add(new Entry
                    {
                        Name = name,
                        Source = source,
                        GetStyle = () =>
                        {
                            GUISkin skin = skinGetter();
                            return string.IsNullOrEmpty(name) ? null : skin?.FindStyle(name);
                        }
                    });
                }
            }
            catch
            {
                /* ignore */
            }
        }

        private void TryCollectEditorStyles(string source, HashSet<string> seen)
        {
            Type t = typeof(EditorStyles);

            IEnumerable<PropertyInfo> props = t.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(GUIStyle) && p.CanRead);

            foreach (PropertyInfo p in props)
            {
                string name = p.Name;
                string key = $"{source}:{name}";
                if (!seen.Add(key)) continue;

                _entries.Add(new Entry
                {
                    Name = name,
                    Source = source,
                    GetStyle = () =>
                    {
                        try
                        {
                            return p.GetValue(null, null) as GUIStyle;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                });
            }

            IEnumerable<FieldInfo> fields = t.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(GUIStyle));

            foreach (FieldInfo f in fields)
            {
                string name = f.Name;
                string key = $"{source}:{name}";
                if (!seen.Add(key)) continue;

                _entries.Add(new Entry
                {
                    Name = name,
                    Source = source,
                    GetStyle = () =>
                    {
                        try
                        {
                            return f.GetValue(null) as GUIStyle;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                });
            }
        }

        private void SeedCanonicalNames(HashSet<string> seen)
        {
            string[] canonical =
            {
                "box", "button", "toggle", "label", "window", "textField", "textArea",
                "horizontalSlider", "horizontalSliderThumb", "verticalSlider", "verticalSliderThumb",
                "horizontalScrollbar", "horizontalScrollbarThumb", "horizontalScrollbarLeftButton",
                "horizontalScrollbarRightButton",
                "verticalScrollbar", "verticalScrollbarThumb", "verticalScrollbarUpButton",
                "verticalScrollbarDownButton",
                "scrollView"
            };

            foreach (string n in canonical)
            {
                string key = $"GUI.skin (lookup):{n}";
                if (!seen.Add(key)) continue;

                _entries.Add(new Entry
                {
                    Name = n,
                    Source = "GUI.skin (lookup)",
                    GetStyle = () =>
                    {
                        try
                        {
                            return GUI.skin?.FindStyle(n);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                });
            }
        }
    }
}