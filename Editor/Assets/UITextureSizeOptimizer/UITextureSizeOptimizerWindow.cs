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
        private enum ResultFilter
        {
            All,
            Actionable,
            Estimated,
            Risky,
            Unsupported,
            NoReduction
        }

        private enum ResultSort
        {
            Path,
            RecommendedSize,
            EstimatedSaving,
            UsageCount
        }

        private const string WidthPreference = "LegendaryTools.UITextureOptimizer.ScreenWidth";
        private const string HeightPreference = "LegendaryTools.UITextureOptimizer.ScreenHeight";
        private const string RoundingPreference = "LegendaryTools.UITextureOptimizer.RoundingMode";
        private const string CachePreference = "LegendaryTools.UITextureOptimizer.UseIncrementalCache";

        private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _usageFoldouts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<UITextureOptimizationResult> _results = new();
        private readonly List<string> _scanErrors = new();
        private UITextureSizeScanService _scanService;
        private Vector2 _scroll;
        private int _screenWidth;
        private int _screenHeight;
        private string _search = string.Empty;
        private string _status = "No scan has been executed.";
        private ResultFilter _filter;
        private ResultSort _sort;
        private bool _sortDescending;
        private bool _showScanErrors;
        private UITextureRoundingMode _roundingMode;
        private bool _useIncrementalCache;

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
        }

        private void OnDisable()
        {
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
                        _status = "Results cleared.";
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(_status, EditorStyles.miniLabel, GUILayout.MaxWidth(520f));
                }
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
            int candidates = _results.Count(result => result.IsCandidate);
            int estimated = _results.Count(result => result.Confidence == UITextureConfidence.Estimated);
            int risky = _results.Count(result => result.Confidence == UITextureConfidence.Risky);
            int unsupported = _results.Count(result => result.Confidence == UITextureConfidence.Unsupported);
            long estimatedSaving = _results.Where(result => result.CanApply).Sum(result => result.EstimatedMemorySaving);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Textures: {_results.Count}", GUILayout.Width(120f));
                EditorGUILayout.LabelField($"Actionable: {candidates}", GUILayout.Width(120f));
                EditorGUILayout.LabelField($"Estimated: {estimated}", GUILayout.Width(110f));
                EditorGUILayout.LabelField($"Risky: {risky}", GUILayout.Width(90f));
                EditorGUILayout.LabelField($"Unsupported: {unsupported}", GUILayout.Width(120f));
                EditorGUILayout.LabelField($"Estimated saving: {EditorUtility.FormatBytes(estimatedSaving)}");
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
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField("Search", _search);
                _filter = (ResultFilter)EditorGUILayout.EnumPopup(_filter, GUILayout.Width(110f));
                _sort = (ResultSort)EditorGUILayout.EnumPopup(_sort, GUILayout.Width(130f));
                _sortDescending = GUILayout.Toggle(_sortDescending, "Descending", GUILayout.Width(90f));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Selected: {_selectedPaths.Count}", GUILayout.Width(100f));
                if (GUILayout.Button("Select Reducible", GUILayout.Width(120f)))
                {
                    foreach (UITextureOptimizationResult result in GetVisibleResults().Where(result => result.CanApply))
                    {
                        _selectedPaths.Add(result.AssetPath);
                    }
                }

                if (GUILayout.Button("Clear Selection", GUILayout.Width(110f)))
                {
                    _selectedPaths.Clear();
                }

                using (new EditorGUI.DisabledScope(IsScanning || !_results.Any(result =>
                           _selectedPaths.Contains(result.AssetPath) && result.CanApply)))
                {
                    if (GUILayout.Button("Apply Selected", GUILayout.Width(110f)))
                    {
                        ApplySelected();
                    }
                }

                GUILayout.FlexibleSpace();
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
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (UITextureOptimizationResult result in visible)
            {
                DrawResult(result);
            }

            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox("No results match the current filter.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
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
            foreach (UITextureUsageRecord usage in result.Usages
                         .OrderByDescending(item => item.RequiredMaxSize)
                         .ThenBy(item => item.ContainerPath, StringComparer.OrdinalIgnoreCase))
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
            IEnumerable<UITextureOptimizationResult> query = _results;
            if (!string.IsNullOrWhiteSpace(_search))
            {
                query = query.Where(result =>
                    result.AssetPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    result.Usages.Any(usage => usage.ContainerPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            query = _filter switch
            {
                ResultFilter.Actionable => query.Where(result => result.IsCandidate),
                ResultFilter.Estimated => query.Where(result => result.Confidence == UITextureConfidence.Estimated),
                ResultFilter.Risky => query.Where(result => result.Confidence == UITextureConfidence.Risky),
                ResultFilter.Unsupported => query.Where(result => result.Confidence == UITextureConfidence.Unsupported),
                ResultFilter.NoReduction => query.Where(result => !result.CanApply),
                _ => query
            };

            query = _sort switch
            {
                ResultSort.RecommendedSize => query.OrderBy(result => result.RecommendedMaxSize),
                ResultSort.EstimatedSaving => query.OrderBy(result => result.EstimatedMemorySaving),
                ResultSort.UsageCount => query.OrderBy(result => result.Usages.Count),
                _ => query.OrderBy(result => result.AssetPath, StringComparer.OrdinalIgnoreCase)
            };

            if (_sortDescending)
            {
                query = query.Reverse();
            }

            return query.ToList();
        }

        private void StartScan(bool forceRescan)
        {
            StopScan(true);
            _results.Clear();
            _scanErrors.Clear();
            _selectedPaths.Clear();
            _usageFoldouts.Clear();
            EditorPrefs.SetInt(WidthPreference, _screenWidth);
            EditorPrefs.SetInt(HeightPreference, _screenHeight);
            EditorPrefs.SetInt(RoundingPreference, (int)_roundingMode);
            EditorPrefs.SetBool(CachePreference, _useIncrementalCache);
            _scanService = new UITextureSizeScanService(
                new Vector2(_screenWidth, _screenHeight),
                _roundingMode,
                _useIncrementalCache,
                forceRescan);
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
