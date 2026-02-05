using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class PrefabUGUIFinderWindow : EditorWindow
    {
        private enum ViewMode
        {
            AlphabeticalList = 0,
            TreeView = 1
        }

        [Flags]
        private enum ComponentFilterMask
        {
            None = 0,

            Canvas = 1 << 0,
            Button = 1 << 1,
            ScrollRect = 1 << 2,
            Text = 1 << 3,
            Image = 1 << 4,
            RawImage = 1 << 5,

            VerticalLayoutGroup = 1 << 6,
            HorizontalLayoutGroup = 1 << 7,
            GridLayoutGroup = 1 << 8,

            Dropdown = 1 << 9,
            Slider = 1 << 10,

            TMP_Dropdown = 1 << 11,
            TMP_InputField = 1 << 12,
            TextMeshProUGUI = 1 << 13
        }

        [Serializable]
        private sealed class PrefabInfo
        {
            public string Guid;
            public string Path;
            public string Name;

            public bool HasAnyUGUI;
            public ComponentFilterMask PresentMask;

            public string ComponentsText;
            public string Tooltip;
        }

        private const string WindowTitle = "uGUI Prefab Browser";
        private const float RightLabelWidth = 380f;
        private const float RowHeight = 22f;

        // UI state
        private ViewMode _viewMode = ViewMode.AlphabeticalList;
        private string _search = string.Empty;

        private Vector2 _listScroll;
        private int _listSelectedIndex = -1;

        private bool _showFilters = true;
        private ComponentFilterMask _selectedFilters = ComponentFilterMask.None;

        // Data
        private List<PrefabInfo> _allPrefabs = new(); // Prefabs that have any uGUI.
        private List<PrefabInfo> _filtered = new();

        // Tree
        private TreeViewState _treeState;
        private UGUITreeView _treeView;

        // Avoid reopening same prefab repeatedly
        private string _lastOpenedPrefabPath = null;

        // Styles
        private GUIStyle _headerTitle;
        private GUIStyle _headerSubtitle;
        private GUIStyle _card;
        private GUIStyle _pill;
        private GUIStyle _pillOn;
        private GUIStyle _miniHint;
        private GUIStyle _rowName;
        private GUIStyle _rowComponents;
        private GUIStyle _statusBar;
        private GUIStyle _statusBarText;

        // Icons
        private GUIContent _iconWindow;
        private GUIContent _iconList;
        private GUIContent _iconTree;
        private GUIContent _iconRefresh;
        private GUIContent _iconPrefab;

        // Reflection-based TMP type access (avoids compile errors if TMP is not installed)
        private static class TMPTypes
        {
            public static readonly Type TMP_Dropdown = FindType(
                "TMPro.TMP_Dropdown, Unity.TextMeshPro",
                "TMPro.TMP_Dropdown, Unity.TextMeshPro.Runtime");

            public static readonly Type TMP_InputField = FindType(
                "TMPro.TMP_InputField, Unity.TextMeshPro",
                "TMPro.TMP_InputField, Unity.TextMeshPro.Runtime");

            public static readonly Type TextMeshProUGUI = FindType(
                "TMPro.TextMeshProUGUI, Unity.TextMeshPro",
                "TMPro.TextMeshProUGUI, Unity.TextMeshPro.Runtime");

            private static Type FindType(params string[] assemblyQualifiedNames)
            {
                for (int i = 0; i < assemblyQualifiedNames.Length; i++)
                {
                    Type t = Type.GetType(assemblyQualifiedNames[i], false);
                    if (t != null)
                        return t;
                }

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null)
                        continue;

                    try
                    {
                        foreach (string name in assemblyQualifiedNames)
                        {
                            string typeName = name.Split(',')[0].Trim();
                            Type t = asm.GetType(typeName, false);
                            if (t != null)
                                return t;
                        }
                    }
                    catch
                    {
                        // Ignore reflection errors.
                    }
                }

                return null;
            }
        }

        [MenuItem("Tools/LegendaryTools/UI/Prefab uGUI Finder")]
        public static void Open()
        {
            PrefabUGUIFinderWindow window = GetWindow<PrefabUGUIFinderWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(900, 460);
            window.Show();
        }

        private void OnEnable()
        {
            _treeState ??= new TreeViewState();
            _treeView ??= new UGUITreeView(_treeState, OpenPrefabInPrefabModeDiscardingCurrent);

            LoadIcons();
            BuildStyles();

            RefreshData();
        }

        private void LoadIcons()
        {
            _iconWindow = EditorGUIUtility.IconContent("Canvas Icon");
            _iconList = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow");
            _iconTree = EditorGUIUtility.IconContent("d_UnityEditor.HierarchyWindow");
            _iconRefresh = EditorGUIUtility.IconContent("d_Refresh");
            _iconPrefab = EditorGUIUtility.IconContent("Prefab Icon");
        }

        private void BuildStyles()
        {
            _headerTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16
            };

            _headerSubtitle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.75f, 0.75f, 0.75f)
                        : new Color(0.35f, 0.35f, 0.35f)
                }
            };

            _card = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(8, 8, 6, 6)
            };

            _pill = new GUIStyle("Button")
            {
                fontSize = 11,
                padding = new RectOffset(10, 10, 5, 5),
                margin = new RectOffset(2, 2, 2, 2)
            };

            _pillOn = new GUIStyle(_pill);

            _miniHint = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.7f, 0.7f, 0.7f)
                        : new Color(0.35f, 0.35f, 0.35f)
                }
            };

            _rowName = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal
            };

            _rowComponents = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.72f, 0.72f, 0.72f)
                        : new Color(0.32f, 0.32f, 0.32f)
                }
            };

            _statusBar = new GUIStyle(EditorStyles.toolbar)
            {
                fixedHeight = 22
            };

            _statusBarText = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }

        private void OnGUI()
        {
            DrawTopHeader();
            DrawToolbar();

            using (new EditorGUILayout.VerticalScope(_card))
            {
                DrawFiltersPanel();
                EditorGUILayout.Space(6);

                switch (_viewMode)
                {
                    case ViewMode.AlphabeticalList:
                        DrawAlphabeticalList();
                        HandleListKeyboardOpenOnArrows();
                        break;

                    case ViewMode.TreeView:
                        DrawTreeView();
                        break;
                }
            }

            DrawStatusBar();
        }

        private void DrawTopHeader()
        {
            using (new EditorGUILayout.HorizontalScope(new GUIStyle("Toolbar") { fixedHeight = 0 },
                       GUILayout.ExpandWidth(true)))
            {
                GUILayout.Space(6);
                GUILayout.Label(_iconWindow?.image, GUILayout.Width(20), GUILayout.Height(20));

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(WindowTitle, _headerTitle);
                    EditorGUILayout.LabelField(
                        "Browse and open prefabs containing uGUI. Arrow navigation opens immediately; switching discards current Prefab Mode changes. Listing shows detected components.",
                        _headerSubtitle);
                }

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(2);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool listOn = _viewMode == ViewMode.AlphabeticalList;
                bool treeOn = _viewMode == ViewMode.TreeView;

                GUIContent listContent = new(" List", _iconList.image, "Alphabetical List");
                GUIContent treeContent = new(" Tree", _iconTree.image, "Folder Tree View");

                bool newList = GUILayout.Toggle(listOn, listContent, EditorStyles.toolbarButton, GUILayout.Width(80));
                bool newTree = GUILayout.Toggle(treeOn, treeContent, EditorStyles.toolbarButton, GUILayout.Width(80));

                if (newList && !listOn) _viewMode = ViewMode.AlphabeticalList;
                if (newTree && !treeOn) _viewMode = ViewMode.TreeView;

                // Tree-specific actions
                if (_viewMode == ViewMode.TreeView && _treeView != null)
                {
                    GUILayout.Space(10);

                    if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(90)))
                        _treeView.ExpandAll();

                    if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(90)))
                        _treeView.CollapseAll();
                }

                GUILayout.Space(10);

                string newSearch = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSearchTextField"),
                    GUILayout.ExpandWidth(true));
                if (newSearch != _search)
                {
                    _search = newSearch;
                    ApplyFilterAndRebuild(true);
                }

                if (GUILayout.Button(GUIContent.none, GUI.skin.FindStyle("ToolbarSearchCancelButton")))
                {
                    _search = string.Empty;
                    GUI.FocusControl(null);
                    ApplyFilterAndRebuild(true);
                }

                GUILayout.Space(8);

                GUIContent refreshContent = new(" Refresh", _iconRefresh.image, "Rescan Project Prefabs");
                if (GUILayout.Button(refreshContent, EditorStyles.toolbarButton, GUILayout.Width(90))) RefreshData();
            }
        }

        private void DrawFiltersPanel()
        {
            using (new EditorGUILayout.VerticalScope(new GUIStyle("HelpBox")
                       { padding = new RectOffset(10, 10, 8, 8) }))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _showFilters = EditorGUILayout.Foldout(_showFilters, "Component Filters (OR)", true);

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(_selectedFilters == ComponentFilterMask.None))
                    {
                        if (GUILayout.Button("Clear", GUILayout.Width(70)))
                        {
                            _selectedFilters = ComponentFilterMask.None;
                            ApplyFilterAndRebuild(true);
                        }
                    }

                    if (GUILayout.Button("All", GUILayout.Width(60)))
                    {
                        _selectedFilters =
                            ComponentFilterMask.Canvas |
                            ComponentFilterMask.Button |
                            ComponentFilterMask.ScrollRect |
                            ComponentFilterMask.Text |
                            ComponentFilterMask.Image |
                            ComponentFilterMask.RawImage |
                            ComponentFilterMask.VerticalLayoutGroup |
                            ComponentFilterMask.HorizontalLayoutGroup |
                            ComponentFilterMask.GridLayoutGroup |
                            ComponentFilterMask.Dropdown |
                            ComponentFilterMask.TMP_Dropdown |
                            ComponentFilterMask.TMP_InputField |
                            ComponentFilterMask.Slider |
                            ComponentFilterMask.TextMeshProUGUI;

                        ApplyFilterAndRebuild(true);
                    }
                }

                if (!_showFilters)
                {
                    DrawSelectedFilterChips();
                    return;
                }

                EditorGUILayout.Space(6);

                bool changed = false;

                DrawFilterRow(ref changed,
                    ("Canvas", ComponentFilterMask.Canvas),
                    ("Button", ComponentFilterMask.Button),
                    ("ScrollRect", ComponentFilterMask.ScrollRect),
                    ("Text", ComponentFilterMask.Text));

                DrawFilterRow(ref changed,
                    ("Image", ComponentFilterMask.Image),
                    ("RawImage", ComponentFilterMask.RawImage),
                    ("VerticalLayoutGroup", ComponentFilterMask.VerticalLayoutGroup),
                    ("HorizontalLayoutGroup", ComponentFilterMask.HorizontalLayoutGroup));

                DrawFilterRow(ref changed,
                    ("GridLayoutGroup", ComponentFilterMask.GridLayoutGroup),
                    ("Dropdown", ComponentFilterMask.Dropdown),
                    ("TMP_Dropdown", ComponentFilterMask.TMP_Dropdown),
                    ("TMP_InputField", ComponentFilterMask.TMP_InputField));

                DrawFilterRow(ref changed,
                    ("Slider", ComponentFilterMask.Slider),
                    ("TextMeshProUGUI", ComponentFilterMask.TextMeshProUGUI));

                if (changed)
                    ApplyFilterAndRebuild(true);

                EditorGUILayout.Space(6);
                DrawSelectedFilterChips();

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(
                    _selectedFilters == ComponentFilterMask.None
                        ? "No filters selected: showing ANY prefab that contains uGUI."
                        : "Showing prefabs that contain ANY of the selected components (OR).",
                    _miniHint);
            }
        }

        private void DrawFilterRow(ref bool changed, params (string label, ComponentFilterMask bit)[] filters)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < filters.Length; i++)
                {
                    changed |= DrawPillToggle(filters[i].label, filters[i].bit);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private bool DrawPillToggle(string label, ComponentFilterMask bit)
        {
            bool on = (_selectedFilters & bit) != 0;

            GUIStyle style = on ? _pillOn : _pill;

            Color prev = GUI.backgroundColor;
            if (on)
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.35f, 0.55f, 0.95f, 1f)
                    : new Color(0.25f, 0.45f, 0.90f, 1f);

            bool clicked = GUILayout.Button(label, style);

            GUI.backgroundColor = prev;

            if (!clicked)
                return false;

            if (on) _selectedFilters &= ~bit;
            else _selectedFilters |= bit;

            return true;
        }

        private void DrawSelectedFilterChips()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Active:", GUILayout.Width(50));

                if (_selectedFilters == ComponentFilterMask.None)
                {
                    EditorGUILayout.LabelField("Any uGUI", EditorStyles.miniLabel);
                    return;
                }

                foreach ((string label, ComponentFilterMask bit) in EnumerateAllFilters())
                {
                    if ((_selectedFilters & bit) == 0)
                        continue;

                    DrawChip(label);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawChip(string text)
        {
            GUIStyle chip = new(EditorStyles.miniButton)
            {
                padding = new RectOffset(8, 8, 2, 2),
                margin = new RectOffset(2, 2, 0, 0)
            };

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.25f, 0.25f, 0.25f, 1f)
                : new Color(0.9f, 0.9f, 0.9f, 1f);
            GUILayout.Button(text, chip, GUILayout.Height(18));
            GUI.backgroundColor = prev;
        }

        private IEnumerable<(string label, ComponentFilterMask bit)> EnumerateAllFilters()
        {
            yield return ("Canvas", ComponentFilterMask.Canvas);
            yield return ("Button", ComponentFilterMask.Button);
            yield return ("ScrollRect", ComponentFilterMask.ScrollRect);
            yield return ("Text", ComponentFilterMask.Text);
            yield return ("Image", ComponentFilterMask.Image);
            yield return ("RawImage", ComponentFilterMask.RawImage);
            yield return ("VerticalLayoutGroup", ComponentFilterMask.VerticalLayoutGroup);
            yield return ("HorizontalLayoutGroup", ComponentFilterMask.HorizontalLayoutGroup);
            yield return ("GridLayoutGroup", ComponentFilterMask.GridLayoutGroup);
            yield return ("Dropdown", ComponentFilterMask.Dropdown);
            yield return ("TMP_Dropdown", ComponentFilterMask.TMP_Dropdown);
            yield return ("TMP_InputField", ComponentFilterMask.TMP_InputField);
            yield return ("Slider", ComponentFilterMask.Slider);
            yield return ("TextMeshProUGUI", ComponentFilterMask.TextMeshProUGUI);
        }

        private void DrawAlphabeticalList()
        {
            if (_filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("No matching prefabs found for current search/filters.", MessageType.Info);
                return;
            }

            if (_listSelectedIndex >= _filtered.Count)
                _listSelectedIndex = _filtered.Count - 1;

            Rect listRect = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            using (new GUI.GroupScope(listRect))
            {
                Rect inner = new(0, 0, listRect.width, listRect.height);

                using (GUI.ScrollViewScope scroll = new(inner, _listScroll,
                           new Rect(0, 0, inner.width - 16, _filtered.Count * RowHeight)))
                {
                    _listScroll = scroll.scrollPosition;

                    float y = 0f;
                    for (int i = 0; i < _filtered.Count; i++)
                    {
                        PrefabInfo info = _filtered[i];
                        Rect row = new(0, y, inner.width - 16, RowHeight);
                        y += RowHeight;

                        bool selected = i == _listSelectedIndex;

                        DrawListRow(row, info, selected);

                        if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                        {
                            _listSelectedIndex = i;
                            GUI.FocusControl(null);
                            Repaint();

                            OpenPrefabInPrefabModeDiscardingCurrent(info.Path);
                            Event.current.Use();
                        }
                    }
                }
            }
        }

        private void DrawListRow(Rect row, PrefabInfo info, bool selected)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color bg;
                if (selected)
                    bg = EditorGUIUtility.isProSkin
                        ? new Color(0.22f, 0.45f, 0.85f, 0.35f)
                        : new Color(0.20f, 0.42f, 0.86f, 0.22f);
                else
                    bg = Mathf.FloorToInt(row.y / RowHeight) % 2 == 0
                        ? EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.03f)
                        : Color.clear;

                EditorGUI.DrawRect(row, bg);
                EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1), new Color(0, 0, 0, 0.12f));
            }

            float x = row.x + 8;

            Rect iconRect = new(x, row.y + 2, 18, 18);
            if (_iconPrefab?.image != null)
                GUI.DrawTexture(iconRect, _iconPrefab.image, ScaleMode.ScaleToFit);
            x += 24;

            Rect nameRect = new(x, row.y + 2, 260, row.height - 4);
            GUIContent nameContent = new(info.Name, info.Tooltip);
            EditorGUI.LabelField(nameRect, nameContent, _rowName);

            Rect compRect = new(nameRect.xMax + 8, row.y + 2, row.width - (nameRect.xMax + 8) - 10,
                row.height - 4);
            GUIContent compContent = new(info.ComponentsText, info.Tooltip);
            EditorGUI.LabelField(compRect, compContent, _rowComponents);
        }

        private void DrawTreeView()
        {
            if (_treeView == null)
                return;

            _treeView.searchString = _search ?? string.Empty;

            Rect rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            _treeView.OnGUI(rect);
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(_statusBar))
            {
                GUILayout.Label($"Matches: {_filtered.Count}", _statusBarText, GUILayout.Width(120));

                GUILayout.Space(10);

                string modeText = _viewMode == ViewMode.AlphabeticalList
                    ? "List: ↑/↓ opens prefabs"
                    : "Tree: arrows navigate; opening happens on selection";

                GUILayout.Label(modeText, _statusBarText);

                GUILayout.FlexibleSpace();

                GUILayout.Label("Hover rows to see full asset path in tooltip.", _statusBarText);
            }
        }

        private void HandleListKeyboardOpenOnArrows()
        {
            if (_viewMode != ViewMode.AlphabeticalList)
                return;

            if (focusedWindow != this)
                return;

            Event e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            if (_filtered.Count == 0)
                return;

            if (_listSelectedIndex < 0)
                _listSelectedIndex = 0;

            bool moved = false;

            if (e.keyCode == KeyCode.DownArrow)
            {
                int next = Mathf.Min(_listSelectedIndex + 1, _filtered.Count - 1);
                moved = next != _listSelectedIndex;
                _listSelectedIndex = next;
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                int next = Mathf.Max(_listSelectedIndex - 1, 0);
                moved = next != _listSelectedIndex;
                _listSelectedIndex = next;
            }

            if (moved)
            {
                e.Use();
                Repaint();

                OpenPrefabInPrefabModeDiscardingCurrent(_filtered[_listSelectedIndex].Path);
            }
        }

        private void RefreshData()
        {
            _allPrefabs = ScanPrefabsWithUGUIAndMasks();
            ApplyFilterAndRebuild(true);
        }

        private void ApplyFilterAndRebuild(bool forceResetSelection)
        {
            ApplyFilter();

            if (forceResetSelection || _listSelectedIndex >= _filtered.Count)
                _listSelectedIndex = _filtered.Count > 0 ? 0 : -1;

            _treeView?.SetData(_filtered);
            Repaint();
        }

        private void ApplyFilter()
        {
            string s = (_search ?? string.Empty).Trim();
            IEnumerable<PrefabInfo> q = _allPrefabs;

            if (_selectedFilters != ComponentFilterMask.None)
            {
                ComponentFilterMask desired = _selectedFilters;
                q = q.Where(p => (p.PresentMask & desired) != 0);
            }

            if (!string.IsNullOrEmpty(s))
                q = q.Where(p =>
                    p.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Path.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.ComponentsText.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);

            _filtered = q.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<PrefabInfo> ScanPrefabsWithUGUIAndMasks()
        {
            List<PrefabInfo> results = new(512);

            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            int total = guids.Length;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    string guid = guids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    EvaluatePrefab(path, out bool hasAnyUGUI, out ComponentFilterMask presentMask);

                    if (hasAnyUGUI)
                    {
                        string components = BuildComponentsText(presentMask);

                        results.Add(new PrefabInfo
                        {
                            Guid = guid,
                            Path = path,
                            Name = Path.GetFileNameWithoutExtension(path),
                            HasAnyUGUI = true,
                            PresentMask = presentMask,
                            ComponentsText = string.IsNullOrEmpty(components) ? "uGUI" : components,
                            Tooltip = path
                        });
                    }

                    if ((i & 63) == 0)
                        EditorUtility.DisplayProgressBar(WindowTitle, $"Scanning prefabs... ({i + 1}/{total})",
                            (float)(i + 1) / total);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        private static string BuildComponentsText(ComponentFilterMask mask)
        {
            List<string> parts = new(16);

            void AddIf(ComponentFilterMask bit, string label)
            {
                if ((mask & bit) != 0)
                    parts.Add(label);
            }

            AddIf(ComponentFilterMask.Canvas, "Canvas");
            AddIf(ComponentFilterMask.Button, "Button");
            AddIf(ComponentFilterMask.ScrollRect, "ScrollRect");
            AddIf(ComponentFilterMask.Text, "Text");
            AddIf(ComponentFilterMask.TextMeshProUGUI, "TextMeshProUGUI");
            AddIf(ComponentFilterMask.Image, "Image");
            AddIf(ComponentFilterMask.RawImage, "RawImage");
            AddIf(ComponentFilterMask.VerticalLayoutGroup, "VerticalLayoutGroup");
            AddIf(ComponentFilterMask.HorizontalLayoutGroup, "HorizontalLayoutGroup");
            AddIf(ComponentFilterMask.GridLayoutGroup, "GridLayoutGroup");
            AddIf(ComponentFilterMask.Dropdown, "Dropdown");
            AddIf(ComponentFilterMask.TMP_Dropdown, "TMP_Dropdown");
            AddIf(ComponentFilterMask.TMP_InputField, "TMP_InputField");
            AddIf(ComponentFilterMask.Slider, "Slider");

            return string.Join(", ", parts);
        }

        private static void EvaluatePrefab(string prefabPath, out bool hasAnyUGUI, out ComponentFilterMask presentMask)
        {
            hasAnyUGUI = false;
            presentMask = ComponentFilterMask.None;

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return;

                Component[] components = root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component c = components[i];
                    if (c == null)
                        continue;

                    Type t = c.GetType();
                    string ns = t.Namespace ?? string.Empty;

                    if (c is Canvas)
                        hasAnyUGUI = true;
                    else if (ns.StartsWith("UnityEngine.UI", StringComparison.Ordinal) ||
                             ns.StartsWith("UnityEngine.EventSystems", StringComparison.Ordinal))
                        hasAnyUGUI = true;

                    if (c is Canvas) presentMask |= ComponentFilterMask.Canvas;

                    string fullName = t.FullName ?? string.Empty;

                    if (fullName == "UnityEngine.UI.Button") presentMask |= ComponentFilterMask.Button;
                    else if (fullName == "UnityEngine.UI.ScrollRect") presentMask |= ComponentFilterMask.ScrollRect;
                    else if (fullName == "UnityEngine.UI.Text") presentMask |= ComponentFilterMask.Text;
                    else if (fullName == "UnityEngine.UI.Image") presentMask |= ComponentFilterMask.Image;
                    else if (fullName == "UnityEngine.UI.RawImage") presentMask |= ComponentFilterMask.RawImage;
                    else if (fullName == "UnityEngine.UI.VerticalLayoutGroup")
                        presentMask |= ComponentFilterMask.VerticalLayoutGroup;
                    else if (fullName == "UnityEngine.UI.HorizontalLayoutGroup")
                        presentMask |= ComponentFilterMask.HorizontalLayoutGroup;
                    else if (fullName == "UnityEngine.UI.GridLayoutGroup")
                        presentMask |= ComponentFilterMask.GridLayoutGroup;
                    else if (fullName == "UnityEngine.UI.Dropdown") presentMask |= ComponentFilterMask.Dropdown;
                    else if (fullName == "UnityEngine.UI.Slider") presentMask |= ComponentFilterMask.Slider;

                    if (TMPTypes.TMP_Dropdown != null && TMPTypes.TMP_Dropdown.IsAssignableFrom(t))
                    {
                        presentMask |= ComponentFilterMask.TMP_Dropdown;
                        hasAnyUGUI = true;
                    }

                    if (TMPTypes.TMP_InputField != null && TMPTypes.TMP_InputField.IsAssignableFrom(t))
                    {
                        presentMask |= ComponentFilterMask.TMP_InputField;
                        hasAnyUGUI = true;
                    }

                    if (TMPTypes.TextMeshProUGUI != null && TMPTypes.TextMeshProUGUI.IsAssignableFrom(t))
                    {
                        presentMask |= ComponentFilterMask.TextMeshProUGUI;
                        hasAnyUGUI = true;
                    }
                }
            }
            catch
            {
                // Ignore load failures gracefully.
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------
        // Prefab Mode switching that discards changes (best effort)
        // ------------------------------------------------------------

        private void OpenPrefabInPrefabModeDiscardingCurrent(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return;

            if (string.Equals(_lastOpenedPrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase))
                return;

            DiscardAndCloseCurrentPrefabStageIfNeeded(prefabPath);

            Object prefabAsset = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
            if (prefabAsset == null)
                return;

            _lastOpenedPrefabPath = prefabPath;
            AssetDatabase.OpenAsset(prefabAsset);
        }

        private static void DiscardAndCloseCurrentPrefabStageIfNeeded(string nextPrefabPath)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return;

            if (string.Equals(stage.assetPath, nextPrefabPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (TryClosePrefabStageNoPrompt(stage, false))
                return;

            // Fallback (may prompt in some versions).
            StageUtility.GoToMainStage();
        }

        private static bool TryClosePrefabStageNoPrompt(PrefabStage stage, bool saveChanges)
        {
            if (stage == null)
                return false;

            Type utilType = typeof(PrefabStageUtility);
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            MethodInfo[] methods = utilType.GetMethods(Flags);

            MethodInfo[] candidates = methods
                .Where(m => string.Equals(m.Name, "ClosePrefabStage", StringComparison.Ordinal) ||
                            string.Equals(m.Name, "CloseStage", StringComparison.Ordinal))
                .ToArray();

            if (candidates.Length == 0)
                candidates = methods
                    .Where(m =>
                        m.Name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        m.Name.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        m.GetParameters().Any(p => p.ParameterType == typeof(PrefabStage)))
                    .ToArray();

            foreach (MethodInfo mi in candidates)
            {
                try
                {
                    ParameterInfo[] ps = mi.GetParameters();
                    object[] args = BuildArgs(ps, stage, saveChanges);
                    if (args == null)
                        continue;

                    mi.Invoke(null, args);
                    return true;
                }
                catch
                {
                    // Try next.
                }
            }

            return false;
        }

        private static object[] BuildArgs(ParameterInfo[] ps, PrefabStage stage, bool saveChanges)
        {
            if (ps == null || ps.Length == 0)
                return null;

            int stageIndex = Array.FindIndex(ps, p => p.ParameterType == typeof(PrefabStage));
            if (stageIndex < 0)
                return null;

            object[] args = new object[ps.Length];

            for (int i = 0; i < ps.Length; i++)
            {
                Type t = ps[i].ParameterType;

                if (t == typeof(PrefabStage))
                {
                    args[i] = stage;
                }
                else if (t == typeof(bool))
                {
                    args[i] = saveChanges;
                    saveChanges = false;
                }
                else if (t.IsEnum)
                {
                    object enumValue = TryPickEnumValue(t,
                        new[] { "Discard", "DontSave", "DoNotSave", "NoSave", "Cancel" });
                    args[i] = enumValue ?? Activator.CreateInstance(t);
                }
                else
                {
                    return null;
                }
            }

            return args;
        }

        private static object TryPickEnumValue(Type enumType, string[] preferredNames)
        {
            try
            {
                string[] names = Enum.GetNames(enumType);
                for (int i = 0; i < preferredNames.Length; i++)
                {
                    string wanted = preferredNames[i];
                    for (int j = 0; j < names.Length; j++)
                    {
                        if (string.Equals(names[j], wanted, StringComparison.OrdinalIgnoreCase))
                            return Enum.Parse(enumType, names[j]);
                    }
                }
            }
            catch
            {
                // Ignore.
            }

            return null;
        }

        // -----------------------------
        // Tree View Implementation
        // -----------------------------

        private sealed class UGUITreeView : TreeView
        {
            private readonly Action<string> _onOpenPrefab;
            private readonly Dictionary<int, PrefabInfo> _idToPrefab = new();
            private List<PrefabInfo> _data = new();
            private int _nextId = 1;

            public UGUITreeView(TreeViewState state, Action<string> onOpenPrefab)
                : base(state)
            {
                _onOpenPrefab = onOpenPrefab;
                showBorder = true;
                showAlternatingRowBackgrounds = true;
                Reload();
            }

            public void SetData(List<PrefabInfo> data)
            {
                _data = data ?? new List<PrefabInfo>();
                Reload();

                if (_data.Count > 0 && GetSelection().Count == 0)
                    SetSelection(new List<int> { 1 }, TreeViewSelectionOptions.RevealAndFrame);
            }

            protected override TreeViewItem BuildRoot()
            {
                _idToPrefab.Clear();
                _nextId = 1;

                TreeViewItem root = new(0, -1, "Root");

                if (_data == null || _data.Count == 0)
                {
                    root.children = new List<TreeViewItem>();
                    SetupDepthsFromParentsAndChildren(root);
                    return root;
                }

                Dictionary<string, TreeViewItem> pathToNode = new(StringComparer.OrdinalIgnoreCase)
                {
                    { "Assets", new TreeViewItem(_nextId++, 0, "Assets") }
                };

                root.AddChild(pathToNode["Assets"]);

                foreach (PrefabInfo p in _data)
                {
                    string[] parts = p.Path.Split('/');
                    if (parts.Length == 0)
                        continue;

                    if (!string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string currentPath = "Assets";
                    TreeViewItem parent = pathToNode["Assets"];

                    for (int i = 1; i < parts.Length - 1; i++)
                    {
                        string folder = parts[i];
                        currentPath += "/" + folder;

                        if (!pathToNode.TryGetValue(currentPath, out TreeViewItem folderNode))
                        {
                            folderNode = new TreeViewItem(_nextId++, parent.depth + 1, folder);
                            pathToNode[currentPath] = folderNode;
                            parent.AddChild(folderNode);
                        }

                        parent = folderNode;
                    }

                    TreeViewItem leaf = new(_nextId++, parent.depth + 1, p.Name);
                    parent.AddChild(leaf);
                    _idToPrefab[leaf.id] = p;
                }

                SortChildrenRecursive(root);
                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            private static void SortChildrenRecursive(TreeViewItem node)
            {
                if (node.children == null || node.children.Count == 0)
                    return;

                node.children.Sort((a, b) =>
                    string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < node.children.Count; i++)
                {
                    SortChildrenRecursive(node.children[i]);
                }
            }

            protected override void SingleClickedItem(int id)
            {
                base.SingleClickedItem(id);

                if (_idToPrefab.TryGetValue(id, out PrefabInfo info))
                    _onOpenPrefab?.Invoke(info.Path);
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                base.SelectionChanged(selectedIds);

                if (selectedIds == null || selectedIds.Count == 0)
                    return;

                int id = selectedIds[0];
                if (_idToPrefab.TryGetValue(id, out PrefabInfo info))
                    _onOpenPrefab?.Invoke(info.Path);
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                base.RowGUI(args);

                if (_idToPrefab.TryGetValue(args.item.id, out PrefabInfo info))
                {
                    Rect r = args.rowRect;
                    r.xMin = Mathf.Max(r.xMin, r.xMax - RightLabelWidth);
                    r.y += 1;

                    GUIStyle style = new(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip
                    };

                    GUIContent content = new(info.ComponentsText, info.Tooltip);
                    GUI.Label(r, content, style);
                }
            }
        }
    }
}