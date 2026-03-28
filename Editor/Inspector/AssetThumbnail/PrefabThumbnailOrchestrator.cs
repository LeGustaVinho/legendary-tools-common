using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [InitializeOnLoad]
    internal static class PrefabThumbnailOrchestrator
    {
        private const string EnabledEditorPrefKey = "PrefabThumbnailOrchestrator.Enabled";
        private const double ProcessingBudgetSeconds = 0.015d;
        private const int MaxPrefabsPerTick = 2;

        private static readonly Queue<string> PendingAssetGuids = new Queue<string>();
        private static readonly HashSet<string> QueuedAssetGuids = new HashSet<string>(StringComparer.Ordinal);

        private static PrefabThumbnailGenerator.BatchSession _batchSession;
        private static bool _startupScanPending = true;
        private static bool _projectWindowDirty;
        private static bool _isEnabled = EditorPrefs.GetBool(EnabledEditorPrefKey, true);

        static PrefabThumbnailOrchestrator()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;

            AssemblyReloadEvents.beforeAssemblyReload -= DisposeBatchSession;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeBatchSession;

            EditorApplication.quitting -= DisposeBatchSession;
            EditorApplication.quitting += DisposeBatchSession;
        }

        internal static void ClearPendingWork()
        {
            PendingAssetGuids.Clear();
            QueuedAssetGuids.Clear();
            _startupScanPending = false;
            _projectWindowDirty = false;
            DisposeBatchSession();
        }

        internal static bool IsEnabled()
        {
            return _isEnabled;
        }

        internal static void SetEnabled(bool enabled)
        {
            if (_isEnabled == enabled)
            {
                return;
            }

            _isEnabled = enabled;
            EditorPrefs.SetBool(EnabledEditorPrefKey, enabled);

            if (enabled)
            {
                _startupScanPending = true;
            }
            else
            {
                DisposeBatchSession();
            }
        }

        internal static void NotifyPrefabAssetsChanged(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            EnqueuePrefabPaths(importedAssets);
            EnqueuePrefabPaths(movedAssets);

            if (deletedAssets != null)
            {
                _startupScanPending = true;
            }

            if (movedFromAssetPaths != null && movedFromAssetPaths.Length > 0)
            {
                _startupScanPending = true;
            }
        }

        private static void Update()
        {
            if (!_isEnabled)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (_startupScanPending)
            {
                ScanProjectPrefabs();
                _startupScanPending = false;
            }

            if (PendingAssetGuids.Count == 0)
            {
                DisposeBatchSession();

                if (_projectWindowDirty)
                {
                    _projectWindowDirty = false;
                    EditorApplication.RepaintProjectWindow();
                }

                return;
            }

            double endTime = EditorApplication.timeSinceStartup + ProcessingBudgetSeconds;
            int processedCount = 0;

            while (PendingAssetGuids.Count > 0 &&
                   processedCount < MaxPrefabsPerTick &&
                   EditorApplication.timeSinceStartup <= endTime)
            {
                string assetGuid = PendingAssetGuids.Dequeue();
                QueuedAssetGuids.Remove(assetGuid);
                processedCount++;

                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    PrefabThumbnailCache.RemoveThumbnail(assetGuid);
                    _projectWindowDirty = true;
                    continue;
                }

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefabAsset == null)
                {
                    continue;
                }

                bool isUiPrefab = PrefabThumbnailGenerator.IsUiPrefabAsset(prefabAsset);
                if (isUiPrefab)
                {
                    DisposeBatchSession();
                }
                else if (_batchSession == null)
                {
                    _batchSession = new PrefabThumbnailGenerator.BatchSession(PrefabThumbnailGenerator.CurrentExecutionMode);
                }

                bool skipped;
                bool generated = isUiPrefab
                    ? PrefabThumbnailGenerator.GenerateThumbnail(
                        prefabAsset,
                        PrefabThumbnailGenerator.CurrentExecutionMode,
                        false,
                        out skipped)
                    : _batchSession.Generate(prefabAsset, false, out skipped);

                if (generated)
                {
                    _projectWindowDirty = true;
                }
            }
        }

        private static void ScanProjectPrefabs()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            HashSet<string> validPrefabGuids = new HashSet<string>(prefabGuids, StringComparer.Ordinal);
            PrefabThumbnailCache.PruneMissingEntries(validPrefabGuids);

            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string assetGuid = prefabGuids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);

                string prefabHash;
                string resolvedGuid;
                if (!PrefabThumbnailGenerator.TryGetPrefabInfo(assetPath, out resolvedGuid, out prefabHash))
                {
                    continue;
                }

                if (PrefabThumbnailCache.IsUpToDate(resolvedGuid, prefabHash))
                {
                    continue;
                }

                EnqueuePrefabGuid(resolvedGuid);
            }
        }

        private static void EnqueuePrefabPaths(string[] assetPaths)
        {
            if (assetPaths == null)
            {
                return;
            }

            for (int i = 0; i < assetPaths.Length; i++)
            {
                string assetPath = assetPaths[i];
                if (string.IsNullOrEmpty(assetPath) ||
                    !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string prefabHash;
                string assetGuid;
                if (!PrefabThumbnailGenerator.TryGetPrefabInfo(assetPath, out assetGuid, out prefabHash))
                {
                    continue;
                }

                if (PrefabThumbnailCache.IsUpToDate(assetGuid, prefabHash))
                {
                    continue;
                }

                EnqueuePrefabGuid(assetGuid);
            }
        }

        private static void EnqueuePrefabGuid(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !QueuedAssetGuids.Add(assetGuid))
            {
                return;
            }

            PendingAssetGuids.Enqueue(assetGuid);
        }

        private static void DisposeBatchSession()
        {
            if (_batchSession == null)
            {
                return;
            }

            _batchSession.Dispose();
            _batchSession = null;
        }

        private sealed class PrefabThumbnailAssetPostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                NotifyPrefabAssetsChanged(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
            }
        }
    }
}
