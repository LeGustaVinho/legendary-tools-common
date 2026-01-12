using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    public sealed class AICodePasteHubController : IDisposable
    {
        internal static class PrefKeys
        {
            public const string Enabled = "AICodePasteHub.Enabled";
            public const string AutoCapture = "AICodePasteHub.AutoCapture";
            public const string AutoApply = "AICodePasteHub.AutoApply";
            public const string AutoCopyError = "AICodePasteHub.AutoCopyError";
            public const string LogErrorToConsole = "AICodePasteHub.LogErrorToConsole";
            public const string MaxHistory = "AICodePasteHub.MaxHistory";
            public const string FallbackFolder = "AICodePasteHub.FallbackFolder";
            public const string LintTimeout = "AICodePasteHub.LintTimeout";

            // Window prefs
            public const string ShowFullDiff = "AICodePasteHub.ShowFullDiff";
        }

        public enum HistoryStatus
        {
            Pending,
            Applied,
            Error,
            Ignored
        }

        public enum HistoryType
        {
            FullFile,
            Patch
        }

        [Serializable]
        public sealed class HistoryItem
        {
            public string Id;
            public DateTime Timestamp;
            public HistoryStatus Status;
            public HistoryType Type;

            public string Title;
            public string Summary;
            public string Diff;

            public string LogicalKey;
            public int LogicalVersion;
            public string AppliedAssetPath;
        }

        public event Action StateChanged;

        public bool Enabled { get; private set; }

        public bool AutoCapture { get; private set; }
        public bool AutoApply { get; private set; }
        public bool AutoCopyErrorReport { get; private set; }
        public bool LogErrorReportToConsole { get; private set; }

        public int MaxHistory { get; private set; }
        public string FallbackFolder { get; private set; }
        public float LintTimeoutSeconds { get; private set; }

        public bool ShowFullDiff { get; private set; }

        public IReadOnlyList<HistoryItem> Items => _items;
        public int SelectedIndex => _selectedIndex;

        public HistoryItem SelectedItem
        {
            get
            {
                if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
                    return null;

                return _items[_selectedIndex];
            }
        }

        public string PreviewText
        {
            get
            {
                HistoryItem item = SelectedItem;
                if (item == null)
                    return string.Empty;

                return item.Diff ?? string.Empty;
            }
        }

        public string LastErrorReport
        {
            get
            {
#if UNITY_EDITOR_WIN
                return ClipboardHistoryStore.instance.LastErrorReport;
#else
                return string.Empty;
#endif
            }
        }

        public string ListenerStatus
        {
            get
            {
#if UNITY_EDITOR_WIN
                return Enabled ? "Running" : "Stopped";
#else
                return "Unavailable (Windows only)";
#endif
            }
        }

        private readonly List<HistoryItem> _items = new();
        private int _selectedIndex = -1;
        private string _selectedId;

#if UNITY_EDITOR_WIN
        private bool _storeSubscribed;
#endif

        public AICodePasteHubController()
        {
            LoadPrefs();
            ApplyRuntimeSettings();
            SubscribeStoreIfAvailable();
            RebuildHistoryFromStore();
        }

        public void Dispose()
        {
            UnsubscribeStoreIfAvailable();
        }

        // -------------------------
        // Entry operations
        // -------------------------

        public bool CanApply(HistoryItem item)
        {
            if (item == null)
                return false;

            // Full file and patch are supported now.
            return item.Type == HistoryType.FullFile || item.Type == HistoryType.Patch;
        }

        public void ApplyById(string id)
        {
#if UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(id))
                return;

            AICodePasteApplier.Settings settings = new()
            {
                FallbackFolder = FallbackFolder
            };

            AICodePasteApplier.TryApplyEntryById(id, settings);
#endif
        }

        public void DeleteById(string id)
        {
#if UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(id))
                return;

            ClipboardHistoryStore.instance.DeleteById(id);
#endif
        }

        public void IgnoreById(string id)
        {
#if UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(id))
                return;

            ClipboardHistoryStore.instance.SetStatus(id, ClipboardHistoryStore.EntryStatus.Ignored);
#endif
        }

        public void SelectIndex(int index)
        {
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                _selectedId = null;
                NotifyChanged();
                return;
            }

            int clamped = Mathf.Clamp(index, 0, _items.Count - 1);
            if (_selectedIndex == clamped)
                return;

            _selectedIndex = clamped;
            _selectedId = _items[_selectedIndex].Id;
            NotifyChanged();
        }

        // -------------------------
        // Settings operations (persisted)
        // -------------------------

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled)
                return;

            Enabled = enabled;
            SavePrefs();
            ApplyRuntimeSettings();
            NotifyChanged();
        }

        public void SetAutoCapture(bool value)
        {
            if (AutoCapture == value)
                return;

            AutoCapture = value;
            SavePrefs();
            ApplyRuntimeSettings();
            NotifyChanged();
        }

        public void SetAutoApply(bool value)
        {
            if (AutoApply == value)
                return;

            AutoApply = value;
            SavePrefs();
            NotifyChanged();
        }

        public void SetAutoCopyErrorReport(bool value)
        {
            if (AutoCopyErrorReport == value)
                return;

            AutoCopyErrorReport = value;
            SavePrefs();
            NotifyChanged();
        }

        public void SetLogErrorReportToConsole(bool value)
        {
            if (LogErrorReportToConsole == value)
                return;

            LogErrorReportToConsole = value;
            SavePrefs();
            NotifyChanged();
        }

        public void SetMaxHistory(int value)
        {
            value = Mathf.Clamp(value, 1, 5000);
            if (MaxHistory == value)
                return;

            MaxHistory = value;
            SavePrefs();
            ApplyRuntimeSettings();
            NotifyChanged();
        }

        public void SetFallbackFolder(string value)
        {
            value ??= string.Empty;
            if (string.Equals(FallbackFolder, value, StringComparison.Ordinal))
                return;

            FallbackFolder = value;
            SavePrefs();
            RebuildHistoryFromStore();
            NotifyChanged();
        }

        public void SetLintTimeoutSeconds(float value)
        {
            value = Mathf.Clamp(value, 1f, 600f);
            if (Math.Abs(LintTimeoutSeconds - value) < 0.0001f)
                return;

            LintTimeoutSeconds = value;
            SavePrefs();
            NotifyChanged();
        }

        public void SetShowFullDiff(bool value)
        {
            if (ShowFullDiff == value)
                return;

            ShowFullDiff = value;
            EditorPrefs.SetBool(PrefKeys.ShowFullDiff, ShowFullDiff);
            NotifyChanged();
        }

        public void CopyLastErrorReportToClipboard()
        {
            EditorGUIUtility.systemCopyBuffer = LastErrorReport ?? string.Empty;
        }

        public void ClearLastErrorReport()
        {
#if UNITY_EDITOR_WIN
            ClipboardHistoryStore.instance.SetLastErrorReport(string.Empty);
#endif
            NotifyChanged();
        }

        // -------------------------
        // Runtime integration
        // -------------------------

        private void ApplyRuntimeSettings()
        {
#if UNITY_EDITOR_WIN
            ClipboardHistoryStore.instance.SetCapacity(MaxHistory);
            ClipboardHistoryBootstrap.ApplyRuntimeSettings();
#endif
        }

        private void SubscribeStoreIfAvailable()
        {
#if UNITY_EDITOR_WIN
            if (_storeSubscribed)
                return;

            ClipboardHistoryStore.instance.Changed += OnStoreChanged;
            _storeSubscribed = true;
#endif
        }

        private void UnsubscribeStoreIfAvailable()
        {
#if UNITY_EDITOR_WIN
            if (!_storeSubscribed)
                return;

            ClipboardHistoryStore.instance.Changed -= OnStoreChanged;
            _storeSubscribed = false;
#endif
        }

        private void OnStoreChanged()
        {
            RebuildHistoryFromStore();
            NotifyChanged();
        }

        private void RebuildHistoryFromStore()
        {
            _items.Clear();

#if UNITY_EDITOR_WIN
            IReadOnlyList<ClipboardHistoryStore.Entry> entries = ClipboardHistoryStore.instance.Entries;

            for (int i = 0; i < entries.Count; i++)
            {
                ClipboardHistoryStore.Entry e = entries[i];
                if (e == null)
                    continue;

                HistoryItem item = new()
                {
                    Id = e.id,
                    Timestamp = ParseTimestamp(e),
                    Status = MapStatus(e.status),
                    Type = MapType(e.typeId),
                    Title = BuildTitle(e),
                    Summary = BuildSummary(e),
                    Diff = e.text ?? string.Empty,
                    LogicalKey = e.logicalKey,
                    LogicalVersion = e.logicalVersion,
                    AppliedAssetPath = e.appliedAssetPath
                };

                _items.Add(item);
            }
#endif

            RestoreSelectionById();
        }

        private void RestoreSelectionById()
        {
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                _selectedId = null;
                return;
            }

            if (string.IsNullOrEmpty(_selectedId))
            {
                _selectedIndex = 0;
                _selectedId = _items[0].Id;
                return;
            }

            int idx = _items.FindIndex(x => string.Equals(x.Id, _selectedId, StringComparison.Ordinal));
            _selectedIndex = idx >= 0 ? idx : 0;
            _selectedId = _items[_selectedIndex].Id;
        }

