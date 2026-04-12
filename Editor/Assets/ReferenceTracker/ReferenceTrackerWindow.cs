using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Renders the Reference Tracker editor window.
    /// </summary>
    public sealed class ReferenceTrackerWindow : EditorWindow
    {
        private const string WindowTitle = "Reference Tracker";
        private const string MainMenuPath = "Tools/LegendaryTools/Assets/Reference Tracker";
        private const string GameObjectMenuPath = "GameObject/Reference Tracker/Find References In Current Scope";
        private const string TransformContextPath = "CONTEXT/Transform/Reference Tracker/Find References In Current Scope";
        private const string ComponentContextPath = "CONTEXT/Component/Reference Tracker/Find References In Current Scope";
        private const string AssetContextPath = "Assets/Reference Tracker/Find References";

        private const float TableWidth = 1520f;
        private const float TableHeaderHeight = 28f;
        private const float TableGroupHeight = 28f;
        private const float TableRowHeight = 34f;
        private const float KindColumnWidth = 92f;
        private const float AssetColumnWidth = 260f;
        private const float GameObjectColumnWidth = 230f;
        private const float ComponentColumnWidth = 170f;
        private const float PropertyColumnWidth = 230f;
        private const float ReferenceColumnWidth = 150f;
        private const float ActionsColumnWidth = 388f;

        private static readonly ReferenceTrackerScopeResolver ScopeResolver = new ReferenceTrackerScopeResolver();
        private static readonly ReferenceTrackerSearchService SearchService = new ReferenceTrackerSearchService(ScopeResolver);
        private static readonly ReferenceTrackerGroupingService GroupingService = new ReferenceTrackerGroupingService();
        private static readonly ReferenceTrackerSelectionService SelectionService = new ReferenceTrackerSelectionService();
        private static readonly ReferenceTrackerWindowController Controller =
            new ReferenceTrackerWindowController(ScopeResolver, SearchService, GroupingService, SelectionService);

        [SerializeField] private ReferenceTrackerWindowState _state = new ReferenceTrackerWindowState();

        private readonly Dictionary<string, bool> _groupStates = new Dictionary<string, bool>(System.StringComparer.Ordinal);

        private Vector2 _resultsScroll;
        private CancellationTokenSource _searchCancellation;

        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _tableHeaderStyle;
        private GUIStyle _tableCellStyle;
        private GUIStyle _tableMiniCellStyle;
        private GUIStyle _tableActionStyle;
        private GUIStyle _emptyStateStyle;

        [MenuItem(MainMenuPath)]
        private static void OpenWindow()
        {
            ReferenceTrackerWindow window = GetWindow<ReferenceTrackerWindow>(WindowTitle);
            window.minSize = new Vector2(1040f, 520f);
            window.Show();
        }

        [MenuItem(GameObjectMenuPath, false, 49)]
        private static void FindFromGameObjectMenu(MenuCommand command)
        {
            GameObject gameObject = command.context as GameObject;
            if (gameObject == null)
            {
                gameObject = Selection.activeGameObject;
            }

            if (gameObject != null)
            {
                OpenWithTargetAndSearch(gameObject);
            }
        }

        [MenuItem(GameObjectMenuPath, true)]
        private static bool ValidateFindFromGameObjectMenu()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem(TransformContextPath)]
        private static void FindFromTransformContext(MenuCommand command)
        {
            Transform transform = command.context as Transform;
            if (transform != null)
            {
                OpenWithTargetAndSearch(transform.gameObject);
            }
        }

        [MenuItem(ComponentContextPath)]
        private static void FindFromComponentContext(MenuCommand command)
        {
            Component component = command.context as Component;
            if (component != null)
            {
                OpenWithTargetAndSearch(component);
            }
        }

        [MenuItem(AssetContextPath, false, 2002)]
        private static void FindFromAssetMenu()
        {
            if (Selection.activeObject != null)
            {
                OpenWithTargetAndSearch(Selection.activeObject);
            }
        }

        [MenuItem(AssetContextPath, true)]
        private static bool ValidateFindFromAssetMenu()
        {
            return Selection.activeObject != null &&
                   !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Selection.activeObject));
        }

        private static void OpenWithTargetAndSearch(UnityEngine.Object target)
        {
            ReferenceTrackerWindow window = GetWindow<ReferenceTrackerWindow>(WindowTitle);
            window.minSize = new Vector2(1040f, 520f);
            window.EnsureState();
            window._state.Target = target;
            window._state.SearchScopes = string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target))
                ? ScopeResolver.GetCurrentScope()
                : GetDefaultAssetScopes();
            window._groupStates.Clear();
            window.Show();
            window.Focus();
            window.RunSearchFromUi();
        }

        private static ReferenceTrackerSearchScope GetDefaultAssetScopes()
        {
            return ScopeResolver.Normalize(
                ReferenceTrackerSearchScope.CurrentScene |
                ReferenceTrackerSearchScope.ScenesInProject |
                ReferenceTrackerSearchScope.PrefabMode |
                ReferenceTrackerSearchScope.Prefabs |
                ReferenceTrackerSearchScope.Materials |
                ReferenceTrackerSearchScope.ScriptableObjects |
                ReferenceTrackerSearchScope.Others);
        }

        private void OnDisable()
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = null;
        }

        private void OnGUI()
        {
            EnsureState();
            EnsureStyles();
            Controller.NormalizeScopes(_state);

            DrawHeader();
            DrawToolbar();
            DrawStatus();
            DrawResults();
        }

        private void EnsureState()
        {
            if (_state == null)
            {
                _state = new ReferenceTrackerWindowState();
            }
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                fixedHeight = 26f,
            };

            _sectionTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 11,
                padding = new RectOffset(2, 2, 0, 4),
            };

            _statusStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 4, 4),
            };

            _tableHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(8, 6, 0, 0),
            };

            _tableCellStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(8, 6, 0, 0),
            };

            _tableMiniCellStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(8, 6, 0, 0),
            };

            _tableActionStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                padding = new RectOffset(4, 4, 1, 1),
            };

            _emptyStateStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
            };
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(WindowTitle, _titleStyle);
                GUILayout.FlexibleSpace();

                if (_state.Results.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        string.Format("{0} result(s)", _state.Results.Count),
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(92f));
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTargetPanel();
                DrawScopePanel();
            }
        }

        private void DrawTargetPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(440f)))
            {
                EditorGUILayout.LabelField("Target", _sectionTitleStyle);

                EditorGUI.BeginChangeCheck();
                UnityEngine.Object newTarget = EditorGUILayout.ObjectField(_state.Target, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    _state.Target = newTarget;
                }

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Group", GUILayout.Width(44f));

                    EditorGUI.BeginChangeCheck();
                    ReferenceTrackerGroupMode newGroupMode =
                        (ReferenceTrackerGroupMode)EditorGUILayout.EnumPopup(_state.GroupMode);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _groupStates.Clear();
                        Controller.SetGroupMode(_state, newGroupMode);
                    }
                }

                _state.RebuildIndex = EditorGUILayout.ToggleLeft("Rebuild GUID index before next search", _state.RebuildIndex);
                DrawGuidCacheControls();

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Use Selection", GUILayout.Height(24f)))
                    {
                        Controller.UseSelection(_state);
                    }

                    using (new EditorGUI.DisabledScope(!ReferenceTrackerSearchService.IsSupportedTarget(_state.Target) ||
                                                       _state.IsSearching))
                    {
                        if (GUILayout.Button("Search", GUILayout.Height(24f)))
                        {
                            RunSearchFromUi();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!_state.IsSearching))
                    {
                        if (GUILayout.Button("Cancel", GUILayout.Height(24f), GUILayout.Width(72f)))
                        {
                            _searchCancellation?.Cancel();
                        }
                    }

                    if (GUILayout.Button("Clear", GUILayout.Height(24f), GUILayout.Width(64f)))
                    {
                        _groupStates.Clear();
                        Controller.ClearResults(_state);
                    }
                }

                if (_state.Target != null && !ReferenceTrackerSearchService.IsSupportedTarget(_state.Target))
                {
                    EditorGUILayout.HelpBox(
                        "This tool supports assets, scripts, GameObjects, and Components.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawGuidCacheControls()
        {
            bool cacheExists = Controller.GuidCacheExists();
            string cacheLabel = cacheExists ? "Cache: ready" : "Cache: not generated";

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(cacheLabel, Controller.GuidCachePath()),
                    EditorStyles.miniLabel,
                    GUILayout.Width(118f));

                using (new EditorGUI.DisabledScope(_state.IsSearching))
                {
                    if (GUILayout.Button("Generate Cache", EditorStyles.miniButton, GUILayout.Width(108f)))
                    {
                        GenerateGuidCacheFromUi();
                    }
                }

                using (new EditorGUI.DisabledScope(_state.IsSearching || !cacheExists))
                {
                    if (GUILayout.Button("Delete Cache", EditorStyles.miniButton, GUILayout.Width(92f)))
                    {
                        Controller.DeleteGuidCache(_state);
                        Repaint();
                    }
                }
            }
        }

        private void DrawScopePanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(430f)))
            {
                EditorGUILayout.LabelField("Search Scope", _sectionTitleStyle);

                EditorGUI.BeginChangeCheck();
                ReferenceTrackerSearchScope newSearchScopes = DrawSearchScopes(_state.SearchScopes);
                if (EditorGUI.EndChangeCheck())
                {
                    _state.SearchScopes = ScopeResolver.Normalize(newSearchScopes);
                }

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(ScopeResolver.GetDescription(_state.SearchScopes),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private ReferenceTrackerSearchScope DrawSearchScopes(ReferenceTrackerSearchScope scopes)
        {
            bool currentScene = (scopes & ReferenceTrackerSearchScope.CurrentScene) != 0;
            bool scenes = (scopes & ReferenceTrackerSearchScope.ScenesInProject) != 0;
            bool prefabMode = (scopes & ReferenceTrackerSearchScope.PrefabMode) != 0;
            bool prefabs = (scopes & ReferenceTrackerSearchScope.Prefabs) != 0;
            bool materials = (scopes & ReferenceTrackerSearchScope.Materials) != 0;
            bool scriptableObjects = (scopes & ReferenceTrackerSearchScope.ScriptableObjects) != 0;
            bool others = (scopes & ReferenceTrackerSearchScope.Others) != 0;
            bool prefabModeAvailable = ScopeResolver.IsPrefabModeAvailable;

            using (new EditorGUILayout.HorizontalScope())
            {
                currentScene = EditorGUILayout.ToggleLeft("Current Scene", currentScene, GUILayout.Width(118f));
                scenes = EditorGUILayout.ToggleLeft("Scenes (in project)", scenes, GUILayout.Width(138f));

                using (new EditorGUI.DisabledScope(!prefabModeAvailable))
                {
                    prefabMode = EditorGUILayout.ToggleLeft("Prefab Mode (if open)", prefabMode && prefabModeAvailable,
                        GUILayout.Width(148f));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                prefabs = EditorGUILayout.ToggleLeft("Prefabs", prefabs, GUILayout.Width(78f));
                materials = EditorGUILayout.ToggleLeft("Materials", materials, GUILayout.Width(92f));
                scriptableObjects = EditorGUILayout.ToggleLeft("ScriptableObject", scriptableObjects,
                    GUILayout.Width(132f));
                others = EditorGUILayout.ToggleLeft("Others", others, GUILayout.Width(78f));
            }

            ReferenceTrackerSearchScope newScopes = ReferenceTrackerSearchScope.None;

            if (currentScene)
            {
                newScopes |= ReferenceTrackerSearchScope.CurrentScene;
            }

            if (scenes)
            {
                newScopes |= ReferenceTrackerSearchScope.ScenesInProject;
            }

            if (prefabMode && prefabModeAvailable)
            {
                newScopes |= ReferenceTrackerSearchScope.PrefabMode;
            }

            if (prefabs)
            {
                newScopes |= ReferenceTrackerSearchScope.Prefabs;
            }

            if (materials)
            {
                newScopes |= ReferenceTrackerSearchScope.Materials;
            }

            if (scriptableObjects)
            {
                newScopes |= ReferenceTrackerSearchScope.ScriptableObjects;
            }

            if (others)
            {
                newScopes |= ReferenceTrackerSearchScope.Others;
            }

            return newScopes;
        }

        private void DrawStatus()
        {
            EditorGUILayout.Space(6f);

            Rect rect = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.18f, 0.20f, 1f)
                : new Color(0.84f, 0.87f, 0.90f, 1f);

            EditorGUI.DrawRect(rect, background);

            Rect statusRect = new Rect(rect.x + 4f, rect.y, rect.width - 190f, rect.height);
            GUI.Label(statusRect, _state.Status, _statusStyle);

            if (_state.Results.Count > 0 || _state.IsSearching)
            {
                string meta = _state.IsSearching
                    ? "Working..."
                    : string.Format("{0} result(s) - {1:F1} ms", _state.Results.Count, _state.LastSearchDurationMs);

                GUI.Label(
                    new Rect(rect.xMax - 182f, rect.y, 174f, rect.height),
                    meta,
                    EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawResults()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Results", _sectionTitleStyle);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Scroll horizontally for all columns", EditorStyles.miniLabel,
                        GUILayout.Width(190f));
                }

                if (_state.Results.Count == 0)
                {
                    DrawEmptyState();
                    return;
                }

                _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll);

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(TableWidth)))
                {
                    DrawTableHeader();

                    int rowIndex = 0;
                    if (_state.GroupMode == ReferenceTrackerGroupMode.None)
                    {
                        List<ReferenceTrackerUsageResult> rows = GetSortedResults(_state.Results);
                        for (int i = 0; i < rows.Count; i++)
                        {
                            DrawResultRow(rows[i], rowIndex);
                            rowIndex++;
                        }
                    }
                    else
                    {
                        List<ReferenceTrackerGroupBucket> buckets = GetSortedBuckets(_state.Groups);
                        for (int i = 0; i < buckets.Count; i++)
                        {
                            ReferenceTrackerGroupBucket bucket = buckets[i];
                            DrawGroupRow(bucket);

                            if (!GetGroupState(bucket.Key))
                            {
                                continue;
                            }

                            List<ReferenceTrackerUsageResult> rows = GetSortedResults(bucket.Items);
                            for (int j = 0; j < rows.Count; j++)
                            {
                                DrawResultRow(rows[j], rowIndex);
                                rowIndex++;
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawEmptyState()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 120f, GUILayout.ExpandWidth(true));
            GUI.Label(rect, "No results to show.", _emptyStateStyle);
        }

        private void DrawTableHeader()
        {
            Rect row = GUILayoutUtility.GetRect(TableWidth, TableHeaderHeight, GUILayout.Width(TableWidth),
                GUILayout.Height(TableHeaderHeight));

            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.12f, 0.13f, 0.14f, 1f)
                : new Color(0.72f, 0.75f, 0.78f, 1f);

            EditorGUI.DrawRect(row, background);

            float x = row.x;
            DrawHeaderCell(ref x, row, "Kind", KindColumnWidth, ReferenceTrackerSortColumn.Kind, true);
            DrawHeaderCell(ref x, row, "Asset", AssetColumnWidth, ReferenceTrackerSortColumn.Asset, true);
            DrawHeaderCell(ref x, row, "GameObject / Object", GameObjectColumnWidth,
                ReferenceTrackerSortColumn.GameObject, true);
            DrawHeaderCell(ref x, row, "Component", ComponentColumnWidth, ReferenceTrackerSortColumn.Component, true);
            DrawHeaderCell(ref x, row, "Property", PropertyColumnWidth, ReferenceTrackerSortColumn.Property, true);
            DrawHeaderCell(ref x, row, "Reference", ReferenceColumnWidth, ReferenceTrackerSortColumn.Reference, true);
            DrawHeaderCell(ref x, row, "Actions", ActionsColumnWidth, ReferenceTrackerSortColumn.Asset, false);
        }

        private void DrawGroupRow(ReferenceTrackerGroupBucket bucket)
        {
            Rect row = GUILayoutUtility.GetRect(TableWidth, TableGroupHeight, GUILayout.Width(TableWidth),
                GUILayout.Height(TableGroupHeight));

            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.22f, 0.24f, 1f)
                : new Color(0.78f, 0.81f, 0.84f, 1f);

            EditorGUI.DrawRect(row, background);

            bool isExpanded = GetGroupState(bucket.Key);
            Rect foldoutRect = new Rect(row.x + 8f, row.y + 4f, row.width - 16f, 20f);
            isExpanded = EditorGUI.Foldout(
                foldoutRect,
                isExpanded,
                string.Format("{0} ({1})", bucket.Key, bucket.Items.Count),
                true);
            _groupStates[bucket.Key] = isExpanded;
        }

        private void DrawResultRow(ReferenceTrackerUsageResult result, int rowIndex)
        {
            Rect row = GUILayoutUtility.GetRect(TableWidth, TableRowHeight, GUILayout.Width(TableWidth),
                GUILayout.Height(TableRowHeight));

            Color background = GetRowBackground(rowIndex);
            EditorGUI.DrawRect(row, background);

            if (result.IsFallback)
            {
                Color fallbackColor = EditorGUIUtility.isProSkin
                    ? new Color(0.42f, 0.32f, 0.10f, 0.55f)
                    : new Color(1.0f, 0.78f, 0.28f, 0.45f);
                EditorGUI.DrawRect(new Rect(row.x, row.y, 4f, row.height), fallbackColor);
            }

            float x = row.x;
            DrawTextCell(ref x, row, result.AssetKindLabel, result.AssetKindLabel, KindColumnWidth, _tableMiniCellStyle);
            DrawTextCell(ref x, row, GetAssetText(result), result.AssetPath, AssetColumnWidth, _tableCellStyle);
            DrawTextCell(ref x, row, GetHostText(result), GetHostTooltip(result), GameObjectColumnWidth, _tableCellStyle);
            DrawTextCell(ref x, row, result.HostComponentLabel, result.HostComponentLabel, ComponentColumnWidth,
                _tableCellStyle);
            DrawTextCell(ref x, row, GetPropertyText(result), result.PropertyPath, PropertyColumnWidth,
                _tableCellStyle);
            DrawTextCell(ref x, row, result.ReferenceTypeLabel, result.ReferenceTypeLabel, ReferenceColumnWidth,
                _tableMiniCellStyle);
            DrawActionsCell(new Rect(x, row.y, ActionsColumnWidth, row.height), result);
        }

        private void DrawHeaderCell(
            ref float x,
            Rect row,
            string label,
            float width,
            ReferenceTrackerSortColumn sortColumn,
            bool sortable)
        {
            Rect cell = new Rect(x, row.y, width, row.height);
            string sortIndicator = sortable && _state.SortColumn == sortColumn
                ? (_state.SortAscending ? "  ^" : "  v")
                : string.Empty;

            if (sortable)
            {
                if (GUI.Button(cell, new GUIContent(label + sortIndicator, "Sort by " + label), _tableHeaderStyle))
                {
                    SetSortColumn(sortColumn);
                }
            }
            else
            {
                GUI.Label(cell, label, _tableHeaderStyle);
            }

            DrawColumnSeparator(cell);
            x += width;
        }

        private void DrawTextCell(ref float x, Rect row, string text, string tooltip, float width, GUIStyle style)
        {
            Rect cell = new Rect(x, row.y, width, row.height);
            GUI.Label(cell, new GUIContent(text ?? string.Empty, tooltip ?? string.Empty), style);
            DrawColumnSeparator(cell);
            x += width;
        }

        private void SetSortColumn(ReferenceTrackerSortColumn sortColumn)
        {
            if (_state.SortColumn == sortColumn)
            {
                _state.SortAscending = !_state.SortAscending;
            }
            else
            {
                _state.SortColumn = sortColumn;
                _state.SortAscending = true;
            }

            Repaint();
        }

        private List<ReferenceTrackerUsageResult> GetSortedResults(IList<ReferenceTrackerUsageResult> source)
        {
            List<ReferenceTrackerUsageResult> sorted = new List<ReferenceTrackerUsageResult>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    sorted.Add(source[i]);
                }
            }

            sorted.Sort(CompareResultsForCurrentSort);
            return sorted;
        }

        private List<ReferenceTrackerGroupBucket> GetSortedBuckets(IList<ReferenceTrackerGroupBucket> source)
        {
            List<ReferenceTrackerGroupBucket> sorted = new List<ReferenceTrackerGroupBucket>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    sorted.Add(source[i]);
                }
            }

            sorted.Sort(CompareBucketsForCurrentSort);
            return sorted;
        }

        private int CompareBucketsForCurrentSort(ReferenceTrackerGroupBucket left, ReferenceTrackerGroupBucket right)
        {
            ReferenceTrackerUsageResult leftItem = GetFirstSortedItem(left);
            ReferenceTrackerUsageResult rightItem = GetFirstSortedItem(right);

            int byColumn = CompareResultsForCurrentSort(leftItem, rightItem);
            if (byColumn != 0)
            {
                return byColumn;
            }

            return string.Compare(left != null ? left.Key : string.Empty, right != null ? right.Key : string.Empty,
                System.StringComparison.OrdinalIgnoreCase);
        }

        private ReferenceTrackerUsageResult GetFirstSortedItem(ReferenceTrackerGroupBucket bucket)
        {
            if (bucket == null || bucket.Items.Count == 0)
            {
                return null;
            }

            List<ReferenceTrackerUsageResult> sorted = GetSortedResults(bucket.Items);
            return sorted.Count > 0 ? sorted[0] : null;
        }

        private int CompareResultsForCurrentSort(ReferenceTrackerUsageResult left, ReferenceTrackerUsageResult right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int valueCompare = string.Compare(
                GetSortValue(left, _state.SortColumn),
                GetSortValue(right, _state.SortColumn),
                System.StringComparison.OrdinalIgnoreCase);

            if (!_state.SortAscending)
            {
                valueCompare = -valueCompare;
            }

            if (valueCompare != 0)
            {
                return valueCompare;
            }

            int fallback = ReferenceTrackerSearchService.CompareResults(left, right);
            return _state.SortAscending ? fallback : -fallback;
        }

        private static string GetSortValue(ReferenceTrackerUsageResult result, ReferenceTrackerSortColumn sortColumn)
        {
            if (result == null)
            {
                return string.Empty;
            }

            switch (sortColumn)
            {
                case ReferenceTrackerSortColumn.Kind:
                    return result.AssetKindLabel ?? string.Empty;
                case ReferenceTrackerSortColumn.Asset:
                    return GetAssetText(result);
                case ReferenceTrackerSortColumn.GameObject:
                    return GetHostText(result);
                case ReferenceTrackerSortColumn.Component:
                    return result.HostComponentLabel ?? string.Empty;
                case ReferenceTrackerSortColumn.Property:
                    return GetPropertyText(result);
                case ReferenceTrackerSortColumn.Reference:
                    return result.ReferenceTypeLabel ?? string.Empty;
                default:
                    return GetAssetText(result);
            }
        }

        private void DrawActionsCell(Rect cell, ReferenceTrackerUsageResult result)
        {
            DrawColumnSeparator(cell);

            float x = cell.x + 6f;
            float y = cell.y + 6f;
            const float h = 22f;

            DrawActionButton(ref x, y, h, 26f, "P", "Ping", true, () => ReferenceTrackerEditorActions.Ping(result));
            DrawActionButton(ref x, y, h, 38f, "Sel", "Select", true, () => ReferenceTrackerEditorActions.Select(result));
            DrawActionButton(ref x, y, h, 56f, "Target", "Use this result as target and search", true,
                () => UseResultAsTarget(result));
            DrawActionButton(ref x, y, h, 50f, "Prefab", "Open prefab", ReferenceTrackerEditorActions.CanOpenPrefab(result),
                () => ReferenceTrackerEditorActions.OpenPrefab(result));
            DrawActionButton(ref x, y, h, 46f, "Scene", "Open scene", ReferenceTrackerEditorActions.CanOpenScene(result),
                () => ReferenceTrackerEditorActions.OpenScene(result));
            DrawActionButton(ref x, y, h, 42f, "Copy", "Copy path", true,
                () => EditorGUIUtility.systemCopyBuffer = ReferenceTrackerEditorActions.GetCopyPath(result));
            DrawActionButton(ref x, y, h, 58f, "Context", "Search only this open scene or prefab mode",
                CanListOpenUsageContext(result), () => SearchOnlyResultContext(result));
        }

        private void DrawActionButton(
            ref float x,
            float y,
            float height,
            float width,
            string label,
            string tooltip,
            bool enabled,
            System.Action action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;

            if (GUI.Button(new Rect(x, y, width, height), new GUIContent(label, tooltip), _tableActionStyle))
            {
                action?.Invoke();
            }

            GUI.enabled = previousEnabled;
            x += width + 4f;
        }

        private static void DrawColumnSeparator(Rect cell)
        {
            Color separator = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.06f)
                : new Color(0f, 0f, 0f, 0.10f);

            EditorGUI.DrawRect(new Rect(cell.xMax - 1f, cell.y, 1f, cell.height), separator);
        }

        private static Color GetRowBackground(int rowIndex)
        {
            if (EditorGUIUtility.isProSkin)
            {
                return rowIndex % 2 == 0
                    ? new Color(0.17f, 0.18f, 0.19f, 1f)
                    : new Color(0.14f, 0.15f, 0.16f, 1f);
            }

            return rowIndex % 2 == 0
                ? new Color(0.93f, 0.94f, 0.95f, 1f)
                : new Color(0.88f, 0.90f, 0.92f, 1f);
        }

        private static string GetAssetText(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(result.AssetPath) ? result.AssetLabel : result.AssetPath;
        }

        private static string GetHostText(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(result.HostGameObjectPath))
            {
                return result.HostGameObjectPath;
            }

            if (result.HostObject != null)
            {
                return result.HostObject.name;
            }

            return string.Empty;
        }

        private static string GetHostTooltip(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(result.HostGameObjectPath)
                ? result.AssetLabel
                : result.HostGameObjectPath;
        }

        private static string GetPropertyText(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(result.PropertyDisplayName))
            {
                return result.PropertyPath;
            }

            return string.Format("{0} ({1})", result.PropertyDisplayName, result.PropertyPath);
        }

        private async void GenerateGuidCacheFromUi()
        {
            EnsureState();
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = new CancellationTokenSource();

            try
            {
                _state.IsSearching = true;
                _state.Status = "Generating AssetGuidMapper cache...";
                Repaint();
                await Controller.GenerateGuidCacheAsync(_state, _searchCancellation.Token);
            }
            catch (System.OperationCanceledException)
            {
                _state.IsSearching = false;
                _state.Status = "AssetGuidMapper cache generation canceled.";
            }
            catch (System.Exception ex)
            {
                _state.IsSearching = false;
                _state.Status = string.Format("AssetGuidMapper cache generation failed: {0}", ex.Message);
                Debug.LogException(ex);
            }
            finally
            {
                Repaint();
            }
        }

        private async void RunSearchFromUi()
        {
            EnsureState();
            _groupStates.Clear();
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = new CancellationTokenSource();

            try
            {
                _state.IsSearching = true;
                _state.Status = "Searching references...";
                Repaint();
                await Controller.RunSearchAsync(_state, _searchCancellation.Token);
            }
            catch (System.OperationCanceledException)
            {
                _state.IsSearching = false;
                _state.Status = "Search canceled.";
            }
            catch (System.Exception ex)
            {
                _state.IsSearching = false;
                _state.Status = string.Format("Search failed: {0}", ex.Message);
                Debug.LogException(ex);
            }
            finally
            {
                Repaint();
            }
        }

        private void UseResultAsTarget(ReferenceTrackerUsageResult result)
        {
            UnityEngine.Object target = ReferenceTrackerEditorActions.GetBestSelectableObject(result);
            if (target == null)
            {
                target = result != null ? result.ReferencedObject : null;
            }

            if (target == null)
            {
                return;
            }

            _state.Target = target;
            RunSearchFromUi();
        }

        private bool CanListOpenUsageContext(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return false;
            }

            if (result.SourceScope == ReferenceTrackerSearchScope.CurrentScene ||
                result.SourceScope == ReferenceTrackerSearchScope.PrefabMode)
            {
                return true;
            }

            return IsResultInOpenPrefabMode(result) || IsResultInCurrentScene(result);
        }

        private void SearchOnlyResultContext(ReferenceTrackerUsageResult result)
        {
            if (!CanListOpenUsageContext(result))
            {
                return;
            }

            if (IsResultInOpenPrefabMode(result))
            {
                _state.SearchScopes = ReferenceTrackerSearchScope.PrefabMode;
            }
            else
            {
                _state.SearchScopes = ReferenceTrackerSearchScope.CurrentScene;
            }

            RunSearchFromUi();
        }

        private static bool IsResultInOpenPrefabMode(ReferenceTrackerUsageResult result)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null &&
                   !string.IsNullOrEmpty(stage.assetPath) &&
                   result != null &&
                   string.Equals(stage.assetPath, result.AssetPath, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsResultInCurrentScene(ReferenceTrackerUsageResult result)
        {
            if (result == null)
            {
                return false;
            }

            Scene activeScene = EditorSceneManager.GetActiveScene();
            return activeScene.IsValid() &&
                   activeScene.isLoaded &&
                   !string.IsNullOrEmpty(activeScene.path) &&
                   string.Equals(activeScene.path, result.AssetPath, System.StringComparison.OrdinalIgnoreCase);
        }

        private bool GetGroupState(string key)
        {
            bool state;
            if (_groupStates.TryGetValue(key, out state))
            {
                return state;
            }

            _groupStates[key] = true;
            return true;
        }
    }
}
