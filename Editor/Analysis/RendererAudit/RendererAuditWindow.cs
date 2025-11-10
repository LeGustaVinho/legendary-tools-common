using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class RendererAuditWindow : EditorWindow
    {
        // ------------------------------
        // UI State
        // ------------------------------
        private Vector2 _scroll;
        private bool _includeInactive = true;
        private GroupMode _groupMode = GroupMode.Material;

        // ------------------------------
        // Filters
        // ------------------------------
        private StaticFilter _staticFilter = StaticFilter.All;
        private bool _includeMeshRenderer = true;
        private bool _includeSkinnedMeshRenderer = true;
        private bool _includeParticleSystem = true;

        private InstancingFilter _instancingFilter = InstancingFilter.All;
        private BatchingFilter _batchingFilter = BatchingFilter.All;
        private ShadowFilter _shadowFilter = ShadowFilter.All;
        private TransparencyFilter _transparencyFilter = TransparencyFilter.All;

        // Shadow thresholds
        private float _shadowSmallSizeThreshold = 0.25f; // meters (bounds.size.magnitude)
        private float _shadowFarDistanceThreshold = 80f; // meters (distance to camera)

        // Transparency threshold (overdraw heuristic)
        private float _transpSizeOverDistThreshold = 0.08f;

        // ------------------------------
        // Group Sorting
        // ------------------------------
        private GroupSortKey _sortKey = GroupSortKey.Count;
        private bool _sortDescending = true;

        // ------------------------------
        // Data
        // ------------------------------
        private List<RendererEntry> _entries = new();
        private ScanSummary _summary = new();
        private GroupIndex _index;

        // ------------------------------
        // UI Persistence
        // ------------------------------
        private readonly Dictionary<string, bool> _foldouts = new();
        private readonly Pagination _pagination = new();

        // ------------------------------
        // Styles
        // ------------------------------
        private static GUIStyle _miniBold;

        private static GUIStyle MiniBold
        {
            get
            {
                if (_miniBold == null)
                    _miniBold = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.UpperLeft,
                        wordWrap = false
                    };
                return _miniBold;
            }
        }

        // ------------------------------
        // Custom Tooltip Infra
        // ------------------------------
        private string _queuedTooltip;
        private Vector2 _queuedTooltipPos;

        private static GUIStyle _tooltipStyle;

        private static GUIStyle TooltipStyle
        {
            get
            {
                if (_tooltipStyle == null)
                    _tooltipStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        wordWrap = true,
                        fontSize = 11,
                        padding = new RectOffset(6, 6, 4, 6)
                    };
                return _tooltipStyle;
            }
        }

        // Inside RendererAuditWindow class (fields region)
        private bool _statsFoldout = true; // Collapsible statistics panel state

        // ------------------------------
        // Menu
        // ------------------------------
        [MenuItem("Tools/LegendaryTools/Analysis/Renderer Audit")]
        private static void Open()
        {
            RendererAuditWindow win = GetWindow<RendererAuditWindow>("Renderer Audit");
            win.minSize = new Vector2(1150, 660);
            win.RefreshNow();
        }

        // ------------------------------
        // IMGUI Entry
        // ------------------------------
        private void OnGUI()
        {
            // Reset tooltip queue each frame.
            _queuedTooltip = null;

            DrawToolbar();
            DrawFiltersPanel();
            EditorGUILayout.Space(6);

            DrawStats();
            EditorGUILayout.Space(6);

            DrawGroupedLists();

            // Render any queued tooltip after all controls.
            ShowQueuedTooltip();
        }

        // ------------------------------
        // Toolbar
        // ------------------------------
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) RefreshNow();

                GUILayout.Space(8);

                _includeInactive = GUILayout.Toggle(_includeInactive, "Include Inactive", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                GUILayout.Label("Group By:", GUILayout.Width(64));
                GroupMode newMode = (GroupMode)EditorGUILayout.EnumPopup(_groupMode, GUILayout.Width(160));
                if (newMode != _groupMode)
                {
                    _groupMode = newMode;
                    _pagination.ResetAll();
                }
            }
        }

        // ------------------------------
        // Filters Panel
        // ------------------------------
        private void DrawFiltersPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

                // Row 1: high-level filters
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Static:", GUILayout.Width(48));
                    StaticFilter newStatic =
                        (StaticFilter)EditorGUILayout.EnumPopup(_staticFilter, GUILayout.Width(140));
                    if (newStatic != _staticFilter)
                    {
                        _staticFilter = newStatic;
                        _pagination.ResetAll();
                    }

                    GUILayout.Space(12);
                    GUILayout.Label("Instancing:", GUILayout.Width(72));
                    InstancingFilter newInst =
                        (InstancingFilter)EditorGUILayout.EnumPopup(_instancingFilter, GUILayout.Width(220));
                    if (newInst != _instancingFilter)
                    {
                        _instancingFilter = newInst;
                        _pagination.ResetAll();
                    }

                    GUILayout.Space(12);
                    GUILayout.Label("Batching:", GUILayout.Width(64));
                    BatchingFilter newBatch =
                        (BatchingFilter)EditorGUILayout.EnumPopup(_batchingFilter, GUILayout.Width(220));
                    if (newBatch != _batchingFilter)
                    {
                        _batchingFilter = newBatch;
                        _pagination.ResetAll();
                    }

                    GUILayout.Space(12);
                    GUILayout.Label("Shadows:", GUILayout.Width(64));
                    ShadowFilter newShadow =
                        (ShadowFilter)EditorGUILayout.EnumPopup(_shadowFilter, GUILayout.Width(220));
                    if (newShadow != _shadowFilter)
                    {
                        _shadowFilter = newShadow;
                        _pagination.ResetAll();
                    }

                    GUILayout.Space(12);
                    GUILayout.Label("Transparency:", GUILayout.Width(90));
                    TransparencyFilter newTransp =
                        (TransparencyFilter)EditorGUILayout.EnumPopup(_transparencyFilter, GUILayout.Width(220));
                    if (newTransp != _transparencyFilter)
                    {
                        _transparencyFilter = newTransp;
                        _pagination.ResetAll();
                    }

                    GUILayout.FlexibleSpace();
                }

                // Row 2: type toggles
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Types:", GUILayout.Width(48));
                    bool nm = EditorGUILayout.ToggleLeft("MeshRenderer", _includeMeshRenderer, GUILayout.Width(140));
                    bool ns = EditorGUILayout.ToggleLeft("SkinnedMeshRenderer", _includeSkinnedMeshRenderer,
                        GUILayout.Width(180));
                    bool np = EditorGUILayout.ToggleLeft("ParticleSystem", _includeParticleSystem,
                        GUILayout.Width(140));

                    if (nm != _includeMeshRenderer || ns != _includeSkinnedMeshRenderer || np != _includeParticleSystem)
                    {
                        _includeMeshRenderer = nm;
                        _includeSkinnedMeshRenderer = ns;
                        _includeParticleSystem = np;
                        _pagination.ResetAll();
                        RefreshNow();
                    }

                    GUILayout.FlexibleSpace();
                }

                // Row 3: thresholds + pagination + sorting
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Shadow thresholds
                    GUILayout.Label(
                        new GUIContent("Small Size",
                            "Bounds size magnitude threshold (m) below which receiving shadows may be flagged unnecessary."),
                        GUILayout.Width(80));
                    float newSmall = EditorGUILayout.FloatField(_shadowSmallSizeThreshold, GUILayout.Width(80));
                    newSmall = Mathf.Max(0.0001f, newSmall);
                    if (!Mathf.Approximately(newSmall, _shadowSmallSizeThreshold)) _shadowSmallSizeThreshold = newSmall;

                    GUILayout.Space(8);
                    GUILayout.Label(
                        new GUIContent("Far Dist",
                            "Distance (m) from camera above which receiving shadows may be flagged unnecessary."),
                        GUILayout.Width(70));
                    float newFar = EditorGUILayout.FloatField(_shadowFarDistanceThreshold, GUILayout.Width(80));
                    newFar = Mathf.Max(0f, newFar);
                    if (!Mathf.Approximately(newFar, _shadowFarDistanceThreshold)) _shadowFarDistanceThreshold = newFar;

                    // Transparency threshold
                    GUILayout.Space(16);
                    GUILayout.Label(
                        new GUIContent("Overdraw S/D", "Size/Distance threshold for transparent overdraw risk."),
                        GUILayout.Width(95));
                    float newOD = EditorGUILayout.FloatField(_transpSizeOverDistThreshold, GUILayout.Width(80));
                    newOD = Mathf.Max(0f, newOD);
                    if (!Mathf.Approximately(newOD, _transpSizeOverDistThreshold)) _transpSizeOverDistThreshold = newOD;

                    // Pagination
                    GUILayout.Space(16);
                    GUILayout.Label("Page Size:", GUILayout.Width(72));
                    int newSize = Mathf.Clamp(EditorGUILayout.IntField(_pagination.PageSize, GUILayout.Width(80)), 5,
                        500);
                    if (newSize != _pagination.PageSize) _pagination.SetPageSize(newSize);

                    // Sorting
                    GUILayout.Space(16);
                    GUILayout.Label("Sort groups by:", GUILayout.Width(110));
                    GroupSortKey newSortKey = (GroupSortKey)EditorGUILayout.EnumPopup(_sortKey, GUILayout.Width(180));
                    if (newSortKey != _sortKey) _sortKey = newSortKey;

                    GUILayout.Space(12);
                    bool newDesc = EditorGUILayout.ToggleLeft("Descending", _sortDescending, GUILayout.Width(120));
                    if (newDesc != _sortDescending) _sortDescending = newDesc;

                    GUILayout.FlexibleSpace();
                }
            }
        }

        // Collapsible Statistics panel with batching metrics
        private void DrawStats()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Foldout header
                using (new EditorGUILayout.HorizontalScope())
                {
                    _statsFoldout = EditorGUILayout.Foldout(_statsFoldout, "Statistics", true);
                    GUILayout.FlexibleSpace();
                }

                if (!_statsFoldout)
                    return;

                // --- Totals ---
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Open Scenes:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(UnityEngine.SceneManagement.SceneManager.sceneCount.ToString());
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Renderers (Total):", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.RendererTotal.ToString());
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("MeshRenderer:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.MeshRendererCount.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("SkinnedMeshRenderer:", GUILayout.Width(170));
                    EditorGUILayout.LabelField(_summary.SkinnedRendererCount.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("ParticleSystemRenderer:", GUILayout.Width(180));
                    EditorGUILayout.LabelField(_summary.ParticleSystemRendererCount.ToString());
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Unique Assets", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Materials:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.UniqueMaterials.Count.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Shaders:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.UniqueShaders.Count.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Textures:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.UniqueTextures.Count.ToString());
                }

                // --- GPU Instancing ---
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("GPU Instancing", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Eligible Renderers:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.InstancingEligible.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Enabled:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(_summary.InstancingEnabled.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Supported but Disabled:", GUILayout.Width(170));
                    EditorGUILayout.LabelField(_summary.InstancingSupportedButDisabled.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Unsupported/Unknown:", GUILayout.Width(170));
                    EditorGUILayout.LabelField(_summary.InstancingUnsupportedOrUnknown.ToString());
                }

                // --- Shadows ---
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Cast Shadows:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.CastShadowCount.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Receive Shadows:", GUILayout.Width(160));
                    EditorGUILayout.LabelField(_summary.ReceiveShadowCount.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Receive Probably Unnecessary:", GUILayout.Width(200));
                    EditorGUILayout.LabelField(_summary.ReceiveProbablyUnnecessaryCount.ToString());
                }

                // --- Transparency ---
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Transparency", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Transparent Renderers:", GUILayout.Width(180));
                    EditorGUILayout.LabelField(_summary.TransparentRendererCount.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("High Queue (>=3000):", GUILayout.Width(180));
                    EditorGUILayout.LabelField(_summary.HighQueueRendererCount.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Overdraw Risk:", GUILayout.Width(120));
                    EditorGUILayout.LabelField(_summary.OverdrawRiskRendererCount.ToString());
                }

                // --- NEW: Batching stats (computed from current entries) ---
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Batching", EditorStyles.boldLabel);

                // Compute on the fly to avoid changing the scanner/summary contracts.
                int dynEligible = _entries.Count(e => e != null && e.DynamicBatchEligible);
                int dynNotEligible = _entries.Count(e => e != null && !e.DynamicBatchEligible);
                int statEligible = _entries.Count(e => e != null && e.StaticBatchEligible);
                int statNotEligible = _entries.Count(e => e != null && !e.StaticBatchEligible);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Dynamic Batching — Eligible:", GUILayout.Width(210));
                    EditorGUILayout.LabelField(dynEligible.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Not Eligible:", GUILayout.Width(100));
                    EditorGUILayout.LabelField(dynNotEligible.ToString());
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Static Batching — Eligible:", GUILayout.Width(210));
                    EditorGUILayout.LabelField(statEligible.ToString());
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField("Not Eligible:", GUILayout.Width(100));
                    EditorGUILayout.LabelField(statNotEligible.ToString());
                }
            }
        }

        // ------------------------------
        // Grouped Lists
        // ------------------------------
        private void DrawGroupedLists()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            bool Predicate(RendererEntry e)
            {
                return InstancingUtil.PassesInstancingFilter(e, _instancingFilter) &&
                       PassesBatchingFilter(e, _batchingFilter) &&
                       PassesShadowFilter(e, _shadowFilter) &&
                       PassesTransparencyFilter(e, _transparencyFilter);
            }

            if (_index == null)
            {
                EditorGUILayout.HelpBox("No data. Click Refresh.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            switch (_groupMode)
            {
                case GroupMode.Material:
                {
                    List<GroupRow<Material>> rows = CostAndSort.BuildSortedRows(
                        _index.ByMaterial,
                        CostAndSort.CostForMaterial,
                        Predicate,
                        _sortKey, _sortDescending);

                    DrawRows(rows, m => $"Material: {m.name}", obj => AssetDatabase.GetAssetPath(obj));
                    DrawNullGroup("Material: (None)", _index.NoMaterial, Predicate);
                    break;
                }

                case GroupMode.Shader:
                {
                    List<GroupRow<Shader>> rows = CostAndSort.BuildSortedRows(
                        _index.ByShader,
                        CostAndSort.CostForShader,
                        Predicate,
                        _sortKey, _sortDescending);

                    DrawRows(rows, s => $"Shader: {s.name}", obj => AssetDatabase.GetAssetPath(obj));
                    DrawNullGroup("Shader: (None)", _index.NoShader, Predicate);
                    break;
                }

                case GroupMode.Texture:
                {
                    List<GroupRow<Texture>> rows = CostAndSort.BuildSortedRows(
                        _index.ByTexture,
                        CostAndSort.CostForTexture,
                        Predicate,
                        _sortKey, _sortDescending);

                    DrawRows(rows,
                        t => $"Texture: {TextureUtil.Clamp(t.name, 40)} {TextureUtil.GetTextureSizeSuffix(t)}",
                        obj => AssetDatabase.GetAssetPath(obj));

                    DrawNullGroup("Texture: (None)", _index.NoTexture, Predicate);
                    break;
                }

                case GroupMode.ParticleSystem:
                {
                    List<KeyValuePair<ParticleSystem, List<RendererEntry>>> groups = GroupIndex
                        .ApplyFilter(_index.ByParticleSystem, Predicate, ps => ps.gameObject.name).ToList();
                    DrawSimpleGroups(groups, ps => $"ParticleSystem: {ps.gameObject.name}", _index.NoParticleSystem);
                    break;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------
        // Predicates
        // ------------------------------
        private static bool PassesBatchingFilter(RendererEntry e, BatchingFilter bf)
        {
            switch (bf)
            {
                case BatchingFilter.All: return true;
                case BatchingFilter.DynamicEligibleOnly: return e.DynamicBatchEligible;
                case BatchingFilter.StaticEligibleOnly: return e.StaticBatchEligible;
                case BatchingFilter.DynamicNotEligibleOnly: return !e.DynamicBatchEligible;
                case BatchingFilter.StaticNotEligibleOnly: return !e.StaticBatchEligible;
                default: return true;
            }
        }

        private static bool PassesShadowFilter(RendererEntry e, ShadowFilter sf)
        {
            switch (sf)
            {
                case ShadowFilter.All: return true;
                case ShadowFilter.CastOnly: return e.CastsShadows && !e.ReceivesShadows;
                case ShadowFilter.ReceiveOnly: return e.ReceivesShadows && !e.CastsShadows;
                case ShadowFilter.CastAndReceive: return e.CastsShadows && e.ReceivesShadows;
                case ShadowFilter.NoShadows: return !e.CastsShadows && !e.ReceivesShadows;
                case ShadowFilter.ReceiveProbablyUnnecessaryOnly: return e.ReceivesShadowProbablyUnnecessary;
                default: return true;
            }
        }

        private static bool PassesTransparencyFilter(RendererEntry e, TransparencyFilter tf)
        {
            switch (tf)
            {
                case TransparencyFilter.All: return true;
                case TransparencyFilter.TransparentOnly: return e.HasTransparentMaterial;
                case TransparencyFilter.OpaqueOnly: return !e.HasTransparentMaterial;
                case TransparencyFilter.HighQueueOnly: return e.HasTransparentMaterial && e.MaxRenderQueue >= 3000;
                case TransparencyFilter.OverdrawRiskOnly: return e.TransparencyOverdrawRisk;
                default: return true;
            }
        }

        // ------------------------------
        // Column Headers
        // ------------------------------
        private void DrawEntryHeaderRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Type", MiniBold, GUILayout.Width(170));
                EditorGUILayout.LabelField("Static", MiniBold, GUILayout.Width(90));
                EditorGUILayout.LabelField("Scene", MiniBold, GUILayout.Width(200));
                EditorGUILayout.LabelField("Path", MiniBold, GUILayout.Width(260));

                EditorGUILayout.LabelField("Materials", MiniBold, GUILayout.MinWidth(160), GUILayout.MaxWidth(220));
                EditorGUILayout.LabelField("Shaders", MiniBold, GUILayout.MinWidth(140), GUILayout.MaxWidth(200));
                EditorGUILayout.LabelField("Textures", MiniBold, GUILayout.MinWidth(180), GUILayout.MaxWidth(260));
                EditorGUILayout.LabelField("Shadows", MiniBold, GUILayout.MinWidth(160), GUILayout.MaxWidth(200));
                EditorGUILayout.LabelField("Transparency", MiniBold, GUILayout.MinWidth(180), GUILayout.MaxWidth(260));
                EditorGUILayout.LabelField("Instancing", MiniBold, GUILayout.MinWidth(140), GUILayout.MaxWidth(180));
                EditorGUILayout.LabelField("Dynamic Batching", MiniBold, GUILayout.MinWidth(180),
                    GUILayout.MaxWidth(260));
                EditorGUILayout.LabelField("Static Batching", MiniBold, GUILayout.MinWidth(180),
                    GUILayout.MaxWidth(260));
            }

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1));
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.2f));
        }

        // ------------------------------
        // Draw Groups
        // ------------------------------
        private string FoldKey(UnityEngine.Object key)
        {
            string type = key != null ? key.GetType().Name : "None";
            string id = key != null ? key.GetInstanceID().ToString() : "0";
            return $"{type}:{id}";
        }

        // Dentro de DrawRows<T>()
        private void DrawRows<T>(
            List<GroupRow<T>> rows,
            Func<T, string> headerGen,
            Func<T, string> assetPathResolver)
            where T : UnityEngine.Object
        {
            foreach (GroupRow<T> row in rows)
            {
                T key = row.Key;
                List<RendererEntry> list = row.VisibleList;
                GroupCost cost = row.Cost;

                string header =
                    $"{headerGen(key)} [{cost.RendererCount}] — DC≈{cost.PotentialDrawCalls}, Tex≈{TextureUtil.FormatBytes(cost.TextureBytes)}";
                string fkey = FoldKey(key);

                if (!_foldouts.ContainsKey(fkey)) _foldouts[fkey] = false;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    // Header
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _foldouts[fkey] = EditorGUILayout.Foldout(_foldouts[fkey], header, true);
                        GUILayout.FlexibleSpace();

                        // Decide target based on current states: if any inactive => Enable All; else Disable All.
                        bool anyInactive = list.Any(e =>
                            e != null && e.Renderer != null && !e.Renderer.gameObject.activeSelf);
                        string toggleLabel = anyInactive ? "Enable All" : "Disable All";

                        if (GUILayout.Button(toggleLabel, GUILayout.Width(100)))
                        {
                            try
                            {
                                Undo.IncrementCurrentGroup();
                                Undo.SetCurrentGroupName("Toggle Group Active");
                                int undoGroup = Undo.GetCurrentGroup();

                                bool targetActive = anyInactive; // enable if there is any inactive; else disable all
                                foreach (RendererEntry e in list)
                                {
                                    if (e == null || e.Renderer == null) continue;

                                    GameObject go = e.Renderer.gameObject;
                                    if (go == null || go.activeSelf == targetActive) continue;

                                    // Record undo and apply
                                    Undo.RegisterCompleteObjectUndo(go, "Toggle Active");
                                    go.SetActive(targetActive);
                                    EditorUtility.SetDirty(go);
                                }

                                Undo.CollapseUndoOperations(undoGroup);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"Renderer Audit: Toggle Group Active failed.\n{ex}");
                            }

                            // Rescan to reflect changes in UI immediately.
                            RefreshNow();
                            // Early continue because RefreshNow will rebuild data; avoid drawing stale UI this frame.
                            return;
                        }

                        if (key != null)
                        {
                            if (GUILayout.Button("Ping", GUILayout.Width(50)))
                            {
                                EditorGUIUtility.PingObject(key);
                                Selection.activeObject = key;
                            }

                            string path = assetPathResolver(key);
                            if (!string.IsNullOrEmpty(path))
                                EditorGUILayout.LabelField(path, EditorStyles.miniLabel, GUILayout.MaxWidth(420));
                        }
                    }

                    if (_foldouts[fkey])
                    {
                        // Pagination controls
                        (int start, int end, int page, int totalPages) = _pagination.GetRange(fkey, list.Count);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"Items {start + 1}–{end} of {list.Count}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();

                            GUI.enabled = page > 0;
                            if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _pagination.SetPage(fkey, page - 1);
                            GUI.enabled = page < totalPages - 1;
                            if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _pagination.SetPage(fkey, page + 1);
                            GUI.enabled = true;

                            GUILayout.Space(8);
                            GUILayout.Label($"Page {page + 1}/{totalPages}", EditorStyles.miniLabel);
                        }

                        // Column headers
                        DrawEntryHeaderRow();

                        // Page slice
                        for (int i = start; i < end; i++)
                        {
                            DrawEntryRow(list[i]);
                        }
                    }
                }
            }
        }

        private void DrawSimpleGroups<T>(
            IEnumerable<KeyValuePair<T, List<RendererEntry>>> groups,
            Func<T, string> headerGen,
            List<RendererEntry> nullBucket)
            where T : UnityEngine.Object
        {
            foreach (KeyValuePair<T, List<RendererEntry>> kv in groups)
            {
                T key = kv.Key;
                List<RendererEntry> list = kv.Value;
                if (list == null || list.Count == 0) continue;

                string header = $"{headerGen(key)} [{list.Count}]";
                string fkey = FoldKey(key);
                if (!_foldouts.ContainsKey(fkey)) _foldouts[fkey] = false;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _foldouts[fkey] = EditorGUILayout.Foldout(_foldouts[fkey], header, true);
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                        {
                            EditorGUIUtility.PingObject(key);
                            Selection.activeObject = key;
                        }
                    }

                    if (_foldouts[fkey])
                    {
                        (int start, int end, int page, int totalPages) = _pagination.GetRange(fkey, list.Count);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"Items {start + 1}–{end} of {list.Count}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();

                            GUI.enabled = page > 0;
                            if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _pagination.SetPage(fkey, page - 1);
                            GUI.enabled = page < totalPages - 1;
                            if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _pagination.SetPage(fkey, page + 1);
                            GUI.enabled = true;

                            GUILayout.Space(8);
                            GUILayout.Label($"Page {page + 1}/{totalPages}", EditorStyles.miniLabel);
                        }

                        DrawEntryHeaderRow();

                        for (int i = start; i < end; i++)
                        {
                            DrawEntryRow(list[i]);
                        }
                    }
                }
            }

            if (nullBucket != null && nullBucket.Count > 0)
            {
                string header = $"{headerGen(null) ?? "(None)"}";
                string fkey = $"Null:{typeof(T).Name}";
                if (!_foldouts.ContainsKey(fkey)) _foldouts[fkey] = false;

                bool Pred(RendererEntry e)
                {
                    return InstancingUtil.PassesInstancingFilter(e, _instancingFilter) &&
                           PassesBatchingFilter(e, _batchingFilter) &&
                           PassesShadowFilter(e, _shadowFilter) &&
                           PassesTransparencyFilter(e, _transparencyFilter);
                }

                List<RendererEntry> visible = nullBucket.Where(Pred).ToList();
                if (visible.Count == 0) return;

                header = $"{typeof(T).Name}: (None) [{visible.Count}]";

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _foldouts[fkey] = EditorGUILayout.Foldout(_foldouts[fkey], header, true);
                        GUILayout.FlexibleSpace();
                    }

                    if (_foldouts[fkey])
                    {
                        (int start, int end, int page, int totalPages) = _pagination.GetRange(fkey, visible.Count);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label($"Items {start + 1}–{end} of {visible.Count}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();

                            GUI.enabled = page > 0;
                            if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _pagination.SetPage(fkey, page - 1);
                            GUI.enabled = page < totalPages - 1;
                            if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _pagination.SetPage(fkey, page + 1);
                            GUI.enabled = true;

                            GUILayout.Space(8);
                            GUILayout.Label($"Page {page + 1}/{totalPages}", EditorStyles.miniLabel);
                        }

                        DrawEntryHeaderRow();

                        for (int i = start; i < end; i++)
                        {
                            DrawEntryRow(visible[i]);
                        }
                    }
                }
            }
        }

        private void DrawNullGroup(string headerBase, List<RendererEntry> list, Func<RendererEntry, bool> predicate)
        {
            if (list == null || list.Count == 0) return;

            List<RendererEntry> visible = list.Where(predicate).ToList();
            if (visible.Count == 0) return;

            string header = $"{headerBase} [{visible.Count}]";
            string fkey = $"Null:{headerBase}";
            if (!_foldouts.ContainsKey(fkey)) _foldouts[fkey] = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _foldouts[fkey] = EditorGUILayout.Foldout(_foldouts[fkey], header, true);
                    GUILayout.FlexibleSpace();
                }

                if (_foldouts[fkey])
                {
                    (int start, int end, int page, int totalPages) = _pagination.GetRange(fkey, visible.Count);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"Items {start + 1}–{end} of {visible.Count}", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();

                        GUI.enabled = page > 0;
                        if (GUILayout.Button("◀ Prev", GUILayout.Width(70))) _pagination.SetPage(fkey, page - 1);
                        GUI.enabled = page < totalPages - 1;
                        if (GUILayout.Button("Next ▶", GUILayout.Width(70))) _pagination.SetPage(fkey, page + 1);
                        GUI.enabled = true;

                        GUILayout.Space(8);
                        GUILayout.Label($"Page {page + 1}/{totalPages}", EditorStyles.miniLabel);
                    }

                    DrawEntryHeaderRow();

                    for (int i = start; i < end; i++)
                    {
                        DrawEntryRow(visible[i]);
                    }
                }
            }
        }

        // ------------------------------
        // Tooltip Helpers
        // ------------------------------
        /// <summary>
        /// Draws a label and queues a custom tooltip when hovered.
        /// </summary>
        private void LabelWithTooltip(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(content, style, options);
            GUI.Label(r, content, style);

            if (!string.IsNullOrEmpty(content.tooltip) &&
                r.Contains(Event.current.mousePosition))
            {
                _queuedTooltip = content.tooltip;
                _queuedTooltipPos = Event.current.mousePosition + new Vector2(16f, 18f);
                if (Event.current.type == EventType.Repaint)
                    Repaint();
            }
        }

        /// <summary>
        /// Shows the queued tooltip, clamped to the window bounds.
        /// </summary>
        private void ShowQueuedTooltip()
        {
            if (string.IsNullOrEmpty(_queuedTooltip))
                return;

            GUIContent gc = new(_queuedTooltip);
            Vector2 size = TooltipStyle.CalcSize(gc);
            Rect rect = new(_queuedTooltipPos, size);

            float pad = 8f;
            rect.x = Mathf.Min(rect.x, position.width - rect.width - pad);
            rect.y = Mathf.Min(rect.y, position.height - rect.height - pad);

            GUI.Label(rect, gc, TooltipStyle);
        }

        // ------------------------------
        // Entry Row (single renderer)
        // ------------------------------
        private void DrawEntryRow(RendererEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // Fixed columns: Type / Static / Scene / Path
                string typeLabel = entry.IsParticleSystem
                    ? "ParticleSystemRenderer"
                    : entry.IsSkinned
                        ? "SkinnedMeshRenderer"
                        : "MeshRenderer";

                GUIContent typeGc = new(typeLabel, typeLabel);
                GUIContent staticGc = new(entry.IsStatic ? "Static" : "Non-Static",
                    entry.IsStatic ? "Static" : "Non-Static");
                GUIContent sceneGc = new(entry.SceneName, entry.SceneName);
                GUIContent pathGc = new(entry.GameObjectPath, entry.GameObjectPath);

                LabelWithTooltip(typeGc, EditorStyles.label, GUILayout.Width(170));
                LabelWithTooltip(staticGc, EditorStyles.label, GUILayout.Width(90));
                LabelWithTooltip(sceneGc, EditorStyles.label, GUILayout.Width(200));

                if (GUILayout.Button(pathGc, EditorStyles.linkLabel, GUILayout.Width(260)))
                    if (entry.Renderer != null)
                    {
                        Selection.activeObject = entry.Renderer.gameObject;
                        EditorGUIUtility.PingObject(entry.Renderer.gameObject);
                    }

                // Manual tooltip for the link rect
                Rect pathRect = GUILayoutUtility.GetLastRect();
                if (pathRect.Contains(Event.current.mousePosition) && !string.IsNullOrEmpty(pathGc.tooltip))
                {
                    _queuedTooltip = pathGc.tooltip;
                    _queuedTooltipPos = Event.current.mousePosition + new Vector2(16f, 18f);
                    if (Event.current.type == EventType.Repaint) Repaint();
                }

                // Detail columns: Materials | Shaders | Textures | Shadows | Transparency | Instancing | Dynamic | Static
                string matNames = string.Join(", ",
                    entry.Materials.Where(m => m != null).Select(m => m.name).Distinct());
                string shaderNames =
                    string.Join(", ", entry.Shaders.Where(s => s != null).Select(s => s.name).Distinct());
                string texNames = string.Join(", ",
                    entry.Textures.Where(t => t != null).Select(t => TextureUtil.GetTextureDisplayName(t, 28))
                        .Distinct());

                string shadowTxt = BuildShadowText(entry);

                string transpTxt = entry.HasTransparentMaterial
                    ? $"Transparent ({entry.TransparentMaterialCount}), MaxQ={entry.MaxRenderQueue}" +
                      (entry.TransparencyOverdrawRisk ? " — Overdraw risk!" : "")
                    : "Opaque";
                string transpTip = entry.HasTransparentMaterial
                    ? $"Transparent materials: {entry.TransparentMaterialCount}\nMax Render Queue: {entry.MaxRenderQueue}\nOverdraw risk: {entry.TransparencyOverdrawRisk}"
                    : "No transparent materials (opaque).";

                string instancing = InstancingUtil.GetRendererInstancingLabel(entry);
                string dyn = entry.DynamicBatchEligible
                    ? "Dynamic: Eligible"
                    : $"Dynamic: Not ({string.Join(", ", entry.DynamicBatchReasons)})";
                string stat = entry.StaticBatchEligible
                    ? "Static: Eligible"
                    : $"Static: Not ({string.Join(", ", entry.StaticBatchReasons)})";

                GUIContent matsGc = new(matNames, matNames);
                GUIContent shGc = new(shaderNames, shaderNames);
                GUIContent texGc = new(texNames, texNames);
                GUIContent shadGc = new(shadowTxt, shadowTxt);
                GUIContent transpGc = new(transpTxt, transpTip);
                GUIContent instGc = new(instancing, instancing);
                GUIContent dynGc = new(dyn, dyn);
                GUIContent statGc = new(stat, stat);

                LabelWithTooltip(matsGc, EditorStyles.miniLabel, GUILayout.MinWidth(160), GUILayout.MaxWidth(220));
                LabelWithTooltip(shGc, EditorStyles.miniLabel, GUILayout.MinWidth(140), GUILayout.MaxWidth(200));
                LabelWithTooltip(texGc, EditorStyles.miniLabel, GUILayout.MinWidth(180), GUILayout.MaxWidth(260));
                LabelWithTooltip(shadGc, EditorStyles.miniLabel, GUILayout.MinWidth(160), GUILayout.MaxWidth(200));
                LabelWithTooltip(transpGc, EditorStyles.miniLabel, GUILayout.MinWidth(180), GUILayout.MaxWidth(260));
                LabelWithTooltip(instGc, EditorStyles.miniLabel, GUILayout.MinWidth(140), GUILayout.MaxWidth(180));
                LabelWithTooltip(dynGc, EditorStyles.miniLabel, GUILayout.MinWidth(180), GUILayout.MaxWidth(260));
                LabelWithTooltip(statGc, EditorStyles.miniLabel, GUILayout.MinWidth(180), GUILayout.MaxWidth(260));
            }
        }

        private static string BuildShadowText(RendererEntry e)
        {
            string cast = e.CastsShadows ? "Cast" : "No Cast";
            string recv = e.ReceivesShadows ? "Receive" : "No Receive";
            string extra = e.ReceivesShadowProbablyUnnecessary ? " — Receive unnecessary?" : "";
            return $"{cast}, {recv}{extra}";
        }

        // ------------------------------
        // Refresh
        // ------------------------------
        private void RefreshNow()
        {
            try
            {
                // Prefer SceneView camera; fallback to Camera.main.
                Camera cam = null;
                if (SceneView.lastActiveSceneView != null)
                    cam = SceneView.lastActiveSceneView.camera;
                if (cam == null) cam = Camera.main;

                bool hasCam = cam != null;
                Vector3 camPos = hasCam ? cam.transform.position : Vector3.zero;

                SceneScanner scanner = new(
                    _includeInactive,
                    _staticFilter,
                    _includeMeshRenderer,
                    _includeSkinnedMeshRenderer,
                    _includeParticleSystem,
                    _shadowSmallSizeThreshold,
                    _shadowFarDistanceThreshold,
                    hasCam,
                    camPos,
                    _transpSizeOverDistThreshold);

                (_entries, _summary) = scanner.Scan();
                _index = new GroupIndex(_entries);
                _pagination.ResetAll();

                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Renderer Audit: Refresh failed.\n{ex}");
            }
        }
    }
}