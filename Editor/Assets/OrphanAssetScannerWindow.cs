using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class OrphanAssetScannerWindow : EditorWindow
    {
        [Flags]
        private enum RuntimeUsageFlags
        {
            None = 0,
            Resources = 1 << 0,
            AssetBundle = 1 << 1,
            Addressables = 1 << 2
        }

        private enum ResultFilter
        {
            All,
            ProbablyOrphan,
            VerifiedOrphan,
            RuntimeReferenced,
            IndirectlyReferenced,
            Unknown,
            ResourcesOnly,
            AssetBundlesOnly,
            AddressablesOnly
        }

        private enum ScanReferenceMode
        {
            ScanOnlyInScope,
            ScanWholeProject
        }

        private enum OrphanStatus
        {
            Unknown,
            ProbablyOrphan,
            VerifiedOrphan,
            RuntimeReferenced,
            IndirectlyReferenced
        }

        [Flags]
        private enum AssetCategoryFilter
        {
            None = 0,
            Prefab = 1 << 0,
            Scene = 1 << 1,
            ScriptableObject = 1 << 2,
            Texture = 1 << 3,
            Sprite = 1 << 4,
            Material = 1 << 5,
            AudioClip = 1 << 6,
            AnimationClip = 1 << 7,
            AnimatorController = 1 << 8,
            Model = 1 << 9,
            Font = 1 << 10,
            VideoClip = 1 << 11,
            Shader = 1 << 12,
            Other = 1 << 13,
            All = Prefab | Scene | ScriptableObject | Texture | Sprite | Material | AudioClip | AnimationClip |
                  AnimatorController | Model | Font | VideoClip | Shader | Other
        }

        private enum SortColumn
        {
            Name,
            Type,
            Path,
            Status,
            RuntimeUsage,
            ReferencedByCount,
            EstimatedSize,
            Notes
        }

        private sealed class OrphanAssetResult
        {
            public string Guid;
            public string Name;
            public string Path;
            public string TypeName;
            public AssetCategoryFilter Category;
            public string AssetBundleName;
            public RuntimeUsageFlags RuntimeUsage;
            public OrphanStatus Status;
            public string Notes;
            public long EstimatedSizeBytes;
            public bool DeepScanPerformed;
            public int DeepScanReferenceCount;
            public int BuildSettingsSceneReferenceCount;
            public UnityEngine.Object Asset;
        }

        private static readonly string[] MapperExtensions =
        {
            ".prefab",
            ".asset",
            ".unity",
            ".mat",
            ".anim",
            ".controller",
            ".shader",
            ".meta"
        };

        private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".asmdef",
            ".asmref",
            ".rsp",
            ".dll",
            ".so",
            ".jar",
            ".aar",
            ".a",
            ".h",
            ".hpp",
            ".c",
            ".cpp",
            ".m",
            ".mm",
            ".java",
            ".kt",
            ".gradle",
            ".swift",
            ".cginc",
            ".hlsl",
            ".shaderinclude"
        };

        private const float ToggleColumnWidth = 26f;
        private const float NameColumnWidth = 180f;
        private const float TypeColumnWidth = 130f;
        private const float PathColumnWidth = 360f;
        private const float StatusColumnWidth = 160f;
        private const float RuntimeUsageColumnWidth = 230f;
        private const float ReferencedByCountColumnWidth = 86f;
        private const float EstimatedSizeColumnWidth = 110f;
        private const float NotesColumnWidth = 360f;
        private const float ActionsColumnWidth = 140f;

        private readonly List<OrphanAssetResult> _results = new();
        private readonly HashSet<string> _selectedAssetPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _ignoredFolders = new();

        private Vector2 _scrollPosition;
        private AssetGuidMapper _assetGuidMapper;
        private CancellationTokenSource _scanCancellationSource;
        private bool _isScanning;
        private float _scanProgress;
        private string _scanStatus = "No scan has been executed.";
        private string _searchText = string.Empty;
        private string _typeFilter = string.Empty;
        private string _rootFolder = "Assets";
        private string _moveTargetFolder = "Assets";
        private string _ignoredFolderDraft = "Assets";
        private ResultFilter _resultFilter = ResultFilter.All;
        private ScanReferenceMode _scanReferenceMode = ScanReferenceMode.ScanWholeProject;
        private AssetCategoryFilter _assetCategoryFilter = AssetCategoryFilter.None;
        private SortColumn _sortColumn = SortColumn.Status;
        private bool _sortAscending;
        private GUIStyle _heroTitleStyle;
        private GUIStyle _heroBodyStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _sectionBodyStyle;
        private GUIStyle _statValueStyle;
        private GUIStyle _statLabelStyle;
        private GUIStyle _headerButtonStyle;
        private GUIStyle _pathLabelStyle;
        private GUIStyle _notesLabelStyle;
        private GUIStyle _chipStyle;
        private GUIStyle _selectedChipStyle;
        private GUIStyle _statusPillStyle;
        private GUIStyle _mutedMiniLabelStyle;
        private GUIStyle _dangerButtonStyle;

        [MenuItem("Tools/LegendaryTools/Assets/Orphan Asset Scanner")]
        private static void OpenWindow()
        {
            OrphanAssetScannerWindow window = GetWindow<OrphanAssetScannerWindow>("Orphan Assets");
            window.minSize = new Vector2(1380f, 560f);
            window.Show();
        }

        private void OnDisable()
        {
            CancelScan();
            EditorUtility.ClearProgressBar();
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(8f);
            DrawToolbar();

            EditorGUILayout.Space(8f);
            BeginSection("Scan Scope");
            DrawFilters();
            EndSection();

            EditorGUILayout.Space(8f);
            BeginSection("Bulk Actions");
            DrawBulkActions();
            EndSection();

            EditorGUILayout.Space(8f);
            DrawSummary();

            EditorGUILayout.Space(8f);
            BeginSection("Results");
            DrawResultsTable();
            EndSection();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(_sectionStyle))
            {
                EditorGUILayout.LabelField("Orphan Asset Scanner", _heroTitleStyle);
                EditorGUILayout.LabelField(
                    "Find assets without serialized GUID references, while clearly flagging Resources, AssetBundles, and Addressables cases.",
                    _heroBodyStyle);

                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_isScanning))
                    {
                        if (GUILayout.Button("Scan Project Scope", GUILayout.Height(30f), GUILayout.Width(150f)))
                        {
                            StartScan();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!_isScanning))
                    {
                        if (GUILayout.Button("Cancel", GUILayout.Height(30f), GUILayout.Width(110f)))
                        {
                            CancelScan();
                        }
                    }

                    if (GUILayout.Button("Clear Results", GUILayout.Height(30f), GUILayout.Width(120f)))
                    {
                        _results.Clear();
                        _selectedAssetPaths.Clear();
                        _scanProgress = 0f;
                        _scanStatus = "Results cleared.";
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(_scanStatus, _mutedMiniLabelStyle, GUILayout.Height(28f));
                }

                Rect progressRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(progressRect, _scanProgress, $"{Mathf.RoundToInt(_scanProgress * 100f)}%");
            }
        }

        private void DrawFilters()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _rootFolder = EditorGUILayout.TextField("Root Folder", _rootFolder);

                if (GUILayout.Button("Use Selected Folder", GUILayout.Width(160f)))
                {
                    AssignFolderFromSelection(ref _rootFolder);
                }

                if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                {
                    BrowseForProjectFolder(ref _rootFolder);
                }

                if (GUILayout.Button("Reset", GUILayout.Width(80f)))
                {
                    _rootFolder = "Assets";
                }
            }

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                _searchText = EditorGUILayout.TextField("Search", _searchText);
                _typeFilter = EditorGUILayout.TextField("Type Contains", _typeFilter);
                _resultFilter = (ResultFilter)EditorGUILayout.EnumPopup("Status", _resultFilter, GUILayout.Width(320f));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _scanReferenceMode = (ScanReferenceMode)EditorGUILayout.EnumPopup(
                    "Reference Scope",
                    _scanReferenceMode,
                    GUILayout.Width(380f));

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Ignored Folders", _sectionBodyStyle);
            DrawIgnoredFoldersUi();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Asset Type Filters", _sectionBodyStyle);
            DrawAssetCategoryFilters();

            if (!TryNormalizeProjectFolder(_rootFolder, out _, out string folderError))
            {
                EditorGUILayout.HelpBox(folderError, MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "The scanner uses AssetGuidMapper to find serialized GUID references. Scan Whole Project checks references across the project, while Scan Only In Scope limits reference detection to the selected root folder. Assets without direct references can still be used at runtime through Resources, AssetBundles, or Addressables. Estimated size is based on the asset file plus its .meta file on disk.",
                MessageType.Info);
        }

        private void DrawBulkActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Selected: {_selectedAssetPaths.Count}", _sectionBodyStyle, GUILayout.Width(140f));

                if (GUILayout.Button("Select Visible", GUILayout.Width(120f)))
                {
                    foreach (OrphanAssetResult result in GetVisibleResults())
                    {
                        _selectedAssetPaths.Add(result.Path);
                    }
                }

                if (GUILayout.Button("Clear Selection", GUILayout.Width(120f)))
                {
                    _selectedAssetPaths.Clear();
                }

                GUILayout.FlexibleSpace();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _moveTargetFolder = EditorGUILayout.TextField("Move Target Folder", _moveTargetFolder);

                if (GUILayout.Button("Use Selected Folder", GUILayout.Width(160f)))
                {
                    AssignFolderFromSelection(ref _moveTargetFolder);
                }

                if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                {
                    BrowseForProjectFolder(ref _moveTargetFolder);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selectedAssetPaths.Count == 0))
                {
                    if (GUILayout.Button("Deep Scan", GUILayout.Height(26f), GUILayout.Width(140f)))
                    {
                        DeepScanSelectedAssets();
                    }
                }

                using (new EditorGUI.DisabledScope(_selectedAssetPaths.Count == 0))
                {
                    if (GUILayout.Button("Delete Selected", GUILayout.Height(26f), GUILayout.Width(140f)))
                    {
                        DeleteSelectedAssets();
                    }
                }

                using (new EditorGUI.DisabledScope(_selectedAssetPaths.Count == 0))
                {
                    if (GUILayout.Button("Move Selected", GUILayout.Height(26f), GUILayout.Width(140f)))
                    {
                        MoveSelectedAssets();
                    }
                }
            }
        }

        private void DrawSummary()
        {
            List<OrphanAssetResult> activeResults = _results
                .Where(result => !IsIgnoredAssetPath(result.Path))
                .ToList();

            int probablyOrphanCount = activeResults.Count(result => result.Status == OrphanStatus.ProbablyOrphan);
            int verifiedOrphanCount = activeResults.Count(result => result.Status == OrphanStatus.VerifiedOrphan);
            int runtimeReferencedCount = activeResults.Count(result => result.Status == OrphanStatus.RuntimeReferenced);
            int indirectlyReferencedCount = activeResults.Count(result => result.Status == OrphanStatus.IndirectlyReferenced);
            int unknownCount = activeResults.Count(result => result.Status == OrphanStatus.Unknown);
            int resourcesCount = activeResults.Count(result => HasFlag(result.RuntimeUsage, RuntimeUsageFlags.Resources));
            int assetBundleCount = activeResults.Count(result => HasFlag(result.RuntimeUsage, RuntimeUsageFlags.AssetBundle));
            int addressablesCount = activeResults.Count(result => HasFlag(result.RuntimeUsage, RuntimeUsageFlags.Addressables));

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStatCard("Candidates", activeResults.Count.ToString(), new Color(0.22f, 0.46f, 0.84f, 0.22f));
                DrawStatCard("Probably Orphan", probablyOrphanCount.ToString(), new Color(0.16f, 0.62f, 0.42f, 0.22f));
                DrawStatCard("Verified Orphan", verifiedOrphanCount.ToString(), new Color(0.14f, 0.49f, 0.32f, 0.26f));
                DrawStatCard("Runtime-Referenced", runtimeReferencedCount.ToString(), new Color(0.82f, 0.56f, 0.17f, 0.22f));
                DrawStatCard("Indirectly Referenced", indirectlyReferencedCount.ToString(), new Color(0.74f, 0.36f, 0.20f, 0.22f));
                DrawStatCard("Unknown", unknownCount.ToString(), new Color(0.48f, 0.50f, 0.58f, 0.22f));
                DrawStatCard("Build Scenes", activeResults.Sum(result => result.BuildSettingsSceneReferenceCount).ToString(), new Color(0.18f, 0.68f, 0.70f, 0.22f));
                DrawStatCard("Resources", resourcesCount.ToString(), new Color(0.60f, 0.33f, 0.77f, 0.22f));
                DrawStatCard("Bundles", assetBundleCount.ToString(), new Color(0.84f, 0.42f, 0.20f, 0.22f));
                DrawStatCard("Addressables", addressablesCount.ToString(), new Color(0.18f, 0.68f, 0.70f, 0.22f));
            }
        }

        private void DrawResultsTable()
        {
            EditorGUILayout.Space();

            List<OrphanAssetResult> visibleResults = GetVisibleResults().ToList();
            DrawTableHeader(visibleResults);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (int i = 0; i < visibleResults.Count; i++)
            {
                DrawRow(visibleResults[i], i);
            }

            if (visibleResults.Count == 0)
            {
                EditorGUILayout.HelpBox("No results match the current filters.", MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTableHeader(IReadOnlyCollection<OrphanAssetResult> visibleResults)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                DrawSelectionHeader(visibleResults);
                DrawSortableHeader("Name", SortColumn.Name, NameColumnWidth);
                DrawSortableHeader("Type", SortColumn.Type, TypeColumnWidth);
                DrawSortableHeader("Path", SortColumn.Path, PathColumnWidth);
                DrawSortableHeader("Status", SortColumn.Status, StatusColumnWidth);
                DrawSortableHeader("Runtime Usage", SortColumn.RuntimeUsage, RuntimeUsageColumnWidth);
                DrawSortableHeader("Ref By", SortColumn.ReferencedByCount, ReferencedByCountColumnWidth);
                DrawSortableHeader("Est. Size", SortColumn.EstimatedSize, EstimatedSizeColumnWidth);
                DrawSortableHeader("Notes", SortColumn.Notes, NotesColumnWidth);
                GUILayout.Label("Actions", _sectionBodyStyle, GUILayout.Width(ActionsColumnWidth));
            }
        }

        private void DrawSelectionHeader(IReadOnlyCollection<OrphanAssetResult> visibleResults)
        {
            bool hasVisibleResults = visibleResults.Count > 0;
            bool allVisibleSelected = hasVisibleResults && visibleResults.All(result => _selectedAssetPaths.Contains(result.Path));
            bool newValue = GUILayout.Toggle(allVisibleSelected, GUIContent.none, GUILayout.Width(ToggleColumnWidth));

            if (newValue == allVisibleSelected)
            {
                return;
            }

            foreach (OrphanAssetResult result in visibleResults)
            {
                if (newValue)
                {
                    _selectedAssetPaths.Add(result.Path);
                }
                else
                {
                    _selectedAssetPaths.Remove(result.Path);
                }
            }
        }

        private void DrawSortableHeader(string label, SortColumn column, float width)
        {
            string displayLabel = label;
            if (_sortColumn == column)
            {
                displayLabel += _sortAscending ? " ^" : " v";
            }

            if (GUILayout.Button(displayLabel, _headerButtonStyle, GUILayout.Width(width), GUILayout.Height(22f)))
            {
                ApplySort(column);
            }
        }

        private void DrawRow(OrphanAssetResult result, int index)
        {
            GUIStyle backgroundStyle = new GUIStyle((index & 1) == 0 ? "CN EntryBackEven" : "CN EntryBackOdd");
            Rect rowRect = EditorGUILayout.BeginHorizontal();
            GUI.Box(rowRect, GUIContent.none, backgroundStyle);

            bool isSelected = _selectedAssetPaths.Contains(result.Path);
            bool newSelection = GUILayout.Toggle(isSelected, GUIContent.none, GUILayout.Width(ToggleColumnWidth));
            if (newSelection != isSelected)
            {
                if (newSelection)
                {
                    _selectedAssetPaths.Add(result.Path);
                }
                else
                {
                    _selectedAssetPaths.Remove(result.Path);
                }
            }

            GUILayout.Label(result.Name, GUILayout.Width(NameColumnWidth));
            GUILayout.Label(GetDisplayCategoryLabel(result), GUILayout.Width(TypeColumnWidth));
            GUILayout.Label(result.Path, _pathLabelStyle, GUILayout.Width(PathColumnWidth));
            DrawStatusPill(GetDisplayStatusLabel(result.Status), GUILayout.Width(StatusColumnWidth));
            GUILayout.Label(BuildRuntimeUsageLabel(result), GUILayout.Width(RuntimeUsageColumnWidth));
            GUILayout.Label(result.DeepScanPerformed ? result.DeepScanReferenceCount.ToString() : "-", GUILayout.Width(ReferencedByCountColumnWidth));
            GUILayout.Label(FormatBytes(result.EstimatedSizeBytes), GUILayout.Width(EstimatedSizeColumnWidth));
            GUILayout.Label(result.Notes, _notesLabelStyle, GUILayout.Width(NotesColumnWidth));

            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(ActionsColumnWidth)))
            {
                if (GUILayout.Button("Ping", GUILayout.Width(60f)))
                {
                    FocusAsset(result.Asset);
                }

                if (GUILayout.Button("Reveal", GUILayout.Width(70f)))
                {
                    EditorUtility.RevealInFinder(ToAbsolutePath(result.Path));
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private IEnumerable<OrphanAssetResult> GetVisibleResults()
        {
            IEnumerable<OrphanAssetResult> query = GetFilteredResultsWithoutCategoryFilter();
            if (_assetCategoryFilter != AssetCategoryFilter.None)
            {
                query = query.Where(result => (_assetCategoryFilter & result.Category) != 0);
            }

            return ApplySort(query);
        }

        private IEnumerable<OrphanAssetResult> GetFilteredResultsWithoutCategoryFilter()
        {
            IEnumerable<OrphanAssetResult> query = _results.Where(result => !IsIgnoredAssetPath(result.Path));

            if (TryNormalizeProjectFolder(_rootFolder, out string normalizedRootFolder, out _))
            {
                query = query.Where(result => IsPathUnderFolder(result.Path, normalizedRootFolder));
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string search = _searchText.Trim();
                query = query.Where(result =>
                    Contains(result.Name, search) ||
                    Contains(result.Path, search) ||
                    Contains(result.TypeName, search) ||
                    Contains(result.Notes, search) ||
                    Contains(GetDisplayStatusLabel(result.Status), search));
            }

            if (!string.IsNullOrWhiteSpace(_typeFilter))
            {
                string typeSearch = _typeFilter.Trim();
                query = query.Where(result =>
                    Contains(result.TypeName, typeSearch) ||
                    Contains(GetDisplayCategoryLabel(result), typeSearch));
            }

            return ApplyResultFilter(query);
        }

        private IEnumerable<OrphanAssetResult> ApplyResultFilter(IEnumerable<OrphanAssetResult> query)
        {
            return _resultFilter switch
            {
                ResultFilter.ProbablyOrphan => query.Where(result => result.Status == OrphanStatus.ProbablyOrphan),
                ResultFilter.VerifiedOrphan => query.Where(result => result.Status == OrphanStatus.VerifiedOrphan),
                ResultFilter.RuntimeReferenced => query.Where(result => result.Status == OrphanStatus.RuntimeReferenced),
                ResultFilter.IndirectlyReferenced => query.Where(result => result.Status == OrphanStatus.IndirectlyReferenced),
                ResultFilter.Unknown => query.Where(result => result.Status == OrphanStatus.Unknown),
                ResultFilter.ResourcesOnly => query.Where(result => HasFlag(result.RuntimeUsage, RuntimeUsageFlags.Resources)),
                ResultFilter.AssetBundlesOnly => query.Where(result => HasFlag(result.RuntimeUsage, RuntimeUsageFlags.AssetBundle)),
                ResultFilter.AddressablesOnly => query.Where(result => HasFlag(result.RuntimeUsage, RuntimeUsageFlags.Addressables)),
                _ => query
            };
        }

        private IEnumerable<OrphanAssetResult> ApplySort(IEnumerable<OrphanAssetResult> results)
        {
            Func<OrphanAssetResult, IComparable> keySelector = _sortColumn switch
            {
                SortColumn.Name => result => result.Name ?? string.Empty,
                SortColumn.Type => result => result.TypeName ?? string.Empty,
                SortColumn.Path => result => result.Path ?? string.Empty,
                SortColumn.Status => result => GetStatusSortValue(result.Status),
                SortColumn.RuntimeUsage => result => BuildRuntimeUsageLabel(result),
                SortColumn.ReferencedByCount => result => result.DeepScanReferenceCount,
                SortColumn.EstimatedSize => result => result.EstimatedSizeBytes,
                SortColumn.Notes => result => result.Notes ?? string.Empty,
                _ => result => result.Path ?? string.Empty
            };

            IOrderedEnumerable<OrphanAssetResult> orderedResults = _sortAscending
                ? results.OrderBy(keySelector).ThenBy(result => result.Path, StringComparer.OrdinalIgnoreCase)
                : results.OrderByDescending(keySelector).ThenBy(result => result.Path, StringComparer.OrdinalIgnoreCase);

            return orderedResults;
        }

        private void ApplySort(SortColumn column)
        {
            if (_sortColumn == column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = column;
                _sortAscending = true;
            }
        }

        private void StartScan()
        {
            if (_isScanning)
            {
                return;
            }

            if (!TryNormalizeProjectFolder(_rootFolder, out string normalizedRootFolder, out string folderError))
            {
                EditorUtility.DisplayDialog("Invalid Root Folder", folderError, "OK");
                return;
            }

            _rootFolder = normalizedRootFolder;
            _ = RunScanAsync(normalizedRootFolder, _scanReferenceMode);
        }

        private void CancelScan()
        {
            _scanCancellationSource?.Cancel();
        }

        private async Task RunScanAsync(string rootFolder, ScanReferenceMode scanReferenceMode)
        {
            _isScanning = true;
            _scanProgress = 0f;
            _scanStatus = "Preparing scan...";
            _results.Clear();
            _selectedAssetPaths.Clear();
            _assetGuidMapper ??= new AssetGuidMapper();
            _scanCancellationSource = new CancellationTokenSource();

            try
            {
                CancellationToken cancellationToken = _scanCancellationSource.Token;
                List<string> ignoredFolders = GetIgnoredFoldersSnapshot();
                HashSet<string> addressableGuids = CollectAddressableGuids();

                _scanStatus = "Mapping serialized references with AssetGuidMapper...";
                await _assetGuidMapper.MapProjectGUIDsAsync(MapperExtensions, UpdateMapperProgress, cancellationToken);

                string[] allAssetGuids = AssetDatabase.FindAssets(string.Empty, new[] { rootFolder });
                int totalAssets = allAssetGuids.Length;
                int processedAssets = 0;

                foreach (string guid in allAssetGuids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedAssets++;

                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!ShouldAnalyzeAsset(assetPath, rootFolder, ignoredFolders))
                    {
                        UpdateScanProgress(processedAssets, totalAssets, "Skipping unsupported assets...");
                        continue;
                    }

                    IReadOnlyCollection<string> references = await _assetGuidMapper.FindFilesContainingGuidAsync(guid);
                    int externalReferenceCount = CountExternalReferences(
                        assetPath,
                        references,
                        rootFolder,
                        scanReferenceMode,
                        ignoredFolders);

                    UpdateScanProgress(processedAssets, totalAssets, $"Analyzing {assetPath}");

                    if (externalReferenceCount > 0)
                    {
                        continue;
                    }

                    _results.Add(BuildResult(assetPath, guid, addressableGuids, scanReferenceMode));

                    if ((processedAssets % 50) == 0)
                    {
                        Repaint();
                    }
                }

                _scanProgress = 1f;
                _scanStatus = $"Scan complete. {_results.Count} candidate assets found.";
            }
            catch (OperationCanceledException)
            {
                _scanStatus = "Scan canceled.";
            }
            catch (Exception exception)
            {
                _scanStatus = "Scan failed.";
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _scanCancellationSource?.Dispose();
                _scanCancellationSource = null;
                _isScanning = false;
                Repaint();
            }
        }

        private void UpdateMapperProgress(float progress, string message)
        {
            _scanProgress = progress * 0.45f;
            _scanStatus = message;
        }

        private void UpdateScanProgress(int processedAssets, int totalAssets, string message)
        {
            float assetPhase = totalAssets <= 0 ? 1f : (float)processedAssets / totalAssets;
            _scanProgress = 0.45f + (assetPhase * 0.55f);
            _scanStatus = message;

            if ((processedAssets % 100) != 0)
            {
                return;
            }

            if (EditorUtility.DisplayCancelableProgressBar("Orphan Asset Scanner", message, _scanProgress))
            {
                CancelScan();
            }
        }

        private void DeleteSelectedAssets()
        {
            List<OrphanAssetResult> selectedResults = GetSelectedResults();
            if (selectedResults.Count == 0)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Selected Assets",
                $"Delete {selectedResults.Count} selected assets from the project?",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (OrphanAssetResult result in selectedResults)
                {
                    AssetDatabase.DeleteAsset(result.Path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            HashSet<string> deletedPaths = new(selectedResults.Select(result => result.Path), StringComparer.OrdinalIgnoreCase);
            _results.RemoveAll(result => deletedPaths.Contains(result.Path));
            foreach (string deletedPath in deletedPaths)
            {
                _selectedAssetPaths.Remove(deletedPath);
            }

            _scanStatus = $"Deleted {deletedPaths.Count} assets.";
        }

        private void DeepScanSelectedAssets()
        {
            List<OrphanAssetResult> selectedResults = GetSelectedResults()
                .Where(result => !IsIgnoredAssetPath(result.Path))
                .ToList();
            if (selectedResults.Count == 0)
            {
                _scanStatus = "No selected assets are eligible for Deep Scan.";
                return;
            }

            HashSet<string> selectedPaths = new(selectedResults.Select(result => result.Path), StringComparer.OrdinalIgnoreCase);
            HashSet<string> buildSettingsScenes = GetEnabledBuildSettingsScenes();
            Dictionary<string, HashSet<string>> referencesByAsset = selectedResults.ToDictionary(
                result => result.Path,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> buildSceneReferencesByAsset = selectedResults.ToDictionary(
                result => result.Path,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            string[] dependencyRoots = AssetDatabase
                .GetAllAssetPaths()
                .Where(path => ShouldIncludeDeepScanRoot(path) && !IsIgnoredAssetPath(path))
                .ToArray();

            try
            {
                for (int i = 0; i < dependencyRoots.Length; i++)
                {
                    string rootPath = dependencyRoots[i];
                    bool isBuildSettingsScene = buildSettingsScenes.Contains(rootPath);
                    float progress = dependencyRoots.Length == 0 ? 1f : (float)(i + 1) / dependencyRoots.Length;
                    if (EditorUtility.DisplayCancelableProgressBar("Orphan Asset Deep Scan", rootPath, progress))
                    {
                        _scanStatus = "Deep Scan canceled.";
                        return;
                    }

                    string[] dependencies = AssetDatabase.GetDependencies(rootPath, true);
                    foreach (string dependency in dependencies)
                    {
                        if (!selectedPaths.Contains(dependency) ||
                            string.Equals(dependency, rootPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        referencesByAsset[dependency].Add(rootPath);
                        if (isBuildSettingsScene)
                        {
                            buildSceneReferencesByAsset[dependency].Add(rootPath);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            foreach (OrphanAssetResult result in selectedResults)
            {
                result.DeepScanPerformed = true;
                result.DeepScanReferenceCount = referencesByAsset.TryGetValue(result.Path, out HashSet<string> references)
                    ? references.Count
                    : 0;
                result.BuildSettingsSceneReferenceCount =
                    buildSceneReferencesByAsset.TryGetValue(result.Path, out HashSet<string> buildSceneReferences)
                        ? buildSceneReferences.Count
                        : 0;

                if (references != null && references.Count > 0)
                {
                    result.Status = OrphanStatus.IndirectlyReferenced;
                    result.Notes = BuildDeepScanReferenceNote(references, result.BuildSettingsSceneReferenceCount);
                    continue;
                }

                if (result.RuntimeUsage != RuntimeUsageFlags.None)
                {
                    result.Status = OrphanStatus.RuntimeReferenced;
                    result.Notes =
                        $"Unity dependency scan found no serialized dependents, but this asset is still exposed through {BuildRuntimeUsageLabel(result)}.";
                    continue;
                }

                result.Status = OrphanStatus.VerifiedOrphan;
                result.Notes = "Unity dependency scan found no serialized dependents for this asset.";
            }

            _scanStatus = $"Deep Scan complete for {selectedResults.Count} selected asset(s).";
            Repaint();
        }

        private void MoveSelectedAssets()
        {
            List<OrphanAssetResult> selectedResults = GetSelectedResults();
            if (selectedResults.Count == 0)
            {
                return;
            }

            if (!TryNormalizeProjectFolder(_moveTargetFolder, out string normalizedTargetFolder, out string folderError))
            {
                EditorUtility.DisplayDialog("Invalid Move Target Folder", folderError, "OK");
                return;
            }

            _moveTargetFolder = normalizedTargetFolder;

            bool confirmed = EditorUtility.DisplayDialog(
                "Move Selected Assets",
                $"Move {selectedResults.Count} selected assets to {normalizedTargetFolder}?",
                "Move",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            Dictionary<string, string> movedPaths = new(StringComparer.OrdinalIgnoreCase);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (OrphanAssetResult result in selectedResults)
                {
                    string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                        $"{normalizedTargetFolder}/{Path.GetFileName(result.Path)}");
                    string error = AssetDatabase.MoveAsset(result.Path, targetPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning($"Could not move asset {result.Path}: {error}");
                        continue;
                    }

                    movedPaths[result.Path] = targetPath;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            if (movedPaths.Count == 0)
            {
                _scanStatus = "No selected assets were moved.";
                return;
            }

            foreach (OrphanAssetResult result in _results)
            {
                if (!movedPaths.TryGetValue(result.Path, out string newPath))
                {
                    continue;
                }

                _selectedAssetPaths.Remove(result.Path);
                result.Path = newPath;
                result.Asset = AssetDatabase.LoadMainAssetAtPath(newPath);
                result.Name = result.Asset != null ? result.Asset.name : Path.GetFileNameWithoutExtension(newPath);
                result.EstimatedSizeBytes = GetEstimatedSizeBytes(newPath);
                _selectedAssetPaths.Add(newPath);
            }

            _scanStatus = $"Moved {movedPaths.Count} assets to {normalizedTargetFolder}.";
            PruneSelectionOfIgnoredAssets();
        }

        private List<OrphanAssetResult> GetSelectedResults()
        {
            return _results
                .Where(result => _selectedAssetPaths.Contains(result.Path))
                .ToList();
        }

        private static int CountExternalReferences(
            string assetPath,
            IEnumerable<string> references,
            string rootFolder,
            ScanReferenceMode scanReferenceMode,
            IReadOnlyCollection<string> ignoredFolders)
        {
            if (references == null)
            {
                return 0;
            }

            string metaPath = $"{assetPath}.meta";
            return references
                .Where(reference => !string.IsNullOrEmpty(reference))
                .Select(NormalizePath)
                .Where(reference => scanReferenceMode == ScanReferenceMode.ScanWholeProject ||
                                    IsPathUnderFolder(reference, rootFolder))
                .Where(reference => !IsPathIgnored(reference, ignoredFolders))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(reference =>
                    !string.Equals(reference, assetPath, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(reference, metaPath, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldIncludeDeepScanRoot(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                   !AssetDatabase.IsValidFolder(assetPath) &&
                   !assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> GetEnabledBuildSettingsScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => NormalizePath(scene.path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildReferencePreview(IReadOnlyList<string> references)
        {
            const int previewCount = 3;
            if (references == null || references.Count == 0)
            {
                return "none";
            }

            List<string> distinctReferences = references
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctReferences.Count <= previewCount)
            {
                return string.Join(", ", distinctReferences);
            }

            return $"{string.Join(", ", distinctReferences.Take(previewCount))} and {distinctReferences.Count - previewCount} more";
        }

        private static string BuildDeepScanReferenceNote(IEnumerable<string> references, int buildSettingsSceneReferenceCount)
        {
            List<string> distinctReferences = references
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string baseNote =
                $"Unity dependency scan found {distinctReferences.Count} referencing asset(s): {BuildReferencePreview(distinctReferences)}.";

            if (buildSettingsSceneReferenceCount <= 0)
            {
                return baseNote;
            }

            return
                $"{baseNote} {buildSettingsSceneReferenceCount} reference(s) come from enabled Scenes In Build Settings.";
        }

        private OrphanAssetResult BuildResult(
            string assetPath,
            string guid,
            HashSet<string> addressableGuids,
            ScanReferenceMode scanReferenceMode)
        {
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            RuntimeUsageFlags runtimeUsage = GetRuntimeUsage(assetPath, guid, addressableGuids, out string assetBundleName);
            AssetCategoryFilter category = GetAssetCategory(assetType, assetPath, asset);

            return new OrphanAssetResult
            {
                Guid = guid,
                Name = asset != null ? asset.name : Path.GetFileNameWithoutExtension(assetPath),
                Path = assetPath,
                TypeName = assetType != null ? assetType.Name : "<Unknown>",
                Category = category,
                AssetBundleName = assetBundleName,
                RuntimeUsage = runtimeUsage,
                Status = GetInitialStatus(runtimeUsage, scanReferenceMode),
                Notes = BuildNotes(runtimeUsage, assetBundleName, scanReferenceMode),
                EstimatedSizeBytes = GetEstimatedSizeBytes(assetPath),
                DeepScanPerformed = false,
                DeepScanReferenceCount = 0,
                BuildSettingsSceneReferenceCount = 0,
                Asset = asset
            };
        }

        private static RuntimeUsageFlags GetRuntimeUsage(
            string assetPath,
            string guid,
            HashSet<string> addressableGuids,
            out string assetBundleName)
        {
            RuntimeUsageFlags runtimeUsage = RuntimeUsageFlags.None;

            if (IsInResourcesFolder(assetPath))
            {
                runtimeUsage |= RuntimeUsageFlags.Resources;
            }

            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            assetBundleName = importer != null ? importer.assetBundleName : string.Empty;
            if (!string.IsNullOrWhiteSpace(assetBundleName))
            {
                runtimeUsage |= RuntimeUsageFlags.AssetBundle;
            }

            if (addressableGuids.Contains(guid))
            {
                runtimeUsage |= RuntimeUsageFlags.Addressables;
            }

            return runtimeUsage;
        }

        private static OrphanStatus GetInitialStatus(RuntimeUsageFlags runtimeUsage, ScanReferenceMode scanReferenceMode)
        {
            if (runtimeUsage != RuntimeUsageFlags.None)
            {
                return OrphanStatus.RuntimeReferenced;
            }

            return scanReferenceMode == ScanReferenceMode.ScanWholeProject
                ? OrphanStatus.ProbablyOrphan
                : OrphanStatus.Unknown;
        }

        private static string BuildNotes(
            RuntimeUsageFlags runtimeUsage,
            string assetBundleName,
            ScanReferenceMode scanReferenceMode)
        {
            if (runtimeUsage == RuntimeUsageFlags.None)
            {
                return scanReferenceMode == ScanReferenceMode.ScanWholeProject
                    ? "No serialized references were found by AssetGuidMapper across the whole project."
                    : "No serialized references were found by AssetGuidMapper inside the selected scope. References may still exist outside the scope.";
            }

            List<string> sources = new();

            if (HasFlag(runtimeUsage, RuntimeUsageFlags.Resources))
            {
                sources.Add("Resources");
            }

            if (HasFlag(runtimeUsage, RuntimeUsageFlags.AssetBundle))
            {
                sources.Add(string.IsNullOrWhiteSpace(assetBundleName)
                    ? "AssetBundle"
                    : $"AssetBundle ({assetBundleName})");
            }

            if (HasFlag(runtimeUsage, RuntimeUsageFlags.Addressables))
            {
                sources.Add("Addressables");
            }

            string scopeSuffix = scanReferenceMode == ScanReferenceMode.ScanWholeProject
                ? "across the whole project"
                : "inside the selected scope";

            return $"No direct serialized references were found {scopeSuffix}. The asset may still be loaded dynamically through: {string.Join(", ", sources)}.";
        }

        private static string BuildRuntimeUsageLabel(OrphanAssetResult result)
        {
            if (result.RuntimeUsage == RuntimeUsageFlags.None)
            {
                return "None";
            }

            List<string> labels = new();

            if (HasFlag(result.RuntimeUsage, RuntimeUsageFlags.Resources))
            {
                labels.Add("Resources");
            }

            if (HasFlag(result.RuntimeUsage, RuntimeUsageFlags.AssetBundle))
            {
                labels.Add(string.IsNullOrWhiteSpace(result.AssetBundleName)
                    ? "AssetBundle"
                    : $"AssetBundle: {result.AssetBundleName}");
            }

            if (HasFlag(result.RuntimeUsage, RuntimeUsageFlags.Addressables))
            {
                labels.Add("Addressables");
            }

            return string.Join(" | ", labels);
        }

        private static AssetCategoryFilter GetAssetCategory(Type assetType, string assetPath, UnityEngine.Object asset)
        {
            if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return AssetCategoryFilter.Prefab;
            }

            if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return AssetCategoryFilter.Scene;
            }

            if (asset is ScriptableObject)
            {
                return AssetCategoryFilter.ScriptableObject;
            }

            if (asset is Sprite)
            {
                return AssetCategoryFilter.Sprite;
            }

            if (asset is Texture || assetType == typeof(Texture2D))
            {
                return AssetCategoryFilter.Texture;
            }

            if (asset is Material)
            {
                return AssetCategoryFilter.Material;
            }

            if (asset is AudioClip)
            {
                return AssetCategoryFilter.AudioClip;
            }

            if (asset is AnimationClip)
            {
                return AssetCategoryFilter.AnimationClip;
            }

            if (assetPath.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
            {
                return AssetCategoryFilter.AnimatorController;
            }

            if (asset is Font)
            {
                return AssetCategoryFilter.Font;
            }

            if (assetPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || asset is Shader)
            {
                return AssetCategoryFilter.Shader;
            }

            if (assetType != null && assetType.Name.IndexOf("VideoClip", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AssetCategoryFilter.VideoClip;
            }

            string extension = Path.GetExtension(assetPath);
            if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".dae", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".blend", StringComparison.OrdinalIgnoreCase))
            {
                return AssetCategoryFilter.Model;
            }

            return AssetCategoryFilter.Other;
        }

        private static bool ShouldAnalyzeAsset(
            string assetPath,
            string rootFolder,
            IReadOnlyCollection<string> ignoredFolders = null)
        {
            if (string.IsNullOrWhiteSpace(assetPath) ||
                !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.IsValidFolder(assetPath) ||
                !IsPathUnderFolder(assetPath, rootFolder) ||
                IsPathIgnored(assetPath, ignoredFolders))
            {
                return false;
            }

            string extension = Path.GetExtension(assetPath);
            if (IgnoredExtensions.Contains(extension))
            {
                return false;
            }

            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            return assetType != typeof(MonoScript);
        }

        private static bool IsInResourcesFolder(string assetPath)
        {
            return assetPath.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<string> CollectAddressableGuids()
        {
            HashSet<string> guids = new(StringComparer.OrdinalIgnoreCase);

            Type settingsDefaultObjectType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultObjectType == null)
            {
                return guids;
            }

            PropertyInfo settingsProperty = settingsDefaultObjectType.GetProperty(
                "Settings",
                BindingFlags.Public | BindingFlags.Static);
            object settings = settingsProperty?.GetValue(null);
            if (settings == null)
            {
                return guids;
            }

            PropertyInfo groupsProperty = settings.GetType().GetProperty("groups", BindingFlags.Public | BindingFlags.Instance);
            if (!(groupsProperty?.GetValue(settings) is IEnumerable groups))
            {
                return guids;
            }

            foreach (object group in groups)
            {
                if (group == null)
                {
                    continue;
                }

                PropertyInfo entriesProperty = group.GetType().GetProperty("entries", BindingFlags.Public | BindingFlags.Instance);
                if (!(entriesProperty?.GetValue(group) is IEnumerable entries))
                {
                    continue;
                }

                foreach (object entry in entries)
                {
                    AddAddressableEntryGuids(guids, entry);
                }
            }

            return guids;
        }

        private static void AddAddressableEntryGuids(HashSet<string> guids, object entry)
        {
            if (entry == null)
            {
                return;
            }

            Type entryType = entry.GetType();
            string guid = entryType.GetProperty("guid", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) as string;
            if (!string.IsNullOrWhiteSpace(guid))
            {
                guids.Add(guid);
            }

            string assetPath = entryType.GetProperty("AssetPath", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) as string;
            object isFolderValue = entryType.GetProperty("IsFolder", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry);
            bool isFolder = isFolderValue is bool folderValue && folderValue;

            if (!isFolder || string.IsNullOrWhiteSpace(assetPath) || !AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            foreach (string childGuid in AssetDatabase.FindAssets(string.Empty, new[] { assetPath }))
            {
                string childPath = AssetDatabase.GUIDToAssetPath(childGuid);
                if (ShouldAnalyzeAsset(childPath, assetPath))
                {
                    guids.Add(childGuid);
                }
            }
        }

        private static long GetEstimatedSizeBytes(string assetPath)
        {
            long size = 0L;

            string absoluteAssetPath = ToAbsolutePath(assetPath);
            if (File.Exists(absoluteAssetPath))
            {
                size += new FileInfo(absoluteAssetPath).Length;
            }

            string metaPath = $"{absoluteAssetPath}.meta";
            if (File.Exists(metaPath))
            {
                size += new FileInfo(metaPath).Length;
            }

            return size;
        }

        private static string FormatBytes(long sizeInBytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = sizeInBytes;
            int unitIndex = 0;

            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
        }

        private static void FocusAsset(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasFlag(RuntimeUsageFlags value, RuntimeUsageFlags flag)
        {
            return (value & flag) == flag;
        }

        private static string GetDisplayCategoryLabel(OrphanAssetResult result)
        {
            return result.Category switch
            {
                AssetCategoryFilter.AudioClip => "AudioClip",
                AssetCategoryFilter.AnimationClip => "AnimationClip",
                AssetCategoryFilter.AnimatorController => "AnimatorController",
                AssetCategoryFilter.ScriptableObject => "ScriptableObject",
                AssetCategoryFilter.VideoClip => "VideoClip",
                _ => result.Category.ToString()
            };
        }

        private static string GetDisplayStatusLabel(OrphanStatus status)
        {
            return status switch
            {
                OrphanStatus.ProbablyOrphan => "Probably Orphan",
                OrphanStatus.VerifiedOrphan => "Verified Orphan",
                OrphanStatus.RuntimeReferenced => "Runtime-Referenced",
                OrphanStatus.IndirectlyReferenced => "Indirectly Referenced",
                _ => "Unknown"
            };
        }

        private static int GetStatusSortValue(OrphanStatus status)
        {
            return status switch
            {
                OrphanStatus.VerifiedOrphan => 0,
                OrphanStatus.ProbablyOrphan => 1,
                OrphanStatus.Unknown => 2,
                OrphanStatus.RuntimeReferenced => 3,
                OrphanStatus.IndirectlyReferenced => 4,
                _ => 5
            };
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static bool IsPathIgnored(string assetPath, IEnumerable<string> ignoredFolders)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || ignoredFolders == null)
            {
                return false;
            }

            string normalizedPath = NormalizePath(assetPath);
            foreach (string ignoredFolder in ignoredFolders)
            {
                if (string.IsNullOrWhiteSpace(ignoredFolder))
                {
                    continue;
                }

                if (IsPathUnderFolder(normalizedPath, NormalizePath(ignoredFolder)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsIgnoredAssetPath(string assetPath)
        {
            return IsPathIgnored(assetPath, _ignoredFolders);
        }

        private static bool IsPathUnderFolder(string assetPath, string folderPath)
        {
            if (string.Equals(folderPath, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
            }

            string folderPrefix = folderPath.EndsWith("/", StringComparison.Ordinal) ? folderPath : $"{folderPath}/";
            return string.Equals(assetPath, folderPath, StringComparison.OrdinalIgnoreCase) ||
                   assetPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private void DrawIgnoredFoldersUi()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _ignoredFolderDraft = EditorGUILayout.TextField("Folder", _ignoredFolderDraft);

                    if (GUILayout.Button("Use Selected Folder", GUILayout.Width(160f)))
                    {
                        AssignFolderFromSelection(ref _ignoredFolderDraft);
                    }

                    if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                    {
                        BrowseForProjectFolder(ref _ignoredFolderDraft);
                    }

                    if (GUILayout.Button("Add", GUILayout.Width(80f)))
                    {
                        TryAddIgnoredFolder(_ignoredFolderDraft);
                    }

                    using (new EditorGUI.DisabledScope(_ignoredFolders.Count == 0))
                    {
                        if (GUILayout.Button("Clear All", GUILayout.Width(90f)))
                        {
                            _ignoredFolders.Clear();
                            PruneSelectionOfIgnoredAssets();
                        }
                    }
                }

                if (_ignoredFolders.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        "No ignored folders configured. Assets inside ignored folders are excluded from scan results, searches, filters, and Deep Scan roots.",
                        _mutedMiniLabelStyle);
                    return;
                }

                for (int i = 0; i < _ignoredFolders.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(_ignoredFolders[i], _pathLabelStyle);

                        if (GUILayout.Button("Remove", GUILayout.Width(80f)))
                        {
                            _ignoredFolders.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        private void TryAddIgnoredFolder(string folderPath)
        {
            if (!TryNormalizeProjectFolder(folderPath, out string normalizedFolder, out string folderError))
            {
                EditorUtility.DisplayDialog("Invalid Ignored Folder", folderError, "OK");
                return;
            }

            if (_ignoredFolders.Any(existing =>
                    string.Equals(existing, normalizedFolder, StringComparison.OrdinalIgnoreCase)))
            {
                _ignoredFolderDraft = normalizedFolder;
                return;
            }

            _ignoredFolders.Add(normalizedFolder);
            _ignoredFolders.Sort(StringComparer.OrdinalIgnoreCase);
            _ignoredFolderDraft = normalizedFolder;
            PruneSelectionOfIgnoredAssets();
        }

        private List<string> GetIgnoredFoldersSnapshot()
        {
            return _ignoredFolders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void PruneSelectionOfIgnoredAssets()
        {
            _selectedAssetPaths.RemoveWhere(IsIgnoredAssetPath);
        }

        private static bool TryNormalizeProjectFolder(string input, out string normalizedFolder, out string error)
        {
            normalizedFolder = string.Empty;
            error = string.Empty;

            string value = string.IsNullOrWhiteSpace(input) ? "Assets" : input.Trim();
            value = NormalizePath(value);

            if (Path.IsPathRooted(value))
            {
                string projectRoot = NormalizePath(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty);
                if (!value.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    error = "The folder must be inside the current Unity project.";
                    return false;
                }

                value = value.Substring(projectRoot.Length).TrimStart('/');
                value = string.IsNullOrEmpty(value) ? "Assets" : value;
            }

            if (!value.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                value = $"Assets/{value.TrimStart('/')}";
            }

            value = value.TrimEnd('/');

            if (!AssetDatabase.IsValidFolder(value))
            {
                error = $"Folder not found in project: {value}";
                return false;
            }

            normalizedFolder = value;
            return true;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, assetPath);
        }

        private static void AssignFolderFromSelection(ref string folderField)
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(selectedPath))
            {
                folderField = selectedPath;
                return;
            }

            string parentFolder = Path.GetDirectoryName(selectedPath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                folderField = NormalizePath(parentFolder);
            }
        }

        private static void BrowseForProjectFolder(ref string folderField)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string startingFolder = TryNormalizeProjectFolder(folderField, out string normalizedFolder, out _)
                ? ToAbsolutePath(normalizedFolder)
                : Application.dataPath;

            string selectedFolder = EditorUtility.OpenFolderPanel("Select Folder", startingFolder, string.Empty);
            if (string.IsNullOrEmpty(selectedFolder))
            {
                return;
            }

            string normalizedSelectedFolder = NormalizePath(selectedFolder);
            string normalizedProjectRoot = NormalizePath(projectRoot);
            if (!normalizedSelectedFolder.StartsWith(normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "The selected folder must be inside the current Unity project.", "OK");
                return;
            }

            string relativePath = normalizedSelectedFolder.Substring(normalizedProjectRoot.Length).TrimStart('/');
            folderField = string.IsNullOrEmpty(relativePath) ? "Assets" : relativePath;
        }

        private void EnsureStyles()
        {
            if (_heroTitleStyle != null)
            {
                return;
            }

            _heroTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                fixedHeight = 24f
            };

            _heroBodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                richText = false
            };

            _sectionStyle = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(12, 12, 10, 12),
                margin = new RectOffset(6, 6, 0, 0)
            };

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };

            _sectionBodyStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10
            };

            _statValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft
            };

            _statLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            };

            _headerButtonStyle = new GUIStyle(EditorStyles.miniButtonMid)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 3, 3)
            };

            _pathLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                clipping = TextClipping.Clip
            };

            _notesLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                clipping = TextClipping.Clip
            };

            _chipStyle = new GUIStyle(EditorStyles.miniButton)
            {
                padding = new RectOffset(10, 10, 5, 5),
                margin = new RectOffset(0, 6, 0, 6)
            };

                _selectedChipStyle = new GUIStyle(_chipStyle)
            {
                fontStyle = FontStyle.Bold
            };

            _statusPillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 4, 4),
                fontStyle = FontStyle.Bold
            };

            _mutedMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };

            _dangerButtonStyle = new GUIStyle(EditorStyles.miniButtonMid)
            {
                fontStyle = FontStyle.Bold
            };
        }

        private void BeginSection(string title)
        {
            EditorGUILayout.BeginVertical(_sectionStyle);
            EditorGUILayout.LabelField(title, _sectionTitleStyle);
            EditorGUILayout.Space(4f);
        }

        private static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        private void DrawAssetCategoryFilters()
        {
            Dictionary<AssetCategoryFilter, int> categoryCounts = GetCategoryCounts();
            AssetCategoryFilter[] orderedCategories =
            {
                AssetCategoryFilter.Prefab,
                AssetCategoryFilter.Scene,
                AssetCategoryFilter.ScriptableObject,
                AssetCategoryFilter.Texture,
                AssetCategoryFilter.Sprite,
                AssetCategoryFilter.Material,
                AssetCategoryFilter.AudioClip,
                AssetCategoryFilter.AnimationClip,
                AssetCategoryFilter.AnimatorController,
                AssetCategoryFilter.Model,
                AssetCategoryFilter.Font,
                AssetCategoryFilter.VideoClip,
                AssetCategoryFilter.Shader,
                AssetCategoryFilter.Other
            };

            using (new EditorGUILayout.HorizontalScope())
            {
                bool noFilterActive = _assetCategoryFilter == AssetCategoryFilter.None;
                if (GUILayout.Button(
                        noFilterActive ? "No Filter Active" : "Category Filter Active",
                        noFilterActive ? _selectedChipStyle : _chipStyle,
                        GUILayout.Width(150f)))
                {
                    _assetCategoryFilter = AssetCategoryFilter.None;
                }

                if (GUILayout.Button("Select All", _chipStyle, GUILayout.Width(90f)))
                {
                    _assetCategoryFilter = AssetCategoryFilter.All;
                }

                if (GUILayout.Button("Clear", _chipStyle, GUILayout.Width(70f)))
                {
                    _assetCategoryFilter = AssetCategoryFilter.None;
                }

                GUILayout.Space(8f);
                EditorGUILayout.LabelField(
                    noFilterActive ? "Showing every asset category." : "Showing only the selected categories.",
                    _mutedMiniLabelStyle);
            }

            int columns = Mathf.Max(2, Mathf.FloorToInt((position.width - 80f) / 120f));
            for (int i = 0; i < orderedCategories.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + columns && j < orderedCategories.Length; j++)
                    {
                        AssetCategoryFilter category = orderedCategories[j];
                        categoryCounts.TryGetValue(category, out int count);
                        DrawCategoryChip(category, $"{GetFriendlyCategoryName(category)} ({count})");
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        private Dictionary<AssetCategoryFilter, int> GetCategoryCounts()
        {
            Dictionary<AssetCategoryFilter, int> counts = new();
            foreach (OrphanAssetResult result in GetFilteredResultsWithoutCategoryFilter())
            {
                counts.TryGetValue(result.Category, out int currentCount);
                counts[result.Category] = currentCount + 1;
            }

            return counts;
        }

        private void DrawCategoryChip(AssetCategoryFilter category, string label)
        {
            bool isSelected = (_assetCategoryFilter & category) != 0;
            GUIStyle style = isSelected ? _selectedChipStyle : _chipStyle;
            string buttonLabel = isSelected ? $"● {label}" : $"○ {label}";

            Color previousBackground = GUI.backgroundColor;
            Color previousContent = GUI.contentColor;
            GUI.backgroundColor = isSelected
                ? new Color(0.24f, 0.62f, 0.42f, 1f)
                : new Color(0.23f, 0.23f, 0.23f, 1f);
            GUI.contentColor = isSelected ? Color.white : new Color(0.82f, 0.84f, 0.88f, 1f);

            if (GUILayout.Button(buttonLabel, style, GUILayout.MinWidth(108f)))
            {
                if (isSelected)
                {
                    _assetCategoryFilter &= ~category;
                }
                else
                {
                    _assetCategoryFilter |= category;
                }
            }

            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private static string GetFriendlyCategoryName(AssetCategoryFilter category)
        {
            return category switch
            {
                AssetCategoryFilter.AudioClip => "AudioClip",
                AssetCategoryFilter.AnimationClip => "Animation",
                AssetCategoryFilter.AnimatorController => "Controller",
                AssetCategoryFilter.ScriptableObject => "ScriptableObject",
                AssetCategoryFilter.VideoClip => "VideoClip",
                _ => category.ToString()
            };
        }

        private void DrawStatCard(string label, string value, Color tint)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 58f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, tint);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            Rect contentRect = new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, rect.height - 14f);
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 24f), value, _statValueStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 28f, contentRect.width, 18f), label, _statLabelStyle);
        }

        private void DrawStatusPill(string text, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, _statusPillStyle, options);
            Color background = GetStatusColor(text);
            EditorGUI.DrawRect(rect, background);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(rect, text, _statusPillStyle);
        }

        private static Color GetStatusColor(string text)
        {
            return text switch
            {
                "Verified Orphan" => new Color(0.10f, 0.52f, 0.29f, 0.32f),
                "Probably Orphan" => new Color(0.16f, 0.62f, 0.42f, 0.28f),
                "Runtime-Referenced" => new Color(0.82f, 0.56f, 0.17f, 0.28f),
                "Indirectly Referenced" => new Color(0.74f, 0.36f, 0.20f, 0.28f),
                _ => new Color(0.45f, 0.48f, 0.55f, 0.28f)
            };
        }
    }
}
