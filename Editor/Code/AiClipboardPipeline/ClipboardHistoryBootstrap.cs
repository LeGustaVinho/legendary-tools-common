#if UNITY_EDITOR_WIN
using System;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    [InitializeOnLoad]
    public static class ClipboardHistoryBootstrap
    {
        private const string PrefEnabled = "AICodePasteHub.Enabled";
        private const string PrefAutoCapture = "AICodePasteHub.AutoCapture";
        private const string PrefAutoApply = "AICodePasteHub.AutoApply";
        private const string PrefMaxHistory = "AICodePasteHub.MaxHistory";
        private const string PrefFallbackFolder = "AICodePasteHub.FallbackFolder";

        static ClipboardHistoryBootstrap()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            try
            {
                ClipboardWatcher.ClipboardChanged -= OnClipboardChanged;
                ClipboardWatcher.ClipboardChanged += OnClipboardChanged;

                ApplyRuntimeSettings();
            }
            catch (Exception ex)
            {
                ClipboardHistoryStore.instance.SetLastErrorReport("[ClipboardHistoryBootstrap] Initialize failed:\n\n" +
                                                                  ex);
            }
        }

        public static void ApplyRuntimeSettings()
        {
            bool enabled = EditorPrefs.GetBool(PrefEnabled, true);

            int cap = Mathf.Clamp(EditorPrefs.GetInt(PrefMaxHistory, 200), 1, 5000);
            ClipboardHistoryStore.instance.SetCapacity(cap);

            if (enabled)
                ClipboardWatcher.Start();
            else
                ClipboardWatcher.Stop();
        }

        private static void OnClipboardChanged(string text)
        {
            bool enabled = EditorPrefs.GetBool(PrefEnabled, true);
            if (!enabled)
                return;

            bool autoCapture = EditorPrefs.GetBool(PrefAutoCapture, true);
            if (!autoCapture)
                return;

            string normalized = NormalizeClipboardText(text);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                Debug.Log(
                    "[AI Clipboard Pipeline] Clipboard detected but rejected: clipboard text is empty/unavailable (possible clipboard lock).");
                return;
            }

            if (!ClipboardClassifierRegistry.TryClassify(normalized, out ClipboardClassification classification))
            {
                // Diagnose why the two known classifiers rejected.
                string csReason = string.Empty;
                _ = CSharpFileClipboardClassifier.IsValidCSharpCode(normalized, out _, out csReason);

                string hint =
                    "[AI Clipboard Pipeline] Clipboard detected but no classifier accepted the content.\n" +
                    "Details:\n" +
                    " - C# Full File: " + (string.IsNullOrEmpty(csReason) ? "not matched" : csReason) + "\n" +
                    " - Git Patch: not matched (missing diff/hunk headers)\n" +
                    "\n" +
                    "Hint: Only full C# files (class/struct/interface/enum/record) and unified git patches are captured.";

                Debug.Log(hint);
                return;
            }

            bool stored = ClipboardHistoryStore.instance.TryAddClassified(normalized, classification);
            if (!stored)
            {
                Debug.Log("[AI Clipboard Pipeline] Clipboard detected but entry was not stored (dedupe or policy).");
                return;
            }

            bool autoApply = EditorPrefs.GetBool(PrefAutoApply, true);
            if (!autoApply)
                return;

            ClipboardHistoryStore.Entry newest = ClipboardHistoryStore.instance.Entries.Count > 0
                ? ClipboardHistoryStore.instance.Entries[0]
                : null;
            if (newest == null)
                return;

            if (newest.status == (int)ClipboardHistoryStore.EntryStatus.Applied)
                return;

            AICodePasteApplier.Settings settings = new()
            {
                FallbackFolder = EditorPrefs.GetString(PrefFallbackFolder, "Assets/Scripts/Generated/")
            };

            // Auto-apply: supports both csharp_file and git_patch (if enabled).
            _ = AICodePasteApplier.TryApplyEntry(newest, settings, false);
        }

        private static string NormalizeClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string t = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Prefer extracting a fenced block ONLY if it contains a valid, classifiable payload.
            // This prevents accidental extraction of small code fences (e.g., Console reports)
            // that do not represent a full C# file or a git patch.
            if (TryExtractBestClassifiableFencedBlock(t, "```", out string fenced))
                return fenced.Trim();

            if (TryExtractBestClassifiableFencedBlock(t, "~~~", out string fencedTilde))
                return fencedTilde.Trim();

            return t.Trim();
        }

        private static bool TryExtractBestClassifiableFencedBlock(string text, string fence, out string content)
        {
            content = string.Empty;

            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fence))
                return false;

            int searchIndex = 0;
            string best = string.Empty;

            while (true)
            {
                if (!TryExtractNextFencedBlock(text, fence, searchIndex, out string candidate, out int nextIndex))
                    break;

                searchIndex = Math.Max(nextIndex, searchIndex + 1);

                string c = (candidate ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(c))
                    continue;

                // Validate as C# full file or git patch before accepting.
                bool isCSharp = CSharpFileClipboardClassifier.IsValidCSharpCode(c, out _, out _);

                bool isPatch = false;
                try
                {
                    GitPatchClipboardClassifier patchClassifier = new();
                    isPatch = patchClassifier.TryClassify(c, out _);
                }
                catch
                {
                    isPatch = false;
                }

                if (!isCSharp && !isPatch)
                    continue;

                if (c.Length > best.Length)
                    best = c;
            }

            if (string.IsNullOrEmpty(best))
                return false;

            content = best;
            return true;
        }

        private static bool TryExtractNextFencedBlock(
            string text,
            string fence,
            int startSearchIndex,
            out string content,
            out int nextSearchIndex)
        {
            content = string.Empty;
            nextSearchIndex = startSearchIndex;

            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fence))
                return false;

            int n = text.Length;
            int start = text.IndexOf(fence, startSearchIndex, StringComparison.Ordinal);
            if (start < 0)
                return false;

            // Find end of the fence header line (to skip optional language).
            int headerEnd = text.IndexOf('\n', start + fence.Length);
            if (headerEnd < 0)
                return false;

            // Find the closing fence. Prefer a fence that begins on its own line.
            int end = text.IndexOf("\n" + fence, headerEnd + 1, StringComparison.Ordinal);
            if (end < 0)
            {
                // Fallback: any next fence occurrence.
                end = text.IndexOf(fence, headerEnd + 1, StringComparison.Ordinal);
                if (end < 0)
                    return false;
            }

            int bodyStart = headerEnd + 1;
            int bodyLen = end - bodyStart;
            if (bodyLen < 0)
                return false;

            content = text.Substring(bodyStart, bodyLen);
            nextSearchIndex = Math.Min(n, end + fence.Length);
            return true;
        }
    }
}
#endif