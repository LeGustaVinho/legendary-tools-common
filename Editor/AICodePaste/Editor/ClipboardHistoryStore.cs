#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    [FilePath("ProjectSettings/AiClipboardPipelineClipboardHistory.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ClipboardHistoryStore : ScriptableSingleton<ClipboardHistoryStore>
    {
        // IMPORTANT: Stored as int in assets. Keep backward compatibility.
        // Old versions used: 0=Ignored, 1=Applied, 2=Error
        // New mapping:
        // 0=Pending (old Ignored becomes Pending)
        // 1=Applied
        // 2=Error
        // 3=Ignored (new explicit user-ignored)
        public enum EntryStatus
        {
            Pending = 0,
            Applied = 1,
            Error = 2,
            Ignored = 3
        }

        [Serializable]
        public sealed class Entry
        {
            public string id;

            public string typeId;
            public string typeName;
            public string logicalKey;

            public string timestampIso;
            public long timestampTicks;

            public int length;
            public string preview;

            public string text;

            /// <summary>
            /// Monotonic version number per logicalKey.
            /// </summary>
            public int logicalVersion;

            // Apply state
            public int status; // EntryStatus as int
            public string appliedAssetPath;
            public string errorReport;
        }

        public event Action Changed;

        [SerializeField] private int capacity = 200;
        [SerializeField] private List<Entry> entries = new();
        [SerializeField] private string lastErrorReport = string.Empty;

        private readonly Dictionary<string, int> _latestVersionByKey = new(StringComparer.Ordinal);

        public int Capacity => capacity;
        public IReadOnlyList<Entry> Entries => entries;
        public string LastErrorReport => lastErrorReport ?? string.Empty;

        public void SetCapacity(int newCapacity)
        {
            newCapacity = Mathf.Clamp(newCapacity, 1, 5000);
            if (capacity == newCapacity)
                return;

            capacity = newCapacity;

            if (entries.Count > capacity)
                entries.RemoveRange(capacity, entries.Count - capacity);

            SaveAndNotify();
        }

        public void Clear()
        {
            if (entries.Count == 0 && string.IsNullOrEmpty(lastErrorReport))
                return;

            entries.Clear();
            _latestVersionByKey.Clear();
            lastErrorReport = string.Empty;
            SaveAndNotify();
        }

        public void DeleteById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            int idx = entries.FindIndex(e => string.Equals(e.id, id, StringComparison.Ordinal));
            if (idx < 0)
                return;

            entries.RemoveAt(idx);
            RebuildVersionIndex();
            SaveAndNotify();
        }

        public Entry GetById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].id, id, StringComparison.Ordinal))
                    return entries[i];
            }

            return null;
        }

        public void SetLastErrorReport(string report)
        {
            lastErrorReport = report ?? string.Empty;
            SaveAndNotify();
        }

        public void UpdateEntryResult(string id, EntryStatus status, string appliedAssetPath, string errorReport)
        {
            Entry e = GetById(id);
            if (e == null)
                return;

            e.status = (int)status;
            e.appliedAssetPath = appliedAssetPath ?? string.Empty;
            e.errorReport = errorReport ?? string.Empty;
            e.preview = BuildPreview(e);

            SaveAndNotify();
        }

        public void SetStatus(string id, EntryStatus status)
        {
            Entry e = GetById(id);
            if (e == null)
                return;

            e.status = (int)status;
            e.preview = BuildPreview(e);
            SaveAndNotify();
        }

        /// <summary>
        /// Adds classified clipboard text to the ring buffer.
        /// - Does NOT add anything if classification is null.
        /// - Dedupes by exact text: if already present, promotes to front and refreshes timestamp.
        /// - If same logicalKey but different text, inserts a new entry with incremented logicalVersion.
        /// Returns false when the entry is not stored (reason is provided by overload).
        /// </summary>
        public bool TryAddClassified(string text, ClipboardClassification classification)
        {
            return TryAddClassified(text, classification, out _);
        }

        /// <summary>
        /// Same as TryAddClassified, but provides a reason string when the entry is not stored.
        /// Intended for diagnostics/logging only.
        /// </summary>
        public bool TryAddClassified(string text, ClipboardClassification classification, out string reason)
        {
            reason = string.Empty;

            if (classification == null)
            {
                reason = "Rejected: classification is null.";
                return false;
            }

            if (string.IsNullOrEmpty(text))
            {
                reason = "Rejected: clipboard text is empty.";
                return false;
            }

            EnsureVersionIndex();

            // Fast path: same as newest.
            if (entries.Count > 0 && string.Equals(entries[0].text, text, StringComparison.Ordinal))
            {
                reason = "Rejected: clipboard text is identical to the most recent stored entry.";
                return false;
            }

            // Deduplicate by exact text anywhere in the ring buffer.
            int existingExactIndex = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].text, text, StringComparison.Ordinal))
                {
                    existingExactIndex = i;
                    break;
                }
            }

            DateTime now = DateTime.Now;
            string nowIso = now.ToString("yyyy-MM-dd HH:mm:ss");
            long nowTicks = now.Ticks;

            if (existingExactIndex >= 0)
            {
                // Promote existing entry to front and refresh timestamp.
                Entry e = entries[existingExactIndex];
                e.timestampIso = nowIso;
                e.timestampTicks = nowTicks;
                e.length = text.Length;
                e.preview = BuildPreview(e);

                if (existingExactIndex != 0)
                {
                    entries.RemoveAt(existingExactIndex);
                    entries.Insert(0, e);
                }

                SaveAndNotify();

                // This is still a successful capture (history updated).
                reason = "Stored: exact duplicate found; promoted existing entry to top.";
                return true;
            }

            string logicalKey = classification.LogicalKey ?? string.Empty;
            if (string.IsNullOrEmpty(logicalKey))
                logicalKey = $"{classification.TypeId}:unknown";

            int nextVersion = GetNextVersionForKey(logicalKey);

            Entry entry = new()
            {
                id = Guid.NewGuid().ToString("N"),

                typeId = classification.TypeId,
                typeName = classification.DisplayName,
                logicalKey = logicalKey,
                logicalVersion = nextVersion,

                timestampIso = nowIso,
                timestampTicks = nowTicks,

                length = text.Length,
                text = text,

                status = (int)EntryStatus.Pending,
                appliedAssetPath = string.Empty,
                errorReport = string.Empty
            };

            entry.preview = BuildPreview(entry);

            entries.Insert(0, entry);

            if (entries.Count > capacity)
                entries.RemoveRange(capacity, entries.Count - capacity);

            SaveAndNotify();

            reason = "Stored: new entry inserted.";
            return true;
        }

        // ---------------------------------------------------------------------
        // Compatibility API (older services)
        // ---------------------------------------------------------------------

        public bool TryGetById(string id, out Entry entry)
        {
            entry = GetById(id);
            return entry != null;
        }

        public void SetApplied(string id, string appliedAssetPath = "")
        {
            UpdateEntryResult(id, EntryStatus.Applied, appliedAssetPath ?? string.Empty, string.Empty);
        }

        public void SetApplied(Entry entry, string appliedAssetPath = "")
        {
            if (entry == null)
                return;

            UpdateEntryResult(entry.id, EntryStatus.Applied, appliedAssetPath ?? string.Empty, string.Empty);
        }

        public void SetError(string id, string errorReport)
        {
            string report = errorReport ?? string.Empty;
            SetLastErrorReport(report);
            UpdateEntryResult(id, EntryStatus.Error, string.Empty, report);
        }

        public void SetError(Entry entry, string errorReport)
        {
            if (entry == null)
                return;

            SetError(entry.id, errorReport);
        }

        public void SetError(string id, Exception exception)
        {
            SetError(id, exception?.ToString() ?? "Unknown error.");
        }

        // ---------------------------------------------------------------------

        private void EnsureVersionIndex()
        {
            if (_latestVersionByKey.Count == 0 && entries.Count > 0)
                RebuildVersionIndex();
        }

        private void RebuildVersionIndex()
        {
            _latestVersionByKey.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e == null)
                    continue;

                string key = e.logicalKey ?? string.Empty;
                if (string.IsNullOrEmpty(key))
                    continue;

                if (_latestVersionByKey.TryGetValue(key, out int v))
                    _latestVersionByKey[key] = Math.Max(v, e.logicalVersion);
                else
                    _latestVersionByKey[key] = e.logicalVersion;
            }
        }

        private int GetNextVersionForKey(string logicalKey)
        {
            if (!_latestVersionByKey.TryGetValue(logicalKey, out int v))
                v = 0;

            int next = v + 1;
            _latestVersionByKey[logicalKey] = next;
            return next;
        }

        private static string BuildPreview(Entry entry)
        {
            // Backward compat: treat old "Ignored(0)" entries as Pending.
            int raw = entry.status;
            if (raw == 0)
                raw = (int)EntryStatus.Pending;

            string statusMark = raw switch
            {
                (int)EntryStatus.Pending => "⏳",
                (int)EntryStatus.Applied => "✔",
                (int)EntryStatus.Error => "❌",
                (int)EntryStatus.Ignored => "🚫",
                _ => "⏳"
            };

            string header = $"{statusMark} [{entry.typeName}] {entry.logicalKey} (v{entry.logicalVersion})";
            string body = entry.text ?? string.Empty;

            body = body.Replace("\r\n", "\n").Replace("\r", "\n");
            int nl = body.IndexOf('\n');
            string firstLine = nl >= 0 ? body.Substring(0, nl) : body;

            const int max = 140;
            string line = $"{header} • {firstLine}";
            if (line.Length <= max)
                return line;

            return line.Substring(0, max) + "…";
        }

        private void SaveAndNotify()
        {
            Save(true);
            Changed?.Invoke();
        }
    }
}
#endif
