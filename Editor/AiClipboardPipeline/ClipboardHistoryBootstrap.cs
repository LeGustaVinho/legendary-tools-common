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

            // Try to extract the first fenced code block even if there is text around it.
            // Supports ```lang ... ``` and ``` ... ``` (and best-effort ~~~ fences).
            if (TryExtractFirstFencedBlock(t, "```", out string fenced))
                return fenced.Trim();

            if (TryExtractFirstFencedBlock(t, "~~~", out string fencedTilde))
                return fencedTilde.Trim();

            return t.Trim();
        }

        private static bool TryExtractFirstFencedBlock(string text, string fence, out string content)
        {
            content = string.Empty;

            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(fence))
                return false;

            int start = text.IndexOf(fence, StringComparison.Ordinal);
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
            return true;
        }
    }
}
#endif