#if UNITY_EDITOR_WIN
        private static DateTime ParseTimestamp(ClipboardHistoryStore.Entry e)
        {
            if (e == null)
                return DateTime.Now;

            if (e.timestampTicks > 0)
                try
                {
                    return new DateTime(e.timestampTicks);
                }
                catch
                {
                }

            if (!string.IsNullOrEmpty(e.timestampIso) && DateTime.TryParse(e.timestampIso, out DateTime dt))
                return dt;

            return DateTime.Now;
        }

        private static HistoryStatus MapStatus(int rawStatus)
        {
            if (rawStatus == 0)
                return HistoryStatus.Pending;

            return rawStatus switch
            {
                (int)ClipboardHistoryStore.EntryStatus.Applied => HistoryStatus.Applied,
                (int)ClipboardHistoryStore.EntryStatus.Error => HistoryStatus.Error,
                (int)ClipboardHistoryStore.EntryStatus.Ignored => HistoryStatus.Ignored,
                _ => HistoryStatus.Pending
            };
        }

        private static HistoryType MapType(string typeId)
        {
            if (string.Equals(typeId, "csharp_file", StringComparison.Ordinal))
                return HistoryType.FullFile;

            return HistoryType.Patch;
        }

        private static string BuildTitle(ClipboardHistoryStore.Entry e)
        {
            if (e == null)
                return "Unknown";

            if (!string.IsNullOrEmpty(e.logicalKey))
                return e.logicalKey;

            return e.typeName ?? "Unknown";
        }

        private string BuildSummary(ClipboardHistoryStore.Entry e)
        {
            if (e == null)
                return string.Empty;

            if (string.Equals(e.typeId, "csharp_file", StringComparison.Ordinal))
            {
                string targetNote = string.IsNullOrEmpty(e.appliedAssetPath)
                    ? "(pending apply)"
                    : e.appliedAssetPath;

                return
                    "Targets:\n" +
                    $" • {targetNote}\n\n" +
                    "Details:\n" +
                    " • Full file captured\n";
            }

            // git_patch
            return
                "Targets:\n" +
                " • (git patch)\n\n" +
                "Details:\n" +
                " • Patch captured (apply supported)\n";
        }
