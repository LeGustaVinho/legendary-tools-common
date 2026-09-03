using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LegendaryTools.Editor.DomainReload
{
    public sealed class DomainReloadAnalyzerWindow : EditorWindow
    {
        private enum Page { Capture, EditorLog, ProjectAudit, Guide }

        private const int FindingsPerPage = 50;
        private const int TimelineEventsPerPage = 75;
        private const int TimingsPerPage = 75;
        private const int ScriptsPerPage = 100;
        private const int AssembliesPerPage = 100;
        private const int ObjectsPerPage = 100;
        private static readonly string[] FindingKindNames =
            new[] { "All" }.Concat(Enum.GetNames(typeof(DomainReloadFindingKind))).ToArray();
        private static readonly GUIStyle[] RiskStyles = new GUIStyle[4];

        private Page _page;
        private Vector2 _scroll;
        private bool _useProfiler;
        private bool _deepProfile;
        private bool _saveProfilerCapture = true;
        private bool _includePackages = true;
        private bool _includeLiveObjects = true;
        private bool _showSelfTime = true;
        private string _search = string.Empty;
        private DomainReloadRisk _minimumRisk = DomainReloadRisk.Info;
        private DomainReloadFindingKind? _kindFilter;
        private List<DomainReloadReport> _savedReports = new();
        private List<DomainReloadReport> _logReports = new();
        private DomainReloadReport _selectedReport;
        private DomainReloadAudit _audit;
        private readonly Dictionary<string, bool> _foldouts = new();
        private List<DomainReloadFinding> _filteredFindings = new();
        private DomainReloadAudit _filterCacheAudit;
        private string _filterCacheSearch;
        private DomainReloadRisk _filterCacheRisk;
        private DomainReloadFindingKind? _filterCacheKind;
        private bool _findingsCacheDirty = true;
        private int _findingsPage;
        private int _timelinePage;
        private int _scriptTimingsPage;
        private int _assemblyTimingsPage;
        private int _scriptsPage;
        private int _assembliesPage;
        private int _objectsPage;

        [MenuItem("Tools/Legendary Tools/Analysis/Domain Reload Analyzer")]
        public static void Open()
        {
            DomainReloadAnalyzerWindow window = GetWindow<DomainReloadAnalyzerWindow>("Domain Reload Analyzer");
            window.minSize = new Vector2(920, 620);
            window.Show();
        }

        private void OnEnable()
        {
            DomainReloadCaptureCoordinator.CaptureCompleted -= RefreshSavedReports;
            DomainReloadCaptureCoordinator.CaptureCompleted += RefreshSavedReports;
            RefreshSavedReports();
        }

        private void OnDisable()
        {
            DomainReloadCaptureCoordinator.CaptureCompleted -= RefreshSavedReports;
        }

        private void OnGUI()
        {
            DrawHeader();
            _page = (Page)GUILayout.Toolbar((int)_page,
                new[] { "Controlled Capture", "Editor.log History", "Project Impact", "How to Read" });
            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_page)
            {
                case Page.Capture: DrawCapturePage(); break;
                case Page.EditorLog: DrawLogPage(); break;
                case Page.ProjectAudit: DrawAuditPage(); break;
                case Page.Guide: DrawGuidePage(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Domain Reload Analyzer", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Profiler", EditorStyles.toolbarButton))
                    EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
                if (GUILayout.Button("Editor.log", EditorStyles.toolbarButton))
                    EditorUtility.RevealInFinder(DomainReloadCaptureCoordinator.GetEditorLogPath());
                if (GUILayout.Button("Reports", EditorStyles.toolbarButton))
                    EditorUtility.RevealInFinder(DomainReloadCaptureCoordinator.OutputDirectory);
            }

            if (DomainReloadCaptureCoordinator.HasPendingCapture)
            {
                EditorGUILayout.HelpBox(
                    "A capture is pending. If the Editor is responsive, wait for the profiling block to be written. " +
                    "If a previous reload stalled and Unity was restarted, the tool will attempt to recover the partial log section.",
                    MessageType.Warning);
                if (GUILayout.Button("Cancel Pending Capture"))
                    DomainReloadCaptureCoordinator.CancelPendingCapture();
            }
        }

        private void DrawCapturePage()
        {
            EditorGUILayout.LabelField("Measure an isolated Domain Reload", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The capture uses EditorUtility.RequestScriptReload, which reloads assemblies without compiling code. " +
                "This keeps compilation time separate from Domain Reload. The primary breakdown comes from the " +
                "'Domain Reload Profiling' block written by Unity to Editor.log.", MessageType.Info);

            _useProfiler = EditorGUILayout.ToggleLeft("Also capture the Profiler with Unity Editor as the target", _useProfiler);
            using (new EditorGUI.DisabledScope(!_useProfiler))
            {
                _saveProfilerCapture = EditorGUILayout.ToggleLeft("Save a .data capture for the Profiler", _saveProfilerCapture);
                _deepProfile = EditorGUILayout.ToggleLeft("Deep Profiling (experimental; significantly changes measured time)", _deepProfile);
            }
            if (_useProfiler && _deepProfile)
                EditorGUILayout.HelpBox("Deep Profiling instruments managed calls and can make reload much slower. " +
                                        "Use it to locate a method, then repeat without Deep Profiling to validate real timing.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(DomainReloadCaptureCoordinator.HasPendingCapture))
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 0.62f);
                if (GUILayout.Button("Start Capture and Force Domain Reload", GUILayout.Height(34)))
                {
                    try
                    {
                        DomainReloadCaptureCoordinator.BeginCapture(_useProfiler, _deepProfile, _saveProfilerCapture);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog("Domain Reload Analyzer", ex.Message, "OK");
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.Space(10);
            DrawReportPicker(_savedReports, "Saved Captures", true);
            if (_selectedReport != null)
                DrawReport(_selectedReport);
        }

        private void DrawLogPage()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Reloads found in the current Editor.log", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload Log", GUILayout.Width(130)))
                {
                    _logReports = DomainReloadCaptureCoordinator.LoadReportsFromEditorLog();
                    _selectedReport = _logReports.FirstOrDefault();
                }
            }
            EditorGUILayout.HelpBox(
                "The log history also includes reloads caused by startup, compilation, Play Mode and Refresh. " +
                "The associated Asset Pipeline block helps separate compilation/import from Domain Reload itself.", MessageType.Info);
            if (_logReports.Count == 0)
            {
                if (GUILayout.Button("Read Editor.log Now"))
                {
                    _logReports = DomainReloadCaptureCoordinator.LoadReportsFromEditorLog();
                    _selectedReport = _logReports.FirstOrDefault();
                }
                return;
            }
            DrawReportPicker(_logReports, "History", false);
            if (_selectedReport != null)
                DrawReport(_selectedReport);
        }

        private void DrawReportPicker(IReadOnlyList<DomainReloadReport> reports, string title, bool allowExport)
        {
            if (reports == null || reports.Count == 0)
            {
                EditorGUILayout.HelpBox("No report is available.", MessageType.None);
                return;
            }

            string[] names = reports.Select((report, index) =>
                $"#{index + 1}  {report.TotalMs:0.###} ms  " +
                (string.IsNullOrEmpty(report.CapturedAtUtc) ? "Editor.log" : FormatUtc(report.CapturedAtUtc)) +
                (report.Completed ? string.Empty : "  [INCOMPLETE]")).ToArray();
            int selected = Math.Max(0, IndexOfReference(reports, _selectedReport));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, GUILayout.Width(120));
                int next = EditorGUILayout.Popup(selected, names);
                _selectedReport = reports[Mathf.Clamp(next, 0, reports.Count - 1)];
                if (allowExport && GUILayout.Button("Export JSON...", GUILayout.Width(120)))
                    ExportJson(_selectedReport, "domain-reload-report.json");
            }
        }

        private void DrawReport(DomainReloadReport report)
        {
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(report.Completed ? "Domain Reload completed" : "Incomplete capture",
                    EditorStyles.boldLabel);
                DrawMetricRow("Domain Reload Profiling", report.TotalMs, report.TotalMs, "Official timing from the Editor.log block");
                if (report.WallClockMs > 0)
                    DrawMetricRow("Capture wall-clock time", report.WallClockMs, report.WallClockMs,
                        "Includes capture setup, log writes and the post-reload wait");
                if (!string.IsNullOrEmpty(report.Status))
                    EditorGUILayout.LabelField("Status", report.Status);
                if (!string.IsNullOrEmpty(report.LogPath))
                    EditorGUILayout.SelectableLabel(report.LogPath, EditorStyles.miniLabel, GUILayout.Height(18));
            }

            if (!report.Completed)
                EditorGUILayout.HelpBox(report.Status, MessageType.Error);

            if (report.Steps != null && report.Steps.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Steps executed by Unity", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    _showSelfTime = GUILayout.Toggle(_showSelfTime, "Show self time", EditorStyles.miniButton);
                }
                int stepOrder = 1;
                foreach (DomainReloadStep step in report.Steps)
                    DrawStep(step, report.TotalMs, 0, ref stepOrder);

                List<DomainReloadStep> hot = Flatten(report.Steps).OrderByDescending(step => step.DurationMs).Take(10).ToList();
                DrawSimpleTableHeader("Top steps (inclusive time)", "ms", "%");
                foreach (DomainReloadStep step in hot)
                    DrawMetricRow(step.Name, step.DurationMs, report.TotalMs, null);
            }

            DrawMeasuredTimeline(report);

            if (report.AssetPipeline != null)
                DrawAssetPipeline(report.AssetPipeline);
            if (report.ProfilerSamples != null && report.ProfilerSamples.Count > 0)
                DrawProfilerSamples(report);
            if (report.Evidence != null && report.Evidence.Count > 0)
                DrawEvidence(report.Evidence);
        }

        private void DrawMeasuredTimeline(DomainReloadReport report)
        {
            IReadOnlyList<DomainReloadTimelineEvent> timeline = report.Timeline;
            timeline ??= Array.Empty<DomainReloadTimelineEvent>();
            if (timeline.Count == 0)
            {
                if (!string.IsNullOrEmpty(report.TimelineDiagnostic))
                    EditorGUILayout.HelpBox("Ordered tracker timeline: " + report.TimelineDiagnostic, MessageType.Info);
                return;
            }

            string key = "tracker-timeline";
            bool open = !_foldouts.TryGetValue(key, out bool value) || value;
            _foldouts[key] = EditorGUILayout.Foldout(open, $"Observed task/callback order ({timeline.Count})", true);
            if (!_foldouts[key]) return;

            EditorGUILayout.HelpBox(
                report.TimelineDiagnostic ??
                "Events are ordered by Unity Editor tracker timestamps. Nested durations are inclusive and can overlap.",
                MessageType.Info);
            DrawPagination(ref _timelinePage, timeline.Count, TimelineEventsPerPage, "events");
            int start = _timelinePage * TimelineEventsPerPage;
            int end = Mathf.Min(start + TimelineEventsPerPage, timeline.Count);
            for (int index = start; index < end; index++)
            {
                DomainReloadTimelineEvent item = timeline[index];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("#" + item.Order, EditorStyles.miniLabel, GUILayout.Width(38));
                    GUILayout.Label(item.StartMs.ToString("0.###") + " ms", GUILayout.Width(82));
                    GUILayout.Label(item.DurationMs.ToString("0.###") + " ms",
                        item.DurationMs >= 50 ? RiskStyle(DomainReloadRisk.High) : EditorStyles.miniLabel,
                        GUILayout.Width(85));
                    GUILayout.Label(item.Category, EditorStyles.miniLabel, GUILayout.Width(145));
                    EditorGUILayout.SelectableLabel(item.Tracker, EditorStyles.miniLabel, GUILayout.Height(17));
                    if (!string.IsNullOrEmpty(item.AssemblyName))
                        GUILayout.Label(item.AssemblyName, EditorStyles.miniLabel, GUILayout.MaxWidth(180));
                    if (!string.IsNullOrEmpty(item.ScriptPath) &&
                        GUILayout.Button(Path.GetFileName(item.ScriptPath), EditorStyles.linkLabel, GUILayout.MaxWidth(180)))
                        OpenScript(item.ScriptPath);
                    if (item.Count > 1)
                        GUILayout.Label("x" + item.Count, EditorStyles.miniLabel, GUILayout.Width(38));
                }
            }

            DrawOwnerTimings(report.ScriptTimings, "Measured time by script", "script-timings",
                ref _scriptTimingsPage, true);
            DrawOwnerTimings(report.AssemblyTimings, "Measured time by assembly", "assembly-timings",
                ref _assemblyTimingsPage, false);
        }

        private void DrawOwnerTimings(IReadOnlyList<DomainReloadOwnerTiming> timings, string title, string key,
            ref int page, bool scripts)
        {
            if (timings == null || timings.Count == 0)
                return;
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open, $"{title} ({timings.Count})", true);
            if (!_foldouts[key]) return;

            EditorGUILayout.HelpBox(
                "These totals cover callbacks/trackers that Unity identified with an owner. They are inclusive and must not be added to the Domain Reload total.",
                MessageType.None);
            DrawPagination(ref page, timings.Count, TimingsPerPage, scripts ? "scripts" : "assemblies");
            int start = page * TimingsPerPage;
            int end = Mathf.Min(start + TimingsPerPage, timings.Count);
            for (int index = start; index < end; index++)
            {
                DomainReloadOwnerTiming item = timings[index];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(item.TotalMs.ToString("0.###") + " ms", EditorStyles.boldLabel, GUILayout.Width(90));
                    GUILayout.Label("max " + item.MaxMs.ToString("0.###") + " ms", GUILayout.Width(105));
                    GUILayout.Label(item.EventCount + " events", GUILayout.Width(75));
                    if (scripts && !string.IsNullOrEmpty(item.ScriptPath))
                    {
                        if (GUILayout.Button(item.ScriptPath, EditorStyles.linkLabel, GUILayout.MinWidth(300)))
                            OpenScript(item.ScriptPath);
                    }
                    else
                        EditorGUILayout.LabelField(item.Name, GUILayout.MinWidth(300));
                    if (!string.IsNullOrEmpty(item.AssemblyName))
                        GUILayout.Label(item.AssemblyName, EditorStyles.miniLabel, GUILayout.Width(210));
                }
            }
        }

        private void DrawStep(DomainReloadStep step, double totalMs, int visualDepth, ref int order)
        {
            string key = "step:" + visualDepth + ":" + step.Name;
            bool hasChildren = step.Children != null && step.Children.Count > 0;
            int currentOrder = order++;
            string orderedName = "#" + currentOrder + "  " + step.Name;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(visualDepth * 18f);
                if (hasChildren)
                {
                    bool open = _foldouts.TryGetValue(key, out bool value) ? value : true;
                    open = EditorGUILayout.Foldout(open, orderedName, true, EditorStyles.foldout);
                    _foldouts[key] = open;
                }
                else
                {
                    GUILayout.Space(14);
                    EditorGUILayout.LabelField(orderedName, GUILayout.MinWidth(260));
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(step.DurationMs.ToString("0.###") + " ms", GUILayout.Width(90));
                GUILayout.Label(Percent(step.DurationMs, totalMs), GUILayout.Width(58));
                if (_showSelfTime)
                    GUILayout.Label("self " + step.SelfMs.ToString("0.###") + " ms", EditorStyles.miniLabel,
                        GUILayout.Width(110));
            }
            if (hasChildren)
            {
                if (_foldouts[key])
                    foreach (DomainReloadStep child in step.Children)
                        DrawStep(child, totalMs, visualDepth + 1, ref order);
                else
                    order += CountSteps(step.Children);
            }
        }

        private static int CountSteps(IEnumerable<DomainReloadStep> steps)
        {
            return steps.Sum(step => 1 + (step.Children == null ? 0 : CountSteps(step.Children)));
        }

        private void DrawAssetPipeline(AssetPipelineTiming timing)
        {
            string key = "asset-pipeline";
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open,
                $"Asset Pipeline around reload — {timing.TotalMs:0.###} ms", true);
            if (!_foldouts[key]) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Initiator", timing.Initiator ?? "-");
                DrawMetricRow("Compile", timing.CompileMs, timing.TotalMs, null);
                DrawMetricRow("Domain reload (Asset DB summary)", timing.DomainReloadMs, timing.TotalMs,
                    "This counter differs from Domain Reload Profiling; preserve both when comparing captures");
                DrawMetricRow("Asset DB managed", timing.ManagedProcessMs, timing.TotalMs, null);
                DrawMetricRow("Asset DB native", timing.NativeProcessMs, timing.TotalMs, null);
                foreach (DomainReloadStep step in timing.Steps.OrderByDescending(step => step.DurationMs).Take(15))
                    DrawMetricRow(step.Name, step.DurationMs, timing.TotalMs, null);
            }
        }

        private void DrawProfilerSamples(DomainReloadReport report)
        {
            string key = "profiler";
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open,
                $"Editor Profiler — {report.ProfilerSamples.Count} relevant markers", true);
            if (!_foldouts[key]) return;
            EditorGUILayout.HelpBox("Timings are inclusive and can overlap. Without Deep Profiling, this mainly shows native markers; " +
                                    "with Deep Profiling it also shows managed methods, with added overhead.",
                MessageType.None);
            foreach (ProfilerSampleSummary sample in report.ProfilerSamples.Take(80))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(sample.Marker, sample.Thread), GUILayout.MinWidth(300));
                    GUILayout.Label("max " + sample.MaxMs.ToString("0.###") + " ms", GUILayout.Width(115));
                    GUILayout.Label("sum " + sample.InclusiveMs.ToString("0.###") + " ms", GUILayout.Width(115));
                    GUILayout.Label("x" + sample.Count, GUILayout.Width(55));
                    GUILayout.Label(sample.Thread, EditorStyles.miniLabel, GUILayout.Width(160));
                }
            }
            if (!string.IsNullOrEmpty(report.ProfilerCapturePath) && GUILayout.Button("Reveal .data Capture"))
                EditorUtility.RevealInFinder(report.ProfilerCapturePath);
        }

        private void DrawEvidence(IReadOnlyList<string> evidence)
        {
            string key = "evidence";
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open, $"Warnings, errors and clues in the interval ({evidence.Count})", true);
            if (!_foldouts[key]) return;
            foreach (string line in evidence.Take(100))
                EditorGUILayout.SelectableLabel(line, EditorStyles.wordWrappedMiniLabel,
                    GUILayout.MinHeight(EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(line), position.width - 55)));
        }

        private void DrawAuditPage()
        {
            EditorGUILayout.LabelField("Inventory of code and objects that can affect reload", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The scan covers Assets and every resolved package (Registry, Git, Local, Embedded and Built-in), " +
                "confirms compiled callbacks through TypeCache, lists assemblies and counts live objects restored by Unity. " +
                "A static finding is a lead, not proof of cost; confirm it with Editor.log or the Profiler.", MessageType.Info);
            _includePackages = EditorGUILayout.ToggleLeft("Include all resolved Packages", _includePackages);
            _includeLiveObjects = EditorGUILayout.ToggleLeft("Count live MonoBehaviours, ScriptableObjects and EditorWindows", _includeLiveObjects);
            if (GUILayout.Button("Scan Project Now", GUILayout.Height(30)))
            {
                _audit = DomainReloadProjectScanner.Scan(_includePackages, _includeLiveObjects);
                _findingsCacheDirty = true;
                _findingsPage = _scriptsPage = _assembliesPage = _objectsPage = 0;
                Repaint();
            }
            if (_audit == null) return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"{_audit.FilesScanned} files", GUILayout.Width(120));
                GUILayout.Label($"{_audit.PackagesScanned} packages", GUILayout.Width(120));
                GUILayout.Label($"{_audit.Assemblies.Count} assemblies", GUILayout.Width(130));
                GUILayout.Label($"{_audit.Findings.Count} findings", GUILayout.Width(120));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Export Audit JSON...", GUILayout.Width(170)))
                    ExportJson(_audit, "domain-reload-audit.json");
            }

            DrawFindingFilters();
            DrawFindings();
            DrawScripts();
            DrawAssemblies();
            DrawLiveObjects();
            if (_audit.Diagnostics.Count > 0)
                DrawEvidence(_audit.Diagnostics);
        }

        private void DrawFindingFilters()
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField("Search", _search);
                _minimumRisk = (DomainReloadRisk)EditorGUILayout.EnumPopup("Minimum Risk", _minimumRisk,
                    GUILayout.Width(220));
                int current = _kindFilter.HasValue ? (int)_kindFilter.Value + 1 : 0;
                int next = EditorGUILayout.Popup(current, FindingKindNames, GUILayout.Width(180));
                _kindFilter = next == 0 ? null : (DomainReloadFindingKind?)(next - 1);
            }
            if (EditorGUI.EndChangeCheck())
            {
                _findingsCacheDirty = true;
                _findingsPage = 0;
            }
        }

        private void DrawFindings()
        {
            IReadOnlyList<DomainReloadFinding> findings = GetFilteredFindings();

            string key = "findings";
            bool open = !_foldouts.TryGetValue(key, out bool value) || value;
            _foldouts[key] = EditorGUILayout.Foldout(open, $"Findings ({findings.Count})", true);
            if (!_foldouts[key]) return;

            DrawPagination(ref _findingsPage, findings.Count, FindingsPerPage, "findings");
            int start = _findingsPage * FindingsPerPage;
            int end = Mathf.Min(start + FindingsPerPage, findings.Count);
            for (int index = start; index < end; index++)
            {
                DomainReloadFinding finding = findings[index];
                string findingKey = $"finding:{finding.AssetPath}:{finding.Line}:{finding.Symbol}:{finding.Evidence}";
                _foldouts.TryGetValue(findingKey, out bool expanded);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(finding.Risk.ToString(), RiskStyle(finding.Risk), GUILayout.Width(65));
                        GUILayout.Label(finding.Kind.ToString(), EditorStyles.miniLabel, GUILayout.Width(120));
                        expanded = EditorGUILayout.Foldout(expanded, finding.Symbol, true, EditorStyles.foldout);
                        _foldouts[findingKey] = expanded;
                        GUILayout.FlexibleSpace();
                        if (!string.IsNullOrEmpty(finding.AssemblyName))
                            GUILayout.Label(finding.AssemblyName, EditorStyles.miniLabel, GUILayout.MaxWidth(190));
                        GUILayout.Label(finding.PackageName ?? finding.Origin, EditorStyles.miniLabel, GUILayout.MaxWidth(260));
                        if (!string.IsNullOrEmpty(finding.AssetPath) &&
                            GUILayout.Button(Path.GetFileName(finding.AssetPath) +
                                             (finding.Line > 0 ? ":" + finding.Line : string.Empty),
                                EditorStyles.linkLabel, GUILayout.MaxWidth(220)))
                            OpenFinding(finding);
                    }
                    if (expanded)
                    {
                        EditorGUILayout.LabelField(finding.Detail, EditorStyles.wordWrappedMiniLabel);
                        if (!string.IsNullOrEmpty(finding.Evidence))
                            EditorGUILayout.SelectableLabel(finding.Evidence, EditorStyles.miniLabel, GUILayout.Height(17));
                        if (!string.IsNullOrEmpty(finding.AssetPath))
                            EditorGUILayout.SelectableLabel(finding.AssetPath, EditorStyles.miniLabel, GUILayout.Height(17));
                    }
                }
            }
            if (findings.Count > FindingsPerPage)
                DrawPagination(ref _findingsPage, findings.Count, FindingsPerPage, "findings");
        }

        private void DrawAssemblies()
        {
            string key = "assemblies";
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open, $"Loaded/compiled assemblies ({_audit.Assemblies.Count})", true);
            if (!_foldouts[key]) return;
            DrawPagination(ref _assembliesPage, _audit.Assemblies.Count, AssembliesPerPage, "assemblies");
            int start = _assembliesPage * AssembliesPerPage;
            int end = Mathf.Min(start + AssembliesPerPage, _audit.Assemblies.Count);
            for (int index = start; index < end; index++)
            {
                DomainReloadAssemblyInfo assembly = _audit.Assemblies[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(assembly.Name, GUILayout.MinWidth(260));
                    GUILayout.Label(assembly.Origin, EditorStyles.miniLabel, GUILayout.Width(130));
                    GUILayout.Label(assembly.SourceFileCount + " sources", GUILayout.Width(90));
                    GUILayout.Label(assembly.ReferenceCount + " refs", GUILayout.Width(75));
                    GUILayout.Label(EditorUtility.FormatBytes(assembly.BinaryBytes), GUILayout.Width(85));
                    GUILayout.Label(assembly.FindingCount + " findings", GUILayout.Width(85));
                    GUILayout.Label(assembly.HighRiskCount + " high", GUILayout.Width(62));
                    GUILayout.Label(assembly.ReloadCallbackCount + " callbacks", GUILayout.Width(85));
                }
            }
        }

        private void DrawScripts()
        {
            if (_audit.Scripts == null || _audit.Scripts.Count == 0) return;
            string key = "scripts";
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open, $"Impact by script ({_audit.Scripts.Count})", true);
            if (!_foldouts[key]) return;

            EditorGUILayout.HelpBox(
                "This is a static inventory grouped by source file. Counts identify likely reload participants; measured time appears in a controlled capture when Unity exposes a tracker for that callback.",
                MessageType.None);
            DrawPagination(ref _scriptsPage, _audit.Scripts.Count, ScriptsPerPage, "scripts");
            int start = _scriptsPage * ScriptsPerPage;
            int end = Mathf.Min(start + ScriptsPerPage, _audit.Scripts.Count);
            for (int index = start; index < end; index++)
            {
                DomainReloadScriptInfo script = _audit.Scripts[index];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(script.HighRiskCount + " high", RiskStyle(
                        script.HighRiskCount > 0 ? DomainReloadRisk.High : DomainReloadRisk.Info), GUILayout.Width(65));
                    GUILayout.Label(script.ReloadCallbackCount + " callbacks", GUILayout.Width(88));
                    GUILayout.Label(script.FindingCount + " findings", GUILayout.Width(78));
                    if (GUILayout.Button(script.AssetPath, EditorStyles.linkLabel, GUILayout.MinWidth(320)))
                        OpenScript(script.AssetPath, script.FullPath);
                    GUILayout.Label(script.AssemblyName ?? "Unresolved assembly", EditorStyles.miniLabel, GUILayout.Width(220));
                    GUILayout.Label(script.PackageName ?? script.Origin, EditorStyles.miniLabel, GUILayout.MaxWidth(220));
                }
            }
        }

        private void DrawLiveObjects()
        {
            if (_audit.LiveObjects.Count == 0) return;
            string key = "objects";
            _foldouts.TryGetValue(key, out bool open);
            _foldouts[key] = EditorGUILayout.Foldout(open, $"Live serialized/restored objects ({_audit.LiveObjects.Sum(o => o.Count)})", true);
            if (!_foldouts[key]) return;
            DrawPagination(ref _objectsPage, _audit.LiveObjects.Count, ObjectsPerPage, "object types");
            int start = _objectsPage * ObjectsPerPage;
            int end = Mathf.Min(start + ObjectsPerPage, _audit.LiveObjects.Count);
            for (int index = start; index < end; index++)
            {
                DomainReloadObjectInfo item = _audit.LiveObjects[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(item.Count.ToString(), EditorStyles.boldLabel, GUILayout.Width(55));
                    GUILayout.Label(item.Kind, GUILayout.Width(105));
                    EditorGUILayout.LabelField(item.TypeName, GUILayout.MinWidth(300));
                    GUILayout.Label(item.Origin, EditorStyles.miniLabel, GUILayout.Width(140));
                }
            }
        }

        private IReadOnlyList<DomainReloadFinding> GetFilteredFindings()
        {
            if (!_findingsCacheDirty && ReferenceEquals(_filterCacheAudit, _audit) &&
                string.Equals(_filterCacheSearch, _search, StringComparison.Ordinal) &&
                _filterCacheRisk == _minimumRisk && _filterCacheKind == _kindFilter)
                return _filteredFindings;

            string search = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim();
            _filteredFindings.Clear();
            foreach (DomainReloadFinding finding in _audit.Findings)
            {
                if (finding.Risk < _minimumRisk || (_kindFilter.HasValue && finding.Kind != _kindFilter.Value))
                    continue;
                if (search != null && !FindingMatchesSearch(finding, search))
                    continue;
                _filteredFindings.Add(finding);
            }

            _filterCacheAudit = _audit;
            _filterCacheSearch = _search;
            _filterCacheRisk = _minimumRisk;
            _filterCacheKind = _kindFilter;
            _findingsCacheDirty = false;
            return _filteredFindings;
        }

        private static bool FindingMatchesSearch(DomainReloadFinding finding, string search)
        {
            return Contains(finding.Symbol, search) || Contains(finding.AssetPath, search) ||
                   Contains(finding.Evidence, search) || Contains(finding.PackageName, search) ||
                   Contains(finding.Origin, search) || Contains(finding.Detail, search);
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawPagination(ref int page, int count, int pageSize, string itemLabel)
        {
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(count / (float)pageSize));
            page = Mathf.Clamp(page, 0, totalPages - 1);
            int first = count == 0 ? 0 : page * pageSize + 1;
            int last = Mathf.Min((page + 1) * pageSize, count);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{first}-{last} of {count} {itemLabel}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(page <= 0))
                    if (GUILayout.Button("Previous", EditorStyles.toolbarButton, GUILayout.Width(70))) page--;
                GUILayout.Label($"Page {page + 1}/{totalPages}", GUILayout.Width(85));
                using (new EditorGUI.DisabledScope(page >= totalPages - 1))
                    if (GUILayout.Button("Next", EditorStyles.toolbarButton, GUILayout.Width(55))) page++;
            }
        }

        private void DrawGuidePage()
        {
            EditorGUILayout.LabelField("What Unity executes", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. beforeAssemblyReload.\n" +
                "2. Stops the domain: OnDisable, waits for async operations and serializes MonoBehaviours/ScriptableObjects.\n" +
                "3. Disconnects native wrappers, unloads the AppDomain, runs GC/finalizers, terminates threads and discards JIT data.\n" +
                "4. Creates the new domain and loads system, Unity, package and user assemblies.\n" +
                "5. Rebuilds TypeCache and script caches.\n" +
                "6. Restores/deserializes objects: constructors, OnAfterDeserialize, OnValidate and lifecycle callbacks.\n" +
                "7. Executes InitializeOnLoad, InitializeOnLoadMethod, DidReloadScripts/afterAssemblyReload.\n" +
                "8. The Asset Pipeline continues Refresh and invokes post-processors/importers.", MessageType.None);

            EditorGUILayout.LabelField("Recommended workflow", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Capture without the Profiler first and compare at least three reloads. If the bottleneck is " +
                "ProcessInitializeOnLoadAttributes/Methods, use the audit to list callbacks and then capture with Deep Profiling. " +
                "For AwakeInstancesAfterBackupRestoration, sort live objects and inspect OnEnable, OnValidate and serialization. " +
                "For LoadAssemblies/TypeCache, reduce assemblies, types and references. If the long duration only appears in Asset Pipeline, " +
                "the issue is compilation/import rather than isolated Domain Reload. For a hang, preserve the pending capture and restart " +
                "the Editor to recover the last recorded stage.", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Unity documentation: Enter Play Mode details"))
                Help.BrowseURL("https://docs.unity3d.com/Manual/ConfigurableEnterPlayModeDetails.html");
            if (GUILayout.Button("Unity documentation: Editor profiling"))
                Help.BrowseURL("https://docs.unity3d.com/Manual/profiler-profiling-applications.html");
            if (GUILayout.Button("Unity documentation: InitializeOnLoad"))
                Help.BrowseURL("https://docs.unity3d.com/ScriptReference/InitializeOnLoadAttribute.html");
        }

        private void RefreshSavedReports()
        {
            _savedReports = DomainReloadCaptureCoordinator.LoadSavedReports();
            if (_savedReports.Count > 0)
                _selectedReport = _savedReports[0];
            Repaint();
        }

        private static void DrawMetricRow(string label, double value, double total, string tooltip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.MinWidth(300));
                GUILayout.FlexibleSpace();
                GUILayout.Label(value.ToString("0.###") + " ms", GUILayout.Width(105));
                GUILayout.Label(Percent(value, total), GUILayout.Width(60));
            }
        }

        private static void DrawSimpleTableHeader(string title, string col1, string col2)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(col1, GUILayout.Width(105));
                GUILayout.Label(col2, GUILayout.Width(60));
            }
        }

        private static IEnumerable<DomainReloadStep> Flatten(IEnumerable<DomainReloadStep> steps)
        {
            foreach (DomainReloadStep step in steps)
            {
                yield return step;
                if (step.Children != null)
                    foreach (DomainReloadStep child in Flatten(step.Children))
                        yield return child;
            }
        }

        private static string Percent(double value, double total) => total <= 0 ? "-" : (value / total).ToString("P1");
        private static string FormatUtc(string value) => DateTime.TryParse(value, out DateTime date) ? date.ToLocalTime().ToString("G") : value;

        private static int IndexOfReference(IReadOnlyList<DomainReloadReport> reports, DomainReloadReport report)
        {
            for (int i = 0; i < reports.Count; i++)
                if (ReferenceEquals(reports[i], report)) return i;
            return 0;
        }

        private static GUIStyle RiskStyle(DomainReloadRisk risk)
        {
            int index = (int)risk;
            if (RiskStyles[index] != null)
                return RiskStyles[index];
            GUIStyle style = new(EditorStyles.miniBoldLabel);
            style.normal.textColor = risk switch
            {
                DomainReloadRisk.High => new Color(1f, 0.35f, 0.25f),
                DomainReloadRisk.Medium => new Color(1f, 0.65f, 0.15f),
                DomainReloadRisk.Low => new Color(0.35f, 0.65f, 1f),
                _ => EditorStyles.miniLabel.normal.textColor
            };
            RiskStyles[index] = style;
            return RiskStyles[index];
        }

        private static void OpenFinding(DomainReloadFinding finding)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finding.AssetPath);
            if (asset != null)
                AssetDatabase.OpenAsset(asset, Math.Max(1, finding.Line));
            else if (!string.IsNullOrEmpty(finding.FullPath))
                InternalEditorUtility.OpenFileAtLineExternal(finding.FullPath, Math.Max(1, finding.Line), 0);
        }

        private static void OpenScript(string assetPath, string fullPath = null)
        {
            if (!string.IsNullOrEmpty(assetPath))
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset);
                    return;
                }
            }

            string path = !string.IsNullOrEmpty(fullPath) ? fullPath : assetPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                EditorUtility.RevealInFinder(path);
        }

        private static void ExportJson(object value, string defaultName)
        {
            string path = EditorUtility.SaveFilePanel("Export Domain Reload Analyzer", string.Empty, defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, DomainReloadJson.Serialize(value));
            EditorUtility.RevealInFinder(path);
        }
    }
}
