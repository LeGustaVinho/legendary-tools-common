using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class UITextureSizeOptimizerWindow : EditorWindow
    {
        private enum ChangeFilter
        {
            NeedsChange,
            NoOp
        }

        private enum ConfidenceFilter
        {
            All,
            Safe,
            Estimated,
            Risky,
            Unsupported
        }

        private enum ResultViewMode
        {
            Cards,
            Table
        }

        private enum ResultSort
        {
            Path,
            Kind,
            Confidence,
            SourceSize,
            ImportedSize,
            CurrentMaxSize,
            RecommendedSize,
            LargestUse,
            EstimatedSaving,
            UsageCount
        }

        [Serializable]
        private sealed class FolderFilterPreferences
        {
            public List<string> Whitelist = new();
            public List<string> Blacklist = new();
        }

        private const string WidthPreference = "LegendaryTools.UITextureOptimizer.ScreenWidth";
        private const string HeightPreference = "LegendaryTools.UITextureOptimizer.ScreenHeight";
        private const string RoundingPreference = "LegendaryTools.UITextureOptimizer.RoundingMode";
        private const string CachePreference = "LegendaryTools.UITextureOptimizer.UseIncrementalCache";
        private const string FolderFiltersPreference = "LegendaryTools.UITextureOptimizer.FolderFilters";
        private const string ViewModePreference = "LegendaryTools.UITextureOptimizer.ViewMode";
        private const int ResultsPerPage = 75;

        private struct SummaryStats
        {
            public int NeedsChange;
            public int Safe;
            public int Estimated;
            public int Risky;
            public int Unsupported;
            public int NoOp;
            public long EstimatedSaving;
        }

        private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _usageFoldouts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<UITextureOptimizationResult> _results = new();
        private readonly List<UITextureOptimizationResult> _visibleResults = new();
        private readonly List<string> _scanErrors = new();
        private readonly List<string> _whitelistFolders = new();
        private readonly List<string> _blacklistFolders = new();
        private UITextureSizeScanService _scanService;
        private Vector2 _scroll;
        private Vector2 _tableScroll;
        private int _screenWidth;
        private int _screenHeight;
        private string _search = string.Empty;
        private string _status = "No scan has been executed.";
        private ChangeFilter _changeFilter;
        private ConfidenceFilter _confidenceFilter;
        private ResultSort _sort;
        private bool _sortDescending;
        private bool _showScanErrors;
        private UITextureRoundingMode _roundingMode;
        private bool _useIncrementalCache;
        private bool _showFolderFilters;
        private ResultViewMode _viewMode;
        private bool _visibleResultsDirty = true;
        private bool _summaryDirty = true;
        private SummaryStats _summary;
        private int _currentPage;

        [MenuItem("Tools/Legendary Tools/Assets/Analysis/UI Texture Size Optimizer")]
        private static void OpenWindow()
        {
            UITextureSizeOptimizerWindow window = GetWindow<UITextureSizeOptimizerWindow>("UI Texture Optimizer");
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        private bool IsScanning => _scanService != null;

        private void OnEnable()
        {
            _screenWidth = EditorPrefs.GetInt(WidthPreference, Mathf.Max(1, PlayerSettings.defaultScreenWidth));
            _screenHeight = EditorPrefs.GetInt(HeightPreference, Mathf.Max(1, PlayerSettings.defaultScreenHeight));
            _roundingMode = (UITextureRoundingMode)EditorPrefs.GetInt(
                RoundingPreference,
                (int)UITextureRoundingMode.Up);
            _useIncrementalCache = EditorPrefs.GetBool(CachePreference, true);
            _viewMode = (ResultViewMode)EditorPrefs.GetInt(ViewModePreference, (int)ResultViewMode.Cards);
            LoadFolderPreferences();
        }

        private void OnDisable()
        {
            SaveFolderPreferences();
            StopScan(true);
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawScanSettings();
            DrawProgress();
            DrawScanErrors();
            DrawSummary();
            DrawResultToolbar();
            DrawResults();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("UI Texture Size Optimizer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Measures Image and RawImage usage across every Scene and Prefab, then recommends a safe default TextureImporter Max Size.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6f);
        }

        private void DrawScanSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Simulated Screen", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(IsScanning))
                    {
                        _screenWidth = Mathf.Max(1, EditorGUILayout.IntField("Width", _screenWidth));
                        _screenHeight = Mathf.Max(1, EditorGUILayout.IntField("Height", _screenHeight));
                    }

                    if (!IsScanning && GUILayout.Button("Use Player Settings", GUILayout.Width(140f)))
                    {
                        _screenWidth = Mathf.Max(1, PlayerSettings.defaultScreenWidth);
                        _screenHeight = Mathf.Max(1, PlayerSettings.defaultScreenHeight);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(IsScanning))
                    {
                        _roundingMode = (UITextureRoundingMode)EditorGUILayout.EnumPopup(
                            "Power-of-two Rounding",
                            _roundingMode,
                            GUILayout.Width(330f));
                        _useIncrementalCache = EditorGUILayout.ToggleLeft(
                            "Use incremental cache",
                            _useIncrementalCache,
                            GUILayout.Width(155f));
                    }

                    using (new EditorGUI.DisabledScope(IsScanning))
                    {
                        if (GUILayout.Button("Clear Cache", GUILayout.Width(95f)))
                        {
                            bool removed = UITextureScanCache.Clear();
                            _status = removed ? "Incremental scan cache cleared." : "No incremental cache was found.";
                        }
                    }

                    GUILayout.FlexibleSpace();
                }

                if (_roundingMode == UITextureRoundingMode.Down)
                {
                    EditorGUILayout.HelpBox(
                        "Rounding Down can select a Max Size below the measured practical requirement. Review Risky/Estimated assets before applying.",
                        MessageType.Warning);
                }

                DrawFolderFilters();

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(IsScanning))
                    {
                        if (GUILayout.Button("Scan Incremental", GUILayout.Height(28f), GUILayout.Width(135f)))
                        {
                            StartScan(false);
                        }

                        if (GUILayout.Button("Full Rescan", GUILayout.Height(28f), GUILayout.Width(105f)))
                        {
                            StartScan(true);
                        }
                    }

                    using (new EditorGUI.DisabledScope(!IsScanning))
                    {
                        if (GUILayout.Button("Cancel", GUILayout.Height(28f), GUILayout.Width(90f)))
                        {
                            _scanService?.Cancel();
                            _status = "Cancellation requested...";
                        }
                    }

                    if (GUILayout.Button("Clear Results", GUILayout.Height(28f), GUILayout.Width(110f)))
                    {
                        _results.Clear();
                        _scanErrors.Clear();
                        _selectedPaths.Clear();
                        InvalidateResultCaches();
                        _status = "Results cleared.";
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(_status, EditorStyles.miniLabel, GUILayout.MaxWidth(520f));
                }
            }
        }

        private void DrawFolderFilters()
        {
            _showFolderFilters = EditorGUILayout.Foldout(
                _showFolderFilters,
                $"Scan Folders (Whitelist: {_whitelistFolders.Count}, Blacklist: {_blacklistFolders.Count})",
                true);
            if (!_showFolderFilters)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "A non-empty whitelist limits the scan to those folders. The blacklist is then applied and always takes precedence.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(IsScanning))
                {
                    DrawFolderList("Whitelist", _whitelistFolders);
                    EditorGUILayout.Space(3f);
                    DrawFolderList("Blacklist", _blacklistFolders);
                }
            }
        }

        private void DrawFolderList(string label, List<string> folders)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.Width(85f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Folder", GUILayout.Width(90f)))
                {
                    AddFolderFromPanel(folders, label);
                }

                using (new EditorGUI.DisabledScope(folders.Count == 0))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(50f)))
                    {
                        folders.Clear();
                        SaveFolderPreferences();
                    }
                }
            }

            for (int i = 0; i < folders.Count; i++)
            {
                string folderPath = folders[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(
                        folderPath,
                        EditorStyles.miniLabel,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("Ping", GUILayout.Width(45f)))
                    {
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath));
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(65f)))
                    {
                        folders.RemoveAt(i);
                        SaveFolderPreferences();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void AddFolderFromPanel(List<string> folders, string listName)
        {
            string absolutePath = EditorUtility.OpenFolderPanel($"Add folder to {listName}", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(absolutePath))
            {
                return;
            }

            string assetPath = FileUtil.GetProjectRelativePath(absolutePath).Replace('\\', '/').TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(assetPath) ||
                !(string.Equals(assetPath, "Assets", StringComparison.OrdinalIgnoreCase) ||
                  assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)))
            {
                EditorUtility.DisplayDialog(
                    "Invalid scan folder",
                    "Choose a folder inside this project's Assets folder.",
                    "OK");
                return;
            }

            if (!folders.Contains(assetPath, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(assetPath);
                folders.Sort(StringComparer.OrdinalIgnoreCase);
                SaveFolderPreferences();
            }
        }

        private void DrawProgress()
        {
            float progress = _scanService?.Progress ?? (_results.Count > 0 ? 1f : 0f);
            Rect rect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
            string label = IsScanning
                ? $"{_scanService.ProcessedAssets}/{_scanService.TotalAssets}  {_scanService.CurrentAssetPath}"
                : $"{Mathf.RoundToInt(progress * 100f)}%";
            EditorGUI.ProgressBar(rect, progress, label);
            EditorGUILayout.Space(4f);
        }

        private void DrawSummary()
        {
            EnsureSummary();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Textures: {_results.Count}", GUILayout.Width(120f));
                EditorGUILayout.LabelField($"Needs change: {_summary.NeedsChange}", GUILayout.Width(135f));
                EditorGUILayout.LabelField($"Safe: {_summary.Safe}", GUILayout.Width(80f));
                EditorGUILayout.LabelField($"Estimated: {_summary.Estimated}", GUILayout.Width(105f));
                EditorGUILayout.LabelField($"Risky: {_summary.Risky}", GUILayout.Width(80f));
                EditorGUILayout.LabelField($"Unsupported: {_summary.Unsupported}", GUILayout.Width(115f));
                EditorGUILayout.LabelField($"No-op: {_summary.NoOp}", GUILayout.Width(85f));
                EditorGUILayout.LabelField($"Saving: {EditorUtility.FormatBytes(_summary.EstimatedSaving)}");
            }
        }

        private void DrawScanErrors()
        {
            if (_scanErrors.Count == 0)
            {
                return;
            }

            _showScanErrors = EditorGUILayout.Foldout(
                _showScanErrors,
                $"Scan errors ({_scanErrors.Count})",
                true,
                EditorStyles.foldout);
            if (!_showScanErrors)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (string error in _scanErrors)
                {
                    EditorGUILayout.LabelField(error, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawResultToolbar()
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField("Search", _search);
                _changeFilter = (ChangeFilter)EditorGUILayout.EnumPopup(
                    new GUIContent("Change"),
                    _changeFilter,
                    GUILayout.Width(190f));
                _confidenceFilter = (ConfidenceFilter)EditorGUILayout.EnumPopup(
                    new GUIContent("Confidence"),
                    _confidenceFilter,
                    GUILayout.Width(210f));
                _sort = (ResultSort)EditorGUILayout.EnumPopup(_sort, GUILayout.Width(130f));
                _sortDescending = GUILayout.Toggle(_sortDescending, "Descending", GUILayout.Width(90f));
            }
            if (EditorGUI.EndChangeCheck())
            {
                InvalidateVisibleResults();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Selected: {_selectedPaths.Count}", GUILayout.Width(100f));
                if (GUILayout.Button("Select All Filtered", GUILayout.Width(130f)))
                {
                    foreach (UITextureOptimizationResult result in GetVisibleResults())
                    {
                        _selectedPaths.Add(result.AssetPath);
                    }
                }

                if (GUILayout.Button("Clear Selection", GUILayout.Width(110f)))
                {
                    _selectedPaths.Clear();
                }

                using (new EditorGUI.DisabledScope(IsScanning || !HasSelectedApplicableResult()))
                {
                    if (GUILayout.Button("Apply Selected", GUILayout.Width(110f)))
                    {
                        ApplySelected();
                    }
                }

                GUILayout.FlexibleSpace();
                int nextViewMode = GUILayout.Toolbar(
                    (int)_viewMode,
                    new[] { "Cards", "Table" },
                    GUILayout.Width(130f));
                if (nextViewMode != (int)_viewMode)
                {
                    _viewMode = (ResultViewMode)nextViewMode;
                    EditorPrefs.SetInt(ViewModePreference, nextViewMode);
                    _scroll = Vector2.zero;
                    _tableScroll = Vector2.zero;
                }

                using (new EditorGUI.DisabledScope(_results.Count == 0))
                {
                    if (GUILayout.Button("Export CSV", GUILayout.Width(100f)))
                    {
                        ExportCsv();
                    }
                }
            }
        }

        private void DrawResults()
        {
            IReadOnlyList<UITextureOptimizationResult> visible = GetVisibleResults();
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(visible.Count / (float)ResultsPerPage));
            _currentPage = Mathf.Clamp(_currentPage, 0, pageCount - 1);
            int startIndex = _currentPage * ResultsPerPage;
            int endIndex = Mathf.Min(startIndex + ResultsPerPage, visible.Count);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{visible.Count} result(s)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_currentPage == 0))
                {
                    if (GUILayout.Button("Previous", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    {
                        _currentPage--;
                        _scroll = Vector2.zero;
                        _tableScroll = Vector2.zero;
                    }
                }

                GUILayout.Label($"Page {_currentPage + 1}/{pageCount}", EditorStyles.miniLabel, GUILayout.Width(80f));
                using (new EditorGUI.DisabledScope(_currentPage >= pageCount - 1))
                {
                    if (GUILayout.Button("Next", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                    {
                        _currentPage++;
                        _scroll = Vector2.zero;
                        _tableScroll = Vector2.zero;
                    }
                }
            }

            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox("No results match the current filter.", MessageType.Info);
                return;
            }

            if (_viewMode == ResultViewMode.Table)
            {
                DrawTableResults(visible, startIndex, endIndex);
            }
            else
            {
                DrawCardResults(visible, startIndex, endIndex);
            }
        }

        private void DrawCardResults(IReadOnlyList<UITextureOptimizationResult> visible, int startIndex, int endIndex)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = startIndex; i < endIndex; i++)
            {
                DrawResult(visible[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTableResults(IReadOnlyList<UITextureOptimizationResult> visible, int startIndex, int endIndex)
        {
            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll, true, true);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("", GUILayout.Width(22f));
                DrawSortableHeader("Asset", ResultSort.Path, 250f);
                DrawSortableHeader("Kind", ResultSort.Kind, 65f);
                DrawSortableHeader("Confidence", ResultSort.Confidence, 100f);
                DrawSortableHeader("Source", ResultSort.SourceSize, 90f);
                DrawSortableHeader("Imported", ResultSort.ImportedSize, 90f);
                DrawSortableHeader("Current", ResultSort.CurrentMaxSize, 70f);
                DrawSortableHeader("Recommended", ResultSort.RecommendedSize, 100f);
                DrawSortableHeader("Largest Use", ResultSort.LargestUse, 95f);
                DrawSortableHeader("Usages", ResultSort.UsageCount, 60f);
                DrawSortableHeader("Saving", ResultSort.EstimatedSaving, 90f);
                GUILayout.Label("", GUILayout.Width(100f));
            }

            for (int i = startIndex; i < endIndex; i++)
            {
                DrawTableRow(visible[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSortableHeader(string label, ResultSort sort, float width)
        {
            string direction = _sort == sort ? (_sortDescending ? " ▼" : " ▲") : string.Empty;
            if (!GUILayout.Button(label + direction, EditorStyles.toolbarButton, GUILayout.Width(width)))
            {
                return;
            }

            if (_sort == sort)
            {
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sort = sort;
                _sortDescending = false;
            }

            InvalidateVisibleResults();
            _tableScroll = Vector2.zero;
        }

        private void DrawTableRow(UITextureOptimizationResult result)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                bool selected = _selectedPaths.Contains(result.AssetPath);
                bool nextSelected = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(22f));
                if (nextSelected != selected)
                {
                    if (nextSelected) _selectedPaths.Add(result.AssetPath);
                    else _selectedPaths.Remove(result.AssetPath);
                }

                if (GUILayout.Button(
                        new GUIContent(result.AssetName, result.AssetPath),
                        EditorStyles.label,
                        GUILayout.Width(250f)))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(result.AssetPath));
                }

                GUILayout.Label(result.AssetKind, GUILayout.Width(65f));
                GUILayout.Label(GetStatusLabel(result), GUILayout.Width(100f));
                GUILayout.Label($"{result.SourceWidth}x{result.SourceHeight}", GUILayout.Width(90f));
                GUILayout.Label($"{result.ImportedWidth}x{result.ImportedHeight}", GUILayout.Width(90f));
                GUILayout.Label(result.CurrentMaxSize.ToString(), GUILayout.Width(70f));
                GUILayout.Label(result.RecommendedMaxSize.ToString(), GUILayout.Width(100f));
                Vector2 largest = result.LargestRenderedUse;
                GUILayout.Label($"{largest.x:0}x{largest.y:0}", GUILayout.Width(95f));
                GUILayout.Label(result.Usages.Count.ToString(), GUILayout.Width(60f));
                GUILayout.Label(EditorUtility.FormatBytes(result.EstimatedMemorySaving), GUILayout.Width(90f));

                if (GUILayout.Button("Ping", GUILayout.Width(45f)))
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(result.AssetPath));
                }

                using (new EditorGUI.DisabledScope(IsScanning || !result.CanApply))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(50f)))
                    {
                        ApplyOneWithConfirmation(result);
                    }
                }
            }
        }

        private void DrawResult(UITextureOptimizationResult result)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = result.IsCandidate
                ? new Color(0.65f, 1f, 0.7f)
                : result.Confidence switch
                {
                    UITextureConfidence.Estimated => new Color(1f, 0.92f, 0.62f),
                    UITextureConfidence.Risky => new Color(1f, 0.75f, 0.48f),
                    UITextureConfidence.Unsupported => new Color(1f, 0.58f, 0.58f),
                    _ => Color.white
                };

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = previous;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = _selectedPaths.Contains(result.AssetPath);
                    bool nextSelected = GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(20f));
                    if (nextSelected != selected)
                    {
                        if (nextSelected) _selectedPaths.Add(result.AssetPath);
                        else _selectedPaths.Remove(result.AssetPath);
                    }

                    bool expanded = _usageFoldouts.TryGetValue(result.AssetPath, out bool value) && value;
                    expanded = EditorGUILayout.Foldout(
                        expanded,
                        result.AssetName,
                        true,
                        EditorStyles.foldout);
                    _usageFoldouts[result.AssetPath] = expanded;

                    GUILayout.FlexibleSpace();

                    GUILayout.Label(GetStatusLabel(result), EditorStyles.miniBoldLabel, GUILayout.Width(90f));

                    if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                    {
                        EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(result.AssetPath));
                    }

                    using (new EditorGUI.DisabledScope(IsScanning || !result.CanApply))
                    {
                        if (GUILayout.Button("Apply", GUILayout.Width(55f)))
                        {
                            ApplyOneWithConfirmation(result);
                        }
                    }
                }

                Vector2 largest = result.LargestRenderedUse;
                string metrics =
                    $"Source {result.SourceWidth}x{result.SourceHeight}   |   " +
                    $"Imported {result.ImportedWidth}x{result.ImportedHeight}   |   " +
                    $"Largest use {largest.x:0}x{largest.y:0} px   |   " +
                    $"Max Size {result.CurrentMaxSize} -> {result.RecommendedMaxSize}   |   " +
                    $"Usages {result.Usages.Count}   |   " +
                    $"Est. saving {EditorUtility.FormatBytes(result.EstimatedMemorySaving)}";
                EditorGUILayout.LabelField(metrics, EditorStyles.wordWrappedMiniLabel);
                string reasonSummary = result.GetReasonSummary();
                if (!string.IsNullOrEmpty(reasonSummary))
                {
                    EditorGUILayout.LabelField($"Reasons: {reasonSummary}", EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.SelectableLabel(
                    result.AssetPath,
                    EditorStyles.miniLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (_usageFoldouts[result.AssetPath])
                {
                    DrawUsages(result);
                }
            }
        }

        private static void DrawUsages(UITextureOptimizationResult result)
        {
            EditorGUI.indentLevel++;
            foreach (UITextureUsageRecord usage in result.GetSortedUsages())
            {
                string line =
                    $"{usage.Confidence} | {usage.ComponentType} | {usage.RenderedPixels.x:0.#}x{usage.RenderedPixels.y:0.#} px | " +
                    $"requires {usage.RequiredMaxSize} | {usage.ContainerPath} :: {usage.HierarchyPath}";
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                if (usage.Reasons is { Count: > 0 })
                {
                    EditorGUILayout.LabelField(
                        "Reasons: " + string.Join(", ", usage.Reasons.Select(UITextureOptimizationResult.FormatReason)),
                        EditorStyles.wordWrappedMiniLabel);
                }
                if (!string.IsNullOrEmpty(usage.Warning))
                {
                    EditorGUILayout.HelpBox(usage.Warning, MessageType.Warning);
                }
            }

            EditorGUI.indentLevel--;
        }

        private IReadOnlyList<UITextureOptimizationResult> GetVisibleResults()
        {
            if (!_visibleResultsDirty)
            {
                return _visibleResults;
            }

            _visibleResults.Clear();
            foreach (UITextureOptimizationResult result in _results)
            {
                if (MatchesSearch(result) && MatchesFilter(result))
                {
                    _visibleResults.Add(result);
                }
            }

            _visibleResults.Sort((left, right) =>
            {
                int comparison = _sort switch
                {
                    ResultSort.Kind => StringComparer.OrdinalIgnoreCase.Compare(left.AssetKind, right.AssetKind),
                    ResultSort.Confidence => left.Confidence.CompareTo(right.Confidence),
                    ResultSort.SourceSize => CompareDimensions(
                        left.SourceWidth,
                        left.SourceHeight,
                        right.SourceWidth,
                        right.SourceHeight),
                    ResultSort.ImportedSize => CompareDimensions(
                        left.ImportedWidth,
                        left.ImportedHeight,
                        right.ImportedWidth,
                        right.ImportedHeight),
                    ResultSort.CurrentMaxSize => left.CurrentMaxSize.CompareTo(right.CurrentMaxSize),
                    ResultSort.RecommendedSize => left.RecommendedMaxSize.CompareTo(right.RecommendedMaxSize),
                    ResultSort.LargestUse => CompareRenderedUse(left.LargestRenderedUse, right.LargestRenderedUse),
                    ResultSort.EstimatedSaving => left.EstimatedMemorySaving.CompareTo(right.EstimatedMemorySaving),
                    ResultSort.UsageCount => left.Usages.Count.CompareTo(right.Usages.Count),
                    _ => StringComparer.OrdinalIgnoreCase.Compare(left.AssetPath, right.AssetPath)
                };
                return comparison != 0
                    ? comparison
                    : StringComparer.OrdinalIgnoreCase.Compare(left.AssetPath, right.AssetPath);
            });

            if (_sortDescending)
            {
                _visibleResults.Reverse();
            }

            _visibleResultsDirty = false;
            return _visibleResults;
        }

        private static int CompareDimensions(
            int leftWidth,
            int leftHeight,
            int rightWidth,
            int rightHeight)
        {
            long leftArea = (long)leftWidth * leftHeight;
            long rightArea = (long)rightWidth * rightHeight;
            int areaComparison = leftArea.CompareTo(rightArea);
            if (areaComparison != 0)
            {
                return areaComparison;
            }

            return Mathf.Max(leftWidth, leftHeight).CompareTo(Mathf.Max(rightWidth, rightHeight));
        }

        private static int CompareRenderedUse(Vector2 left, Vector2 right)
        {
            float areaComparison = left.x * left.y - right.x * right.y;
            if (!Mathf.Approximately(areaComparison, 0f))
            {
                return areaComparison < 0f ? -1 : 1;
            }

            return Mathf.Max(left.x, left.y).CompareTo(Mathf.Max(right.x, right.y));
        }

        private bool MatchesSearch(UITextureOptimizationResult result)
        {
            if (string.IsNullOrWhiteSpace(_search))
            {
                return true;
            }

            if (result.AssetPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            foreach (UITextureUsageRecord usage in result.Usages)
            {
                if ((usage.ContainerPath?.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (usage.HierarchyPath?.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (usage.SpriteName?.IndexOf(_search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesFilter(UITextureOptimizationResult result)
        {
            bool matchesChange = _changeFilter switch
            {
                ChangeFilter.NeedsChange => result.CanApply,
                ChangeFilter.NoOp => !result.CanApply,
                _ => false
            };

            if (!matchesChange || _confidenceFilter == ConfidenceFilter.All)
            {
                return matchesChange;
            }

            return _confidenceFilter switch
            {
                ConfidenceFilter.Safe => result.Confidence == UITextureConfidence.Safe,
                ConfidenceFilter.Estimated => result.Confidence == UITextureConfidence.Estimated,
                ConfidenceFilter.Risky => result.Confidence == UITextureConfidence.Risky,
                ConfidenceFilter.Unsupported => result.Confidence == UITextureConfidence.Unsupported,
                _ => true
            };
        }

        private bool HasSelectedApplicableResult()
        {
            foreach (UITextureOptimizationResult result in _results)
            {
                if (result.CanApply && _selectedPaths.Contains(result.AssetPath))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureSummary()
        {
            if (!_summaryDirty)
            {
                return;
            }

            _summary = default;
            foreach (UITextureOptimizationResult result in _results)
            {
                if (!result.CanApply)
                {
                    _summary.NoOp++;
                    continue;
                }

                _summary.NeedsChange++;
                _summary.EstimatedSaving += result.EstimatedMemorySaving;
                switch (result.Confidence)
                {
                    case UITextureConfidence.Safe:
                        _summary.Safe++;
                        break;
                    case UITextureConfidence.Estimated:
                        _summary.Estimated++;
                        break;
                    case UITextureConfidence.Risky:
                        _summary.Risky++;
                        break;
                    case UITextureConfidence.Unsupported:
                        _summary.Unsupported++;
                        break;
                }
            }

            _summaryDirty = false;
        }

        private void InvalidateVisibleResults()
        {
            _visibleResultsDirty = true;
            _currentPage = 0;
            _scroll = Vector2.zero;
            _tableScroll = Vector2.zero;
        }

        private void InvalidateResultCaches()
        {
            _summaryDirty = true;
            InvalidateVisibleResults();
        }

        private void LoadFolderPreferences()
        {
            _whitelistFolders.Clear();
            _blacklistFolders.Clear();
            string json = EditorPrefs.GetString(FolderFiltersPreference, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                FolderFilterPreferences preferences = JsonUtility.FromJson<FolderFilterPreferences>(json);
                AddValidFolders(preferences?.Whitelist, _whitelistFolders);
                AddValidFolders(preferences?.Blacklist, _blacklistFolders);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load UI Texture Optimizer folder filters: {exception.Message}");
            }
        }

        private void SaveFolderPreferences()
        {
            FolderFilterPreferences preferences = new()
            {
                Whitelist = new List<string>(_whitelistFolders),
                Blacklist = new List<string>(_blacklistFolders)
            };
            EditorPrefs.SetString(FolderFiltersPreference, JsonUtility.ToJson(preferences));
        }

        private static void AddValidFolders(IEnumerable<string> source, List<string> destination)
        {
            if (source == null)
            {
                return;
            }

            foreach (string value in source)
            {
                string folder = value?.Replace('\\', '/').TrimEnd('/');
                if (!string.IsNullOrEmpty(folder) &&
                    AssetDatabase.IsValidFolder(folder) &&
                    !destination.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    destination.Add(folder);
                }
            }

            destination.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void StartScan(bool forceRescan)
        {
            StopScan(true);
            _results.Clear();
            _scanErrors.Clear();
            _selectedPaths.Clear();
            _usageFoldouts.Clear();
            InvalidateResultCaches();
            EditorPrefs.SetInt(WidthPreference, _screenWidth);
            EditorPrefs.SetInt(HeightPreference, _screenHeight);
            EditorPrefs.SetInt(RoundingPreference, (int)_roundingMode);
            EditorPrefs.SetBool(CachePreference, _useIncrementalCache);
            SaveFolderPreferences();
            _scanService = new UITextureSizeScanService(
                new Vector2(_screenWidth, _screenHeight),
                _roundingMode,
                _useIncrementalCache,
                forceRescan,
                _whitelistFolders,
                _blacklistFolders);
            _status = $"Scanning {_scanService.TotalAssets} assets...";
            EditorApplication.update += ScanUpdate;
        }

        private void ScanUpdate()
        {
            if (_scanService == null)
            {
                return;
            }

            _scanService.Tick();
            Repaint();
            if (!_scanService.IsComplete)
            {
                return;
            }

            bool cancelled = _scanService.WasCancelled;
            int processed = _scanService.ProcessedAssets;
            int errorCount = _scanService.Errors.Count;
            int cacheHits = _scanService.CacheHits;
            int rescanned = _scanService.RescannedAssets;
            _results.Clear();
            _results.AddRange(_scanService.Results);
            _scanErrors.Clear();
            _scanErrors.AddRange(_scanService.Errors);
            InvalidateResultCaches();
            StopScan(false);
            _status = cancelled
                ? $"Cancelled after {processed} assets. Partial report contains {_results.Count} textures."
                : $"Scan complete: {processed} assets ({cacheHits} cached, {rescanned} rescanned), {_results.Count} textures, {errorCount} errors.";
        }

        private void StopScan(bool requestCancellation)
        {
            EditorApplication.update -= ScanUpdate;
            if (_scanService == null)
            {
                return;
            }

            if (requestCancellation)
            {
                _scanService.Cancel();
            }

            _scanService.Dispose();
            _scanService = null;
        }

        private static string GetStatusLabel(UITextureOptimizationResult result)
        {
            string confidence = result.Confidence.ToString();
            if (!result.IsEditable) return confidence + " / RO";
            if (!result.CanApply) return confidence + " / No-op";
            return confidence;
        }

        private void ApplyOneWithConfirmation(UITextureOptimizationResult result)
        {
            if (!EditorUtility.DisplayDialog(
                    "Apply UI texture recommendation",
                    BuildIndividualApplyMessage(result),
                    "Apply and Reimport",
                    "Cancel"))
            {
                return;
            }

            if (UITextureImporterApplier.Apply(result, out string error))
            {
                InvalidateResultCaches();
                _status = $"Applied Max Size {result.RecommendedMaxSize} to {result.AssetName}.";
            }
            else
            {
                _status = error;
                EditorUtility.DisplayDialog("Unable to apply", error, "OK");
            }
        }

        private void ApplySelected()
        {
            List<UITextureOptimizationResult> selected = _results
                .Where(result => _selectedPaths.Contains(result.AssetPath) && result.CanApply)
                .ToList();
            int reviewCount = selected.Count(result => result.Confidence != UITextureConfidence.Safe);
            string blockedWarning = reviewCount > 0
                ? $"\n\nWarning: {reviewCount} selected texture(s) are Estimated, Risky, or Unsupported. Applying may change borders, tiling, UV density, animation quality, or appearance."
                : string.Empty;
            if (selected.Count == 0 || !EditorUtility.DisplayDialog(
                    "Apply UI texture recommendations",
                    $"Change the default Max Size and reimport {selected.Count} texture(s)? Platform overrides and SpriteAtlas settings will remain untouched.{blockedWarning}",
                    "Apply Selected",
                    "Cancel"))
            {
                return;
            }

            int applied = 0;
            int failures = 0;
            bool cancelled = false;
            try
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    UITextureOptimizationResult result = selected[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Applying UI texture recommendations",
                            result.AssetPath,
                            i / (float)selected.Count))
                    {
                        cancelled = true;
                        break;
                    }

                    if (UITextureImporterApplier.Apply(result, out string error))
                    {
                        applied++;
                    }
                    else
                    {
                        failures++;
                        Debug.LogError(error);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (applied > 0)
            {
                InvalidateResultCaches();
            }

            _status = cancelled
                ? $"Batch cancelled. Applied {applied}; failed {failures}. Already applied changes were kept."
                : $"Batch complete. Applied {applied}; failed {failures}.";
        }

        private static string BuildIndividualApplyMessage(UITextureOptimizationResult result)
        {
            string warning = result.Confidence != UITextureConfidence.Safe
                ? $"\n\nWarning: confidence is {result.Confidence}. Applying may change borders, tiling, UV density, animation quality, or appearance. Review the reasons and expanded usages before continuing."
                : string.Empty;
            return $"Change only the default Max Size for:\n{result.AssetPath}\n\n{result.CurrentMaxSize} → {result.RecommendedMaxSize}?{warning}";
        }

        private void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export UI Texture Optimization Report", string.Empty,
                "UITextureOptimizationReport.csv", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            StringBuilder csv = new();
            csv.AppendLine("Asset Path,Kind,Source Width,Source Height,Imported Width,Imported Height,Current Max Size,Recommended Max Size,Confidence,Reasons,Usage Count,Largest Use Width,Largest Use Height,Estimated Memory Saving,Container,Hierarchy,Component,Sprite,Warning");
            foreach (UITextureOptimizationResult result in _results.OrderBy(item => item.AssetPath))
            {
                foreach (UITextureUsageRecord usage in result.Usages)
                {
                    string[] fields =
                    {
                        result.AssetPath,
                        result.AssetKind,
                        result.SourceWidth.ToString(CultureInfo.InvariantCulture),
                        result.SourceHeight.ToString(CultureInfo.InvariantCulture),
                        result.ImportedWidth.ToString(CultureInfo.InvariantCulture),
                        result.ImportedHeight.ToString(CultureInfo.InvariantCulture),
                        result.CurrentMaxSize.ToString(CultureInfo.InvariantCulture),
                        result.RecommendedMaxSize.ToString(CultureInfo.InvariantCulture),
                        result.Confidence.ToString(),
                        result.GetReasonSummary(),
                        result.Usages.Count.ToString(CultureInfo.InvariantCulture),
                        usage.RenderedPixels.x.ToString("0.##", CultureInfo.InvariantCulture),
                        usage.RenderedPixels.y.ToString("0.##", CultureInfo.InvariantCulture),
                        result.EstimatedMemorySaving.ToString(CultureInfo.InvariantCulture),
                        usage.ContainerPath,
                        usage.HierarchyPath,
                        usage.ComponentType,
                        usage.SpriteName,
                        usage.Warning
                    };
                    csv.AppendLine(string.Join(",", fields.Select(EscapeCsv)));
                }
            }

            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            _status = $"Report exported to {path}";
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
    }
}