#endif

        // -------------------------
        // Prefs
        // -------------------------

        private void LoadPrefs()
        {
            Enabled = EditorPrefs.GetBool(PrefKeys.Enabled, true);

            AutoCapture = EditorPrefs.GetBool(PrefKeys.AutoCapture, true);
            AutoApply = EditorPrefs.GetBool(PrefKeys.AutoApply, true);
            AutoCopyErrorReport = EditorPrefs.GetBool(PrefKeys.AutoCopyError, true);
            LogErrorReportToConsole = EditorPrefs.GetBool(PrefKeys.LogErrorToConsole, true);

            MaxHistory = Mathf.Clamp(EditorPrefs.GetInt(PrefKeys.MaxHistory, 200), 1, 5000);
            FallbackFolder = EditorPrefs.GetString(PrefKeys.FallbackFolder, "Assets/Scripts/Generated/");
            LintTimeoutSeconds = Mathf.Clamp(EditorPrefs.GetFloat(PrefKeys.LintTimeout, 15f), 1f, 600f);

            ShowFullDiff = EditorPrefs.GetBool(PrefKeys.ShowFullDiff, false);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(PrefKeys.Enabled, Enabled);

            EditorPrefs.SetBool(PrefKeys.AutoCapture, AutoCapture);
            EditorPrefs.SetBool(PrefKeys.AutoApply, AutoApply);
            EditorPrefs.SetBool(PrefKeys.AutoCopyError, AutoCopyErrorReport);
            EditorPrefs.SetBool(PrefKeys.LogErrorToConsole, LogErrorReportToConsole);

            EditorPrefs.SetInt(PrefKeys.MaxHistory, MaxHistory);
            EditorPrefs.SetString(PrefKeys.FallbackFolder, FallbackFolder);
            EditorPrefs.SetFloat(PrefKeys.LintTimeout, LintTimeoutSeconds);

            EditorPrefs.SetBool(PrefKeys.ShowFullDiff, ShowFullDiff);
        }

        private void NotifyChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
