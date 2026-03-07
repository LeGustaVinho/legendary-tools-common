using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderController
    {
        private static readonly string[] SearchExtensions =
        {
            ".unity", ".prefab", ".mat", ".asset", ".anim", ".controller", ".overrideController", ".shader",
            ".compute", ".playable"
        };

        private readonly AssetGuidMapper _guidMapper;
        private readonly AssetUsageFinderUsageScanner _usageScanner;
        private readonly AssetUsageFinderContextualBackend _contextualBackend;
        private readonly string _jsonPath;

        private readonly AssetUsageFinderSerializedFieldFinderBackend _serializedFieldBackend;
        private readonly AssetUsageFinderSerializedFieldValueReplaceBackend _serializedFieldValueReplaceBackend;
        private readonly AssetUsageFinderPrefabOrVariantReplaceBackend _prefabOrVariantReplaceBackend;
        private readonly AssetUsageFinderComponentReplaceBackend _componentReplaceBackend;
        private CancellationTokenSource _serializedFieldCts;
        private CancellationTokenSource _contextualFinderCts;

        public AssetUsageFinderState State { get; } = new();

        public event Action StateChanged;

        public AssetUsageFinderController(
            AssetGuidMapper guidMapper,
            AssetUsageFinderUsageScanner usageScanner,
            AssetUsageFinderContextualBackend contextualBackend,
            string jsonPath,
            AssetUsageFinderSerializedFieldFinderBackend serializedFieldBackend,
            AssetUsageFinderSerializedFieldValueReplaceBackend serializedFieldValueReplaceBackend,
            AssetUsageFinderPrefabOrVariantReplaceBackend prefabOrVariantReplaceBackend,
            AssetUsageFinderComponentReplaceBackend componentReplaceBackend)
        {
            _guidMapper = guidMapper ?? throw new ArgumentNullException(nameof(guidMapper));
            _usageScanner = usageScanner ?? throw new ArgumentNullException(nameof(usageScanner));
            _contextualBackend = contextualBackend ?? throw new ArgumentNullException(nameof(contextualBackend));
            _jsonPath = jsonPath ?? throw new ArgumentNullException(nameof(jsonPath));
            _serializedFieldBackend =
                serializedFieldBackend ?? throw new ArgumentNullException(nameof(serializedFieldBackend));
            _serializedFieldValueReplaceBackend = serializedFieldValueReplaceBackend ??
                                                  throw new ArgumentNullException(
                                                      nameof(serializedFieldValueReplaceBackend));
            _prefabOrVariantReplaceBackend =
                prefabOrVariantReplaceBackend ?? throw new ArgumentNullException(nameof(prefabOrVariantReplaceBackend));
            _componentReplaceBackend =
                componentReplaceBackend ?? throw new ArgumentNullException(nameof(componentReplaceBackend));
        }

        public void Initialize()
        {
            // Best-effort load mapping cache.
            _guidMapper.LoadMappingFromJson(_jsonPath);
            NotifyChanged();
        }

        public void SetTarget(Object target)
        {
            State.TargetAsset = target;
            NotifyChanged();
        }

        public void ClearCache()
        {
            _guidMapper.ClearMapping();
            if (File.Exists(_jsonPath))
                File.Delete(_jsonPath);

            State.Entries.Clear();
            State.StatusMessage = "Cache cleared and JSON file deleted.";

            CancelSerializedFieldFinder();
            State.SerializedFieldFinderResults.Clear();
            State.SerializedFieldFinderStatus = "Serialized Field Finder results cleared.";
            State.SerializedFieldFinderIsBusy = false;

            CancelContextualFinder();
            State.ContextualRequest = null;
            State.ContextualResults.Clear();
            State.ContextualFinderStatus = string.Empty;
            State.ContextualFinderIsBusy = false;

            State.SerializedFieldValueReplacePreview.Clear();
            State.SerializedFieldValueReplaceStatus = string.Empty;
            State.SerializedFieldValueReplaceIsBusy = false;

            State.PrefabOrVariantReplacePreview.Clear();
            State.PrefabOrVariantReplaceStatus = string.Empty;
            State.PrefabOrVariantReplaceIsBusy = false;

            State.ComponentReplacePreview.Clear();
            State.ComponentReplaceStatus = string.Empty;
            State.ComponentReplaceIsBusy = false;

            NotifyChanged();
        }

        // ---------------- Finder: By Asset (existing) ----------------

        public async void FindUsagesAsync()
        {
            if (State.IsBusy)
            {
                Debug.LogWarning("Mapping is already in progress.");
                return;
            }

            State.Entries.Clear();
            State.StatusMessage = string.Empty;

            if (State.TargetAsset == null)
            {
                State.StatusMessage = "Please select an asset or script to find usages.";
                NotifyChanged();
                return;
            }

            List<AssetUsageFinderScopeTarget> scopeTargets =
                AssetUsageFinderSearchScopeUtility.CollectTargets(State.SearchScope, SearchExtensions);

            if (scopeTargets.Count == 0)
            {
                State.StatusMessage =
                    "The selected scope did not resolve to any saved assets or currently open contexts.";
                NotifyChanged();
                return;
            }

            State.IsBusy = true;
            NotifyChanged();

            try
            {
                string assetPathToFind = AssetDatabase.GetAssetPath(State.TargetAsset);
                string guidToFind = AssetDatabase.AssetPathToGUID(assetPathToFind);
                List<AssetUsageFinderEntry> foundEntries = await BuildEntriesForScopeAsync(scopeTargets, guidToFind);

                State.Entries = foundEntries;
                State.StatusMessage = $"Found {State.Entries.Count} references.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                State.StatusMessage = "An error occurred. Check Console.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                State.IsBusy = false;
                NotifyChanged();
            }
        }

        public void SetContextualRequest(AssetUsageFinderContextualRequest request, bool applySuggestedScope)
        {
            CancelContextualFinder();

            State.ContextualRequest = request;
            State.ContextualResults.Clear();
            State.ContextualFinderStatus = request?.Description ?? string.Empty;
            State.ContextualFinderIsBusy = false;

            if (applySuggestedScope && request != null && request.SuggestedScope != AssetUsageFinderSearchScope.None)
                State.SearchScope = request.SuggestedScope;

            NotifyChanged();
        }

        public void CancelContextualFinder()
        {
            try
            {
                _contextualFinderCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            _contextualBackend.CancelScan();
        }

        public void FindContextualUsages(AssetUsageFinderContextualRequest request = null)
        {
            request ??= State.ContextualRequest;

            if (State.ContextualFinderIsBusy)
            {
                Debug.LogWarning("Contextual Find Usages is already running.");
                return;
            }

            State.ContextualResults.Clear();
            State.ContextualRequest = request;

            if (request == null)
            {
                State.ContextualFinderStatus =
                    "Use a GameObject, Component, or SerializedProperty context menu to start a contextual search.";
                NotifyChanged();
                return;
            }

            if (!AssetUsageFinderSearchScopeUtility.HasAnySelection(State.SearchScope))
            {
                State.ContextualFinderStatus = "Select at least one scope before scanning.";
                NotifyChanged();
                return;
            }

            _contextualFinderCts?.Dispose();
            _contextualFinderCts = new CancellationTokenSource();

            State.ContextualFinderIsBusy = true;
            State.ContextualFinderStatus = $"Scanning {request.DisplayName}...";
            NotifyChanged();

            EditorUtility.DisplayCancelableProgressBar("Find Usages", "Starting...", 0f);

            _contextualBackend.StartScan(
                request,
                State.SearchScope,
                (progress, message) =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Find Usages", message, progress))
                        _contextualFinderCts.Cancel();

                    State.ContextualFinderStatus = message;
                    NotifyChanged();
                },
                results =>
                {
                    State.ContextualResults = results;
                    State.ContextualFinderStatus = $"Found {results.Count} usage(s) for {request.DisplayName}.";
                    State.ContextualFinderIsBusy = false;

                    EditorUtility.ClearProgressBar();
                    NotifyChanged();
                },
                () =>
                {
                    State.ContextualFinderStatus = "Find Usages canceled.";
                    State.ContextualFinderIsBusy = false;

                    EditorUtility.ClearProgressBar();
                    NotifyChanged();
                },
                ex =>
                {
                    Debug.LogException(ex);
                    State.ContextualFinderStatus = "An error occurred. Check Console.";
                    State.ContextualFinderIsBusy = false;

                    EditorUtility.ClearProgressBar();
                    NotifyChanged();
                },
                _contextualFinderCts.Token);
        }

        private async Task<List<AssetUsageFinderEntry>> BuildEntriesForScopeAsync(
            IReadOnlyList<AssetUsageFinderScopeTarget> scopeTargets,
            string guidToFind)
        {
            List<AssetUsageFinderEntry> entries = new();
            if (scopeTargets == null || scopeTargets.Count == 0 || State.TargetAsset == null)
                return entries;

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (AssetUsageFinderScopeTarget target in scopeTargets)
            {
                AssetUsageFinderEntry entry = await TryBuildEntryForScopeTargetAsync(target, guidToFind);
                if (entry == null || string.IsNullOrEmpty(entry.AssetPath) || !seen.Add(entry.AssetPath))
                    continue;

                entries.Add(entry);
            }

            return entries;
        }

        private async Task<AssetUsageFinderEntry> TryBuildEntryForScopeTargetAsync(
            AssetUsageFinderScopeTarget target,
            string guidToFind)
        {
            if (target == null || string.IsNullOrEmpty(target.AssetPath) || State.TargetAsset == null)
                return null;

            switch (target.Kind)
            {
                case AssetUsageFinderScopeTargetKind.OpenScene:
                    return _usageScanner.FindUsagesInOpenScene(State.TargetAsset).Count > 0
                        ? new AssetUsageFinderEntry(target.AssetPath, AssetUsageFinderUsageType.Scene)
                        : null;

                case AssetUsageFinderScopeTargetKind.OpenPrefabStage:
                    return _usageScanner.FindUsagesInOpenPrefabStage(State.TargetAsset).Count > 0
                        ? new AssetUsageFinderEntry(target.AssetPath, AssetUsageFinderUsageType.Prefab)
                        : null;
            }

            string path = target.AssetPath;
            string ext = Path.GetExtension(path);

            if (string.Equals(ext, ".unity", StringComparison.OrdinalIgnoreCase))
            {
                return _usageScanner.HasUsagesInSceneAsset(path, State.TargetAsset)
                    ? new AssetUsageFinderEntry(path, AssetUsageFinderUsageType.Scene)
                    : null;
            }

            if (string.Equals(ext, ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return _usageScanner.HasUsagesInPrefabAsset(path, State.TargetAsset)
                    ? new AssetUsageFinderEntry(path, AssetUsageFinderUsageType.Prefab)
                    : null;
            }

            bool fileContainsGuid = await _guidMapper.FileContainsGuidAsync(path, guidToFind);
            if (!fileContainsGuid)
                return null;

            AssetUsageFinderUsageType usageType = GetUsageTypeForAssetPath(path);
            return new AssetUsageFinderEntry(path, usageType);
        }

        private static AssetUsageFinderUsageType GetUsageTypeForAssetPath(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            return ext switch
            {
                ".unity" => AssetUsageFinderUsageType.Scene,
                ".prefab" => AssetUsageFinderUsageType.Prefab,
                ".mat" => AssetUsageFinderUsageType.Material,
                ".asset" => AssetDatabase.LoadAssetAtPath<Object>(path) is ScriptableObject
                    ? AssetUsageFinderUsageType.ScriptableObject
                    : AssetUsageFinderUsageType.Other,
                _ => AssetUsageFinderUsageType.Other
            };
        }

        // ---------------- Finder: By Serialized Field (new backend) ----------------

        public void CancelSerializedFieldFinder()
        {
            try
            {
                _serializedFieldCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            _serializedFieldBackend.CancelScan();
        }

        public void FindBySerializedField(List<SerializedFieldFilterRow> filters)
        {
            if (State.SerializedFieldFinderIsBusy)
            {
                Debug.LogWarning("Serialized Field Finder is already running.");
                return;
            }

            State.SerializedFieldFinderResults.Clear();
            State.SerializedFieldFinderStatus = string.Empty;

            if (filters == null || filters.Count == 0)
            {
                State.SerializedFieldFinderStatus = "Please add at least one filter row.";
                NotifyChanged();
                return;
            }

            if (!AssetUsageFinderSearchScopeUtility.HasAnySelection(State.SearchScope))
            {
                State.SerializedFieldFinderStatus = "Select at least one scope before scanning.";
                NotifyChanged();
                return;
            }

            _serializedFieldCts?.Dispose();
            _serializedFieldCts = new CancellationTokenSource();

            State.SerializedFieldFinderIsBusy = true;
            State.SerializedFieldFinderStatus = "Scanning selected scope...";
            NotifyChanged();

            EditorUtility.DisplayCancelableProgressBar("Serialized Field Finder", "Starting...", 0f);

            _serializedFieldBackend.StartScan(
                filters,
                State.SearchScope,
                (p, msg) =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Serialized Field Finder", msg, p))
                        _serializedFieldCts.Cancel();

                    State.SerializedFieldFinderStatus = msg;
                    NotifyChanged();
                },
                matches =>
                {
                    State.SerializedFieldFinderResults = matches
                        .Select(m => new AssetUsageFinderSerializedFieldResult(
                            m.FileAssetPath,
                            m.ObjectPath,
                            m.ObjectTypeName,
                            m.PropertyPath,
                            m.CurrentValue))
                        .ToList();

                    State.SerializedFieldFinderStatus =
                        $"Found {State.SerializedFieldFinderResults.Count} match(es).";
                    State.SerializedFieldFinderIsBusy = false;

                    EditorUtility.ClearProgressBar();
                    NotifyChanged();
                },
                () =>
                {
                    State.SerializedFieldFinderStatus = "Scan canceled.";
                    State.SerializedFieldFinderIsBusy = false;

                    EditorUtility.ClearProgressBar();
                    NotifyChanged();
                },
                ex =>
                {
                    Debug.LogException(ex);
                    State.SerializedFieldFinderStatus = "An error occurred. Check Console.";
                    State.SerializedFieldFinderIsBusy = false;

                    EditorUtility.ClearProgressBar();
                    NotifyChanged();
                },
                _serializedFieldCts.Token);
        }

        // ---------------- Replace: Serialized Field Value ----------------

        public void PreviewReplaceSerializedFieldValue(
            List<SerializedFieldFilterRow> filters,
            SerializedFieldValueBox replaceWithValue)
        {
            if (!TryCreateSerializedFieldValueReplaceRequest(
                    filters,
                    replaceWithValue,
                    State.SearchScope,
                    out AssetUsageFinderSerializedFieldValueReplaceRequest request,
                    out string validationMessage))
            {
                State.SerializedFieldValueReplacePreview.Clear();
                State.SerializedFieldValueReplaceStatus = validationMessage;
                NotifyChanged();
                return;
            }

            RunSerializedFieldValueReplace(request, false);
        }

        public void ApplyReplaceSerializedFieldValue(
            List<SerializedFieldFilterRow> filters,
            SerializedFieldValueBox replaceWithValue)
        {
            if (!TryCreateSerializedFieldValueReplaceRequest(
                    filters,
                    replaceWithValue,
                    State.SearchScope,
                    out AssetUsageFinderSerializedFieldValueReplaceRequest request,
                    out string validationMessage))
            {
                State.SerializedFieldValueReplaceStatus = validationMessage;
                NotifyChanged();
                return;
            }

            RunSerializedFieldValueReplace(request, true);
        }

        private void RunSerializedFieldValueReplace(
            AssetUsageFinderSerializedFieldValueReplaceRequest request,
            bool applyChanges)
        {
            if (State.SerializedFieldValueReplaceIsBusy)
            {
                Debug.LogWarning("Serialized field value replace is already running.");
                return;
            }

            State.SerializedFieldValueReplaceIsBusy = true;
            State.SerializedFieldValueReplaceStatus = applyChanges ? "Applying changes..." : "Building preview...";
            NotifyChanged();

            try
            {
                AssetUsageFinderSerializedFieldValueReplaceResult result = applyChanges
                    ? _serializedFieldValueReplaceBackend.Apply(
                        request,
                        (progress, message) =>
                        {
                            EditorUtility.DisplayProgressBar("Replace Serialized Field Value", message, progress);
                            State.SerializedFieldValueReplaceStatus = message;
                            NotifyChanged();
                        })
                    : _serializedFieldValueReplaceBackend.BuildPreview(
                        request,
                        (progress, message) =>
                        {
                            EditorUtility.DisplayProgressBar("Replace Serialized Field Value", message, progress);
                            State.SerializedFieldValueReplaceStatus = message;
                            NotifyChanged();
                        });

                State.SerializedFieldValueReplacePreview = result.Items;
                State.SerializedFieldValueReplaceStatus = applyChanges
                    ? result.ReplacedValueCount > 0
                        ? $"Applied {result.ReplacedValueCount} replacement(s) in {result.AffectedFileCount} file(s)."
                        : "No matching serialized fields changed."
                    : result.Items.Count > 0
                        ? $"Preview contains {result.Items.Count} replacement(s) in {result.AffectedFileCount} file(s)."
                        : "No matching serialized fields found.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                State.SerializedFieldValueReplaceStatus = "An error occurred. Check Console.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                State.SerializedFieldValueReplaceIsBusy = false;
                NotifyChanged();
            }
        }

        // ---------------- Replace: Prefab / Variant ----------------

        public void PreviewReplacePrefabOrVariant(
            Object fromPrefab,
            Object toPrefab,
            bool includeVariants,
            bool keepOverrides,
            bool copyCommonRootComponentValues)
        {
            if (!TryCreatePrefabOrVariantReplaceRequest(
                    fromPrefab,
                    toPrefab,
                    includeVariants,
                    keepOverrides,
                    copyCommonRootComponentValues,
                    State.SearchScope,
                    out AssetUsageFinderPrefabOrVariantReplaceRequest request, out string validationMessage))
            {
                State.PrefabOrVariantReplacePreview.Clear();
                State.PrefabOrVariantReplaceStatus = validationMessage;
                NotifyChanged();
                return;
            }

            RunPrefabOrVariantReplace(request, false);
        }

        public void ApplyReplacePrefabOrVariant(
            Object fromPrefab,
            Object toPrefab,
            bool includeVariants,
            bool keepOverrides,
            bool copyCommonRootComponentValues)
        {
            if (!TryCreatePrefabOrVariantReplaceRequest(
                    fromPrefab,
                    toPrefab,
                    includeVariants,
                    keepOverrides,
                    copyCommonRootComponentValues,
                    State.SearchScope,
                    out AssetUsageFinderPrefabOrVariantReplaceRequest request, out string validationMessage))
            {
                State.PrefabOrVariantReplaceStatus = validationMessage;
                NotifyChanged();
                return;
            }

            RunPrefabOrVariantReplace(request, true);
        }

        private void RunPrefabOrVariantReplace(
            AssetUsageFinderPrefabOrVariantReplaceRequest request,
            bool applyChanges)
        {
            if (State.PrefabOrVariantReplaceIsBusy)
            {
                Debug.LogWarning("Prefab / Variant replace is already running.");
                return;
            }

            State.PrefabOrVariantReplaceIsBusy = true;
            State.PrefabOrVariantReplaceStatus = applyChanges ? "Applying changes..." : "Building preview...";
            NotifyChanged();

            try
            {
                AssetUsageFinderPrefabOrVariantReplaceResult result = applyChanges
                    ? _prefabOrVariantReplaceBackend.Apply(
                        request,
                        (progress, message) =>
                        {
                            EditorUtility.DisplayProgressBar("Replace Prefab / Variant", message, progress);
                            State.PrefabOrVariantReplaceStatus = message;
                            NotifyChanged();
                        })
                    : _prefabOrVariantReplaceBackend.BuildPreview(
                        request,
                        (progress, message) =>
                        {
                            EditorUtility.DisplayProgressBar("Replace Prefab / Variant", message, progress);
                            State.PrefabOrVariantReplaceStatus = message;
                            NotifyChanged();
                        });

                State.PrefabOrVariantReplacePreview = result.Items;
                State.PrefabOrVariantReplaceStatus = applyChanges
                    ? result.ReplacedInstanceCount > 0
                        ? $"Applied {result.ReplacedInstanceCount} replacement(s) in {result.AffectedFileCount} file(s)."
                        : "No matching prefab instances found."
                    : result.Items.Count > 0
                        ? $"Preview contains {result.Items.Count} replacement(s) in {result.AffectedFileCount} file(s)."
                        : "No matching prefab instances found.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                State.PrefabOrVariantReplaceStatus = "An error occurred. Check Console.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                State.PrefabOrVariantReplaceIsBusy = false;
                NotifyChanged();
            }
        }

        // ---------------- Replace: Component ----------------

        public void PreviewReplaceComponent(
            string fromTypeName,
            string toTypeName,
            bool copySerializedValues,
            bool disableOldComponentInsteadOfRemove)
        {
            if (!TryCreateComponentReplaceRequest(
                    fromTypeName,
                    toTypeName,
                    copySerializedValues,
                    disableOldComponentInsteadOfRemove,
                    State.SearchScope,
                    out AssetUsageFinderComponentReplaceRequest request,
                    out string validationMessage))
            {
                State.ComponentReplacePreview.Clear();
                State.ComponentReplaceStatus = validationMessage;
                NotifyChanged();
                return;
            }

            RunComponentReplace(request, false);
        }

        public void ApplyReplaceComponent(
            string fromTypeName,
            string toTypeName,
            bool copySerializedValues,
            bool disableOldComponentInsteadOfRemove)
        {
            if (!TryCreateComponentReplaceRequest(
                    fromTypeName,
                    toTypeName,
                    copySerializedValues,
                    disableOldComponentInsteadOfRemove,
                    State.SearchScope,
                    out AssetUsageFinderComponentReplaceRequest request,
                    out string validationMessage))
            {
                State.ComponentReplaceStatus = validationMessage;
                NotifyChanged();
                return;
            }

            RunComponentReplace(request, true);
        }

        private void RunComponentReplace(
            AssetUsageFinderComponentReplaceRequest request,
            bool applyChanges)
        {
            if (State.ComponentReplaceIsBusy)
            {
                Debug.LogWarning("Component replace is already running.");
                return;
            }

            State.ComponentReplaceIsBusy = true;
            State.ComponentReplaceStatus = applyChanges ? "Applying changes..." : "Building preview...";
            NotifyChanged();

            try
            {
                AssetUsageFinderComponentReplaceResult result = applyChanges
                    ? _componentReplaceBackend.Apply(
                        request,
                        (progress, message) =>
                        {
                            EditorUtility.DisplayProgressBar("Replace Component", message, progress);
                            State.ComponentReplaceStatus = message;
                            NotifyChanged();
                        })
                    : _componentReplaceBackend.BuildPreview(
                        request,
                        (progress, message) =>
                        {
                            EditorUtility.DisplayProgressBar("Replace Component", message, progress);
                            State.ComponentReplaceStatus = message;
                            NotifyChanged();
                        });

                State.ComponentReplacePreview = result.Items;
                State.ComponentReplaceStatus = applyChanges
                    ? result.ReplacedComponentCount > 0
                        ? $"Applied {result.ReplacedComponentCount} replacement(s) in {result.AffectedFileCount} file(s)."
                        : "No matching components found."
                    : result.Items.Count > 0
                        ? $"Preview contains {result.Items.Count} replacement(s) in {result.AffectedFileCount} file(s)."
                        : "No matching components found.";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                State.ComponentReplaceStatus = "An error occurred. Check Console.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                State.ComponentReplaceIsBusy = false;
                NotifyChanged();
            }
        }

        // ---------------- Shared helpers ----------------

        public bool IsEntryActive(AssetUsageFinderEntry entry)
        {
            if (entry == null) return false;

            if (entry.UsageType == AssetUsageFinderUsageType.Scene ||
                entry.UsageType == AssetUsageFinderUsageType.SceneWithPrefabInstance)
            {
                Scene activeScene = EditorSceneManager.GetActiveScene();
                if (AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(entry.AssetPath))
                    return activeScene.IsValid() && activeScene.isLoaded && string.IsNullOrEmpty(activeScene.path);

                return !string.IsNullOrEmpty(activeScene.path) &&
                       string.Equals(activeScene.path, entry.AssetPath, StringComparison.OrdinalIgnoreCase);
            }

            if (entry.UsageType == AssetUsageFinderUsageType.Prefab)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                return stage != null &&
                       string.Equals(stage.assetPath, entry.AssetPath, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public bool IsContextualResultActive(AssetUsageFinderContextualResult result)
        {
            if (result == null)
                return false;

            if (result.UsageType == AssetUsageFinderUsageType.Scene ||
                result.UsageType == AssetUsageFinderUsageType.SceneWithPrefabInstance)
            {
                if (AssetUsageFinderSearchScopeUtility.IsUnsavedOpenSceneKey(result.FileAssetPath))
                {
                    Scene activeScene = EditorSceneManager.GetActiveScene();
                    return activeScene.IsValid() && activeScene.isLoaded && string.IsNullOrEmpty(activeScene.path);
                }

                Scene scene = SceneManager.GetSceneByPath(result.FileAssetPath);
                return scene.IsValid() && scene.isLoaded;
            }

            if (result.UsageType == AssetUsageFinderUsageType.Prefab)
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                return stage != null &&
                       string.Equals(stage.assetPath, result.FileAssetPath, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public void FindUsagesInActiveContext(string fileAssetPath)
        {
            if (State.TargetAsset == null)
                return;

            if (!_usageScanner.TryFindUsagesInOpenContext(fileAssetPath, State.TargetAsset,
                    out List<AssetUsageFinderCachedUsage> foundUsages))
            {
                State.StatusMessage = "Open the matching scene or prefab to scan usages for this result.";
                NotifyChanged();
                return;
            }

            _usageScanner.SaveCache(fileAssetPath, foundUsages);
            State.StatusMessage = foundUsages.Count > 0
                ? $"Found {foundUsages.Count} usage(s) in the open context."
                : "No usages found in the open context.";
            NotifyChanged();
        }

        public List<AssetUsageFinderCachedUsage> GetCachedUsages(string fileAssetPath)
        {
            return _usageScanner.LoadCache(fileAssetPath);
        }

        public Object ResolveUsageReference(AssetUsageFinderCachedUsage usage)
        {
            return _usageScanner.ResolveUsageReference(usage);
        }

        public bool TryPingContextualResultObject(AssetUsageFinderContextualResult result)
        {
            if (result == null || !IsContextualResultActive(result))
                return false;

            Object resolved = _usageScanner.ResolveContextualResultReference(
                result.FileAssetPath,
                result.UsageType,
                result.ObjectPath,
                result.ObjectTypeName);
            if (resolved == null)
                return false;

            Selection.activeObject = resolved;
            EditorGUIUtility.PingObject(resolved);
            return true;
        }

        public void SelectResolvedUsage(AssetUsageFinderCachedUsage usage, Object resolvedRef)
        {
            if (usage == null || resolvedRef == null)
                return;

            if (usage.IsComponent)
            {
                Selection.activeObject = resolvedRef;
                EditorGUIUtility.PingObject(resolvedRef);
                return;
            }

            if (resolvedRef is GameObject go)
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
            }
        }

        public void RemoveUsageAndRefreshCache(string fileAssetPath, AssetUsageFinderCachedUsage usage)
        {
            if (State.TargetAsset == null || usage == null)
                return;

            _usageScanner.RemoveUsage(State.TargetAsset, usage);

            if (_usageScanner.TryFindUsagesInOpenContext(fileAssetPath, State.TargetAsset,
                    out List<AssetUsageFinderCachedUsage> newCache))
                _usageScanner.SaveCache(fileAssetPath, newCache);

            NotifyChanged();
        }

        public void FindUsagesForAsset(Object asset)
        {
            if (asset == null)
            {
                Debug.LogWarning("Cannot search: asset is null.");
                return;
            }

            State.TargetAsset = asset;
            NotifyChanged();
            FindUsagesAsync();
        }

        public string GetSelectedPrefabAssetPath()
        {
            return GetPrefabAssetPath(State.TargetAsset);
        }

        public bool IsVariantOfSelectedPrefab(string candidatePrefabPath, string selectedPrefabPath)
        {
            return _usageScanner.IsVariantOfSelectedPrefab(candidatePrefabPath, selectedPrefabPath);
        }

        private static string GetPrefabAssetPath(Object obj)
        {
            if (obj == null) return null;

            string path = AssetDatabase.GetAssetPath(obj);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return null;

            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return null;

            PrefabAssetType type = PrefabUtility.GetPrefabAssetType(go);
            return type == PrefabAssetType.Regular || type == PrefabAssetType.Variant ? path : null;
        }

        private static bool TryCreateSerializedFieldValueReplaceRequest(
            List<SerializedFieldFilterRow> filters,
            SerializedFieldValueBox replaceWithValue,
            AssetUsageFinderSearchScope searchScope,
            out AssetUsageFinderSerializedFieldValueReplaceRequest request,
            out string validationMessage)
        {
            request = null;

            if (!AssetUsageFinderSearchScopeUtility.HasAnySelection(searchScope))
            {
                validationMessage = "Select at least one scope before previewing or applying.";
                return false;
            }

            if (filters == null || filters.Count == 0)
            {
                validationMessage = "Please add at least one filter row.";
                return false;
            }

            SerializedFieldFilterRow firstFilter = filters.FirstOrDefault(filter => filter != null);
            if (firstFilter == null)
            {
                validationMessage = "Please add at least one valid filter row.";
                return false;
            }

            firstFilter.EnsureDefaults();
            Type replaceValueType = firstFilter.EffectiveValueType;

            if (!AssetUsageFinderSerializedFieldValueReplaceBackend.SupportsReplacementType(replaceValueType))
            {
                validationMessage = $"Replace is not supported for type '{replaceValueType?.Name ?? "<null>"}'.";
                return false;
            }

            request = new AssetUsageFinderSerializedFieldValueReplaceRequest(
                filters,
                replaceWithValue,
                replaceValueType,
                searchScope);

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryCreatePrefabOrVariantReplaceRequest(
            Object fromPrefab,
            Object toPrefab,
            bool includeVariants,
            bool keepOverrides,
            bool copyCommonRootComponentValues,
            AssetUsageFinderSearchScope searchScope,
            out AssetUsageFinderPrefabOrVariantReplaceRequest request,
            out string validationMessage)
        {
            request = null;

            if (!AssetUsageFinderSearchScopeUtility.HasAnyHierarchySelection(searchScope))
            {
                validationMessage = "Select at least one scene or prefab scope before previewing or applying.";
                return false;
            }

            string fromPath = GetPrefabAssetPath(fromPrefab);
            string toPath = GetPrefabAssetPath(toPrefab);

            if (string.IsNullOrEmpty(fromPath))
            {
                validationMessage = "Select a valid source prefab or prefab variant.";
                return false;
            }

            if (string.IsNullOrEmpty(toPath))
            {
                validationMessage = "Select a valid target prefab or prefab variant.";
                return false;
            }

            if (string.Equals(fromPath, toPath, StringComparison.OrdinalIgnoreCase))
            {
                validationMessage = "Source and target prefabs must be different.";
                return false;
            }

            GameObject fromGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(fromPath);
            GameObject toGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(toPath);

            if (fromGameObject == null || toGameObject == null)
            {
                validationMessage = "Could not load one of the selected prefabs.";
                return false;
            }

            request = new AssetUsageFinderPrefabOrVariantReplaceRequest(
                fromGameObject,
                toGameObject,
                includeVariants,
                keepOverrides,
                copyCommonRootComponentValues,
                searchScope);

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryCreateComponentReplaceRequest(
            string fromTypeName,
            string toTypeName,
            bool copySerializedValues,
            bool disableOldComponentInsteadOfRemove,
            AssetUsageFinderSearchScope searchScope,
            out AssetUsageFinderComponentReplaceRequest request,
            out string validationMessage)
        {
            request = null;

            if (!AssetUsageFinderSearchScopeUtility.HasAnyHierarchySelection(searchScope))
            {
                validationMessage = "Select at least one scene or prefab scope before previewing or applying.";
                return false;
            }

            if (!TryResolveComponentType(fromTypeName, out Type fromType))
            {
                validationMessage = "Select a valid source component type.";
                return false;
            }

            if (!TryResolveComponentType(toTypeName, out Type toType))
            {
                validationMessage = "Select a valid target component type.";
                return false;
            }

            if (fromType == typeof(Transform))
            {
                validationMessage = "Transform cannot be replaced by this workflow.";
                return false;
            }

            if (toType == typeof(Transform))
            {
                validationMessage = "Transform cannot be added by this workflow.";
                return false;
            }

            if (fromType.IsAbstract || toType.IsAbstract)
            {
                validationMessage = "Component types must be concrete.";
                return false;
            }

            if (fromType.ContainsGenericParameters || toType.ContainsGenericParameters)
            {
                validationMessage = "Generic component types are not supported.";
                return false;
            }

            if (fromType == toType)
            {
                validationMessage = "Source and target component types must be different.";
                return false;
            }

            if (disableOldComponentInsteadOfRemove &&
                !AssetUsageFinderComponentReplaceBackend.SupportsDisableInsteadOfRemove(fromType))
            {
                validationMessage =
                    "The source component type cannot be disabled. Turn off 'Disable old component instead of removing'.";
                return false;
            }

            request = new AssetUsageFinderComponentReplaceRequest(
                fromType,
                toType,
                copySerializedValues,
                disableOldComponentInsteadOfRemove,
                searchScope);

            validationMessage = string.Empty;
            return true;
        }

        private static bool TryResolveComponentType(string typeName, out Type resolvedType)
        {
            resolvedType = null;

            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            string query = typeName.Trim();
            resolvedType = Type.GetType(query, false, true);
            if (resolvedType == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        resolvedType = assembly.GetType(query, false, true);
                    }
                    catch
                    {
                        resolvedType = null;
                    }

                    if (resolvedType != null)
                        break;
                }
            }

            if (resolvedType == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] assemblyTypes;

                    try
                    {
                        assemblyTypes = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        assemblyTypes = ex.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Type candidate in assemblyTypes)
                    {
                        if (candidate == null)
                            continue;

                        bool nameMatches =
                            string.Equals(candidate.Name, query, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(candidate.FullName, query, StringComparison.OrdinalIgnoreCase);

                        if (!nameMatches)
                            continue;

                        resolvedType = candidate;
                        break;
                    }

                    if (resolvedType != null)
                        break;
                }
            }

            return resolvedType != null && typeof(Component).IsAssignableFrom(resolvedType);
        }

        private void NotifyChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
