using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderWindow : EditorWindow
    {
        private AssetUsageFinderController _controller;
        private Vector2 _resultsScroll;

        // -------- Top-level UX navigation --------

        private enum MainTab
        {
            Finder = 0,
            Replace = 1
        }

        private enum FinderMode
        {
            ByAsset = 0,
            BySerializedField = 1
        }

        private enum ReplaceMode
        {
            PrefabOrVariant = 0,
            Component = 1,
            SerializedFieldValue = 2
        }

        private MainTab _mainTab = MainTab.Finder;
        private FinderMode _finderMode = FinderMode.ByAsset;
        private ReplaceMode _replaceMode = ReplaceMode.PrefabOrVariant;

        // -------- Serialized Field query UX state (VIEW-ONLY) --------

        private readonly List<SerializedFieldFilterRow> _finderFilters = new();
        private Vector2 _finderFiltersScroll;

        // Replace serialized-field query state (reuse same UI)
        private readonly List<SerializedFieldFilterRow> _replaceFilters = new();
        private Vector2 _replaceFiltersScroll;

        private readonly SerializedFieldValueBox _replaceWithValue = new();

        // -------- Replace (other modes) UX state --------

        // Prefab/Variant replace
        private Object _replaceFromPrefab;
        private Object _replaceToPrefab;
        private bool _replaceIncludeVariants = true;
        private bool _replaceKeepOverrides = true;
        private bool _replaceCopyCommonRootComponentValues;

        // Component replace
        private string _replaceComponentFromTypeName = string.Empty;
        private string _replaceComponentToTypeName = string.Empty;
        private string _replaceComponentFromTypeQuery = string.Empty;
        private string _replaceComponentToTypeQuery = string.Empty;
        private bool _replaceCopySerializedValues = true;
        private bool _replaceDisableOldComponentInsteadOfRemove = false;

        // General
        private bool _showAdvanced = false;

        private static List<Type> _componentReplaceTypesCache;

        // -------- Menu --------

        [MenuItem("Tools/LegendaryTools/Assets/Asset Usage Finder")]
        public static void ShowWindow()
        {
            AssetUsageFinderWindow window = GetWindow<AssetUsageFinderWindow>("Asset Usage Finder");
            window.minSize = new Vector2(940, 560);
            window.Show();
        }

        private void OnEnable()
        {
            _controller ??= CreateController();
            _controller.StateChanged -= OnStateChanged;
            _controller.StateChanged += OnStateChanged;

            _controller.Initialize();

            EnsureFilterListHasOneRow(_finderFilters);
            EnsureFilterListHasOneRow(_replaceFilters);
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.StateChanged -= OnStateChanged;
                _controller.CancelSerializedFieldFinder();
            }
        }

        private void OnStateChanged()
        {
            Repaint();
        }

        private static AssetUsageFinderController CreateController()
        {
            string jsonPath = Path.Combine("Library", "AssetUsageFinderMapping.json");
            AssetUsageFinderCache cache = new();
            AssetUsageFinderUsageScanner usageScanner = new(cache);

            AssetGuidMapper guidMapper = new();
            AssetUsageFinderSerializedFieldFinderBackend serializedFieldBackend = new();
            AssetUsageFinderSerializedFieldValueReplaceBackend serializedFieldValueReplaceBackend = new();
            AssetUsageFinderPrefabOrVariantReplaceBackend prefabOrVariantReplaceBackend = new();
            AssetUsageFinderComponentReplaceBackend componentReplaceBackend = new();

            return new AssetUsageFinderController(
                guidMapper,
                usageScanner,
                jsonPath,
                serializedFieldBackend,
                serializedFieldValueReplaceBackend,
                prefabOrVariantReplaceBackend,
                componentReplaceBackend);
        }

        // -------- GUI --------

        private void OnGUI()
        {
            DrawTopBar();

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawRightPanel();
            }
        }

        private void DrawTopBar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent title = new("Asset Usage Finder", EditorGUIUtility.IconContent("d_Search Icon").image);
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    _showAdvanced = GUILayout.Toggle(_showAdvanced, "Advanced", EditorStyles.miniButton,
                        GUILayout.Width(90));

                    if (GUILayout.Button(
                            new GUIContent("Clear Cache", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                            GUILayout.Height(22), GUILayout.Width(110)))
                        _controller.ClearCache();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _mainTab = (MainTab)GUILayout.Toolbar((int)_mainTab, new[] { "Finder", "Replace" },
                        GUILayout.Height(24));
                }
            }
        }

        private void DrawLeftPanel()
        {
            AssetUsageFinderState state = _controller.State;

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(380)))
            {
                DrawModeCard();

                EditorGUILayout.Space(8);

                if (_mainTab == MainTab.Finder)
                {
                    if (_finderMode == FinderMode.ByAsset)
                        DrawFinderByAssetCard(state);
                    else
                    {
                        DrawSerializedFieldQueryCard(
                            "Finder • By Serialized Field",
                            _finderFilters,
                            ref _finderFiltersScroll,
                            false,
                            null);

                        DrawSerializedFieldFinderActions(state);
                    }
                }
                else
                    DrawReplaceCard(state);

                EditorGUILayout.Space(8);
                DrawScopeCard(state);

                if (_showAdvanced)
                {
                    EditorGUILayout.Space(8);
                    DrawAdvancedCard(state);
                }
            }
        }

        private void DrawRightPanel()
        {
            AssetUsageFinderState state = _controller.State;

            using (new EditorGUILayout.VerticalScope())
            {
                if (_mainTab == MainTab.Finder)
                {
                    if (_finderMode == FinderMode.ByAsset)
                        DrawFinderResults(state);
                    else
                        DrawSerializedFieldFinderResults(state);
                }
                else
                    DrawReplacePreview(state);
            }
        }

        // -------- Mode / common cards --------

        private void DrawModeCard()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Mode", EditorStyles.boldLabel);

                if (_mainTab == MainTab.Finder)
                    _finderMode = (FinderMode)EditorGUILayout.EnumPopup("Finder", _finderMode);
                else
                    _replaceMode = (ReplaceMode)EditorGUILayout.EnumPopup("Replace", _replaceMode);
            }
        }

        private void DrawScopeCard(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scope", EditorStyles.boldLabel);

                AssetUsageFinderSearchScope scope = state.SearchScope;

                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.ProjectScenes, "Scenes (Project)");
                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.OpenScene, "Current Open Scene");
                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.ProjectPrefabs, "Prefabs (Project)");
                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.OpenPrefab, "Current Open Prefab (Prefab Mode)");
                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.Materials, "Materials");
                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.ScriptableObjects, "ScriptableObjects");
                DrawScopeToggle(ref scope, AssetUsageFinderSearchScope.OtherAssets, "Other");

                state.SearchScope = scope;

                bool requiresHierarchyScopes = _mainTab == MainTab.Replace &&
                                               _replaceMode != ReplaceMode.SerializedFieldValue;

                EditorGUILayout.HelpBox(
                    !AssetUsageFinderSearchScopeUtility.HasAnySelection(scope)
                        ? "No scope selected. Find and Replace will require at least one scope before running."
                        : requiresHierarchyScopes
                            ? "This replace mode only operates on scenes and prefabs. Asset-only toggles are ignored."
                            : "This selection is shared by Find and Replace. You can enable one, many, or none.",
                    !AssetUsageFinderSearchScopeUtility.HasAnySelection(scope)
                        ? MessageType.Warning
                        : MessageType.None);
            }
        }

        private static void DrawScopeToggle(
            ref AssetUsageFinderSearchScope scope,
            AssetUsageFinderSearchScope flag,
            string label)
        {
            bool enabled = scope.HasFlag(flag);
            bool next = EditorGUILayout.ToggleLeft(label, enabled);

            if (next)
                scope |= flag;
            else
                scope &= ~flag;
        }

        private void DrawAdvancedCard(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);

                EditorGUILayout.HelpBox(
                    "Planned: scan scope presets, ignore folders, GUID cache management, export reports, and safety checks before Replace.",
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(true);
                    GUILayout.Button("Scope Presets", GUILayout.Height(24));
                    GUILayout.Button("Ignore Folders…", GUILayout.Height(24));
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Entries", GUILayout.Width(60));
                    EditorGUILayout.LabelField(state.Entries != null ? state.Entries.Count.ToString() : "0");
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Busy", GUILayout.Width(40));
                    EditorGUILayout.LabelField(state.IsBusy ? "Yes" : "No", GUILayout.Width(40));
                }
            }
        }

        // -------- Finder: By Asset --------

        private void DrawFinderByAssetCard(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Finder • By Asset", EditorStyles.boldLabel);

                using (EditorGUI.ChangeCheckScope change = new())
                {
                    Object newTarget =
                        EditorGUILayout.ObjectField("Target Asset", state.TargetAsset, typeof(Object), false);
                    if (change.changed)
                        _controller.SetTarget(newTarget);
                }

                EditorGUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(state.IsBusy);
                    if (GUILayout.Button(
                            new GUIContent("Find Usages", EditorGUIUtility.IconContent("d_Search Icon").image),
                            GUILayout.Height(28)))
                        _controller.FindUsagesAsync();
                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button(new GUIContent("Ping", EditorGUIUtility.IconContent("d_Ping").image),
                            GUILayout.Height(28), GUILayout.Width(90)))
                    {
                        if (state.TargetAsset != null)
                            EditorGUIUtility.PingObject(state.TargetAsset);
                    }
                }

                if (!string.IsNullOrEmpty(state.StatusMessage))
                    EditorGUILayout.HelpBox(state.StatusMessage, MessageType.Info);

                if (state.TargetAsset == null)
                {
                    EditorGUILayout.HelpBox("Select an asset to find references across the selected scope.",
                        MessageType.None);
                }
            }
        }

        // -------- Finder/Replace: Serialized Field Query (Shared UI) --------

        private void DrawSerializedFieldQueryCard(
            string title,
            List<SerializedFieldFilterRow> filters,
            ref Vector2 refFiltersScroll,
            bool showReplaceWith,
            SerializedFieldValueBox replaceWithValue)
        {
            EnsureFilterListHasOneRow(filters);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                EditorGUILayout.HelpBox(
                    "Scan serialized fields across the selected scope.\n" +
                    "• Multiple filters can be combined using AND / OR\n" +
                    "• Value is type-aware (UnityObject, numeric, enum, bool, etc.)\n" +
                    "• For Arrays/Lists, comparison is forced to Contains.",
                    MessageType.Info);

                EditorGUILayout.Space(6);

                DrawFilterHeaderActions(filters);

                float listHeight = Mathf.Clamp(position.height * 0.34f, 160f, 260f);
                using (EditorGUILayout.ScrollViewScope scroll = new(refFiltersScroll, GUILayout.Height(listHeight)))
                {
                    refFiltersScroll = scroll.scrollPosition;

                    for (int i = 0; i < filters.Count; i++)
                    {
                        filters[i].EnsureDefaults();
                        DrawFilterRow(filters, i);
                        GUILayout.Space(4);
                    }
                }

                EditorGUILayout.Space(6);

                if (showReplaceWith && replaceWithValue != null)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("Replace With", EditorStyles.miniBoldLabel);

                        Type inferred = InferReplaceWithTypeFromFilters(filters);
                        DrawTypedValueField(
                            inferred != null ? $"Value ({inferred.Name})" : "Value",
                            inferred ?? typeof(string),
                            replaceWithValue);

                        EditorGUILayout.HelpBox(
                            inferred != null
                                ? "Replace value type is inferred from the first filter row."
                                : "No filter type selected; defaulting to string.",
                            MessageType.None);
                    }
                }
            }
        }

        private void DrawSerializedFieldFinderActions(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(state.SerializedFieldFinderIsBusy);
                    if (GUILayout.Button(
                            new GUIContent("Scan Scope", EditorGUIUtility.IconContent("d_Search Icon").image),
                            GUILayout.Height(30)))
                    {
                        // VIEW delegates to backend through controller.
                        _controller.FindBySerializedField(_finderFilters);
                    }

                    EditorGUI.EndDisabledGroup();

                    EditorGUI.BeginDisabledGroup(!state.SerializedFieldFinderIsBusy);
                    if (GUILayout.Button(
                            new GUIContent("Cancel", EditorGUIUtility.IconContent("d_winbtn_mac_close").image),
                            GUILayout.Height(30), GUILayout.Width(120)))
                        _controller.CancelSerializedFieldFinder();
                    EditorGUI.EndDisabledGroup();
                }

                if (!string.IsNullOrEmpty(state.SerializedFieldFinderStatus))
                    EditorGUILayout.HelpBox(state.SerializedFieldFinderStatus, MessageType.Info);
            }
        }

        private void DrawFilterHeaderActions(List<SerializedFieldFilterRow> filters)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Filters", EditorStyles.miniBoldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent("Add Filter", EditorGUIUtility.IconContent("Toolbar Plus").image),
                            GUILayout.Height(22)))
                    {
                        filters.Add(new SerializedFieldFilterRow
                        {
                            Expanded = true,
                            JoinWithPrevious = LogicalOperator.And,
                            ValueType = typeof(string),
                            Collection = CollectionKind.None,
                            Comparison = FieldComparison.Equals
                        });
                    }

                    if (GUILayout.Button(
                            new GUIContent("Expand All",
                                EditorGUIUtility.IconContent("d_animationvisibilitytoggleon").image),
                            GUILayout.Height(22)))
                    {
                        foreach (SerializedFieldFilterRow f in filters)
                        {
                            f.Expanded = true;
                        }
                    }

                    if (GUILayout.Button(
                            new GUIContent("Collapse All",
                                EditorGUIUtility.IconContent("d_animationvisibilitytoggleoff").image),
                            GUILayout.Height(22)))
                    {
                        foreach (SerializedFieldFilterRow f in filters)
                        {
                            f.Expanded = false;
                        }
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(
                            new GUIContent("Clear", EditorGUIUtility.IconContent("TreeEditor.Refresh").image),
                            GUILayout.Height(22), GUILayout.Width(90)))
                    {
                        filters.Clear();
                        EnsureFilterListHasOneRow(filters);
                    }
                }
            }
        }

        private void DrawFilterRow(List<SerializedFieldFilterRow> filters, int index)
        {
            SerializedFieldFilterRow row = filters[index];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string suffix = row.IsCollection
                        ? $" • {row.Collection}<{row.EffectiveValueType?.Name ?? "?"}> • {row.EffectiveComparison}"
                        : $" • {row.EffectiveValueType?.Name ?? "?"} • {row.EffectiveComparison}";

                    row.Expanded = EditorGUILayout.Foldout(row.Expanded, $"Filter #{index + 1}{suffix}", true);

                    GUILayout.FlexibleSpace();

                    EditorGUI.BeginDisabledGroup(filters.Count <= 1);
                    if (GUILayout.Button(
                            new GUIContent("Remove", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                            GUILayout.Width(90), GUILayout.Height(18)))
                    {
                        filters.RemoveAt(index);
                        return;
                    }

                    EditorGUI.EndDisabledGroup();
                }

                if (!row.Expanded)
                    return;

                if (index > 0)
                    row.JoinWithPrevious = (LogicalOperator)EditorGUILayout.EnumPopup("Join", row.JoinWithPrevious);
                else
                {
                    EditorGUILayout.LabelField(new GUIContent("Join", "First filter has no join operator."),
                        new GUIContent("—"));
                }

                row.Collection = (CollectionKind)EditorGUILayout.EnumPopup(
                    new GUIContent("Collection", "If Array/List, comparison is forced to Contains."),
                    row.Collection);

                DrawValueTypePickerInline(
                    row.IsCollection ? "Element Type" : "Value Type",
                    ref row.TypeQuery,
                    ref row.ValueType);

                if (row.IsCollection)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Comparison", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.EnumPopup(FieldComparison.Contains);
                        EditorGUI.EndDisabledGroup();
                    }

                    EditorGUILayout.HelpBox("For Arrays/Lists the comparison will always be Contains.",
                        MessageType.None);
                }
                else
                    row.Comparison = (FieldComparison)EditorGUILayout.EnumPopup("Comparison", row.Comparison);

                DrawTypedValueField("Value", row.EffectiveValueType, row.Value);
            }
        }

        private static void EnsureFilterListHasOneRow(List<SerializedFieldFilterRow> filters)
        {
            if (filters == null) return;

            if (filters.Count == 0)
            {
                filters.Add(new SerializedFieldFilterRow
                {
                    Expanded = true,
                    JoinWithPrevious = LogicalOperator.And,
                    ValueType = typeof(string),
                    Collection = CollectionKind.None,
                    Comparison = FieldComparison.Equals
                });
            }
        }

        private static Type InferReplaceWithTypeFromFilters(List<SerializedFieldFilterRow> filters)
        {
            if (filters == null || filters.Count == 0) return null;
            SerializedFieldFilterRow first = filters[0];
            if (first == null) return null;
            first.EnsureDefaults();
            return first.EffectiveValueType;
        }

        // -------- Replace cards --------

        private void DrawReplaceCard(AssetUsageFinderState state)
        {
            if (_replaceMode == ReplaceMode.SerializedFieldValue)
            {
                DrawSerializedFieldQueryCard(
                    "Replace • Serialized Field Value",
                    _replaceFilters,
                    ref _replaceFiltersScroll,
                    true,
                    _replaceWithValue);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (!string.IsNullOrEmpty(state.SerializedFieldValueReplaceStatus))
                        EditorGUILayout.HelpBox(state.SerializedFieldValueReplaceStatus, MessageType.Info);

                    EditorGUI.BeginDisabledGroup(state.SerializedFieldValueReplaceIsBusy);
                    if (GUILayout.Button(
                            new GUIContent("Preview Changes", EditorGUIUtility.IconContent("d_PreMatCube").image),
                            GUILayout.Height(30)))
                        _controller.PreviewReplaceSerializedFieldValue(_replaceFilters, _replaceWithValue);

                    if (GUILayout.Button(new GUIContent("Apply", EditorGUIUtility.IconContent("d_PlayButton On").image),
                            GUILayout.Height(30)))
                        _controller.ApplyReplaceSerializedFieldValue(_replaceFilters, _replaceWithValue);
                    EditorGUI.EndDisabledGroup();
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Replace", EditorStyles.boldLabel);

                switch (_replaceMode)
                {
                    case ReplaceMode.PrefabOrVariant:
                        DrawReplacePrefabCard(state);
                        break;

                    case ReplaceMode.Component:
                        DrawReplaceComponentCard(state);
                        break;
                }
            }
        }

        private void DrawReplacePrefabCard(AssetUsageFinderState state)
        {
            EditorGUILayout.LabelField("Replace • Prefab / Prefab Variant", EditorStyles.miniBoldLabel);

            _replaceFromPrefab =
                EditorGUILayout.ObjectField("From Prefab", _replaceFromPrefab, typeof(GameObject), false);
            _replaceToPrefab = EditorGUILayout.ObjectField("To Prefab", _replaceToPrefab, typeof(GameObject), false);

            _replaceIncludeVariants =
                EditorGUILayout.ToggleLeft(new GUIContent("Include Variants"), _replaceIncludeVariants);
            _replaceKeepOverrides =
                EditorGUILayout.ToggleLeft(new GUIContent("Keep Overrides (best-effort)"), _replaceKeepOverrides);
            _replaceCopyCommonRootComponentValues = EditorGUILayout.ToggleLeft(
                new GUIContent("Copy Common Root Component Values"),
                _replaceCopyCommonRootComponentValues);

            EditorGUILayout.Space(6);
            DrawReplacePrefabActionRow(state);

            if (!string.IsNullOrEmpty(state.PrefabOrVariantReplaceStatus))
                EditorGUILayout.HelpBox(state.PrefabOrVariantReplaceStatus, MessageType.Info);
        }

        private void DrawReplaceComponentCard(AssetUsageFinderState state)
        {
            EditorGUILayout.LabelField("Replace • Component", EditorStyles.miniBoldLabel);

            DrawComponentTypePicker("From Type", ref _replaceComponentFromTypeQuery, ref _replaceComponentFromTypeName);
            DrawComponentTypePicker("To Type", ref _replaceComponentToTypeQuery, ref _replaceComponentToTypeName);

            EditorGUILayout.HelpBox(
                "Type selection stores the assembly-qualified name, avoiding ambiguity between identical type names in different namespaces or assemblies.",
                MessageType.None);

            _replaceCopySerializedValues =
                EditorGUILayout.ToggleLeft(new GUIContent("Copy Serialized Values (best-effort)"),
                    _replaceCopySerializedValues);
            _replaceDisableOldComponentInsteadOfRemove = EditorGUILayout.ToggleLeft(
                new GUIContent("Disable old component instead of removing"),
                _replaceDisableOldComponentInsteadOfRemove);

            EditorGUILayout.Space(6);
            DrawReplaceComponentActionRow(state);

            if (!string.IsNullOrEmpty(state.ComponentReplaceStatus))
                EditorGUILayout.HelpBox(state.ComponentReplaceStatus, MessageType.Info);
        }

        private void DrawReplacePrefabActionRow(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(state.PrefabOrVariantReplaceIsBusy);
                if (GUILayout.Button(new GUIContent("Preview", EditorGUIUtility.IconContent("d_PreMatCube").image),
                        GUILayout.Height(28)))
                {
                    _controller.PreviewReplacePrefabOrVariant(
                        _replaceFromPrefab,
                        _replaceToPrefab,
                        _replaceIncludeVariants,
                        _replaceKeepOverrides,
                        _replaceCopyCommonRootComponentValues);
                }

                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(state.PrefabOrVariantReplaceIsBusy);
                if (GUILayout.Button(new GUIContent("Apply", EditorGUIUtility.IconContent("d_PlayButton On").image),
                        GUILayout.Height(28)))
                {
                    _controller.ApplyReplacePrefabOrVariant(
                        _replaceFromPrefab,
                        _replaceToPrefab,
                        _replaceIncludeVariants,
                        _replaceKeepOverrides,
                        _replaceCopyCommonRootComponentValues);
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawReplaceComponentActionRow(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(state.ComponentReplaceIsBusy);
                if (GUILayout.Button(new GUIContent("Preview", EditorGUIUtility.IconContent("d_PreMatCube").image),
                        GUILayout.Height(28)))
                {
                    _controller.PreviewReplaceComponent(
                        _replaceComponentFromTypeName,
                        _replaceComponentToTypeName,
                        _replaceCopySerializedValues,
                        _replaceDisableOldComponentInsteadOfRemove);
                }

                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(state.ComponentReplaceIsBusy);
                if (GUILayout.Button(new GUIContent("Apply", EditorGUIUtility.IconContent("d_PlayButton On").image),
                        GUILayout.Height(28)))
                {
                    _controller.ApplyReplaceComponent(
                        _replaceComponentFromTypeName,
                        _replaceComponentToTypeName,
                        _replaceCopySerializedValues,
                        _replaceDisableOldComponentInsteadOfRemove);
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        // -------- Finder results (By Asset) --------

        private void DrawFinderResults(AssetUsageFinderState state)
        {
            if (state.Entries == null || state.Entries.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("No results yet. Run Finder to see references.", MessageType.None);
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent($"{state.Entries.Count} item(s)"), EditorStyles.miniLabel);

                    GUILayout.FlexibleSpace();

                    EditorGUI.BeginDisabledGroup(true);
                    GUILayout.Button(new GUIContent("Export", EditorGUIUtility.IconContent("d_SaveAs").image),
                        GUILayout.Width(90), GUILayout.Height(22));
                    EditorGUI.EndDisabledGroup();
                }
            }

            Dictionary<string, List<string>> scenePrefabGroups = state.BuildScenePrefabGroups();
            string selectedPrefabPath = _controller.GetSelectedPrefabAssetPath();

            HashSet<string> processedScenes = new(StringComparer.OrdinalIgnoreCase);

            _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll, GUILayout.ExpandHeight(true));

            foreach (AssetUsageFinderEntry entry in state.Entries)
            {
                if (!state.PassesFilter(entry.UsageType))
                    continue;

                if (entry.UsageType == AssetUsageFinderUsageType.SceneWithPrefabInstance)
                {
                    if (!processedScenes.Add(entry.AssetPath))
                        continue;
                }

                DrawResultRow(entry, selectedPrefabPath, scenePrefabGroups);
                GUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawResultRow(
            AssetUsageFinderEntry entry,
            string selectedPrefabPath,
            Dictionary<string, List<string>> scenePrefabGroups)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Texture icon = GetUsageTypeIcon(entry.UsageType);
                    string label = BuildEntryLabel(entry, selectedPrefabPath);
                    EditorGUILayout.LabelField(new GUIContent(label, icon), GUILayout.MinWidth(440));

                    GUILayout.FlexibleSpace();

                    DrawEntryButtons(entry);
                }

                if (entry.UsageType == AssetUsageFinderUsageType.SceneWithPrefabInstance &&
                    scenePrefabGroups.TryGetValue(entry.AssetPath, out List<string> prefabs))
                {
                    EditorGUI.indentLevel++;
                    entry.PrefabListExpanded =
                        EditorGUILayout.Foldout(entry.PrefabListExpanded, $"Prefabs ({prefabs.Count})", true);
                    if (entry.PrefabListExpanded)
                    {
                        foreach (string prefabPath in prefabs)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                string prefabName = Path.GetFileName(prefabPath);
                                string variantTag = !string.IsNullOrEmpty(selectedPrefabPath) &&
                                                    _controller.IsVariantOfSelectedPrefab(prefabPath,
                                                        selectedPrefabPath)
                                    ? " — Variant of selected prefab"
                                    : string.Empty;

                                EditorGUILayout.LabelField(new GUIContent($"• {prefabName}{variantTag}",
                                    EditorGUIUtility.IconContent("Prefab Icon").image));

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("Open", GUILayout.Width(70)))
                                    PrefabStageUtility.OpenPrefab(prefabPath);

                                if (GUILayout.Button("Ping", GUILayout.Width(60)))
                                {
                                    Object prefabObj = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
                                    if (prefabObj != null) EditorGUIUtility.PingObject(prefabObj);
                                }

                                if (GUILayout.Button("Find", GUILayout.Width(60)))
                                {
                                    Object prefabObj = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
                                    _controller.FindUsagesForAsset(prefabObj);
                                }
                            }
                        }
                    }

                    EditorGUI.indentLevel--;
                }

                DrawInFileSection(entry);
            }
        }

        private void DrawEntryButtons(AssetUsageFinderEntry entry)
        {
            Object fileAsset = AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath);
            bool isUnsavedOpenScene = AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(entry.AssetPath);

            if (entry.UsageType == AssetUsageFinderUsageType.Scene ||
                entry.UsageType == AssetUsageFinderUsageType.SceneWithPrefabInstance)
            {
                EditorGUI.BeginDisabledGroup(isUnsavedOpenScene);
                if (GUILayout.Button("Open", GUILayout.Width(70)))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        EditorSceneManager.OpenScene(entry.AssetPath);
                }

                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    EditorGUIUtility.PingObject(fileAsset);
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Find", GUILayout.Width(60)))
                    _controller.FindUsagesForAsset(fileAsset);
                EditorGUI.EndDisabledGroup();
            }
            else if (entry.UsageType == AssetUsageFinderUsageType.Prefab)
            {
                if (GUILayout.Button("Open", GUILayout.Width(70)))
                    PrefabStageUtility.OpenPrefab(entry.AssetPath);

                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    EditorGUIUtility.PingObject(fileAsset);
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Find", GUILayout.Width(60)))
                    _controller.FindUsagesForAsset(fileAsset);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Select", GUILayout.Width(70)))
                    Selection.activeObject = fileAsset;
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    EditorGUIUtility.PingObject(fileAsset);
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(fileAsset == null);
                if (GUILayout.Button("Find", GUILayout.Width(60)))
                    _controller.FindUsagesForAsset(fileAsset);
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawInFileSection(AssetUsageFinderEntry entry)
        {
            bool isActive = _controller.IsEntryActive(entry);
            if (!isActive)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("In File", EditorStyles.miniBoldLabel);

                if (GUILayout.Button(
                        new GUIContent("Scan Usages", EditorGUIUtility.IconContent("d_Search Icon").image),
                        GUILayout.Height(24), GUILayout.Width(140)))
                    _controller.FindUsagesInActiveContext(entry.AssetPath);

                entry.UsageListExpanded = EditorGUILayout.Foldout(entry.UsageListExpanded, "Usages", true);

                if (!entry.UsageListExpanded)
                    return;

                List<AssetUsageFinderCachedUsage> cachedUsages = _controller.GetCachedUsages(entry.AssetPath);
                if (cachedUsages == null || cachedUsages.Count == 0)
                {
                    EditorGUILayout.HelpBox("No usages cached yet. Click 'Scan Usages'.", MessageType.None);
                    return;
                }

                foreach (AssetUsageFinderCachedUsage usage in cachedUsages)
                {
                    Object resolved = _controller.ResolveUsageReference(usage);
                    bool valid = resolved != null;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginDisabledGroup(!valid);
                        if (GUILayout.Button("Select", GUILayout.Width(60)))
                            _controller.SelectResolvedUsage(usage, resolved);
                        EditorGUI.EndDisabledGroup();

                        if (GUILayout.Button(
                                new GUIContent("Remove", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                                GUILayout.Width(80)))
                            _controller.RemoveUsageAndRefreshCache(entry.AssetPath, usage);

                        GUILayout.Space(6);

                        string status = $"Active: {(usage.GameObjectActive ? "Yes" : "No")}";
                        if (usage.IsComponent)
                        {
                            status +=
                                $", Enabled: {(usage.ComponentEnabled.HasValue ? usage.ComponentEnabled.Value ? "Yes" : "No" : "N/A")}";
                        }

                        EditorGUILayout.LabelField(usage.DisplayName, GUILayout.Width(180));
                        EditorGUILayout.LabelField(usage.HierarchyPath, GUILayout.MinWidth(320));
                        EditorGUILayout.LabelField(status, GUILayout.Width(170));
                    }
                }
            }
        }

        // -------- Finder results (By Serialized Field) --------

        private void DrawSerializedFieldFinderResults(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

                int count = state.SerializedFieldFinderResults != null ? state.SerializedFieldFinderResults.Count : 0;
                EditorGUILayout.LabelField($"{count} match(es)", EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(state.SerializedFieldFinderStatus))
                    EditorGUILayout.HelpBox(state.SerializedFieldFinderStatus, MessageType.None);
            }

            if (state.SerializedFieldFinderResults == null || state.SerializedFieldFinderResults.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "No results yet.\n\nTip: For Object references, set Value Type to the desired UnityEngine.Object-derived type and pick the target object.",
                        MessageType.None);
                }

                return;
            }

            _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll, GUILayout.ExpandHeight(true));

            foreach (AssetUsageFinderSerializedFieldResult r in state.SerializedFieldFinderResults)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(Path.GetFileName(r.FileAssetPath), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(r.FileAssetPath, EditorStyles.miniLabel);

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField($"Object: {r.ObjectPath}");
                    EditorGUILayout.LabelField($"Type: {r.ObjectTypeName}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Property: {r.PropertyPath}");
                    EditorGUILayout.LabelField($"Value: {r.CurrentValue}");

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Ping File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(r.FileAssetPath);
                            if (fileObj != null) EditorGUIUtility.PingObject(fileObj);
                        }

                        if (GUILayout.Button("Select File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(r.FileAssetPath);
                            if (fileObj != null) Selection.activeObject = fileObj;
                        }
                    }
                }

                GUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        // -------- Placeholders --------

        private void DrawReplacePreview(AssetUsageFinderState state)
        {
            if (_replaceMode == ReplaceMode.SerializedFieldValue)
            {
                DrawSerializedFieldValueReplacePreview(state);
                return;
            }

            if (_replaceMode == ReplaceMode.Component)
            {
                DrawComponentReplacePreview(state);
                return;
            }

            if (_replaceMode != ReplaceMode.PrefabOrVariant)
            {
                DrawReplacePreviewPlaceholder();
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

                int count = state.PrefabOrVariantReplacePreview != null ? state.PrefabOrVariantReplacePreview.Count : 0;
                EditorGUILayout.LabelField($"{count} replacement(s)", EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(state.PrefabOrVariantReplaceStatus))
                    EditorGUILayout.HelpBox(state.PrefabOrVariantReplaceStatus, MessageType.None);
            }

            if (state.PrefabOrVariantReplacePreview == null || state.PrefabOrVariantReplacePreview.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "No preview yet.\n\nUse Preview to list every prefab instance that will be replaced.",
                        MessageType.None);
                }

                return;
            }

            _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll, GUILayout.ExpandHeight(true));

            foreach (AssetUsageFinderPrefabOrVariantReplacePreviewItem item in state.PrefabOrVariantReplacePreview)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(Path.GetFileName(item.FileAssetPath), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(item.FileAssetPath, EditorStyles.miniLabel);

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField($"Instance: {item.ObjectPath}");
                    EditorGUILayout.LabelField($"Source Prefab: {item.SourcePrefabPath}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(
                        item.IsVariantMatch ? "Match: Variant of selected prefab" : "Match: Direct prefab reference",
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(item.IsScene ? "Context: Scene" : "Context: Prefab Asset",
                        EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Ping File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(item.FileAssetPath);
                            if (fileObj != null) EditorGUIUtility.PingObject(fileObj);
                        }

                        if (GUILayout.Button("Select File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(item.FileAssetPath);
                            if (fileObj != null) Selection.activeObject = fileObj;
                        }
                    }
                }

                GUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawComponentReplacePreview(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

                int count = state.ComponentReplacePreview != null ? state.ComponentReplacePreview.Count : 0;
                EditorGUILayout.LabelField($"{count} replacement(s)", EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(state.ComponentReplaceStatus))
                    EditorGUILayout.HelpBox(state.ComponentReplaceStatus, MessageType.None);
            }

            if (state.ComponentReplacePreview == null || state.ComponentReplacePreview.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "No preview yet.\n\nUse Preview to list every component that will be replaced.",
                        MessageType.None);
                }

                return;
            }

            _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll, GUILayout.ExpandHeight(true));

            foreach (AssetUsageFinderComponentReplacePreviewItem item in state.ComponentReplacePreview)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(Path.GetFileName(item.FileAssetPath), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(item.FileAssetPath, EditorStyles.miniLabel);

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField($"Object: {item.ObjectPath}");
                    EditorGUILayout.LabelField($"Component: {item.ComponentLabel}");
                    EditorGUILayout.LabelField($"Replace: {item.FromTypeName} -> {item.ToTypeName}",
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(
                        item.WillDisableOldComponentInsteadOfRemove
                            ? "Action: Add new component and disable old one"
                            : "Action: Add new component and remove old one",
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(item.IsScene ? "Context: Scene" : "Context: Prefab Asset",
                        EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Ping File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(item.FileAssetPath);
                            if (fileObj != null) EditorGUIUtility.PingObject(fileObj);
                        }

                        if (GUILayout.Button("Select File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(item.FileAssetPath);
                            if (fileObj != null) Selection.activeObject = fileObj;
                        }
                    }
                }

                GUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSerializedFieldValueReplacePreview(AssetUsageFinderState state)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

                int count = state.SerializedFieldValueReplacePreview != null
                    ? state.SerializedFieldValueReplacePreview.Count
                    : 0;
                EditorGUILayout.LabelField($"{count} replacement(s)", EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(state.SerializedFieldValueReplaceStatus))
                    EditorGUILayout.HelpBox(state.SerializedFieldValueReplaceStatus, MessageType.None);
            }

            if (state.SerializedFieldValueReplacePreview == null || state.SerializedFieldValueReplacePreview.Count == 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "No preview yet.\n\nUse Preview Changes to list every serialized value that will be replaced.",
                        MessageType.None);
                }

                return;
            }

            _resultsScroll = EditorGUILayout.BeginScrollView(_resultsScroll, GUILayout.ExpandHeight(true));

            foreach (AssetUsageFinderSerializedFieldValueReplacePreviewItem item in state
                         .SerializedFieldValueReplacePreview)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(Path.GetFileName(item.FileAssetPath), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(item.FileAssetPath, EditorStyles.miniLabel);

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField($"Object: {item.ObjectPath}");
                    EditorGUILayout.LabelField($"Type: {item.ObjectTypeName}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Property: {item.PropertyPath}");
                    EditorGUILayout.LabelField($"Replace: {item.CurrentValue} -> {item.NewValue}");
                    EditorGUILayout.LabelField(item.IsScene ? "Context: Scene" : "Context: Asset / Prefab",
                        EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Ping File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(item.FileAssetPath);
                            if (fileObj != null) EditorGUIUtility.PingObject(fileObj);
                        }

                        if (GUILayout.Button("Select File", GUILayout.Width(90)))
                        {
                            Object fileObj = AssetDatabase.LoadAssetAtPath<Object>(item.FileAssetPath);
                            if (fileObj != null) Selection.activeObject = fileObj;
                        }
                    }
                }

                GUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawReplacePreviewPlaceholder()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Replace preview will appear here.\n" +
                    "Suggested UX: list of changes with checkboxes + 'Apply selected'.",
                    MessageType.None);
            }
        }

        // -------- Type picker UX --------

        private void DrawComponentTypePicker(string label, ref string typeQuery, ref string selectedTypeName)
        {
            Type selectedType = ResolveComponentPickerType(selectedTypeName);
            string searchLabel = $"{label} Search";
            string searchTooltip =
                "Search by type name, namespace, or assembly. Picking stores the assembly-qualified name.";

            typeQuery = EditorGUILayout.TextField(new GUIContent(searchLabel, searchTooltip), typeQuery);

            List<Type> matches = GetComponentTypeMatches(typeQuery, 16);
            List<Type> options = new();
            List<string> displayOptions = new() { "<None>" };

            if (selectedType != null)
            {
                options.Add(selectedType);
                displayOptions.Add(GetComponentTypeDisplayLabel(selectedType));
            }

            foreach (Type match in matches)
            {
                if (options.Contains(match))
                    continue;

                options.Add(match);
                displayOptions.Add(GetComponentTypeDisplayLabel(match));
            }

            int currentIndex = selectedType != null ? 1 : 0;
            using (EditorGUI.ChangeCheckScope change = new())
            {
                int nextIndex = EditorGUILayout.Popup(label, currentIndex, displayOptions.ToArray());
                if (change.changed)
                {
                    selectedTypeName = nextIndex <= 0
                        ? string.Empty
                        : options[nextIndex - 1].AssemblyQualifiedName ?? string.Empty;

                    selectedType = ResolveComponentPickerType(selectedTypeName);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Selected", GUILayout.Width(60));
                EditorGUILayout.SelectableLabel(
                    selectedType != null ? GetComponentTypeDisplayLabel(selectedType) : "<None>",
                    GUILayout.Height(18));
            }

            if (selectedType == null && !string.IsNullOrWhiteSpace(selectedTypeName))
            {
                EditorGUILayout.HelpBox(
                    "The currently stored type could not be resolved. Pick a type again to refresh the selection.",
                    MessageType.Warning);
            }
        }

        private void DrawValueTypePickerInline(string label, ref string typeQuery, ref Type selectedType)
        {
            selectedType ??= typeof(string);

            typeQuery = EditorGUILayout.TextField(new GUIContent(label, "Value type used to draw 'Value' field"),
                typeQuery);

            List<Type> matches = GetValueTypeMatches(typeQuery, 8);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Selected", GUILayout.Width(60));
                EditorGUILayout.SelectableLabel(selectedType != null ? selectedType.FullName : "<None>",
                    GUILayout.Height(18));
            }

            if (matches.Count > 0)
            {
                foreach (Type t in matches)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(t.FullName, GUILayout.MinWidth(240));
                        if (GUILayout.Button("Pick", GUILayout.Width(60)))
                            selectedType = t;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("string", EditorStyles.miniButtonLeft)) selectedType = typeof(string);
                if (GUILayout.Button("int", EditorStyles.miniButtonMid)) selectedType = typeof(int);
                if (GUILayout.Button("float", EditorStyles.miniButtonMid)) selectedType = typeof(float);
                if (GUILayout.Button("bool", EditorStyles.miniButtonMid)) selectedType = typeof(bool);
                if (GUILayout.Button("Vector3", EditorStyles.miniButtonRight)) selectedType = typeof(Vector3);
            }
        }

        private static List<Type> GetComponentTypeMatches(string query, int max)
        {
            IEnumerable<Type> allTypes = GetSelectableComponentTypes();

            if (!string.IsNullOrWhiteSpace(query))
            {
                string normalizedQuery = query.Trim();
                allTypes = allTypes.Where(type =>
                    type.Name.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (type.FullName != null &&
                     type.FullName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    type.Assembly.GetName().Name.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return allTypes.Take(max).ToList();
        }

        private static List<Type> GetSelectableComponentTypes()
        {
            if (_componentReplaceTypesCache != null)
                return _componentReplaceTypesCache;

            _componentReplaceTypesCache = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(type => type != null && !type.IsAbstract && !type.IsGenericTypeDefinition)
                .OrderBy(GetComponentTypeDisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return _componentReplaceTypesCache;
        }

        private static Type ResolveComponentPickerType(string selectedTypeName)
        {
            if (string.IsNullOrWhiteSpace(selectedTypeName))
                return null;

            string normalizedTypeName = selectedTypeName.Trim();
            return GetSelectableComponentTypes().FirstOrDefault(type =>
                string.Equals(type.AssemblyQualifiedName, normalizedTypeName, StringComparison.Ordinal) ||
                string.Equals(type.FullName, normalizedTypeName, StringComparison.Ordinal) ||
                string.Equals(type.Name, normalizedTypeName, StringComparison.Ordinal));
        }

        private static string GetComponentTypeDisplayLabel(Type type)
        {
            if (type == null)
                return "<None>";

            string typeName = type.FullName ?? type.Name;
            string assemblyName = type.Assembly.GetName().Name;
            return $"{typeName} ({assemblyName})";
        }

        private static List<Type> GetValueTypeMatches(string query, int max)
        {
            List<Type> list = new()
            {
                typeof(string),
                typeof(bool),
                typeof(int),
                typeof(long),
                typeof(float),
                typeof(double),
                typeof(short),
                typeof(byte),
                typeof(uint),
                typeof(ulong),
                typeof(Vector2),
                typeof(Vector3),
                typeof(Vector4),
                typeof(Color),
                typeof(Rect),
                typeof(Bounds),
                typeof(AnimationCurve),
                typeof(Quaternion)
            };

            TypeCache.TypeCollection enums = TypeCache.GetTypesDerivedFrom<Enum>();
            TypeCache.TypeCollection unityObjects = TypeCache.GetTypesDerivedFrom<Object>();

            IEnumerable<Type> all = list
                .Concat(enums)
                .Concat(unityObjects)
                .Where(t => t != null && !t.IsGenericTypeDefinition);

            if (string.IsNullOrWhiteSpace(query))
                return all.Take(max).ToList();

            query = query.Trim();
            return all
                .Where(t =>
                    t.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.FullName != null && t.FullName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                .Take(max)
                .ToList();
        }

        // -------- Type-aware value drawer (shared) --------

        private void DrawTypedValueField(string label, Type valueType, SerializedFieldValueBox box)
        {
            if (box == null)
                return;

            Type t = valueType ?? typeof(string);

            if (typeof(Object).IsAssignableFrom(t))
            {
                box.ObjectValue = EditorGUILayout.ObjectField(label, box.ObjectValue, t, false);
                return;
            }

            if (t.IsEnum)
            {
                string[] names = Enum.GetNames(t);
                int idx = Mathf.Clamp(box.EnumIndex, 0, Math.Max(0, names.Length - 1));
                idx = EditorGUILayout.Popup(label, idx, names);
                box.EnumIndex = idx;
                if (names.Length > 0)
                    box.EnumName = names[idx];
                return;
            }

            if (t == typeof(bool))
            {
                box.BoolValue = EditorGUILayout.Toggle(label, box.BoolValue);
                return;
            }

            if (t == typeof(string))
            {
                box.StringValue = EditorGUILayout.TextField(label, box.StringValue);
                return;
            }

            if (t == typeof(int))
            {
                box.IntValue = EditorGUILayout.IntField(label, box.IntValue);
                return;
            }

            if (t == typeof(long))
            {
                box.LongValue = EditorGUILayout.LongField(label, box.LongValue);
                return;
            }

            if (t == typeof(short))
            {
                int tmp = EditorGUILayout.IntField(label, box.IntValue);
                tmp = Mathf.Clamp(tmp, short.MinValue, short.MaxValue);
                box.IntValue = tmp;
                return;
            }

            if (t == typeof(byte))
            {
                int tmp = EditorGUILayout.IntField(label, box.IntValue);
                tmp = Mathf.Clamp(tmp, byte.MinValue, byte.MaxValue);
                box.IntValue = tmp;
                return;
            }

            if (t == typeof(uint))
            {
                long tmp = EditorGUILayout.LongField(label, Math.Max(0, box.LongValue));
                box.LongValue = Math.Max(0, tmp);
                return;
            }

            if (t == typeof(ulong))
            {
                long tmp = EditorGUILayout.LongField(label, Math.Max(0, box.LongValue));
                box.LongValue = Math.Max(0, tmp);
                EditorGUILayout.HelpBox("ulong is displayed as a non-negative long.", MessageType.None);
                return;
            }

            if (t == typeof(float))
            {
                box.FloatValue = EditorGUILayout.FloatField(label, box.FloatValue);
                return;
            }

            if (t == typeof(double))
            {
                box.DoubleValue = EditorGUILayout.DoubleField(label, box.DoubleValue);
                return;
            }

            if (t == typeof(Vector2))
            {
                box.Vector2Value = EditorGUILayout.Vector2Field(label, box.Vector2Value);
                return;
            }

            if (t == typeof(Vector3))
            {
                box.Vector3Value = EditorGUILayout.Vector3Field(label, box.Vector3Value);
                return;
            }

            if (t == typeof(Vector4))
            {
                box.Vector4Value = EditorGUILayout.Vector4Field(label, box.Vector4Value);
                return;
            }

            if (t == typeof(Color))
            {
                box.ColorValue = EditorGUILayout.ColorField(label, box.ColorValue);
                return;
            }

            if (t == typeof(Rect))
            {
                box.RectValue = EditorGUILayout.RectField(label, box.RectValue);
                return;
            }

            if (t == typeof(Bounds))
            {
                box.BoundsValue = EditorGUILayout.BoundsField(label, box.BoundsValue);
                return;
            }

            if (t == typeof(AnimationCurve))
            {
                box.CurveValue = EditorGUILayout.CurveField(label, box.CurveValue);
                return;
            }

            if (t == typeof(Quaternion))
            {
                Vector4 v = new(box.QuaternionValue.x, box.QuaternionValue.y, box.QuaternionValue.z,
                    box.QuaternionValue.w);
                v = EditorGUILayout.Vector4Field($"{label} (x,y,z,w)", v);
                box.QuaternionValue = new Quaternion(v.x, v.y, v.z, v.w);
                return;
            }

            box.StringValue =
                EditorGUILayout.TextField(new GUIContent(label, $"Unsupported type {t.FullName}. Using string input."),
                    box.StringValue);
            EditorGUILayout.HelpBox($"No dedicated drawer for: {t.FullName}. Using TextField fallback.",
                MessageType.None);
        }

        // -------- Helpers --------

        private static Texture GetUsageTypeIcon(AssetUsageFinderUsageType type)
        {
            switch (type)
            {
                case AssetUsageFinderUsageType.Scene:
                case AssetUsageFinderUsageType.SceneWithPrefabInstance:
                    return EditorGUIUtility.IconContent("SceneAsset Icon").image;

                case AssetUsageFinderUsageType.Prefab:
                    return EditorGUIUtility.IconContent("Prefab Icon").image;

                case AssetUsageFinderUsageType.Material:
                    return EditorGUIUtility.IconContent("Material Icon").image;

                case AssetUsageFinderUsageType.ScriptableObject:
                    return EditorGUIUtility.IconContent("ScriptableObject Icon").image;

                default:
                    return EditorGUIUtility.IconContent("DefaultAsset Icon").image;
            }
        }

        private string BuildEntryLabel(AssetUsageFinderEntry entry, string selectedPrefabPath)
        {
            string fileName = Path.GetFileName(entry.AssetPath);
            string label = $"{fileName}  •  {entry.UsageType}";

            if (entry.UsageType == AssetUsageFinderUsageType.Prefab &&
                !string.IsNullOrEmpty(selectedPrefabPath) &&
                _controller.IsVariantOfSelectedPrefab(entry.AssetPath, selectedPrefabPath))
                label += "  —  Variant of selected prefab";

            label += $"{entry.AssetPath}";
            return label;
        }
    }
}